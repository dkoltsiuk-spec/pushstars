using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using PushStars.CV;

namespace PushStars.Editor
{
    /// <summary>Saved scenes own UI authoring. Legacy generators only bootstrap missing scenes.</summary>
    internal static class AuthoredScenes
    {
        internal const string MainPath = "Assets/_Project/Scenes/Main.unity";

        internal static bool PreserveExisting(string path)
        {
            if (!File.Exists(path)) return false;
            Debug.Log($"[Scenes] Using authored {path}. Edit the scene and save with Ctrl+S.");
            return true;
        }

        [MenuItem("Tools/Push Stars/Edit Screens/Main", priority = 0)]
        internal static void OpenMain() => Open(MainPath);

        [MenuItem("Tools/Push Stars/Edit Screens/Boot", priority = 1)]
        static void OpenBoot() => Open(BootSceneSetup.ScenePath);

        [MenuItem("Tools/Push Stars/Edit Screens/Onboarding", priority = 2)]
        static void OpenOnboarding() => Open(OnboardingSceneSetup.ScenePath);

        [MenuItem("Tools/Push Stars/Edit Screens/Fight", priority = 3)]
        static void OpenFight() => Open(FightSceneSetup.ScenePath);

        internal static void Open(string path)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(path);
        }

        // Never save or replace the user's open scenes while validating the CV binding.
        internal static bool HasRealFightPoseSource()
        {
            var scene = SceneManager.GetSceneByPath(FightSceneSetup.ScenePath);
            bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
            if (openedForValidation)
                scene = EditorSceneManager.OpenScene(FightSceneSetup.ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                foreach (var session in root.GetComponentsInChildren<PushupSession>(true))
                {
                    var source = new SerializedObject(session)
                        .FindProperty("_poseSourceBehaviour").objectReferenceValue;
                    if (source != null && source.GetType().FullName == "PushStars.CV.MediaPipePoseSource")
                        return true;
                }
                return false;
            }
            finally
            {
                if (openedForValidation) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
