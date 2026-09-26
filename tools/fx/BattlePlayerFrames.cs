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

/// <summary>Renders the live boss battle with the player's body in the ARMED push-up path (pose
/// correction owns the body, FightAvatar.LateUpdate frames it) at several rep depths →
/// output/battle-player/*.png. Run via `unity command run_script`.</summary>
public static class BattlePlayerFrames
{
    public static float[] Order = { 0f, .5f, 1f };
    public static string Reverse(string tag, float e) { Order = new[] { 1f, .5f, 0f }; try { return Run(tag, e); } finally { Order = new[] { 0f, .5f, 1f }; } }
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static string SkinHand(GameObject character, Camera cam)
    {
        string r = "";
        foreach (var skin in character.GetComponentsInChildren<SkinnedMeshRenderer>())
            foreach (var b in skin.bones)
                if (b != null && (b.name.ToLower().Contains("hand") || b.name.ToLower().Contains("wrist")) && b.name.ToLower().Contains("l") && r.Length < 400)
                { r += skin.name + ":" + b.name + cam.WorldToViewportPoint(b.position) + " "; }
        return r;
    }

    public static string Run(string tag, float elevation = -1f, float shadowAlpha = -1f, float fov = -1f, float handsOut = -1f)
    {
        const string output = "output/battle-player/";
        if (elevation > 0f) typeof(FightAvatar).GetField("_pushupElevation", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, elevation);
        if (handsOut > 0f) typeof(PushupPoseCorrection).GetField("_handsOut", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, handsOut);
        if (fov > 0f) typeof(FightAvatar).GetField("_pushupFieldOfView", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, fov);
        if (shadowAlpha > 0f) typeof(FightAvatar).GetField("ShadowColor", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, new Color(.02f, .03f, .06f, shadowAlpha));
        Directory.CreateDirectory(output);
        FightRequest.Boss();
        var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Fight.unity");
        var previous = RenderTexture.active;
        var textures = new System.Collections.Generic.List<RenderTexture>();
        string log = "";
        try
        {
            var c = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossCombatScreen>(true)).Single();
            var canvas = c.GetComponent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>(); if (scaler) scaler.enabled = false;
            canvas.renderMode = RenderMode.WorldSpace; canvas.scaleFactor = 1;
            var rect = (RectTransform)canvas.transform; rect.position = Vector3.zero; rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity; rect.sizeDelta = new Vector2(390, 844);
            PushupPoseCorrection correction = null;
            foreach (var avatar in new[] { c.PlayerStage, c.BossStage })
            {
                typeof(FightAvatar).GetMethod("Build", Private).Invoke(avatar, null);
                var animator = avatar.Character.GetComponentInChildren<Animator>();
                var stage = avatar.StageCamera; stage.scene = scene;
                var rt = new RenderTexture(720, 1280, 24); textures.Add(rt); stage.targetTexture = rt;
                if (avatar == c.BossStage)
                {
                    if (animator.HasState(0, Animator.StringToHash("StandIdle"))) { animator.Play("StandIdle", 0, .12f); animator.Update(0); }
                    stage.ResetAspect();
                    typeof(FightAvatar).GetMethod("FrameCharacter", Private).Invoke(avatar, null);
                    continue;
                }
                if (animator.HasState(0, Animator.StringToHash("PushUp"))) { animator.Play("PushUp", 0, .12f); animator.Update(0); }
                correction = PushupPoseCorrection.Bind(animator);
                log += $"silhouetteBefore={(typeof(FightAvatar).GetField("_pushupSilhouette", Private).GetValue(avatar) as Vector3[])?.Length ?? -1} ";
                typeof(FightAvatar).GetMethod("CachePushupSilhouette", Private).Invoke(avatar, null);
                log += $"silhouetteAfter={(typeof(FightAvatar).GetField("_pushupSilhouette", Private).GetValue(avatar) as Vector3[])?.Length ?? -1} ";
                var mirror = avatar.GetComponent<PoseMirrorRetargeter>();
                if (mirror != null) typeof(PoseMirrorRetargeter).GetMethod("SetPhase", Private).Invoke(mirror, new object[] { true, Time.unscaledTime });
                log += $"mirrorPhase={(mirror != null ? mirror.MirrorPhase.ToString() : "none")} ";
            }
            c.Configure(true); Canvas.ForceUpdateCanvases(); c.Refresh(1);
            log += $"camAspect={c.PlayerStage.StageCamera.aspect:0.000} ";
            var camera = new GameObject("BattleFramesCamera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.scene = scene; camera.transform.position = new Vector3(0, 0, -50); camera.orthographic = true;
            camera.orthographicSize = 422; camera.cullingMask = ~0; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; canvas.worldCamera = camera;
            var target = new RenderTexture(780, 1688, 24); textures.Add(target); camera.targetTexture = target;
            var late = typeof(FightAvatar).GetMethod("LateUpdate", Private);
            // Live, the Animator evaluates before the correction every frame. Here nothing ticks
            // it, and an enabled Animator can write its clip pose during an edit-mode render —
            // pose the bones with the correction alone.
            c.PlayerStage.Character.GetComponentInChildren<Animator>().enabled = false;
            // Skinning normally runs in the player loop, which a synchronous editor script never
            // reaches: without this every render shows a stale pose from the last editor tick.
            foreach (var skin in c.PlayerStage.Character.GetComponentsInChildren<SkinnedMeshRenderer>())
                skin.forceMatrixRecalculationPerRender = true;
            foreach (float depth in Order)
            {
                correction.SetDepth(depth, true, 0f);
                correction.Apply(depth);
                c.Refresh(0);
                late.Invoke(c.PlayerStage, null);
                correction.Apply(depth);
                c.Refresh(0);
                Canvas.ForceUpdateCanvases();
                // Edit-mode skinning lags one render behind the bones: render once to flush.
                c.PlayerStage.StageCamera.Render();
                c.PlayerStage.StageCamera.Render(); c.BossStage.StageCamera.Render(); camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                File.WriteAllBytes(output + $"{tag}-d{depth:0.0}.png", image.EncodeToPNG());
                var an = c.PlayerStage.Character.GetComponentInChildren<Animator>();
                var cam = c.PlayerStage.StageCamera;
                var hand = an.GetBoneTransform(HumanBodyBones.LeftHand).position;
                var toe = an.GetBoneTransform(HumanBodyBones.LeftToes).position;
                log += $" || pushupFramed={typeof(FightAvatar).GetField("_pushupFramed", Private).GetValue(c.PlayerStage)} d{depth:0.0} cam={cam.transform.position} root={an.transform.position} hand={hand} handVp={cam.WorldToViewportPoint(hand)} toeVp={cam.WorldToViewportPoint(toe)} animators={c.PlayerStage.Character.GetComponentsInChildren<Animator>(true).Length} skinHand={SkinHand(c.PlayerStage.Character, cam)}";
                Object.DestroyImmediate(image);
            }
            log += $"camAspectAfter={c.PlayerStage.StageCamera.aspect:0.000} uv={c.PlayerPortrait.uvRect}";
            return log;
        }
        finally
        {
            RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(scene);
            foreach (var t in textures) { t.Release(); Object.DestroyImmediate(t); }
            FightRequest.Clear();
        }
    }
}
