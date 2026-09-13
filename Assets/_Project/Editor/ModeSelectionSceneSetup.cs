using System;
using System.IO;
using System.Linq;
using PushStars.Core;
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
    /// <summary>Adds only the selector to the authored Main; never rebuilds the home screen.</summary>
    public static class ModeSelectionSceneSetup
    {
        private const string Art = "Assets/_Project/UI/Sprites/ModeSelection/";
        private static TMP_FontAsset _font;
        private static Material _outline, _body;
        private static readonly Color Gold = new Color32(255, 221, 0, 255);

        [MenuItem("Tools/Push Stars/Main/Add Mode Selector")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Install the mode selector outside Play Mode.");
            var scene = SceneManager.GetSceneByPath(AuthoredScenes.MainPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(AuthoredScenes.MainPath, OpenSceneMode.Additive);
            Install(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
            Debug.Log("[ModeSelection] Authored selector installed in Main.");
        }

        [MenuItem("Tools/Push Stars/Main/Rebuild Mode Selector")]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Rebuild outside Play Mode.");
            var scene = SceneManager.GetSceneByPath(AuthoredScenes.MainPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(AuthoredScenes.MainPath, OpenSceneMode.Additive);
            foreach (var controller in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ModeSelectionController>(true)))
            {
                var overlay = new SerializedObject(controller).FindProperty("_overlay").objectReferenceValue;
                if (overlay != null) Object.DestroyImmediate(overlay);
                Object.DestroyImmediate(controller.gameObject);
            }
            Install(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }

        public static void Install(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            if (roots.SelectMany(r => r.GetComponentsInChildren<ModeSelectionController>(true)).Any())
            {
                TrainingHomeActionsSetup.Install(scene);
                return;
            }
            var open = roots.SelectMany(r => r.GetComponentsInChildren<Button>(true)).Single(b => b.name == "PvpButton");
            var canvas = open.GetComponentInParent<Canvas>().rootCanvas;
            foreach (string path in Directory.GetFiles(Art, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite;
                // The exports include transparent artboard margins (especially TRAINING).
                // Crop the sprite's import rectangle, leaving the original PNG untouched.
                var texture = new Texture2D(2, 2);
                texture.LoadImage(File.ReadAllBytes(path));
                var pixels = texture.GetPixels32();
                int minX = texture.width, minY = texture.height, maxX = 0, maxY = 0;
                for (int y = 0; y < texture.height; y++)
                for (int x = 0; x < texture.width; x++)
                    if (pixels[y * texture.width + x].a > 2)
                    { minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y); maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y); }
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritesheet = new[] { new SpriteMetaData {
                    name = Path.GetFileNameWithoutExtension(path), pivot = new Vector2(.5f, .5f),
                    rect = new UnityEngine.Rect(minX, minY, maxX - minX + 1, maxY - minY + 1)
                } };
                Object.DestroyImmediate(texture);
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            PrepareFonts();
            var icons = new[] { Sprite("pvp-icon"), Sprite("boss-icon"), Sprite("training-icon") };
            var controllerRoot = Rect(open.transform.parent.parent, "ModeSelectionController");
            var controller = controllerRoot.gameObject.AddComponent<ModeSelectionController>();
            for (int i = open.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                if (open.onClick.GetPersistentTarget(i) is Toast) UnityEventTools.RemovePersistentListener(open.onClick, i);

            var mirror = canvas.GetComponentInChildren<DeviceSimulatorMirrorFix>(true);
            var overlay = Rect(mirror != null ? mirror.transform : canvas.transform, "ModeSelectionOverlay");
            Stretch(overlay);
            var group = overlay.gameObject.AddComponent<CanvasGroup>();
            var backdrop = Image(overlay, "Dimmer", null, new Color(0, 0, 0, .65f));
            Stretch(backdrop.rectTransform);
            var backdropButton = Button(backdrop);
            var sheet = Image(overlay, "Sheet", Sprite("sheet"), Color.white).rectTransform;
            sheet.anchorMin = sheet.anchorMax = sheet.pivot = new Vector2(.5f, 0);
            sheet.sizeDelta = new Vector2(390, 540);

            // Clip only the moving decor to the silhouette; the icons may overhang their cards.
            var mask = Image(sheet, "SkullMask", Sprite("sheet"), Color.white);
            Stretch(mask.rectTransform);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var field = Rect(mask.transform, "SkullField");
            Stretch(field);
            for (int row = 0; row < 6; row++)
            for (int col = 0; col < 4; col++)
            {
                var skull = Image(field, $"Skull_{row}_{col}", Sprite("skull-pattern"), Color.white);
                var rt = skull.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
                rt.anchoredPosition = new Vector2(-150 + col * 118 + (row % 2) * 59, 255 - row * 102);
                rt.sizeDelta = new Vector2(82, 88);
                skull.preserveAspect = true;
            }
            field.gameObject.AddComponent<ModeSkullField>();
            var close = Image(sheet, "CloseHandle", null, Color.clear);
            Place(close.rectTransform, 135, 0, 120, 36);
            var closeButton = Button(close);
            Label(sheet, "Title", "SELECT MODE", 24, Gold, 35, 43, 320, 38, true);
            var cards = new Button[3];
            var infoButtons = new Button[3];
            TextMeshProUGUI online = null;
            Image dot = null;
            string[] names = { "pvp", "boss", "training" };
            float[] tops = { 97, 253, 368 };
            float[] heights = { 141, 98, 98 };
            for (int i = 0; i < 3; i++)
            {
                var card = Image(sheet, names[i] + "Card", Sprite(names[i] + "-card"), Color.white);
                Place(card.rectTransform, 14, tops[i], 362, heights[i]);
                cards[i] = Button(card);
                if (i == 2)
                {
                    var edge = card.gameObject.AddComponent<Outline>();
                    edge.effectColor = Color.black; edge.effectDistance = new Vector2(1.5f, -1.5f);
                    var shadow = card.gameObject.AddComponent<Shadow>();
                    shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(0, -4);
                }
                Label(card.transform, "Name", ModeSelectionController.Title((GameMode)i), i == 0 ? 44 : 30,
                    i == 2 ? Color.white : Gold, 18, 4, i == 2 ? 260 : 210, i == 0 ? 56 : 43);
                var icon = Image(card.transform, "Icon", icons[i], Color.white);
                icon.preserveAspect = true;
                Place(icon.rectTransform, i == 0 ? 224 : 277, i == 0 ? -7 : -13,
                    i == 0 ? 132 : 84, i == 0 ? 132 : 94);
                // A separate sibling button ensures info taps cannot select the underlying card.
                var info = Image(sheet, names[i] + "Info", Sprite("Group 518"), Color.white);
                Place(info.rectTransform, 348, tops[i] - 12, 28, 26);
                infoButtons[i] = Button(info);
                var glyph = Image(info.transform, "InfoGlyph", Sprite("Group 518-1"), Color.white);
                Place(glyph.rectTransform, 9, 3.5f, 10, 19);
                if (i == 0)
                {
                    dot = Image(card.transform, "OnlineDot", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/circle_128.png"), Color.green);
                    Place(dot.rectTransform, 17, 111, 10, 10);
                    online = Label(card.transform, "OnlineCount", "—", 15, Color.green, 34, 103, 160, 26);
                }
            }

            // Details share the sheet's safe scale; all copy is selectable mode-specific text.
            var infoPanel = Image(sheet, "ModeInfo", null, new Color32(27, 25, 48, 255)).rectTransform;
            Place(infoPanel, 14, 42, 362, 447);
            Button(infoPanel.GetComponent<Image>()); // blocks taps reaching cards below the panel
            var infoIcon = Image(infoPanel, "Icon", icons[0], Color.white);
            infoIcon.preserveAspect = true;
            Place(infoIcon.rectTransform, 21, 14, 54, 58);
            var infoTitle = Label(infoPanel, "Title", "PVP", 27, Gold, 83, 26, 256, 44);
            var infoBody = Label(infoPanel, "Description", "", 15, Color.white, 22, 86, 318, 287);
            infoBody.fontSharedMaterial = _body;
            infoBody.alignment = TextAlignmentOptions.TopLeft;
            infoBody.enableWordWrapping = true;
            infoBody.enableAutoSizing = true;
            infoBody.fontSizeMin = 12;
            infoBody.fontSizeMax = 15;
            var back = Image(infoPanel, "BackToModes", null, new Color32(255, 179, 0, 255));
            Place(back.rectTransform, 22, 386, 318, 44);
            var infoClose = Button(back);
            Label(back.transform, "Label", "К РЕЖИМАМ", 19, Color.white, 0, 0, 318, 44, true);

            var homeLabel = open.GetComponentInChildren<TextMeshProUGUI>(true);
            if (homeLabel == null) throw new InvalidOperationException("PvpButton requires its editable TMP label.");
            homeLabel.enableAutoSizing = true;
            homeLabel.fontSizeMin = 8;
            homeLabel.fontSizeMax = 12;
            var homeIcon = open.transform.Find("Icon").GetComponent<Image>();
            var so = new SerializedObject(controller);
            Set(so, "_openButton", open); Set(so, "_overlay", overlay.gameObject);
            Set(so, "_sheet", sheet); Set(so, "_overlayGroup", group);
            Set(so, "_backdropButton", backdropButton); Set(so, "_closeButton", closeButton);
            Set(so, "_infoPanel", infoPanel.gameObject); Set(so, "_infoClose", infoClose);
            Set(so, "_infoTitle", infoTitle); Set(so, "_infoBody", infoBody); Set(so, "_infoIcon", infoIcon);
            Set(so, "_online", online); Set(so, "_onlineDot", dot);
            Set(so, "_homeLabel", homeLabel); Set(so, "_homeIcon", homeIcon);
            Array(so, "_cards", cards); Array(so, "_infoButtons", infoButtons); Array(so, "_icons", icons);
            so.ApplyModifiedPropertiesWithoutUndo();
            infoPanel.gameObject.SetActive(false);
            overlay.gameObject.SetActive(false);
            TrainingHomeActionsSetup.Install(scene);
        }

        private static void PrepareFonts()
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.BoldAsset);
            _outline = Material("ModeOutline", .24f);
            _body = Material("ModeBody", 0f);
        }

        private static Material Material(string name, float outline)
        {
            string path = Art + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(_font.material) { name = name };
            material.SetFloat("_OutlineWidth", outline);
            material.SetFloat("_FaceDilate", outline > 0 ? .17f : 0f);
            material.SetColor("_FaceColor", Color.white);
            material.SetColor("_OutlineColor", Color.black);
            material.SetFloat("_WeightNormal", 0);
            material.SetFloat("_WeightBold", 0);
            if (outline > 0)
            {
                material.EnableKeyword("OUTLINE_ON");
                material.EnableKeyword("UNDERLAY_ON");
                material.SetColor("_UnderlayColor", Color.black);
                material.SetFloat("_UnderlayDilate", .67f);
                material.SetFloat("_UnderlayOffsetX", 0f);
                material.SetFloat("_UnderlayOffsetY", -1f);
                material.SetFloat("_UnderlaySoftness", 0f);
            }
            else material.DisableKeyword("UNDERLAY_ON");
            material.DisableKeyword("UNDERLAY_INNER");
            ShaderUtilities.UpdateShaderRatios(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static Sprite Sprite(string name) => AssetDatabase.LoadAllAssetsAtPath(Art + name + ".png").OfType<Sprite>().First();
        private static RectTransform Rect(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false); rt.gameObject.layer = 5; return rt;
        }
        private static void Stretch(RectTransform rt)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rt, float x, float top, float w, float h)
        { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -top); rt.sizeDelta = new Vector2(w, h); }
        private static Image Image(Transform parent, string name, Sprite sprite, Color color)
        {
            var image = Rect(parent, name).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.color = color; image.raycastTarget = false; return image;
        }
        private static Button Button(Image image)
        {
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            var colors = button.colors; colors.pressedColor = new Color(.82f, .82f, .82f); button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            return button;
        }
        private static TextMeshProUGUI Label(Transform parent, string name, string value, float size, Color color,
            float x, float y, float w, float h, bool center = false)
        {
            var text = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>();
            Place(text.rectTransform, x, y, w, h);
            text.font = _font; text.fontSharedMaterial = _outline;
            text.text = value; text.fontSize = size; text.color = color;
            text.fontStyle = FontStyles.Normal; text.raycastTarget = false; text.enableWordWrapping = false;
            text.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.Left;
            return text;
        }
        private static void Set(SerializedObject so, string name, Object value) => so.FindProperty(name).objectReferenceValue = value;
        private static void Array(SerializedObject so, string name, Object[] values)
        {
            var property = so.FindProperty(name); property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
    }
}
