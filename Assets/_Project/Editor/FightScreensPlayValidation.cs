using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PushStars.Core;
using PushStars.Fight;
using PushStars.UI.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Loads the seven authored scenes in Play Mode and exercises the optional case route.
    /// Run in a disposable Unity process with graphics and without -quit; Run exits on completion.</summary>
    [InitializeOnLoad]
    public static class FightScreensPlayValidation
    {
        private const string Running = "PushStars.FightScreensValidation.Running";
        private const string BackupKey = "PushStars.FightScreensValidation.Preferences";
        private const string Output = "Logs/fight-screens";
        private static readonly FightScreen[] Screens =
        {
            FightScreen.Preparation, FightScreen.Battle, FightScreen.Results, FightScreen.RewardSummary,
            FightScreen.CaseAward, FightScreen.CaseOpening, FightScreen.CaseReward
        };
        private static readonly string[] Names =
        {
            "preparation", "battle", "results", "reward-summary", "case-award", "case-opening", "case-reward"
        };
        [Serializable] private sealed class Preference
        {
            public string Key, Value;
            public bool Exists, IsInt;
            public int IntValue;
        }
        [Serializable] private sealed class Preferences { public List<Preference> Values = new List<Preference>(); }

        private static double _due, _deadline;
        private static int _screenIndex, _stage, _demoStep;
        private static Camera _camera;
        private static Canvas _canvas;
        private static RenderTexture _texture;
        private static StringBuilder _report;

        static FightScreensPlayValidation() => EditorApplication.playModeStateChanged += StateChanged;

        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start scene validation outside Play Mode.");
            Directory.CreateDirectory(Output);
            _report = new StringBuilder("Authored fight scenes Play validation\n");
            SessionState.SetString(BackupKey, JsonUtility.ToJson(SnapshotPreferences()));
            try
            {
                CaseRewardsRegression.Run();
                FightFlowRegression.Run();
                ScreenLayoutRegression.Run();
                SessionState.SetBool(Running, true);
                SessionState.EraseString(FightController.ScreenPreviewKey);
                FightRequest.Clear();
                EditorSceneManager.OpenScene(FightScreenNavigation.ScenePath(FightScreen.Preparation));
                EditorApplication.isPlaying = true;
            }
            catch (Exception exception)
            {
                _report.AppendLine("FAIL: " + exception);
                Finish(1);
                if (!Application.isBatchMode) throw;
            }
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Running, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _screenIndex = _stage = _demoStep = 0;
                _report = new StringBuilder("Authored fight scenes Play validation\n");
                _deadline = EditorApplication.timeSinceStartup + 150;
                _due = EditorApplication.timeSinceStartup + 2;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _report?.AppendLine("FAIL: Play Mode was stopped before validation completed.");
                Finish(1);
            }
        }

        private static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Scene preview timed out.");
                if (EditorApplication.timeSinceStartup < _due) return;
                if (_screenIndex < Screens.Length) CaptureNextScene();
                else ExerciseOptionalCase();
            }
            catch (Exception exception)
            {
                _report?.AppendLine("FAIL: " + exception);
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static void CaptureNextScene()
        {
            FightScreen screen = Screens[_screenIndex];
            if (_stage == 0)
            {
                // The initial preparation scene was opened directly before entering Play.
                if (_screenIndex > 0) FightScreenNavigation.Preview(screen);
                _stage = 1;
                Delay(1.8);
            }
            else if (_stage == 1)
            {
                RequireScene(screen);
                Require(FightScreenNavigation.IsPreview, "Direct scene preview activated real gameplay.");
                PrepareCapture();
                _stage = 2;
                Delay(.4);
            }
            else
            {
                Capture(Names[_screenIndex]);
                AssertPreferencesUnchanged();
                _screenIndex++;
                _stage = 0;
            }
        }

        private static void ExerciseOptionalCase()
        {
            switch (_demoStep)
            {
                case 0:
                    FightScreenNavigation.Preview(FightScreen.RewardSummary);
                    Delay(1.5);
                    break;
                case 1:
                    RequireRewardScreen(FightScreen.RewardSummary).Home();
                    Delay(1.5);
                    break;
                case 2:
                    RequireScene(FightScreen.Home);
                    _report.AppendLine("PASS: summary can return home without visiting a case scene.");
                    FightScreenNavigation.Preview(FightScreen.CaseAward);
                    Delay(1);
                    break;
                case 3:
                    RequireRewardScreen(FightScreen.CaseAward).Home();
                    Delay(1.5);
                    break;
                case 4:
                    RequireScene(FightScreen.Home);
                    _report.AppendLine("PASS: awarded case can be left for later without forced opening.");
                    FightScreenNavigation.Preview(FightScreen.CaseOpening);
                    Delay(1);
                    break;
                case 5:
                case 6:
                case 7:
                    var opening = RequireRewardScreen(FightScreen.CaseOpening);
                    Invoke(opening.CaseUi.TapButton, "case upgrade");
                    Require((int)FightScreenNavigation.PreviewRarity == _demoStep - 4,
                        "Preview tap did not consume exactly one upgrade opportunity.");
                    Delay(.85);
                    break;
                case 8:
                    Invoke(RequireRewardScreen(FightScreen.CaseOpening).CaseUi.ActionButton, "open case");
                    Delay(1.5);
                    break;
                case 9:
                    var prize = RequireRewardScreen(FightScreen.CaseReward);
                    Require(prize.PrizeUi.Amount != null && prize.PrizeUi.Amount.text == "×250",
                        "Opened demo case did not preserve its legendary rarity and 250-gem prize across scene load.");
                    PrepareCapture();
                    Delay(.4);
                    break;
                case 10:
                    Capture("case-upgraded-prize");
                    var claim = RequireRewardScreen(FightScreen.CaseReward).PrizeUi.ClaimButton;
                    Invoke(claim, "claim prize");
                    // A duplicated UI callback must not start another payout or queued case.
                    claim.onClick.Invoke();
                    Delay(1.5);
                    break;
                default:
                    RequireScene(FightScreen.Home);
                    Require(Object.FindObjectsByType<RewardScreen>(FindObjectsSortMode.None).Length == 0,
                        "Claim automatically opened another reward screen instead of returning home.");
                    AssertPreferencesUnchanged();
                    _report.AppendLine("PASS: three upgrades, opening into CaseReward, repeated claim, return home without a pending-case loop.");
                    _report.AppendLine("PASS: saved case ledger (including daily quota), XP, profile, onboarding and device layouts unchanged.");
                    _report.AppendLine("RESULT: PASS. Seven independent scenes and optional case routes checked.");
                    Finish(0);
                    return;
            }
            _demoStep++;
        }

        private static RewardScreen RequireRewardScreen(FightScreen screen)
        {
            RequireScene(screen);
            var reward = Object.FindObjectsByType<RewardScreen>(FindObjectsSortMode.None)
                .SingleOrDefault(item => item.gameObject.scene == SceneManager.GetActiveScene());
            Require(reward != null && reward.Screen == screen, "Missing active authored RewardScreen: " + screen);
            return reward;
        }

        private static void RequireScene(FightScreen screen)
        {
            string expected = FightScreenNavigation.SceneName(screen);
            Require(SceneManager.GetActiveScene().name == expected,
                "Expected scene " + expected + ", found " + SceneManager.GetActiveScene().name);
        }

        private static void Invoke(Button button, string action)
        {
            Require(button != null && button.gameObject.activeInHierarchy && button.interactable,
                "Authored button is unavailable: " + action);
            button.onClick.Invoke();
        }

        private static void PrepareCapture()
        {
            DisposeCapture();
            Scene active = SceneManager.GetActiveScene();
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(item => item.gameObject.scene == active && item.isRootCanvas && item.enabled).ToArray();
            Require(canvases.Length > 0, "Active scene has no authored root Canvas: " + active.name);
            _canvas = canvases.OrderByDescending(item => item.GetComponentsInChildren<Graphic>().Length).First();
            _texture = new RenderTexture(390, 844, 24, RenderTextureFormat.ARGB32);
            _texture.Create();
            _camera = new GameObject("PreviewCaptureCamera", typeof(Camera)).GetComponent<Camera>();
            _camera.transform.position = new Vector3(0, 0, -50);
            _camera.orthographic = true;
            _camera.orthographicSize = 422;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.cullingMask = 1 << 5;
            _camera.targetTexture = _texture;
            foreach (var canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _camera;
                canvas.planeDistance = 10;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null) scaler.enabled = false;
                canvas.scaleFactor = 1;
            }
            foreach (var layout in Object.FindObjectsByType<ScreenLayoutRoot>(FindObjectsSortMode.None))
                layout.ShowEditButton = false;
            Canvas.ForceUpdateCanvases();
        }

        private static void Capture(string name)
        {
            Require(_canvas != null && _camera != null, "Capture objects were lost across a scene transition.");
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.gameObject.scene == SceneManager.GetActiveScene())
                    foreach (var rect in canvas.GetComponentsInChildren<RectTransform>(true)) rect.gameObject.layer = 5;
            foreach (var avatar in Object.FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
                if (avatar.StageCamera != null) avatar.StageCamera.Render();
            Canvas.ForceUpdateCanvases();
            _camera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(390, 844, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = _texture;
                image.ReadPixels(new Rect(0, 0, 390, 844), 0, 0);
                image.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
            var roots = Object.FindObjectsByType<ScreenLayoutRoot>(FindObjectsSortMode.None)
                .Where(item => item.gameObject.scene == SceneManager.GetActiveScene() && item.AvailableForEditing).ToArray();
            Require(roots.Length > 0 && roots.All(item => item.IsSceneAuthored), "Captured UI is not an authored editable scene.");
            _report.AppendLine("PASS " + SceneManager.GetActiveScene().name + ": " + roots.Sum(item => item.Targets.Count) +
                " editable elements; screenshot " + name + ".png");
        }

        private static Preferences SnapshotPreferences()
        {
            var snapshot = new Preferences();
            foreach (string key in new[] { "rewards.case_ledger.v1", "pending_xp" }
                .Concat(Names.Select(name => "PushStars.ScreenLayout.v1." + name))
                .Concat(new[] { "PushStars.ScreenLayout.v1.battle-solo" }))
                snapshot.Values.Add(new Preference { Key = key, Exists = PlayerPrefs.HasKey(key), Value = PlayerPrefs.GetString(key, "") });
            foreach (string key in new[] { "profile.trophies", "profile.best_reps", "profile.total_reps", "profile.seeded",
                "profile.wins", "profile.losses", "boss_progress", "onboarding.intro_seen", "onboarding.level_test_done",
                "onboarding.level_test_reps", "onboarding.level_test_skipped", "character.gender" })
                snapshot.Values.Add(new Preference { Key = key, Exists = PlayerPrefs.HasKey(key), IsInt = true, IntValue = PlayerPrefs.GetInt(key, 0) });
            return snapshot;
        }

        private static bool Changed(Preference item) => PlayerPrefs.HasKey(item.Key) != item.Exists ||
            (item.IsInt ? PlayerPrefs.GetInt(item.Key, 0) != item.IntValue : PlayerPrefs.GetString(item.Key, "") != item.Value);

        private static void AssertPreferencesUnchanged()
        {
            var backup = JsonUtility.FromJson<Preferences>(SessionState.GetString(BackupKey, ""));
            Require(backup != null, "Preference snapshot is missing.");
            foreach (var item in backup.Values) Require(!Changed(item), "Preview changed saved player data: " + item.Key);
        }

        private static void DisposeCapture()
        {
            if (_camera != null) Object.DestroyImmediate(_camera.gameObject);
            if (_texture != null) { _texture.Release(); Object.DestroyImmediate(_texture); }
            _camera = null; _texture = null; _canvas = null;
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            SessionState.EraseBool(Running);
            // Restore a changed protected preference only on failure; successful previews never write it.
            var json = SessionState.GetString(BackupKey, "");
            var backup = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<Preferences>(json);
            bool restored = false;
            if (backup != null)
                foreach (var item in backup.Values.Where(Changed))
                {
                    code = 1; restored = true;
                    _report?.AppendLine("FAIL: restored unexpectedly changed saved player data: " + item.Key);
                    if (!item.Exists) PlayerPrefs.DeleteKey(item.Key);
                    else if (item.IsInt) PlayerPrefs.SetInt(item.Key, item.IntValue);
                    else PlayerPrefs.SetString(item.Key, item.Value);
                }
            if (restored) PlayerPrefs.Save();
            DisposeCapture();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/validation.txt", _report?.ToString() ?? "Validation did not initialize.");
            SessionState.EraseString(BackupKey);
            SessionState.EraseString(FightController.ScreenPreviewKey);
            if (Application.isBatchMode) EditorApplication.Exit(code);
            else EditorApplication.isPlaying = false;
        }

        private static void Delay(double seconds) => _due = EditorApplication.timeSinceStartup + seconds;
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
