using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.UI;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Exercises real coroutines and cancellation without launching a camera workout.</summary>
    [InitializeOnLoad]
    public static class BossMapPlayValidation
    {
        private const string Key = "PushStars.BossMapValidation";
        private static BossMapController _map;
        private static double _due;
        private static int _step;
        private static Vector3 _restScale;
        private static Vector2 _restPosition;
        private static bool _runInBackground;

        static BossMapPlayValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    _runInBackground = Application.runInBackground;
                    Application.runInBackground = true;
                    _step = 0; _due = EditorApplication.timeSinceStartup + 1.5; EditorApplication.update += Tick;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    if (SessionState.GetBool(Key + ".hadMode", false)) PlayerPrefs.SetInt("selected_game_mode", SessionState.GetInt(Key + ".mode", 0));
                    else PlayerPrefs.DeleteKey("selected_game_mode");
                    PlayerPrefs.Save(); SessionState.SetBool(Key, false);
                }
            };
        }

        [MenuItem("Tools/Push Stars/Validate Boss Map in Play Mode")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play Mode.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != AuthoredScenes.MainPath)
                throw new InvalidOperationException("Open Main before this check.");
            Directory.CreateDirectory("output/boss-map");
            SessionState.SetBool(Key + ".hadMode", PlayerPrefs.HasKey("selected_game_mode"));
            SessionState.SetInt(Key + ".mode", PlayerPrefs.GetInt("selected_game_mode"));
            SessionState.SetBool(Key, true);
            SelectedGameMode.Current = GameMode.Boss;
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _due) return;
            _due = EditorApplication.timeSinceStartup + .4;
            try
            {
                switch (_step++)
                {
                    case 0:
                        _map = UnityEngine.Object.FindFirstObjectByType<BossMapController>();
                        Require(_map != null && _map.Home.activeInHierarchy, "Boss home is visible in Play");
                        Require(_map.CharacterDecor.All(go => !go.activeInHierarchy), "Character decor still visible: " + string.Join(", ", _map.CharacterDecor.Where(go => go.activeInHierarchy).Select(go => go.name)));
                        _restScale = _map.Island.transform.localScale;
                        _restPosition = ((RectTransform)_map.Island.transform).anchoredPosition;
                        _map.Island.onClick.Invoke(); _map.Island.onClick.Invoke();
                        Require(!_map.IsMapOpen, "Opening waits for press feedback");
                        _due = EditorApplication.timeSinceStartup + .07;
                        break;
                    case 1:
                        Require(!_map.IsMapOpen, "Sheet stays closed while island is pressed");
                        Require(_map.Island.transform.localScale.x < _restScale.x, "Island visibly sinks");
                        break;
                    case 2:
                        Require(_map.IsMapOpen, "Map opens after return");
                        Require(_map.Island.transform.localScale == _restScale && ((RectTransform)_map.Island.transform).anchoredPosition == _restPosition, "Island pose restored exactly");
                        _map.Scroll.verticalNormalizedPosition = 1;
                        Require(_map.Scroll.verticalNormalizedPosition > .99f, "Map scrolls to the chest");
                        _map.Close.onClick.Invoke();
                        Require(_map.IsMapOpen, "OK finishes press before closing");
                        break;
                    case 3:
                        Require(!_map.IsMapOpen && _map.Home.activeInHierarchy && _map.BottomNav.activeInHierarchy, "OK restores home");
                        _map.OpenMap();
                        _map.enabled = false;
                        Require(_map.Island.transform.localScale == _restScale, "Disable cancels press and restores scale");
                        _map.enabled = true;
                        break;
                    case 4:
                        Require(_map.Home.activeInHierarchy && !_map.IsMapOpen, "Re-enable recovers home");
                        SelectedGameMode.Current = GameMode.Pvp;
                        break;
                    case 5:
                        Require(!_map.Home.activeInHierarchy && !_map.Background.activeSelf, "PVP restores normal background");
                        Require(_map.CharacterDecor.First(go => go.name == "CharacterArea").activeInHierarchy, "PVP restores avatar");
                        File.WriteAllText("output/boss-map/play-validation.txt", "PASS: real press/return/open order, duplicate tap, scrolling, OK, cancellation, re-enable, mode restoration.\n");
                        Finish();
                        break;
                }
            }
            catch (Exception e)
            { File.WriteAllText("output/boss-map/play-validation.txt", "FAIL: " + e); Finish(); }
        }
        private static void Finish()
        { Application.runInBackground = _runInBackground; EditorApplication.update -= Tick; EditorApplication.isPlaying = false; }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
