using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class TrainingSettingsSceneSetup
    {
        private const string Art = "Assets/_Project/UI/Sprites/TrainingSettings/";
        private const string Modes = "Assets/_Project/UI/Sprites/ModeSelection/";
        [MenuItem("Tools/Push Stars/Main/Add Training Settings")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install outside Play Mode.");
            var scene = SceneManager.GetSceneByPath(AuthoredScenes.MainPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(AuthoredScenes.MainPath, OpenSceneMode.Additive);
            Install(scene); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
        public static void Install(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            if (roots.SelectMany(r => r.GetComponentsInChildren<TrainingSettingsController>(true)).Any()) return;
            var battle = roots.SelectMany(r => r.GetComponentsInChildren<BattleSettingsController>(true)).Single();
            var canvas = battle.GetComponentInParent<Canvas>().rootCanvas;
            foreach (string file in Directory.GetFiles(Art, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(file.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None; importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
            var ctrl = Rect(battle.transform.parent, "TrainingSettingsController").gameObject.AddComponent<TrainingSettingsController>();
            var mirror = canvas.GetComponentInChildren<DeviceSimulatorMirrorFix>(true);
            var overlay = Rect(mirror != null ? mirror.transform : canvas.transform, "TrainingSettingsOverlay"); Stretch(overlay);
            var group = overlay.gameObject.AddComponent<CanvasGroup>();
            var dim = Image(overlay, "TrainingDimmer", null, new Color(0, 0, 0, .65f)); Stretch(dim.rectTransform);
            var backdrop = Button(dim);
            // The shared sheet mask keeps the top handle and straight lower edge; blue art supplies the gradient.
            var silhouette = AssetDatabase.LoadAllAssetsAtPath(Modes + "sheet.png").OfType<Sprite>().First();
            var sheet = Image(overlay, "TrainingSheet", silhouette, Color.white).rectTransform;
            sheet.anchorMin = sheet.anchorMax = sheet.pivot = new Vector2(.5f, 0); sheet.sizeDelta = new Vector2(390, 540);
            sheet.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            sheet.GetComponent<Image>().raycastTarget = true;
            var blue = Image(sheet, "BlueBackground", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/BattleSettings/sheet.png"), Color.white); Stretch(blue.rectTransform);
            var viewport = Rect(sheet, "DumbbellViewport"); Stretch(viewport); viewport.offsetMax = new Vector2(0, -34);
            viewport.gameObject.AddComponent<RectMask2D>();
            var pattern = Rect(viewport, "DumbbellField"); Stretch(pattern);
            pattern.gameObject.AddComponent<CanvasGroup>().alpha = .2f;
            for (int row = 0; row < 6; row++) for (int col = 0; col < 4; col++)
            {
                var image = Image(pattern, $"Dumbbell_{row}_{col}", Sprite("dumbbell-pattern"), Color.white);
                var rt = image.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
                rt.anchoredPosition = new Vector2(-150 + col * 118 + (row % 2) * 59, 255 - row * 102);
                rt.sizeDelta = new Vector2(82, 88); image.preserveAspect = true;
            }
            pattern.gameObject.AddComponent<ModeSkullField>();
            var close = Plate(sheet, "TrainingClose", null, "", 0, 134, 0, 122, 37); close.GetComponent<Image>().color = Color.clear;
            var title = Label(sheet, "TrainingTitle", "TRAINING SETTINGS", 24, 16, 43, 358, 36); title.alignment = TextAlignmentOptions.Center;
            Caption(sheet, "Exercise:", 90);
            Plate(sheet, "TrainingPushup", Sprite("selected"), "PUSHUP", 18, 15, 117, 108, 52);
            var squats = Plate(sheet, "TrainingSquats", Sprite("disabled"), "SQUATS", 18, 141, 117, 108, 52);
            var pullup = Plate(sheet, "TrainingPullup", Sprite("disabled"), "PULLUP", 18, 267, 117, 108, 52);
            foreach (var locked in new[] { squats, pullup })
            {
                locked.interactable = false; var colors = locked.colors; colors.disabledColor = Color.white; locked.colors = colors;
                var icon = Image(locked.transform, "Lock", Sprite("lock"), Color.white); Place(icon.rectTransform, -5, -11, 28, 31);
            }
            Caption(sheet, "Sets:", 181);
            var minus = Plate(sheet, "TrainingMinus", Sprite("option"), "−", 30, 15, 210, 51, 40);
            var count = Label(sheet, "TrainingSetCount", "3", 32, 77, 208, 43, 43); count.alignment = TextAlignmentOptions.Center;
            var plus = Plate(sheet, "TrainingPlus", Sprite("option"), "+", 31, 125, 210, 51, 40);
            Caption(sheet, "Rest time (sec.):", 261);
            var rests = new Button[4];
            string[] names = { "30", "60", "90", "∞" };
            for (int i = 0; i < rests.Length; i++) rests[i] = Plate(sheet, "TrainingRest" + i, Sprite(i == 1 ? "selected" : "option"), names[i], 25, 15 + i * 79, 295, 65, 51);
            var controlOutline = AssetDatabase.LoadAssetAtPath<Material>(Art + "ControlOutline.mat");
            if (controlOutline != null)
            {
                count.fontSharedMaterial = controlOutline;
                foreach (var button in new[] { minus, plus, rests[0], rests[1], rests[2], rests[3] })
                    foreach (var label in button.GetComponentsInChildren<TextMeshProUGUI>(true))
                        label.fontSharedMaterial = controlOutline;
            }
            var summaryBg = Image(sheet, "TrainingSummaryBox", null, new Color(.02f, .23f, .58f, .19f)); Place(summaryBg.rectTransform, 17, 363, 356, 75);
            var summary = Label(summaryBg.transform, "TrainingSummary", "", 15, 15, 13, 246, 47); summary.fontSharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Modes + "ModeBody.mat"); summary.color = new Color32(173, 208, 255, 255);
            var estimate = Label(summaryBg.transform, "TrainingEstimate", "", 15, 266, 27, 79, 28); estimate.fontSharedMaterial = summary.fontSharedMaterial; estimate.color = summary.color;
            var start = Plate(sheet, "TrainingStart", Sprite("selected"), "START", 23, 140, 443, 110, 55);
            var error = Label(sheet, "TrainingError", "", 12, 17, 507, 356, 22); error.alignment = TextAlignmentOptions.Center;
            var so = new SerializedObject(ctrl);
            Set(so, "_overlay", overlay.gameObject); Set(so, "_sheet", sheet); Set(so, "_group", group);
            Set(so, "_backdrop", backdrop); Set(so, "_close", close); Set(so, "_minus", minus); Set(so, "_plus", plus); Set(so, "_start", start);
            Set(so, "_setCount", count); Set(so, "_summary", summary); Set(so, "_estimate", estimate); Set(so, "_error", error);
            Set(so, "_selectedSprite", Sprite("selected")); Set(so, "_optionSprite", Sprite("option"));
            var array = so.FindProperty("_restButtons"); array.arraySize = 4; for (int i = 0; i < 4; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = rests[i]; so.ApplyModifiedPropertiesWithoutUndo();
            var battleSo = new SerializedObject(battle); Set(battleSo, "_trainingSettings", ctrl); battleSo.ApplyModifiedPropertiesWithoutUndo();
            overlay.gameObject.SetActive(false);
        }
        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        private static RectTransform Rect(Transform parent, string name)
        { var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false); rt.gameObject.layer = 5; return rt; }
        private static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rt, float x, float y, float w, float h) { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); }
        private static Image Image(Transform parent, string name, Sprite sprite, Color color) { var image = Rect(parent, name).gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false; return image; }
        private static Button Button(Image image) { image.raycastTarget = true; var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.navigation = new Navigation { mode = Navigation.Mode.None }; return button; }
        private static Button Plate(Transform parent, string name, Sprite sprite, string text, float size, float x, float y, float w, float h)
        { var image = Image(parent, name, sprite, Color.white); Place(image.rectTransform, x, y, w, h); var button = Button(image); if (text.Length > 0) { var label = Label(image.transform, name + "Label", text, size, 0, -2, w, h); label.alignment = TextAlignmentOptions.Center; } return button; }
        private static TextMeshProUGUI Label(Transform parent, string name, string text, float size, float x, float y, float w, float h)
        { var label = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>(); Place(label.rectTransform, x, y, w, h); label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.BoldAsset); label.fontSharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Modes + "ModeOutline.mat"); label.text = text; label.fontSize = size; label.fontStyle = FontStyles.Normal; label.color = Color.white; label.alignment = TextAlignmentOptions.Left; label.enableWordWrapping = false; label.raycastTarget = false; return label; }
        private static void Caption(Transform parent, string text, float y)
        { var label = Label(parent, text, text, 16, 19, y, 345, 23); label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.RegularAsset); label.color = new Color32(128, 180, 255, 255); label.fontSharedMaterial = label.font.material; }
        private static void Set(SerializedObject so, string name, Object value) => so.FindProperty(name).objectReferenceValue = value;
    }
}
