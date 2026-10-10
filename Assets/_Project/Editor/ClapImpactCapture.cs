using System;
using System.IO;
using System.Linq;
using System.Text;
using PushStars.Core;
using PushStars.CV;
using PushStars.Fight;
using PushStars.UI.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Play-mode frames of the duel's clap push-up landing (<see cref="ClapImpactEffect"/>) on the
    /// authored duel screen preview: the takeoff, a plain clap stepped through its timeline, the
    /// finisher, and the opponent's clap coming back. CV stays off; the effect is stepped with its own clockless Preview, so every
    /// frame is the exact moment named. Output: output/clap-impact/*.png.
    ///
    /// <para>The clip variant steps five claps at 60 fps (rep, takeoff, confirm, palms down —
    /// the timing measured on a real set) with two of the opponent's between them, into
    /// output/clap-impact/clip/ with the list of
    /// sounds the game would have played; tools/fx/mix_clap_clip.py turns that into an mp4.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class ClapImpactCapture
    {
        private const string Running = "PushStars.ClapImpactCapture.Running";
        private const string ReturnScene = "PushStars.ClapImpactCapture.ReturnScene";
        private const string Output = "output/clap-impact/";
        private const string ClipMode = "PushStars.ClapImpactCapture.Clip";
        // One clap of the clip, in seconds: the rep counts at the top, the hands leave the floor,
        // the clap confirms on the way down, the character's palms plant. IMG_1158: the takeoff is
        // noticed 0.10-0.17 s after the rep and the clap confirms 0.23-0.26 s after that; the
        // character's flight runs 0.32 s.
        private const float ClipFps = 60f, CycleSec = 2f, RepAt = .4f, TakeoffAt = .53f, ConfirmAt = .77f, FlightSec = .32f;
        private const float PlantIn = TakeoffAt + FlightSec - ConfirmAt;

        // name, clap number, anticipation, seconds since the player's landing and since the
        // opponent's clap (negative = none).
        private static readonly (string name, int clap, float anticipation, float age, float rival)[] Frames =
        {
            ("a0-idle", 2, 0f, -1f, -1f), ("a1-takeoff", 2, 1f, -1f, -1f),
            ("b-000", 2, 0f, .001f, -1f), ("b-030", 2, 0f, .03f, -1f), ("b-070", 2, 0f, .07f, -1f), ("b-120", 2, 0f, .12f, -1f),
            ("b-180", 2, 0f, .18f, -1f), ("b-260", 2, 0f, .26f, -1f), ("b-420", 2, 0f, .42f, -1f), ("b-700", 2, 0f, .7f, -1f),
            ("b-900", 2, 0f, .9f, -1f), ("c-max-030", 5, 0f, .03f, -1f), ("c-max-160", 5, 0f, .16f, -1f),
            ("c-max-220", 5, 0f, .22f, -1f), ("c-max-340", 5, 0f, .34f, -1f), ("c-max-500", 5, 0f, .5f, -1f),
            ("e-rival-090", 2, 0f, -1f, .09f), ("e-rival-130", 2, 0f, -1f, .13f), ("e-rival-200", 2, 0f, -1f, .2f),
            ("e-rival-320", 2, 0f, -1f, .32f), ("e-rival-550", 2, 0f, -1f, .55f), ("e-rival-800", 2, 0f, -1f, .8f),
            ("f-both", 3, 0f, .2f, .25f), ("d-after", 2, 0f, 2f, -1f),
        };

        // The clip: the player's claps (number > 0) with the opponent's (number < 0) between them.
        private static readonly int[] Sequence = { 1, 2, -1, 3, 4, -2, 5 };
        // A clap's second rep goes on the count a legal rep's distance after the first.
        private const float ClapRepAfter = .42f;
        private const float RivalCycleSec = 1.5f, RivalRepAt = .2f, RivalTakeoffAt = .3f, RivalConfirmAt = .54f;

        private static int _step, _frame;
        private static double _due, _deadline;
        private static Camera _camera;
        private static RenderTexture _texture;
        private static StringBuilder _report;
        private static ClapImpactEffect _effect;
        private static FightHud _hud;
        private static bool _clip;
        private static StringBuilder _events;

        static ClapImpactCapture() => EditorApplication.playModeStateChanged += StateChanged;

        [MenuItem("Tools/Push Stars/CV/Capture Clap Impact (duel)", priority = 362)]
        public static void Run() => Start(false);

        [MenuItem("Tools/Push Stars/CV/Record Clap Impact Clip (duel)", priority = 363)]
        public static void RunClip() => Start(true);

        private static void Start(bool clip)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start the capture outside Play Mode.");
            if (SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the open scene first — the capture switches scenes.");
            Directory.CreateDirectory(Output);
            SessionState.SetBool(ClipMode, clip);
            SessionState.SetString(ReturnScene, SceneManager.GetActiveScene().path);
            SessionState.SetBool(Running, true);
            FightRequest.Clear();
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Fight.unity");
            EditorApplication.isPlaying = true;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Running, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _step = _frame = 0;
                _clip = SessionState.GetBool(ClipMode, false);
                _report = new StringBuilder("Clap impact capture\n");
                _events = new StringBuilder();
                if (_clip)
                {
                    if (Directory.Exists(Output + "clip")) Directory.Delete(Output + "clip", true);
                    Directory.CreateDirectory(Output + "clip");
                }
                _deadline = EditorApplication.timeSinceStartup + (_clip ? 420 : 90);
                _due = EditorApplication.timeSinceStartup + 2.5;
                Application.runInBackground = true;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Running, false);
                EditorApplication.update -= Tick;
                string back = SessionState.GetString(ReturnScene, "");
                // Unity restores the pre-Play scene (Fight) after this callback; reopen the
                // caller's scene a few editor ticks later.
                if (!string.IsNullOrEmpty(back) && File.Exists(back))
                {
                    int ticks = 0;
                    EditorApplication.CallbackFunction reopen = null;
                    reopen = () =>
                    {
                        if (EditorApplication.isPlayingOrWillChangePlaymode || ++ticks < 10) return;
                        EditorApplication.update -= reopen;
                        if (SceneManager.GetActiveScene().path != back && !SceneManager.GetActiveScene().isDirty)
                            EditorSceneManager.OpenScene(back);
                    };
                    EditorApplication.update += reopen;
                }
            }
        }

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("capture timed out");
                Step();
            }
            catch (Exception e) { Finish("FAIL: " + e); }
        }

        private static void Step()
        {
            switch (_step)
            {
                case 0:
                    ForcePushup();
                    _step++; Delay(1.0);
                    break;
                case 1:
                    PrepareCapture();
                    _hud = Object.FindFirstObjectByType<FightHud>();
                    _effect = _hud.gameObject.AddComponent<ClapImpactEffect>();
                    _effect.Bind(_hud);
                    _step++; Delay(.4);
                    break;
                case 2:
                    if (_clip) { StageClipFrame(); _step++; break; }
                    if (_frame >= Frames.Length) { Finish("RESULT: done"); return; }
                    var f = Frames[_frame];
                    SetFlight(true, f.anticipation > 0f ? .5f : 0f);
                    if (f.age > ClapImpactEffect.Duration) _effect.Cancel();
                    else _effect.Preview(f.clap, EconomyConfig.AuraMaxClaps, EconomyConfig.AuraPerClap, f.anticipation, f.age, 0f, f.rival);
                    _step++; Delay(.2);
                    break;
                default:
                    if (_clip)
                    {
                        Capture($"clip/f{_frame:00000}", true);
                        if (++_frame >= ClipFrames())
                        {
                            File.WriteAllText(Output + "clip/events.txt", _events.ToString());
                            Finish($"RESULT: done, {_frame} frames at {ClipFps} fps");
                            return;
                        }
                    }
                    else
                    {
                        Capture(Frames[_frame].name, false);
                        if (Frames[_frame].rival >= 0f) SetFlight(false, 0f);
                        _frame++;
                    }
                    _step = 2;
                    break;
            }
        }

        /// <summary>The screen as it stands at clip frame <see cref="_frame"/>, and the sounds
        /// that start on it.</summary>
        private static void StageClipFrame()
        {
            // Which cycle of the sequence this frame falls in, and how far into it.
            int cycle = 0, first = 0, playerReps = 21, rivalReps = 18;
            for (; cycle < Sequence.Length - 1; cycle++)
            {
                int length = Mathf.RoundToInt((Sequence[cycle] > 0 ? CycleSec : RivalCycleSec) * ClipFps);
                if (_frame < first + length) break;
                first += length;
                if (Sequence[cycle] > 0) playerReps += 2; else rivalReps += 2;
            }
            int local = _frame - first, clap = Sequence[cycle];
            float t = local / ClipFps, start = first / ClipFps;
            void Sound(float at, string clip, float volume)
            {
                if (local == Mathf.RoundToInt(at * ClipFps))
                    _events.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.000} {1} {2:0.00}", start + at, clip, volume));
            }

            if (clap < 0)
            {
                // The opponent's turn: their body replays the flight, the hit comes back.
                Sound(RivalConfirmAt, "clap_rival", .6f);
                _hud.SetPlayerReps(playerReps);
                _hud.SetOpponentReps(rivalReps + (t >= RivalRepAt ? 1 : 0) + (t >= RivalRepAt + ClapRepAfter ? 1 : 0));
                float rivalFlight = t >= RivalTakeoffAt ? (t - RivalTakeoffAt) / FlightSec : 0f;
                SetFlight(true, 0f);
                SetFlight(false, rivalFlight < 1f ? rivalFlight : 0f);
                float rival = t - RivalConfirmAt;
                if (rival < 0f || rival > ClapImpactEffect.RivalPlantIn + ClapImpactEffect.RivalDuration) _effect.Cancel();
                else _effect.Preview(1, EconomyConfig.AuraMaxClaps, 0, 0f, -1f, 0f, rival);
                return;
            }

            Sound(RepAt, "rep_0" + ((clap - 1) % 3 + 1), .46f);
            Sound(TakeoffAt, "clap_riser", .5f);
            Sound(ConfirmAt, clap == EconomyConfig.AuraMaxClaps ? "clap_max" : "clap_impact_" + clap, clap == EconomyConfig.AuraMaxClaps ? 1f : .9f);

            _hud.SetOpponentReps(rivalReps);
            _hud.SetPlayerReps(playerReps + (t >= RepAt ? 1 : 0) + (t >= RepAt + ClapRepAfter ? 1 : 0));
            float flight = t >= TakeoffAt ? (t - TakeoffAt) / FlightSec : 0f;
            SetFlight(false, 0f);
            SetFlight(true, flight < 1f ? flight : 0f);
            float age = t - ConfirmAt;
            if (t < TakeoffAt || age > PlantIn + ClapImpactEffect.Duration) _effect.Cancel();
            else _effect.Preview(clap, EconomyConfig.AuraMaxClaps, EconomyConfig.AuraPerClap,
                Mathf.Clamp01((t - TakeoffAt) / .1f), age >= 0f ? age : -1f, PlantIn);
        }

        private static int ClipFrames()
        {
            int frames = 0;
            foreach (int clap in Sequence) frames += Mathf.RoundToInt((clap > 0 ? CycleSec : RivalCycleSec) * ClipFps);
            return frames;
        }

        /// <summary>The armed push-up at the top of a rep, as live play shows it: drivers off, the
        /// pose correction owning the body.</summary>
        private static void ForcePushup()
        {
            foreach (var avatar in Object.FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
            {
                if (avatar.Character == null || !avatar.isActiveAndEnabled) continue;
                var animator = avatar.Character.GetComponentInChildren<Animator>();
                if (animator == null) continue;
                foreach (var behaviour in avatar.GetComponents<MonoBehaviour>())
                    if (behaviour is IAvatarAnimator) behaviour.enabled = false;
                animator.enabled = true;
                animator.speed = 0f;
                if (animator.HasState(0, Animator.StringToHash("PushUp"))) animator.Play("PushUp", 0, .5f);
                PushupPoseCorrection.Bind(animator).SetDepth(0f, true, 0f);
                _report.AppendLine($"{avatar.name}: forced push-up top");
            }
        }

        /// <summary>One fighter's body in the air, palms together — the pose the takeoff beat plays
        /// over. The player's stage is the one driven by the CV avatar driver.</summary>
        private static void SetFlight(bool player, float phase)
        {
            foreach (var avatar in Object.FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
            {
                if (avatar.Character == null || (avatar.GetComponent<PushupAvatarDriver>() != null) != player) continue;
                var animator = avatar.Character.GetComponentInChildren<Animator>();
                if (animator != null) PushupPoseCorrection.Bind(animator).SetFlight(phase, 1f);
            }
        }

        private static void PrepareCapture()
        {
            Scene active = SceneManager.GetActiveScene();
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene == active && c.isRootCanvas && c.enabled).ToArray();
            _texture = new RenderTexture(780, 1688, 24, RenderTextureFormat.ARGB32);
            _texture.Create();
            _camera = new GameObject("ClapCaptureCamera", typeof(Camera)).GetComponent<Camera>();
            _camera.transform.position = new Vector3(0, 0, -50);
            _camera.orthographic = true;
            _camera.orthographicSize = 422;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.cullingMask = 1 << 5;
            _camera.targetTexture = _texture;
            foreach (var canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _camera;
                canvas.planeDistance = 10;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null) scaler.enabled = false;
                canvas.scaleFactor = 2;
                foreach (var rect in canvas.GetComponentsInChildren<RectTransform>(true)) rect.gameObject.layer = 5;
            }
            foreach (var layout in Object.FindObjectsByType<ScreenLayoutRoot>(FindObjectsSortMode.None))
                layout.ShowEditButton = false;
            Canvas.ForceUpdateCanvases();
        }

        private static void Capture(string name, bool jpg)
        {
            foreach (var avatar in Object.FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
                if (avatar.StageCamera != null && avatar.StageCamera.targetTexture != null) avatar.StageCamera.Render();
            Canvas.ForceUpdateCanvases();
            _camera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(_texture.width, _texture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = _texture;
                image.ReadPixels(new Rect(0, 0, _texture.width, _texture.height), 0, 0);
                image.Apply();
                if (jpg) File.WriteAllBytes(Output + name + ".jpg", image.EncodeToJPG(92));
                else
                {
                    File.WriteAllBytes(Output + name + ".png", image.EncodeToPNG());
                    _report.AppendLine("captured " + name);
                }
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
        }

        private static void DisposeCapture()
        {
            if (_camera != null) Object.DestroyImmediate(_camera.gameObject);
            if (_texture != null) { _texture.Release(); Object.DestroyImmediate(_texture); }
            _camera = null; _texture = null;
        }

        private static void Delay(double seconds) => _due = EditorApplication.timeSinceStartup + seconds;

        private static void Finish(string line)
        {
            _report?.AppendLine(line);
            File.WriteAllText(Output + "report.txt", _report?.ToString() ?? line);
            DisposeCapture();
            FightRequest.Clear();
            EditorApplication.update -= Tick;
            // Running stays set until EnteredEditMode, which reopens the caller's scene.
            EditorApplication.isPlaying = false;
        }
    }
}
