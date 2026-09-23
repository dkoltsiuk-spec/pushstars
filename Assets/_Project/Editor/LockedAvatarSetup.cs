using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class LockedAvatarSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string LockPath = "Assets/_Project/Resources/AvatarCollection/Lock.png";
        private const string InfoPath = "Assets/_Project/Resources/AvatarCollection/InfoButton.png";

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes first.");
            var importer = AssetImporter.GetAtPath(LockPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 256;
                importer.SaveAndReimport();
            }
            ImportSprite(InfoPath, 256);
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(ScenePath, "Library/AvatarCollectionBackup/Main-before-locked-robot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            var card = (RectTransform)screen.Cards[3].transform;
            foreach (var surface in card.GetComponentsInChildren<AvatarCardSurface>(true)) surface.SetLocked(true);
            var live = card.Find("LivePortrait").GetComponent<RawImage>();
            var preview = live.GetComponent<AvatarCardPreview>();
            preview.Tint = new Color(.70f, .70f, .70f, 1f);
            live.color = preview.Tint;
            var glow = card.Find("Glow").GetComponent<Image>();
            glow.color = new Color(.3f, .3f, .3f, .62f);
            var lockTransform = card.Find("LockIcon");
            var lockImage = lockTransform != null ? lockTransform.GetComponent<Image>() : UiBuilder.Image(card, "LockIcon", Color.white);
            lockImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LockPath);
            lockImage.preserveAspect = true;
            lockImage.raycastTarget = false;
            Place(lockImage.rectTransform, -7, -5, 43, 43);
            lockImage.rectTransform.SetAsLastSibling();
            screen.Actions[3].text = "BLOCK";
            var infoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(InfoPath);
            foreach (var info in screen.InfoButtons)
            {
                info.image.sprite = infoSprite;
                info.image.preserveAspect = true;
                Place(info.image.rectTransform, 140, -1, 34, 30);
                var label = info.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                label.alignment = TMPro.TextAlignmentOptions.Center;
                label.fontSize = 16;
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.pivot = new Vector2(.5f, .5f);
                label.rectTransform.anchoredPosition = Vector2.zero;
                label.rectTransform.sizeDelta = Vector2.zero;
            }
            screen.InfoButtons[3].transform.SetAsLastSibling();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AvatarCollectionValidation.Run();
        }

        private static void ImportSprite(string path, int maxSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = maxSize;
            importer.SaveAndReimport();
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
