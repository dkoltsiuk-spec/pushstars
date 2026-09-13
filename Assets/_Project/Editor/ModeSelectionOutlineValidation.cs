using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Render the real selection material against the cards' black borders.</summary>
    [InitializeOnLoad]
    internal static class ModeSelectionOutlineValidation
    {
        private const string Sentinel = "Temp/validate-mode-outline";
        private const string Output = "Logs/mode-outline";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        static ModeSelectionOutlineValidation() => EditorApplication.delayCall += RunWhenArmed;

        private static void RunWhenArmed()
        {
            if (!File.Exists(Sentinel)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunWhenArmed;
                return;
            }
            File.Delete(Sentinel);
            Run();
        }

        [MenuItem("Tools/Push Stars/Validate Mode Selection Outline")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            var previousTarget = RenderTexture.active;
            try
            {
                var shader = Resources.Load<Shader>("ModeSelectionOutline");
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                    throw new Exception("Selection shader missing or has compile errors.");
                var root = new GameObject("Outline validation");
                SceneManager.MoveGameObjectToScene(root, scene);
                var camera = new GameObject("Camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.scene = scene;
                camera.transform.localPosition = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 250;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(255, 157, 0, 255);
                target = new RenderTexture(800, 1000, 24);
                camera.targetTexture = target;
                var canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
                canvas.transform.SetParent(root.transform, false);
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(400, 500);
                string[] names = { "pvp", "boss", "training" };
                var cards = new Button[3];
                for (int i = 0; i < 3; i++)
                {
                    var image = new GameObject(names[i], typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
                    image.transform.SetParent(canvas.transform, false);
                    image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                        "Assets/_Project/UI/Sprites/ModeSelection/" + names[i] + "-card.png");
                    image.rectTransform.sizeDelta = new Vector2(362, i == 0 ? 141 : 98);
                    image.rectTransform.anchoredPosition = new Vector2(0, 150 - i * 150);
                    cards[i] = image.gameObject.AddComponent<Button>();
                    cards[i].targetGraphic = image;
                }
                var controller = root.AddComponent<ModeSelectionController>();
                controller.enabled = false; // Invoke only selection rendering, not the live menu flow.
                typeof(ModeSelectionController).GetField("_cards", Private).SetValue(controller, cards);
                Canvas.ForceUpdateCanvases();
                typeof(ModeSelectionController).GetMethod("RefreshSelectionOutline", Private).Invoke(controller, null);
                var outlines = (Image[])typeof(ModeSelectionController).GetField("_selectionOutlines", Private).GetValue(controller);
                if (outlines.Count(o => o.enabled) != 1 || outlines.Any(o => o.raycastTarget))
                    throw new Exception("Expected exactly one highlight with raycasts disabled.");
                foreach (var outline in outlines) outline.enabled = false;
                var baseline = Capture(camera);
                var report = "Shader compiled; one selected outline; raycasts disabled.\n";
                for (int i = 0; i < outlines.Length; i++)
                {
                    outlines[i].enabled = true;
                    outlines[i].color = new Color(1, 1, .42f, 1);
                    var bright = Capture(camera);
                    int yellow = bright.Where((c, p) => c.r > 200 && c.g > 200 && c.b < 180
                        && baseline[p].r < 80 && baseline[p].g < 80).Count();
                    if (yellow < 500) throw new Exception(names[i] + ": missing yellow border on black artwork (" + yellow + ").");
                    Save(bright, names[i] + "-selected.png");
                    outlines[i].color = new Color(1, .82f, 0, .9f);
                    var dim = Capture(camera);
                    if (!bright.Where((c, p) => c.g > dim[p].g + 10).Any())
                        throw new Exception("Pulse brightness did not change.");
                    report += names[i] + ": " + yellow + " black border pixels became yellow; pulse visible.\n";
                    outlines[i].enabled = false;
                }
                File.WriteAllText(Output + "/validation.txt", "PASS\n" + report);
                Debug.Log("[ModeOutlineValidation] PASS\n" + report);
            }
            catch (Exception e)
            {
                File.WriteAllText(Output + "/validation.txt", "FAIL\n" + e);
                Debug.LogException(e);
            }
            finally
            {
                RenderTexture.active = previousTarget;
                EditorSceneManager.ClosePreviewScene(scene);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        private static Color32[] Capture(Camera camera)
        {
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = camera.targetTexture;
            var image = new Texture2D(800, 1000, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, 800, 1000), 0, 0);
            image.Apply();
            var pixels = image.GetPixels32();
            Object.DestroyImmediate(image);
            return pixels;
        }

        private static void Save(Color32[] pixels, string name)
        {
            var image = new Texture2D(800, 1000, TextureFormat.RGBA32, false);
            image.SetPixels32(pixels); image.Apply();
            File.WriteAllBytes(Output + "/" + name, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
    }
}
