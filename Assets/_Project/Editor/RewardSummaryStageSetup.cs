using System;
using System.Linq;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Authors an independent copy of the player's idle stage into RewardSummary.</summary>
    public static class RewardSummaryStageSetup
    {
        [MenuItem("Tools/Push Stars/Rewards/Add Summary Idle Stage")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes first.");
            try
            {
                var scene = EditorSceneManager.OpenScene(FightScreenNavigation.ScenePath(FightScreen.RewardSummary));
                Attach(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single());
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        public static void Attach(RewardScreen screen)
        {
            if (screen.SummaryUi.Avatar != null) return;
            string sourcePath = FightPresentationSceneBuilder.ResultsPath;
            var source = SceneManager.GetSceneByPath(sourcePath);
            bool opened = !source.IsValid() || !source.isLoaded;
            if (opened) source = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
            try
            {
                var player = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true))
                    .First(a => !new SerializedObject(a).FindProperty("_opponentStage").boolValue);
                var originalStage = player.StageCamera.GetComponentInParent<CharacterStage>();
                var clone = Object.Instantiate(originalStage.gameObject);
                clone.name = "SummaryIdleStage";
                SceneManager.MoveGameObjectToScene(clone, screen.gameObject.scene);
                var stage = clone.GetComponent<CharacterStage>();
                var serialized = new SerializedObject(stage);
                serialized.FindProperty("_targetImage").objectReferenceValue = screen.SummaryUi.Portrait;
                serialized.FindProperty("_idleBob").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                stage.StageCamera.targetTexture = null;
                stage.StageCamera.enabled = false;
                screen.SummaryUi.Avatar = clone.GetComponentInChildren<FightAvatar>(true);
                if (screen.SummaryUi.Avatar == null) throw new InvalidOperationException("Player stage has no FightAvatar.");
                EditorUtility.SetDirty(screen);
                EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(source, true); }
        }
    }
}
