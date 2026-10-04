using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Fight;
using PushStars.Core;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class MatchFoundImpactValidation
    {
        [MenuItem("Tools/Push Stars/Validate Match Found Impact")]
        public static void Run() => RunInternal(false);
        public static void ExportPreview() => RunInternal(true);
        private static void RunInternal(bool export)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/FightPreparation.unity");
            RenderTexture texture = null;
            try
            {
                Directory.CreateDirectory("output/match-found");
                var panel = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DuelReadyPanel>(true)).Single();
                var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled = false;
                canvas.renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)canvas.transform;
                rect.position = Vector3.zero;
                rect.localScale = Vector3.one;
                rect.sizeDelta = new Vector2(390, 844);
                var camera = new GameObject("ImpactPreviewCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene;
                camera.transform.position = new Vector3(0, 0, -50);
                camera.orthographic = true;
                camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                texture = new RenderTexture(780, 1688, 24);
                camera.targetTexture = texture;
                canvas.worldCamera = camera;
                panel.Show(new DuelReadyPanel.Side("BEASTCORE_DEV", 120, 32, 52),
                    new DuelReadyPanel.Side("OSKAT009", 98, 32, 52), null, null, false);
                var backdrop = panel.Root.GetComponentInChildren<PreparationArenaBackdrop>(true);
                Require(backdrop != null, "Per-player arena background missing");
                backdrop.SetMaps("jungle", "lava-forge");
                Require(!backdrop.GetComponent<ReadyScreenGraphic>().enabled, "Legacy background still enabled");
                Require(backdrop.transform.Cast<Transform>().Where(t=>t.name!="PlayerArena" && t.name!="OpponentArena" && t.name!="TeamDivider").All(t=>!t.gameObject.activeSelf), "Flying background decoration remains enabled");
                Require(backdrop.transform.Find("PlayerArena").GetComponent<RawImage>().texture == ArenaCatalog.Get("jungle").Home.texture
                    && backdrop.transform.Find("OpponentArena").GetComponent<RawImage>().texture == ArenaCatalog.Get("lava-forge").Home.texture, "Players do not have their own maps");
                Canvas.ForceUpdateCanvases();
                var effect = panel.Root.AddComponent<MatchFoundImpact>();
                // Show attaches this field only in Play Mode; bind it for the isolated preview.
                typeof(DuelReadyPanel).GetField("_matchFoundImpact", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(panel, effect);
                var origin = effect.transform.localPosition;
                effect.Play();
                var crown = effect.GetComponentsInChildren<Image>(true).Single(i => i.name == "FallingVsCrown");
                var crack = effect.GetComponentsInChildren<Image>(true).Single(i => i.name == "ImpactCrack");
                var sample = typeof(MatchFoundImpact).GetMethod("ApplyFrame", BindingFlags.NonPublic | BindingFlags.Instance);
                var numbers=effect.GetComponentsInChildren<TextMeshProUGUI>(true).Where(t=>new[]{"OpponentTrophies","OpponentBest","OpponentWinRate","PlayerTrophies","PlayerBest","PlayerWinRate"}.Contains(t.name)).ToArray();
                Require(numbers.Length==6 && numbers.All(t=>t.color.a==0), "Stats must start hidden");
                foreach (var frame in new[] { ("numbers", .38f), ("before-vs", .98f), ("falling", 1.24f), ("impact", 1.39f), ("settled", 1.70f) })
                {
                    sample.Invoke(effect, new object[] { frame.Item2 });
                    Capture(camera, texture, frame.Item1);
                    if (frame.Item1 == "numbers")
                        Require(crown.color.a==0 && numbers.Any(t=>t.color.a>0), "Numbers must precede VS");
                    if (frame.Item1 == "before-vs")
                        Require(crown.color.a==0 && numbers.Single(t=>t.name=="PlayerTrophies").text=="120" && numbers.All(t=>t.color.a==1), "Stats must complete before VS");
                    if (frame.Item1 == "falling")
                        Require(crown.rectTransform.anchoredPosition.y > 100 && crack.color.a == 0, "Crack visible before impact");
                    if (frame.Item1 == "settled")
                        Require(crown.rectTransform.anchoredPosition == Vector2.zero && crown.rectTransform.localScale == Vector3.one && crack.color.a == 1, "Entrance did not settle");
                }
                if (export)
                {
                    Directory.CreateDirectory("output/match-found/frames");
                    for(int i=0;i<60;i++)
                    {
                        sample.Invoke(effect,new object[]{i/24f});
                        Capture(camera,texture,"frames/frame-"+i.ToString("D3"));
                    }
                }
                // Interrupt mid-shake, then replay: no stranded screen offset or duplicate art.
                sample.Invoke(effect, new object[] { 1.39f });
                panel.Hide();
                Require(effect.transform.localPosition == origin, "Closing left screen displaced");
                Require(numbers.All(t=>t.color.a==1) && numbers.Single(t=>t.name=="PlayerTrophies").text=="120", "Cancel did not restore stats");
                panel.Root.SetActive(true);
                effect.Play();
                Require(effect.GetComponentsInChildren<Image>(true).Count(i => i.name == "FallingVsCrown") == 1, "Replay duplicated artwork");
                Require(crack.color.a == 0, "Replay retained old crack");
                typeof(DuelReadyPanel).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(panel, null);
                Require(effect.transform.localPosition == origin && !crown.gameObject.activeSelf, "Disable did not reset entrance");
                File.WriteAllText("output/match-found/validation.txt", "PASS: individual arena backgrounds, no flying lightning, numbers before delayed VS, exact final statistics, falling/impact/settled frames, interruption reset, replay without duplicate art, disable cleanup. Device haptics require iOS/Android verification.\n");
                Debug.Log("[MatchFoundImpact] Validation PASS");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
            }
        }

        private static void Capture(Camera camera, RenderTexture texture, string name)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                image.Apply();
                File.WriteAllBytes("output/match-found/" + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
