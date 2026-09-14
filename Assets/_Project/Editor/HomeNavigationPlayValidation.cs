using System;
using System.IO;
using PushStars.Core;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>Exercises real preparation HOME without starting CV, a battle or rewards.</summary>
    [InitializeOnLoad]
    public static class HomeNavigationPlayValidation
    {
        private const string Key = "PushStars.ValidatePreparationHome";
        private static int _step;
        private static double _due;
        private static bool _background;

        static HomeNavigationPlayValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    _background = Application.runInBackground; Application.runInBackground = true;
                    _step = 0; _due = EditorApplication.timeSinceStartup + 1.5;
                    EditorApplication.update += Tick;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    if (SessionState.GetBool(Key + ".hadMode", false)) PlayerPrefs.SetInt("selected_game_mode", SessionState.GetInt(Key + ".mode", 0));
                    else PlayerPrefs.DeleteKey("selected_game_mode");
                    PlayerPrefs.Save(); SessionState.SetBool(Key, false);
                }
            };
        }

        [MenuItem("Tools/Push Stars/Validate Preparation HOME in Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != AuthoredScenes.MainPath)
                throw new InvalidOperationException("Run from Main outside Play mode.");
            Directory.CreateDirectory("output/home-navigation");
            SessionState.SetBool(Key + ".hadMode", PlayerPrefs.HasKey("selected_game_mode"));
            SessionState.SetInt(Key + ".mode", PlayerPrefs.GetInt("selected_game_mode"));
            SessionState.SetBool(Key, true); SelectedGameMode.Current = GameMode.Boss;
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _due) return;
            _due = EditorApplication.timeSinceStartup + 1.5;
            try
            {
                switch (_step++)
                {
                    case 0:
                        FightRequest.Boss();
                        FightScreenNavigation.Navigate(FightScreen.Preparation);
                        break;
                    case 1:
                        if (FightScreenNavigation.IsPreview) throw new InvalidOperationException("Must exercise the real, non-preview HOME route.");
                        var preparation = UnityEngine.Object.FindFirstObjectByType<PreparationScreen>();
                        if (preparation == null) throw new InvalidOperationException("Preparation did not load.");
                        var home = (Button)new SerializedObject(preparation).FindProperty("_homeButton").objectReferenceValue;
                        if (home == null) throw new InvalidOperationException("HOME button is missing.");
                        home.onClick.Invoke();
                        break;
                    case 2:
                        if (SceneManager.GetActiveScene().path != AuthoredScenes.MainPath)
                            throw new InvalidOperationException("HOME loaded " + SceneManager.GetActiveScene().path);
                        var map = UnityEngine.Object.FindFirstObjectByType<BossMapController>();
                        if (map == null || !map.Home.activeInHierarchy || FightRequest.HasRequest)
                            throw new InvalidOperationException("Current boss home/request reset check failed.");
                        File.WriteAllText("output/home-navigation/validation.txt", "PASS: Main -> real Boss preparation -> actual HOME button -> authored Main with boss island; fight request cleared; no OTA MainRemote loaded.\n");
                        Finish();
                        break;
                }
            }
            catch (Exception e)
            { File.WriteAllText("output/home-navigation/validation.txt", "FAIL: " + e); Finish(); }
        }

        private static void Finish()
        { Application.runInBackground = _background; EditorApplication.update -= Tick; EditorApplication.isPlaying = false; }
    }
}
