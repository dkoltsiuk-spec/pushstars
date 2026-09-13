using System;
using System.Linq;
using System.Reflection;
using PushStars.Fight;
using PushStars.UI;
using PushStars.UI.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>One-time migration. Existing authored scenes are never rebuilt over user edits.</summary>
    public static class IndependentFightScenesSetup
    {
        public static void Validate()
        {
            CaseRewardsRegression.Run();
            FightFlowRegression.Run();
            ScreenLayoutRegression.Run();
        }

        [MenuItem("Tools/Push Stars/Create Missing Fight Scenes", priority = 21)]
        public static void BuildAll()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string hostPath = null;
            if (Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                hostPath = "Assets/__SceneBuildHost_" + Guid.NewGuid().ToString("N") + ".unity";
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), hostPath);
            }
            try
            {
            FightPresentationSceneBuilder.BuildAll();
            RewardSceneBuilder.BuildAll();
            BakeBattle();
            AddBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("Independent fight scenes are ready for ordinary Scene editing and Ctrl+S.");
            }
            finally
            {
                if (hostPath != null)
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    AssetDatabase.DeleteAsset(hostPath);
                }
            }
        }

        private static void BakeBattle()
        {
            const string path = "Assets/_Project/Scenes/Fight.unity";
            string temporary = "Assets/__BattleMigration_" + Guid.NewGuid().ToString("N") + ".unity";
            if (!AssetDatabase.CopyAsset(path, temporary)) throw new InvalidOperationException("Cannot copy Fight scene for migration.");
            Scene scene = default;
            var previous = SceneManager.GetActiveScene();
            try
            {
                scene = EditorSceneManager.OpenScene(temporary, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var all = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
                if (all.Any(item => item.GetComponent<ScreenLayoutRoot>()?.IsSceneAuthored == true)) return;
                var hud = all.Select(item => item.GetComponent<FightHud>()).FirstOrDefault(item => item != null);
                if (hud == null) throw new InvalidOperationException("Fight HUD is missing.");

                foreach (var panel in all.Select(item => item.GetComponent<DuelReadyPanel>()).Where(item => item != null).ToArray())
                    RemovePresentation(panel);
                foreach (var panel in all.Select(item => item != null ? item.GetComponent<FightResultScreen>() : null).Where(item => item != null).ToArray())
                    RemovePresentation(panel);
                foreach (var flow in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<FightRewardFlow>(true)))
                    UnityEngine.Object.DestroyImmediate(flow);

                // Registration imports legacy saved positions once. After this, scene rects win.
                typeof(FightHud).GetMethod("ConfigureEditableHud", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(hud, new object[] { false });
                foreach (var layout in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<ScreenLayoutRoot>(true)))
                {
                    layout.ApplySavedLayout();
                    layout.MarkSceneAuthored();
                }

                var portrait = AssetDatabase.LoadAssetAtPath<Texture2D>(FightPresentationSceneBuilder.PronePortraitPath);
                foreach (var avatar in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<FightAvatar>(true)))
                    FightPresentationSceneBuilder.RepairAvatarPrefabs(avatar);
                foreach (var stage in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CharacterStage>(true)))
                {
                    var image = new SerializedObject(stage).FindProperty("_targetImage")?.objectReferenceValue as RawImage;
                    if (image == null) continue;
                    var runtimeTexture = image.texture;
                    var preview = image.GetComponent<ScenePortraitPreview>() ?? image.gameObject.AddComponent<ScenePortraitPreview>();
                    preview.Configure(runtimeTexture, portrait);
                }
                foreach (var root in scene.GetRootGameObjects()) FightPresentationSceneBuilder.PersistTextMaterials(root);
                if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Could not save authored Fight scene.");
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                AssetDatabase.DeleteAsset(temporary);
            }
        }

        private static void RemovePresentation(MonoBehaviour component)
        {
            var serialized = new SerializedObject(component);
            var root = serialized.FindProperty("_root")?.objectReferenceValue as GameObject;
            if (root != null && root != component.gameObject) UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(component);
        }

        private static void AddBuildScenes()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            foreach (FightScreen screen in Enum.GetValues(typeof(FightScreen)))
            {
                if (screen == FightScreen.Home) continue;
                string path = FightScreenNavigation.ScenePath(screen);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) throw new InvalidOperationException("Missing scene: " + path);
                var existing = scenes.FirstOrDefault(item => item.path == path);
                if (existing != null) existing.enabled = true;
                else scenes.Add(new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
