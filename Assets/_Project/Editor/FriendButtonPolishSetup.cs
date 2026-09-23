using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PushStars.Editor
{
    public static class FriendButtonPolishSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private const string IconPath = "Assets/_Project/Resources/FriendDuel/AddFriend.png";

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before changing the friend button.");

            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();
            }

            Directory.CreateDirectory("Library/FriendDuelBackup");
            File.Copy(ScenePath, "Library/FriendDuelBackup/Main-before-friend-button-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var controller = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<FriendDuelController>(true)).Single();
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            if (icon == null) throw new InvalidOperationException("Friend button sprite was not imported.");

            controller.PlusIcon.sprite = icon;
            controller.PlusIcon.color = Color.white;
            controller.PlusIcon.preserveAspect = true;
            var rect = controller.PlusIcon.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(21, -2);
            rect.sizeDelta = new Vector2(60, 60);
            var slot = (RectTransform)controller.Slot.transform.parent;
            slot.anchoredPosition = new Vector2(-118, 102);
            controller.SlotName.text = controller.SlotStatus.text = "";
            controller.SlotName.gameObject.SetActive(false);
            controller.SlotStatus.gameObject.SetActive(false);

            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(controller.PlusIcon);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            FriendDuelValidation.Run();
        }
    }
}
