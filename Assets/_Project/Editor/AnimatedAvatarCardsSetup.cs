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
    public static class AnimatedAvatarCardsSetup
    {
        public static void RunAndValidate()
        {
            Run();
            AvatarCollectionValidation.Run();
        }

        [MenuItem("Tools/Push Stars/UI/Install Animated Avatar Cards")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before updating avatar cards.");
            const string glowPath = "Assets/_Project/Resources/AvatarCollection/Glow.png";
            AssetDatabase.Refresh();
            var importer = (TextureImporter)AssetImporter.GetAtPath(glowPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            var glowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(glowPath);
            const string path = "Assets/_Project/Scenes/Main.unity";
            var scene = EditorSceneManager.OpenScene(path);
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(path, "Library/AvatarCollectionBackup/Main-before-live-cards-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            var home = screen.Roster.GetComponent<CharacterStage>();
            int layer = UiBuilder.EnsureLayer("AvatarCards");
            string[] prefabs = { "Assets/Character/Sonic/Sonic.prefab", "Assets/Character/Main_woman/MainWoman.prefab",
                "Assets/Character/Main_man/MainMan.prefab", "Assets/Character/Robot/Robot.prefab" };
            for (int i = 0; i < 4; i++)
            {
                var card = (RectTransform)screen.Cards[i].transform;
                var poster = card.Find("Portrait");
                if (poster != null) poster.gameObject.SetActive(false);
                var glowTransform = card.Find("Glow");
                var glow = glowTransform != null ? glowTransform.GetComponent<Image>() : UiBuilder.Image(card, "Glow", Color.white);
                glow.sprite = glowSprite;
                Place(glow.rectTransform, -11, 16, 188, 188);
                var cardSurface = card.Find("CleanCard");
                if (cardSurface != null) cardSurface.SetAsFirstSibling();
                glow.rectTransform.SetSiblingIndex(cardSurface != null ? 1 : 0);
                var liveTransform = card.Find("LivePortrait");
                var live = liveTransform != null ? liveTransform.GetComponent<RawImage>() : UiBuilder.RawImage(card, "LivePortrait", Color.white);
                Place(live.rectTransform, 6, 29, 154, 151);
                live.rectTransform.SetSiblingIndex(cardSurface != null ? 2 : 1);
                var preview = live.GetComponent<AvatarCardPreview>() ?? live.gameObject.AddComponent<AvatarCardPreview>();
                preview.Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabs[i]);
                preview.Image = live;
                preview.HomeStage = home;
                preview.Slot = i;
                preview.PreviewLayer = layer;
                preview.Facing = home.AvatarRoot.rotation;
                preview.SubjectOffsetX = 0f;
                // Static fallback remains visible in edit mode; runtime replaces it with the live target.
                live.texture = poster != null ? poster.GetComponent<Image>().sprite.texture : null;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[AvatarCollection] Four animated cards installed with home lighting and supplied cyan glow.");
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(w, h);
        }
    }
}
