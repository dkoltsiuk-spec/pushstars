using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class BattleSettingsSceneSetup
    {
        private const string Art = "Assets/_Project/UI/Sprites/BattleSettings/";
        private const string Modes = "Assets/_Project/UI/Sprites/ModeSelection/";

        [MenuItem("Tools/Push Stars/Main/Add Battle Settings")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install outside Play Mode.");
            var scene = SceneManager.GetSceneByPath(AuthoredScenes.MainPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(AuthoredScenes.MainPath, OpenSceneMode.Additive);
            Install(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }

        public static void Install(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            if (roots.SelectMany(r => r.GetComponentsInChildren<BattleSettingsController>(true)).Any()) return;
            var open = roots.SelectMany(r => r.GetComponentsInChildren<Button>(true)).Single(b => b.name == "PushupButton");
            var canvas = open.GetComponentInParent<Canvas>().rootCanvas;
            foreach (string file in Directory.GetFiles(Art, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(file.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var controller = Rect(open.transform.parent.parent, "BattleSettingsController").gameObject.AddComponent<BattleSettingsController>();
            for (int i = open.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                if (open.onClick.GetPersistentTarget(i) is Toast) UnityEventTools.RemovePersistentListener(open.onClick, i);
            var mirror = canvas.GetComponentInChildren<DeviceSimulatorMirrorFix>(true);
            var overlay = Rect(mirror != null ? mirror.transform : canvas.transform, "BattleSettingsOverlay");
            Stretch(overlay);
            var group = overlay.gameObject.AddComponent<CanvasGroup>();
            var dim = Image(overlay, "SettingsDimmer", null, new Color(0, 0, 0, .65f));
            Stretch(dim.rectTransform);
            var backdrop = Button(dim);
            var sheet = Image(overlay, "SettingsSheet", Sprite("sheet"), Color.white).rectTransform;
            sheet.GetComponent<Image>().raycastTarget = true;
            sheet.anchorMin = sheet.anchorMax = sheet.pivot = new Vector2(.5f, 0);
            sheet.sizeDelta = new Vector2(390, 540);
            // Keep decor behind all content and within the blue sheet's silhouette.
            var mask = Image(sheet, "SettingsSkullMask", Sprite("sheet"), Color.white);
            Stretch(mask.rectTransform);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var viewport = Rect(mask.transform, "SettingsSkullViewport");
            Stretch(viewport);
            viewport.offsetMax = new Vector2(0, -34);
            viewport.gameObject.AddComponent<RectMask2D>();
            var field = Rect(viewport, "SettingsSkullField");
            Stretch(field);
            // Additional opacity belongs to a group: the shared drifting component animates image alpha.
            field.gameObject.AddComponent<CanvasGroup>().alpha = .65f;
            var skullSprite = AssetDatabase.LoadAllAssetsAtPath(Modes + "skull-pattern.png").OfType<Sprite>().First();
            for (int row = 0; row < 6; row++)
            for (int col = 0; col < 4; col++)
            {
                var skull = Image(field, $"SettingsSkull_{row}_{col}", skullSprite, Color.white);
                var rt = skull.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
                rt.anchoredPosition = new Vector2(-150 + col * 118 + (row % 2) * 59, 255 - row * 102);
                rt.sizeDelta = new Vector2(82, 88);
                skull.preserveAspect = true;
            }
            field.gameObject.AddComponent<ModeSkullField>();
            var handle = Image(sheet, "SettingsClose", null, Color.clear);
            Place(handle.rectTransform, 134, 0, 122, 38);
            var close = Button(handle);
            var title = Label(sheet, "SettingsTitle", "BOSS SETTINGS", 25, 20, 45, 350, 36);
            title.alignment = TextAlignmentOptions.Center;
            title.enableAutoSizing = true; title.fontSizeMin = 20; title.fontSizeMax = 25;
            string[] names = { "PushupExercise", "SquatsLocked", "PullupLocked" };
            string[] art = { "pushup", "squats-locked", "pullup-locked" };
            string[] labels = { "PUSHUP", "SQUATS", "PULLUP" };
            float[] tops = { 92, 210, 340 };
            float[] heights = { 115, 126, 126 };
            var cards = new RectTransform[3];
            var groups = new CanvasGroup[3];
            Button pushup = null;
            for (int i = 0; i < cards.Length; i++)
            {
                var card = Image(sheet, names[i], Sprite(art[i]), Color.white);
                Place(card.rectTransform, 15, tops[i], 360, heights[i]);
                var button = Button(card);
                if (i == 0) pushup = button;
                else
                {
                    // The supplied grey art and padlocks already express the disabled state.
                    button.interactable = false;
                    var colors = button.colors; colors.disabledColor = Color.white; button.colors = colors;
                }
                cards[i] = card.rectTransform;
                groups[i] = card.gameObject.AddComponent<CanvasGroup>();
                Label(card.transform, names[i] + "Label", labels[i], 32, 28, i == 0 ? 12 : 23, 245, 48);
            }
            var so = new SerializedObject(controller);
            Set(so, "_openButton", open); Set(so, "_overlay", overlay.gameObject); Set(so, "_sheet", sheet);
            Set(so, "_group", group); Set(so, "_backdrop", backdrop); Set(so, "_close", close);
            Set(so, "_pushup", pushup); Set(so, "_title", title);
            Set(so, "_homeLabel", open.GetComponentInChildren<TextMeshProUGUI>(true));
            Array(so, "_cards", cards); Array(so, "_cardGroups", groups);
            so.ApplyModifiedPropertiesWithoutUndo();
            overlay.gameObject.SetActive(false);
        }

        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        private static RectTransform Rect(Transform parent, string name)
        { var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false); rt.gameObject.layer = 5; return rt; }
        private static void Stretch(RectTransform rt)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rt, float x, float top, float width, float height)
        { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -top); rt.sizeDelta = new Vector2(width, height); }
        private static Image Image(Transform parent, string name, Sprite sprite, Color color)
        { var image = Rect(parent, name).gameObject.AddComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false; return image; }
        private static Button Button(Image image)
        {
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }
        private static TextMeshProUGUI Label(Transform parent, string name, string value, float size, float x, float top, float w, float h)
        {
            var label = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            Place(label.rectTransform, x, top, w, h);
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.BoldAsset);
            label.fontSharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Modes + "ModeOutline.mat");
            label.fontStyle = FontStyles.Normal; label.fontSize = size; label.text = value; label.color = Color.white;
            label.alignment = TextAlignmentOptions.Left; label.enableWordWrapping = false; label.raycastTarget = false;
            return label;
        }
        private static void Set(SerializedObject so, string name, Object value) => so.FindProperty(name).objectReferenceValue = value;
        private static void Array(SerializedObject so, string name, Object[] values)
        { var p = so.FindProperty(name); p.arraySize = values.Length; for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
    }
}
