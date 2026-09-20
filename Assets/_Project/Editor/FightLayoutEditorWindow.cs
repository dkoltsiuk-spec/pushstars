using System.Linq;
using PushStars.Fight;
using PushStars.UI.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Shortcut to ordinary scene authoring; no runtime preview or layout catalog needed.</summary>
    public sealed class FightLayoutEditorWindow : EditorWindow
    {
        public const string PreviewSessionKey = "PushStars.FightScreenPreview";
        private static readonly FightScreen[] Screens =
        {
            FightScreen.Preparation, FightScreen.Battle, FightScreen.Results, FightScreen.RewardSummary,
            FightScreen.CaseAward, FightScreen.CaseOpening, FightScreen.CaseReward
        };
        private static readonly string[] Labels =
        {
            "Battle preparation", "Battle", "Battle results", "Reward summary",
            "Case award", "Case opening", "Case reward"
        };
        private int _index;
        private Vector2 _scroll;

        [MenuItem("Tools/Push Stars/Fight Layout Editor", priority = 22)]
        public static void Open() => GetWindow<FightLayoutEditorWindow>("Fight Scenes");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Game scenes", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Open a scene, move elements with the Rect tool (T), and save with Ctrl+S. " +
                "Configure navigation to other screens in the screen component in the Inspector.", MessageType.Info);
            _index = EditorGUILayout.Popup("Scene", _index, Labels);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Open scene", GUILayout.Height(32)))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        SessionState.EraseString(PreviewSessionKey);
                        EditorSceneManager.OpenScene(FightScreenNavigation.ScenePath(Screens[_index]));
                    }
                }
                if (GUILayout.Button("Save scene", GUILayout.Height(28))) EditorSceneManager.SaveOpenScenes();
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Stop Play Mode before saving scene elements.", MessageType.Info);

            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorGUILayout.LabelField("Open", active.name);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var root in Resources.FindObjectsOfTypeAll<ScreenLayoutRoot>().Where(item => item.gameObject.scene == active))
            {
                EditorGUILayout.LabelField(root.ScreenId, EditorStyles.boldLabel);
                root.RefreshTargets();
                foreach (var target in root.Targets)
                {
                    if (target.rect == null) continue;
                    if (GUILayout.Button(target.rect.name))
                    {
                        Selection.activeGameObject = target.rect.gameObject;
                        UnityEditor.Tools.current = Tool.Rect;
                        SceneView.lastActiveSceneView?.FrameSelected();
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
