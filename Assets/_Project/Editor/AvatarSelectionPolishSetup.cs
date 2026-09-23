using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PushStars.Editor
{
    public static class AvatarSelectionPolishSetup
    {
        private static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/AvatarCollection/" + name + ".png");
        public static void FixTabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes first.");
            const string path = "Assets/_Project/Scenes/Main.unity";
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(path, "Library/AvatarCollectionBackup/Main-before-short-all-tab-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var scene = EditorSceneManager.OpenScene(path);
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            ApplyTabs(screen);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AvatarCollectionValidation.Run();
        }

        private static void ApplyTabs(AvatarCollectionScreen screen)
        {
            screen.TabOn = Art("ActionActive"); screen.TabOff = Art("ActionIdle");
            screen.ShortTabOn = Art("TabActive"); screen.ShortTabOff = Art("TabIdle");
            // Keep the smaller tabs left-aligned with 6 px gaps.
            float[] positions = { 20, 94, 200 };
            for (int i = 0; i < screen.Tabs.Length; i++)
            {
                var button = screen.Tabs[i];
                button.image.sprite = i == 0 ? screen.ShortTabOn : screen.TabOff;
                button.image.preserveAspect = true;
                Place((RectTransform)button.transform, positions[i], 103, i == 0 ? 68 : 100, 40);
                UiBuilder.Stretch(button.GetComponentInChildren<TextMeshProUGUI>().rectTransform, 3, 5, 9, 1);
            }
        }
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes first.");
            AvatarCollectionSetup.ImportCardArt();
            const string path = "Assets/_Project/Scenes/Main.unity";
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(path, "Library/AvatarCollectionBackup/Main-before-selection-polish-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var scene = EditorSceneManager.OpenScene(path);
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            var roster = new SerializedObject(screen.Roster);
            roster.FindProperty("_sonicPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Sonic/Sonic.prefab");
            roster.ApplyModifiedPropertiesWithoutUndo();
            ApplyTabs(screen);
            var page = screen.PreviewPage;
            var icon = page.transform.Find("SkinIcon");
            var image = icon == null ? UiBuilder.Image((RectTransform)page.transform, "SkinIcon", Color.white) : icon.GetComponent<UnityEngine.UI.Image>();
            image.sprite = Art("SkinIcon"); image.preserveAspect = true; image.raycastTarget = false;
            Place(image.rectTransform, 24, 112, 40, 40);
            Place(page.Name.rectTransform, 74, 111, 292, 24);
            Place(screen.InfoBody.rectTransform, 74, 135, 292, 20);
            Place(page.Preview.Image.rectTransform, 41, 140, 308, 440);
            Place((RectTransform)page.transform.Find("FloorShadow"), 85, 546, 220, 22);
            page.Action.image.sprite = Art("TabActive"); page.Action.image.preserveAspect = true;
            Place((RectTransform)page.Action.transform, 140, 634, 110, 62);
            page.ActionText.fontSize = 17;
            UiBuilder.Stretch(page.ActionText.rectTransform, 3, 7, 7, 2);
            screen.Actions[0].text = "SELECT";
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AvatarCollectionValidation.Run();
        }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }
    }
}
