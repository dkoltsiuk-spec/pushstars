using System.Linq;
using PushStars.Core;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Editor
{
    /// <summary>
    /// Plays any boss's preparation or battle screen in the Game view, for tuning its look live: boss
    /// bodies render with their shared materials, so edits made to them in the Inspector during Play show
    /// at once and stay after Play stops. Both screens open the way the game does (battle as if READY was
    /// tapped, camera tracking included); the player's boss progress is restored when Play ends.
    /// </summary>
    public sealed class BossPreviewWindow : EditorWindow
    {
        private const string Key = "PushStars.BossPreview";
        private static readonly string[] Prefs = { "boss_progress", "selected_game_mode" };
        private int _boss;

        [MenuItem("Tools/Push Stars/Boss/Boss Preview")]
        private static void Open() => GetWindow<BossPreviewWindow>("Boss Preview");

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    // Give Main a second to boot its services, as a player tapping the boss would.
                    double due = EditorApplication.timeSinceStartup + 1;
                    EditorApplication.CallbackFunction wait = null;
                    wait = () =>
                    {
                        if (EditorApplication.isPlaying && EditorApplication.timeSinceStartup < due) return;
                        EditorApplication.update -= wait; Launch();
                    };
                    EditorApplication.update += wait;
                }
                if (state != PlayModeStateChange.EnteredEditMode) return;
                foreach (string pref in Prefs)
                {
                    if (SessionState.GetBool(Key + pref + ".had", false)) PlayerPrefs.SetInt(pref, SessionState.GetInt(Key + pref, 0));
                    else PlayerPrefs.DeleteKey(pref);
                }
                PlayerPrefs.Save(); SessionState.SetBool(Key, false);
            };
        }

        private void OnGUI()
        {
            var bosses = BossCatalog.Bosses;
            var names = bosses.Select((b, i) => i + "  " + BossCatalog.ChapterOf(b.Id)?.Title + " / " + b.DisplayName + "  (" + b.PrefabId + ")").ToArray();
            _boss = EditorGUILayout.Popup("Boss", Mathf.Clamp(_boss, 0, bosses.Count - 1), names);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Play preparation")) Play(false);
                if (GUILayout.Button("Play battle")) Play(true);
            }
            if (EditorApplication.isPlaying && GUILayout.Button("Stop")) EditorApplication.isPlaying = false;
            EditorGUILayout.Space();
            if (GUILayout.Button("Select boss material")) SelectMaterials(bosses[_boss].PrefabId);
            EditorGUILayout.HelpBox("Material edits made during Play are kept: they change the asset itself. " +
                "Golem rim and crack glow are reset by Tools/Push Stars/Boss/Apply Golem Look, so copy values you like into GolemBossSetup.", MessageType.Info);
        }

        private static void SelectMaterials(string prefabId)
        {
            var prefab = Resources.Load<GameObject>("Bosses/" + prefabId);
            if (prefab == null) return;
            var materials = prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
            Selection.objects = materials;
            if (materials.Length > 0) EditorGUIUtility.PingObject(materials[0]);
        }

        private void Play(bool battle)
        {
            if (SceneManager.GetActiveScene().path != AuthoredScenes.MainPath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(AuthoredScenes.MainPath);
            }
            foreach (string pref in Prefs)
            {
                SessionState.SetBool(Key + pref + ".had", PlayerPrefs.HasKey(pref));
                SessionState.SetInt(Key + pref, PlayerPrefs.GetInt(pref));
            }
            PlayerPrefs.SetInt("boss_progress", _boss); SelectedGameMode.Current = GameMode.Boss;
            SessionState.SetBool(Key, true); SessionState.SetBool(Key + ".battle", battle);
            EditorApplication.isPlaying = true;
        }

        private static void Launch()
        {
            if (!EditorApplication.isPlaying) return;
            FightRequest.Boss();
            FightScreenNavigation.Navigate(SessionState.GetBool(Key + ".battle", false) ? FightScreen.Battle : FightScreen.Preparation);
        }
    }
}
