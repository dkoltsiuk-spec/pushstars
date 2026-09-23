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
    public static class AvatarTactileSetup
    {
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes first.");
            const string path = "Assets/_Project/Scenes/Main.unity";
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(path, "Library/AvatarCollectionBackup/Main-before-tactile-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var scene = EditorSceneManager.OpenScene(path);
            var roots = scene.GetRootGameObjects();
            var screen = roots.SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            foreach (var button in screen.Overlay.GetComponentsInChildren<Button>(true)) Add(button.gameObject);
            foreach (var card in screen.Cards) Add(card.gameObject).PressScale = .965f;
            var overlayMotion = Add(screen.Overlay);
            overlayMotion.RevealScale = overlayMotion.PressScale = 1;
            Add(screen.PreviewPage.Preview.gameObject).RevealScale = .94f;
            var tap = roots.SelectMany(r => r.GetComponentsInChildren<AvatarCollectionTap>(true)).First();
            var avatarMotion = Add(tap.gameObject);
            avatarMotion.PressScale = .98f;
            avatarMotion.MotionTarget = screen.Roster.GetComponent<CharacterStage>().AvatarRoot;
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AvatarCollectionValidation.Run();
        }
        private static UiTactile Add(GameObject target)
        {
            if (target.GetComponent<CanvasGroup>() == null) target.AddComponent<CanvasGroup>();
            return target.GetComponent<UiTactile>() ?? target.AddComponent<UiTactile>();
        }
    }
}
