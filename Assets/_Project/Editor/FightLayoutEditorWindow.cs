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
            "Подготовка к бою", "Бой", "Результаты боя", "Начисление награды",
            "Получение кейса", "Открытие кейса", "Награда из кейса"
        };
        private int _index;
        private Vector2 _scroll;

        [MenuItem("Tools/Push Stars/Fight Layout Editor", priority = 22)]
        public static void Open() => GetWindow<FightLayoutEditorWindow>("Fight Scenes");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Сцены игры", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Открой сцену, двигай элементы инструментом Rect (T) и сохраняй Ctrl+S. " +
                "Переходы на другие экраны настраиваются в компоненте экрана в Inspector.", MessageType.Info);
            _index = EditorGUILayout.Popup("Сцена", _index, Labels);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Открыть сцену", GUILayout.Height(32)))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        SessionState.EraseString(PreviewSessionKey);
                        EditorSceneManager.OpenScene(FightScreenNavigation.ScenePath(Screens[_index]));
                    }
                }
                if (GUILayout.Button("Сохранить сцену", GUILayout.Height(28))) EditorSceneManager.SaveOpenScenes();
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("Для сохранения элементов в сцене останови Play Mode.", MessageType.Info);

            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorGUILayout.LabelField("Открыта", active.name);
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
