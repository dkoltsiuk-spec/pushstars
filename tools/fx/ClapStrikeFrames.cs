using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.Fight;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Renders the boss battle screen (edit-mode preview scene, real avatars) at several
/// moments of the clap strike → output/clap-strike/*.png. Run via `unity command run_script`.</summary>
public static class ClapStrikeFrames
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public static string Run()
    {
        const string output = "output/clap-strike/";
        Directory.CreateDirectory(output);
        FightRequest.Boss();
        var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Fight.unity");
        var previous = RenderTexture.active;
        var textures = new System.Collections.Generic.List<RenderTexture>();
        try
        {
            var c = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossCombatScreen>(true)).Single();
            var canvas = c.GetComponent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>(); if (scaler) scaler.enabled = false;
            canvas.renderMode = RenderMode.WorldSpace; canvas.scaleFactor = 1;
            var rect = (RectTransform)canvas.transform; rect.position = Vector3.zero; rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity; rect.sizeDelta = new Vector2(390, 844);
            foreach (var avatar in new[] { c.PlayerStage, c.BossStage })
            {
                typeof(FightAvatar).GetMethod("Build", Private).Invoke(avatar, null);
                var animator = avatar.Character.GetComponentInChildren<Animator>();
                string pose = avatar == c.BossStage ? "StandIdle" : "PushUp";
                if (animator.HasState(0, Animator.StringToHash(pose))) { animator.Play(pose, 0, .12f); animator.Update(0); }
                var stage = avatar.StageCamera; stage.scene = scene;
                var rt = new RenderTexture(720, 1024, 24); textures.Add(rt); stage.targetTexture = rt; stage.ResetAspect();
                typeof(FightAvatar).GetMethod("FrameCharacter", Private).Invoke(avatar, null);
                stage.Render();
            }
            c.Configure(true); Canvas.ForceUpdateCanvases(); c.Refresh(1);
            var camera = new GameObject("ClapFramesCamera").AddComponent<Camera>(); SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.scene = scene; camera.transform.position = new Vector3(0, 0, -50); camera.orthographic = true;
            camera.orthographicSize = 422; camera.cullingMask = ~0; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black; canvas.worldCamera = camera;
            var target = new RenderTexture(780, 1688, 24); textures.Add(target); camera.targetTexture = target;

            var health = (BossCombatState)typeof(BossCombatScreen).GetField("_health", Private).GetValue(c);
            health.PlayerRep(100);
            health.PlayerClapStrike(100);
            var slash = (ClawSlashGraphic)typeof(BossCombatScreen).GetField("_clawSlash", Private).GetValue(c);
            if (slash == null) return "no slash built";
            var started = typeof(ClawSlashGraphic).GetField("_startedAt", Private);
            var clapAt = typeof(BossCombatScreen).GetField("_clapAt", Private);
            string log = "";
            foreach (float age in new[] { .03f, .06f, .1f, .15f, .21f, .28f, .4f, .55f, .7f, .9f })
            {
                float at = Time.unscaledTime - age;
                started.SetValue(slash, at); clapAt.SetValue(c, at);
                c.Refresh(0); slash.SetVerticesDirty();
                Canvas.ForceUpdateCanvases(); c.BossStage.StageCamera.Render(); camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                File.WriteAllBytes(output + $"t{Mathf.RoundToInt(age * 1000):000}.png", image.EncodeToPNG());
                Object.DestroyImmediate(image);
                log += $"{age:0.00} ";
            }
            return "frames: " + log + " bossHp=" + health.BossHp;
        }
        finally
        {
            RenderTexture.active = previous; EditorSceneManager.ClosePreviewScene(scene);
            foreach (var t in textures) { t.Release(); Object.DestroyImmediate(t); }
            FightRequest.Clear();
        }
    }
}
