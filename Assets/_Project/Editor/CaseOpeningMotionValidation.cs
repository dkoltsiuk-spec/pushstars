using System;
using System.IO;
using System.Linq;
using System.Text;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class CaseOpeningMotionValidation
    {
        [MenuItem("Tools/Push Stars/Rewards/Validate Case Motion")]
        public static void Run()
        {
            Directory.CreateDirectory("output/case-motion");
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/CaseOpening.unity");
            RenderTexture target = null;
            try
            {
                var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single();
                var motion = screen.GetComponent<CaseOpeningMotion>();
                Require(motion != null && motion.Behind != null && motion.InFront != null, "Scene rig not serialized.");
                var ui = screen.CaseUi;
                Require(ui.Artwork.GetComponents<BaseMeshEffect>().Length == 0, "Case must remain an intact image.");
                Require(!motion.Behind.raycastTarget && !motion.InFront.raycastTarget && !ui.Artwork.raycastTarget, "Effect intercepts input.");
                Require(motion.Behind.transform.GetSiblingIndex() < ui.Content.GetSiblingIndex() && motion.InFront.transform.GetSiblingIndex() > ui.Content.GetSiblingIndex(), "Orbit depth ordering invalid.");
                var canvas = ui.Content.GetComponentInParent<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled = false;
                canvas.renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)canvas.transform;
                rect.position = Vector3.zero; rect.localScale = Vector3.one; rect.sizeDelta = new Vector2(390, 844);
                var camera = new GameObject("CaseMotionPreviewCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.transform.position = new Vector3(0, 0, -50);
                camera.orthographic = true; camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.045f, .03f, .13f);
                target = new RenderTexture(780, 1688, 24); camera.targetTexture = target; canvas.worldCamera = camera;
                Canvas.ForceUpdateCanvases();
                motion.Initialize();
                Vector2 position = ui.Content.anchoredPosition;
                Vector3 scale = ui.Content.localScale;
                Quaternion rotation = ui.Content.localRotation;
                var random = UnityEngine.Random.state;
                ui.Hint.text = "Tap to upgrade · 3 chances left";
                ui.Status.text = "";
                var report = new StringBuilder();
                for (int rarity = 0; rarity < 4; rarity++)
                {
                    ApplyRarity(screen, rarity);
                    Sprite intactArtwork = ui.Artwork.sprite;
                    motion.Idle(1.8f); Capture(camera, target, "rarity-" + rarity);
                    motion.SampleTap(.08f, true, 1);
                    Require(ui.Content.localScale.x > scale.x && ui.Content.localScale.y < scale.y, "No anticipation squash.");
                    motion.SampleTap(.35f, true, 1);
                    Require(ui.Content.anchoredPosition.y > position.y + 10, "No jump.");
                    Capture(camera, target, "rarity-" + rarity + "-upgrade");
                    motion.SampleReveal(.64f);
                    Require(ui.Content.localScale.y > scale.y * 1.10f && ui.Artwork.sprite == intactArtwork, "Final stretch must preserve the whole case artwork.");
                    Capture(camera, target, "rarity-" + rarity + "-stretch");
                    motion.ResetPose();
                    Require(ui.Content.anchoredPosition == position && ui.Content.localScale == scale && ui.Content.localRotation == rotation, "Interrupted motion changed authored pose.");
                    Require(motion.InFront.Flash == 0 && motion.Behind.Charge == 0, "Interrupted effects did not clear.");
                    report.AppendLine("PASS rarity " + rarity + ": squash, jump, intact artwork, final stretch, reset, rendered frames.");
                }
                Require(JsonUtility.ToJson(random) == JsonUtility.ToJson(UnityEngine.Random.state), "Animation consumed reward randomness.");
                ApplyRarity(screen, 1);
                for (int i = 0; i <= 48; i++)
                {
                    motion.SampleTap(i / 60f, true, 1);
                    if (i == 17) ApplyRarity(screen, 2);
                    Capture(camera, target, "tap-" + i.ToString("D3"));
                }
                motion.ResetPose(); ui.Hint.text = "Tap to open case";
                for (int i = 0; i <= 54; i++)
                {
                    motion.SampleReveal(i / 60f);
                    Capture(camera, target, "open-" + i.ToString("D3"));
                }
                motion.ResetPose();
                rect.sizeDelta = new Vector2(320, 568); camera.orthographicSize = 284;
                target.Release(); target.width = 640; target.height = 1136; target.Create();
                Canvas.ForceUpdateCanvases(); motion.SampleReveal(.64f); Capture(camera, target, "small-phone-stretch");
                report.AppendLine("PASS: raycasts, front/back ordering, deterministic random state, 60fps timeline captures, small phone render.");
                File.WriteAllText("output/case-motion/validation.txt", report.ToString());
                Debug.Log("[CaseOpeningMotion] PASS");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }
        private static void ApplyRarity(RewardScreen screen, int rarity)
        {
            screen.GetComponent<CaseRarityPresentation>().Apply(rarity);
            var ui = screen.CaseUi;
            ui.Artwork.sprite = ui.RarityArtwork[rarity];
            ui.Rarity.text = new[] { "COMMON", "RARE", "EPIC", "LEGENDARY" }[rarity];
            ui.Rarity.color = RewardScreen.RarityColor((PushStars.Core.CaseRarity)rarity);
            for (int i = 0; i < ui.RarityStarGroups.Length; i++) ui.RarityStarGroups[i].SetActive(i == rarity);
        }
        private static void Capture(Camera camera, RenderTexture target, string name)
        {
            Canvas.ForceUpdateCanvases(); camera.Render();
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                File.WriteAllBytes("output/case-motion/" + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
