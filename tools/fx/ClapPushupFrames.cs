using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.CV;
using PushStars.Fight;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>The clap push-up on the boss battle's player, rendered in edit mode
/// → output/clap-pushup/. Run via `unity command run_script`.
/// <list type="bullet">
/// <item><c>Run</c>: the pose table stepped through one flight (PushupPoseCorrection.Apply(depth, flight)).</item>
/// <item><c>Replay</c>: a real recording (CVRecordings/clap/*.pose.csv) through the production
/// PushupSession and PushupAvatarDriver, frames taken around one of its flights.</item>
/// </list></summary>
public static class ClapPushupFrames
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Output = "output/clap-pushup/";
    static readonly float[] Phases = { 0f, .12f, .25f, .38f, .5f, .62f, .75f, .88f, 1f };

    sealed class Stand : IDisposable
    {
        public Scene Scene;
        public BossCombatScreen Screen;
        public PushupPoseCorrection Correction;
        public Animator Animator;
        public Camera StageCamera, ScreenCamera;
        public RenderTexture Target;
        readonly List<RenderTexture> _textures = new List<RenderTexture>();
        readonly RenderTexture _previous = RenderTexture.active;

        public Stand()
        {
            Directory.CreateDirectory(Output);
            FightRequest.Boss();
            Scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Fight.unity");
            var c = Screen = Scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossCombatScreen>(true)).Single();
            var canvas = c.GetComponent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>(); if (scaler) scaler.enabled = false;
            canvas.renderMode = RenderMode.WorldSpace; canvas.scaleFactor = 1;
            var rect = (RectTransform)canvas.transform; rect.position = Vector3.zero; rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity; rect.sizeDelta = new Vector2(390, 844);
            foreach (var avatar in new[] { c.PlayerStage, c.BossStage })
            {
                typeof(FightAvatar).GetMethod("Build", Private).Invoke(avatar, null);
                var animator = avatar.Character.GetComponentInChildren<Animator>();
                var stage = avatar.StageCamera; stage.scene = Scene;
                var rt = new RenderTexture(720, 1280, 24); _textures.Add(rt); stage.targetTexture = rt;
                if (avatar == c.BossStage)
                {
                    if (animator.HasState(0, Animator.StringToHash("StandIdle"))) { animator.Play("StandIdle", 0, .12f); animator.Update(0); }
                    stage.ResetAspect();
                    typeof(FightAvatar).GetMethod("FrameCharacter", Private).Invoke(avatar, null);
                    continue;
                }
                if (animator.HasState(0, Animator.StringToHash("PushUp"))) { animator.Play("PushUp", 0, .12f); animator.Update(0); }
                Correction = PushupPoseCorrection.Bind(animator);
                typeof(FightAvatar).GetMethod("CachePushupSilhouette", Private).Invoke(avatar, null);
                var mirror = avatar.GetComponent<PoseMirrorRetargeter>();
                if (mirror != null) typeof(PoseMirrorRetargeter).GetMethod("SetPhase", Private).Invoke(mirror, new object[] { true, Time.unscaledTime });
            }
            c.Configure(true); Canvas.ForceUpdateCanvases(); c.Refresh(1);
            ScreenCamera = new GameObject("ClapFramesCamera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(ScreenCamera.gameObject, Scene);
            ScreenCamera.scene = Scene; ScreenCamera.transform.position = new Vector3(0, 0, -50); ScreenCamera.orthographic = true;
            ScreenCamera.orthographicSize = 422; ScreenCamera.cullingMask = ~0; ScreenCamera.clearFlags = CameraClearFlags.SolidColor;
            ScreenCamera.backgroundColor = Color.black; canvas.worldCamera = ScreenCamera;
            Target = new RenderTexture(780, 1688, 24); _textures.Add(Target); ScreenCamera.targetTexture = Target;
            Animator = c.PlayerStage.Character.GetComponentInChildren<Animator>();
            StageCamera = c.PlayerStage.StageCamera;
            // Live, the Animator evaluates before the correction every frame. Here nothing ticks
            // it, and an enabled Animator can write its clip pose during an edit-mode render.
            Animator.enabled = false;
            // Skinning normally runs in the player loop, which a synchronous editor script never reaches.
            foreach (var skin in c.PlayerStage.Character.GetComponentsInChildren<SkinnedMeshRenderer>())
                skin.forceMatrixRecalculationPerRender = true;

            // Frame the shot on the planted plank, exactly as the live screen does when it arms.
            Correction.SetDepth(0f, true, 0f);
            Correction.Apply(0f);
            c.Refresh(0);
            typeof(FightAvatar).GetMethod("LateUpdate", Private).Invoke(c.PlayerStage, null);
        }

        /// <summary>Renders the screen with the body at this pose and returns the requested crop.</summary>
        public Color[] Shot(float depth, float flight, float clap, RectInt crop, string fullPath = null)
        {
            Correction.Apply(depth, flight, clap);
            Screen.Refresh(0);
            Canvas.ForceUpdateCanvases();
            // Edit-mode skinning lags one render behind the bones: render once to flush.
            StageCamera.Render();
            StageCamera.Render(); Screen.BossStage.StageCamera.Render(); ScreenCamera.Render();
            RenderTexture.active = Target;
            var image = new Texture2D(Target.width, Target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Target.width, Target.height), 0, 0); image.Apply();
            if (fullPath != null) File.WriteAllBytes(fullPath, image.EncodeToPNG());
            var pixels = image.GetPixels(crop.x, crop.y, crop.width, crop.height);
            Object.DestroyImmediate(image);
            return pixels;
        }

        public void Dispose()
        {
            RenderTexture.active = _previous; EditorSceneManager.ClosePreviewScene(Scene);
            foreach (var t in _textures) { t.Release(); Object.DestroyImmediate(t); }
            FightRequest.Clear();
        }
    }

    static void SaveSheet(string path, List<Color[]> cells, RectInt crop)
    {
        var sheet = new Texture2D(crop.width * cells.Count, crop.height, TextureFormat.RGB24, false);
        for (int i = 0; i < cells.Count; i++) sheet.SetPixels(i * crop.width, 0, crop.width, crop.height, cells[i]);
        sheet.Apply();
        File.WriteAllBytes(path, sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
    }

    /// <param name="cropX">Region of the 780×1688 screen put on the sheet: x, y, w, h from the bottom-left.</param>
    public static string Run(string tag, int cropX = 150, int cropY = 150, int cropW = 480, int cropH = 660)
    {
        var crop = new RectInt(cropX, cropY, cropW, cropH);
        using (var stand = new Stand())
        {
            string log = $"headroom={typeof(PushupPoseCorrection).GetField("_flightHeadroom", Private).GetValue(stand.Correction)}";
            var cells = new List<Color[]>();
            var an = stand.Animator; var root = an.transform;
            foreach (float phase in Phases)
            {
                cells.Add(stand.Shot(0f, phase, 1f, crop, Mathf.Approximately(phase, .5f) ? Output + $"{tag}-apex.png" : null));
                var l = an.GetBoneTransform(HumanBodyBones.LeftHand).position;
                var r = an.GetBoneTransform(HumanBodyBones.RightHand).position;
                log += $"\np={phase:0.00} headVpY={stand.StageCamera.WorldToViewportPoint(an.GetBoneTransform(HumanBodyBones.Head).position).y:0.000} " +
                       $"wristGap={Vector3.Distance(l, r):0.000} wristY={root.InverseTransformPoint(l).y:0.000} " +
                       $"shoulderY={root.InverseTransformPoint(an.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).y:0.000}";
            }
            SaveSheet(Output + $"{tag}-sheet.png", cells, crop);
            return log;
        }
    }

    /// <summary>Replays a recording through the scene's own PushupSession and PushupAvatarDriver
    /// (two driver steps per pose frame: 60 fps display over a 30 fps detector) and puts every
    /// <paramref name="every"/>-th display frame around flight number <paramref name="flight"/>
    /// on a sheet. The log is the driver's timeline for every flight in the recording.</summary>
    public static string Replay(string tag, string fixture = "CVRecordings/clap/1_clap_IMG_1158.pose.csv",
        int flight = 1, int every = 3, int cropX = 150, int cropY = 150, int cropW = 480, int cropH = 660)
    {
        var crop = new RectInt(cropX, cropY, cropW, cropH);
        string path = RecordedPoseSource.ResolvePath(fixture);
        var frames = RecordedPoseSource.Read(path, out float aspect);
        using (var stand = new Stand())
        {
            var driver = (PushupAvatarDriver)typeof(FightAvatar).GetField("_driverBehaviour", Private).GetValue(stand.Screen.PlayerStage);
            var session = (PushupSession)typeof(PushupAvatarDriver).GetField("_session", Private).GetValue(driver);
            var step = typeof(PushupAvatarDriver).GetMethod("Step", Private);
            var phaseField = typeof(PushupAvatarDriver).GetField("_flightPhase", Private);
            var closedField = typeof(PushupAvatarDriver).GetField("_handsClosed", Private);
            session.EnsureBuilt();
            int reps = 0, claps = 0, flights = 0, started = 0, shotIndex = 0;
            float clock = 0f;
            string log = "";
            session.OnRep += _ => { reps++; log += $"\n   {clock:0.00} REP {reps}"; };
            session.OnClapRep += _ => { claps++; log += $"\n   {clock:0.00} CLAP x2"; };
            session.Clap.OnFlightLanded += f => { flights++; log += $"\n   {clock:0.00} landed {f}"; };
            bool wasInFlight = false;

            var cells = new List<Color[]>();
            const float clock0 = 100f; // live Time.time is never 0
            float previous = frames[0].Item1, lastPhase = -1f, quietUntil = float.NegativeInfinity;
            foreach (var (t, image, world) in frames)
            {
                var frame = RecordedPoseSource.ToFrame(image, world, clock0 + t, aspect);
                clock = t;
                session.ProcessOffline(frame, image == null ? TrackingQuality.Lost : PoseQuality.Classify(frame), clock0 + t);
                if (session.Clap.InFlight && !wasInFlight)
                    log += $"\n   {t:0.00} detector takeoff, {session.Clap.TakeoffSec - session.Tracker.BottomLatchTimeSec:0.00}s after the last bottom";
                wasInFlight = session.Clap.InFlight;
                float dt = Mathf.Max(0f, t - previous) * .5f; previous = t;
                for (int sub = 0; sub < 2; sub++)
                {
                    step.Invoke(driver, new object[] { dt });
                    float phase = (float)phaseField.GetValue(driver), closed = (float)closedField.GetValue(driver);
                    if (phase >= 0f && lastPhase < 0f) { started++; log += $"\n#{started} {t:0.00} avatar takes off"; }
                    if (phase < 0f && lastPhase >= 0f) { quietUntil = t + .5f; log += $"\n   {t:0.00} avatar lands"; }
                    lastPhase = phase;
                    bool near = phase >= 0f || t < quietUntil;
                    if (near) log += $"\n   {t:0.00}{(sub == 0 ? " " : "+")} phase={phase:0.00} hands={closed:0.00} depth={driver.SmoothedDepth:0.00} raw={session.Tracker.CurrentDepth01:0.00} inFlight={session.Clap.InFlight}";
                    if (near && started == flight && shotIndex++ % every == 0 && cells.Count < 14)
                        cells.Add(stand.Shot(driver.SmoothedDepth, Mathf.Max(0f, phase), closed, crop));
                }
            }
            if (cells.Count > 0) SaveSheet(Output + $"{tag}-replay.png", cells, crop);
            return $"reps={reps} claps={claps} flights={flights} avatarFlights={started} cells={cells.Count}{log}";
        }
    }
}
