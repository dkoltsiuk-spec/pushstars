using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class RobotAvatarSetup
    {
        [MenuItem("Tools/Push Stars/Character/Prepare Robot and Update Collection")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before importing Robot.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before updating the collection.");
            AvatarCollectionSetup.PrepareCharacter("Robot");
            AvatarCollectionSetup.ImportCardArt();
            const string path = "Assets/_Project/Scenes/Main.unity";
            var scene = EditorSceneManager.OpenScene(path);
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(path, "Library/AvatarCollectionBackup/Main-before-robot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var card = (RectTransform)screen.Cards[3].transform;
            card.Find("Name").GetComponent<TextMeshProUGUI>().text = "ROBOT";
            var unknown = card.Find("Unknown");
            if (unknown != null) unknown.gameObject.SetActive(false);
            var existing = card.Find("Portrait");
            var portrait = existing != null ? existing.GetComponent<Image>() : UiBuilder.Image(card, "Portrait", Color.white);
            portrait.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/AvatarCollection/Robot.png");
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            var rect = portrait.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(6, -29);
            rect.sizeDelta = new Vector2(154, 151);
            rect.SetAsFirstSibling();
            screen.Actions[3].text = "PREVIEW";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[AvatarCollection] Robot imported and fourth card updated. Home avatar unchanged.");
        }

        public static void RunAndValidate()
        {
            Run();
            AvatarCollectionValidation.Run();
        }
    }
}
