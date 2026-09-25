using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class AvatarPreviewPageSetup
    {
        private static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/AvatarCollection/" + name + ".png");
        private static Sprite Profile(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/ProfileSettings/" + name + ".png");

        [MenuItem("Tools/Push Stars/UI/Install Avatar Preview Page")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before updating the preview page.");
            AvatarCollectionSetup.ImportCardArt();
            const string path = "Assets/_Project/Scenes/Main.unity";
            var scene = EditorSceneManager.OpenScene(path);
            var roots = scene.GetRootGameObjects();
            var screen = roots.SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            if (screen.PreviewPage != null)
            {
                var existingShadow = screen.PreviewPage.transform.Find("FloorShadow") as RectTransform;
                Place(existingShadow, 95, 544, 200, 20);
                Place((RectTransform)screen.InfoClose.transform, 20, 48, 57, 44);
                Place((RectTransform)screen.PreviewPage.Home.transform, 313, 48, 57, 44);
                Place(screen.InfoTitle.rectTransform, 82, 50, 226, 42);
                screen.InfoBody.fontSharedMaterial = screen.InfoBody.font.material;
                screen.PreviewPage.Status.fontSharedMaterial = screen.PreviewPage.Status.font.material;
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
                return;
            }
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(path, "Library/AvatarCollectionBackup/Main-before-preview-page-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var title = screen.Art.Find("Title").GetComponent<TextMeshProUGUI>();
            Bold(title, 22);
            screen.TabOn = Art("TabActive"); screen.TabOff = Art("TabIdle");
            for (int i = 0; i < screen.Tabs.Length; i++)
            {
                var button = screen.Tabs[i];
                button.image.sprite = i == 0 ? screen.TabOn : screen.TabOff;
                button.image.preserveAspect = false;
                Place((RectTransform)button.transform, 20 + i * 121, 103, 108, 42);
                var label = button.GetComponentInChildren<TextMeshProUGUI>();
                UiBuilder.Stretch(label.rectTransform, 3, 5, 9, 1);
                Bold(label, 14);
            }
            foreach (var bolt in screen.Overlay.GetComponentsInChildren<AvatarLightningGraphic>(true)) bolt.gameObject.SetActive(false);
            var original = roots.SelectMany(r => r.GetComponentsInChildren<LightningField>(true))
                .FirstOrDefault(f => !f.transform.IsChildOf(screen.Overlay.transform));
            var pattern = original != null ? Object.Instantiate(original.gameObject, screen.Overlay.transform, false)
                : LightningField.Build((RectTransform)screen.Overlay.transform,
                    AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/icon_lightning_BG.png")).gameObject;
            pattern.name = "HomeLightningPattern";
            pattern.SetActive(true);
            UiBuilder.Stretch((RectTransform)pattern.transform);
            pattern.transform.SetAsFirstSibling();
            screen.InfoPanel.name = "LegacyAvatarInfo";
            screen.InfoPanel.SetActive(false);
            screen.CollectionContent = screen.Art.Cast<Transform>().Where(t => t.gameObject != screen.InfoPanel).Select(t => t.gameObject).ToArray();
            var pageRoot = UiBuilder.Rect(screen.Art, "AvatarPreviewPage");
            UiBuilder.Stretch(pageRoot);
            var page = pageRoot.gameObject.AddComponent<AvatarPreviewPage>();
            page.Collection = screen;
            page.Prefabs = screen.Cards.Select(c => c.GetComponentInChildren<AvatarCardPreview>(true).Prefab).ToArray();
            screen.PreviewPage = page;
            screen.InfoPanel = pageRoot.gameObject;
            screen.InfoClose = Button(pageRoot, "Back", Profile("Group 549"), "", 20, 48, 57, 44);
            page.Home = Button(pageRoot, "Home", Profile("Group 550"), "", 313, 48, 57, 44);
            screen.InfoTitle = Text(pageRoot, "Title", "SONIC", 82, 50, 226, 42, 22);
            page.Name = Text(pageRoot, "SkinName", "SONIC", 24, 110, 342, 25, 15);
            page.Name.alignment = TextAlignmentOptions.Left;
            screen.InfoBody = Text(pageRoot, "Category", "CHARACTER SKIN", 24, 135, 342, 21, 11);
            screen.InfoBody.alignment = TextAlignmentOptions.Left;
            screen.InfoBody.color = new Color(.65f, .78f, 1);
            screen.InfoBody.fontSharedMaterial = screen.InfoBody.font.material;
            var glow = UiBuilder.Image(pageRoot, "Glow", Color.white);
            glow.sprite = Art("Glow"); Place(glow.rectTransform, 5, 164, 380, 413);
            var shadowRect = UiBuilder.Rect(pageRoot, "FloorShadow");
            Place(shadowRect, 95, 544, 200, 20);
            shadowRect.gameObject.AddComponent<CanvasRenderer>();
            var shadow = shadowRect.gameObject.AddComponent<HardEllipseGraphic>();
            shadow.color = new Color(0, .08f, .3f, .3f); shadow.raycastTarget = false;
            var live = UiBuilder.RawImage(pageRoot, "FullBody", Color.white);
            Place(live.rectTransform, 55, 175, 280, 400);
            page.Preview = live.gameObject.AddComponent<AvatarCardPreview>();
            var source = screen.Cards[0].GetComponentInChildren<AvatarCardPreview>(true);
            page.Preview.FullBody = true; page.Preview.Slot = 8;
            page.Preview.HomeStage = source.HomeStage; page.Preview.PreviewLayer = source.PreviewLayer;
            page.Preview.Facing = source.Facing; page.Preview.Image = live;
            page.Action = Button(pageRoot, "Action", Art("ActionActive"), "COMING SOON", 117, 640, 156, 49);
            page.ActionText = page.Action.GetComponentInChildren<TextMeshProUGUI>();
            var colors = page.Action.colors; colors.disabledColor = Color.white; page.Action.colors = colors;
            page.Status = Text(pageRoot, "Status", "PREVIEW", 45, 611, 300, 23, 12);
            page.Status.color = new Color(.7f, .82f, 1);
            page.Status.fontSharedMaterial = page.Status.font.material;
            pageRoot.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[AvatarCollection] Preview page, home lightning, smaller upright title and supplied buttons installed.");
        }

        private static void Bold(TextMeshProUGUI text, float size)
        {
            text.font = FontSetup.Resolve(FontStyles.Bold, out var style);
            text.fontStyle = style; text.fontSize = size;
            text.fontSharedMaterial = text.font.material;
            FontSetup.ApplyOutlineFor(text, size);
        }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }
        private static TextMeshProUGUI Text(RectTransform root, string name, string value, float x, float y, float w, float h, float size)
        { var text = UiBuilder.Text(root, name, Color.white, value, size, FontStyles.Bold); Place(text.rectTransform, x, y, w, h); return text; }
        private static Button Button(RectTransform root, string name, Sprite sprite, string value, float x, float y, float w, float h)
        {
            var image = UiBuilder.Image(root, name, Color.white); image.sprite = sprite; image.raycastTarget = true;
            Place(image.rectTransform, x, y, w, h);
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            if (value.Length > 0) { var text = Text(image.rectTransform, "Label", value, 0, 0, w, h, 15); UiBuilder.Stretch(text.rectTransform, 4, 5, 10, 1); }
            return button;
        }
        public static void RunAndValidate() { Run(); AvatarCollectionValidation.Run(); }
    }
}
