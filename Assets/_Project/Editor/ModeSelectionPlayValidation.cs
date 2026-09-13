using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Run in an isolated Unity project, with graphics and without -quit.</summary>
    [InitializeOnLoad]
    public static class ModeSelectionPlayValidation
    {
        private const string Key = "PushStars.ModeValidation";
        private const string Output = "Logs/mode-selection";
        private static int _step;
        private static double _due;
        private static ModeSelectionController _controller;
        private static Camera _camera;
        private static RenderTexture _texture;
        private static Canvas _canvas;
        private static Vector2 _skullPosition;
        private static BattleSettingsController _settings;
        private static string _runtimeError;
        private static TrainingSettingsController _trainingSettings;
        private static double _loadDeadline;
        private static TrainingProgress _workout;
        private static float _pausedRest;

        static ModeSelectionPlayValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                _step = 0; _due = EditorApplication.timeSinceStartup + 2;
                _runtimeError = null;
                Application.logMessageReceived += CaptureError;
                EditorApplication.update += Tick;
            };
        }

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/validation.txt", "Mode selector Play validation\n");
            SessionState.SetInt(Key + ".selection", PlayerPrefs.GetInt("selected_game_mode", -1));
            SessionState.SetInt(Key + ".sets", PlayerPrefs.GetInt("training.sets", -100));
            SessionState.SetInt(Key + ".rest", PlayerPrefs.GetInt("training.rest", -100));
            TrainingSettingsRegression.Run();
            ModeSelectionSceneSetup.Run();
            BattleSettingsSceneSetup.Run();
            TrainingSettingsSceneSetup.Run();
            EditorSceneManager.OpenScene(AuthoredScenes.MainPath);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                if (_runtimeError != null) throw new Exception(_runtimeError);
                switch (_step++)
                {
                    case 0:
                        _controller = Object.FindObjectOfType<ModeSelectionController>();
                        Require(_controller != null && !_controller.IsOpen, "Starts closed");
                        Prepare(390, 844);
                        Click("PvpButton");
                        break;
                    case 1:
                        Require(_controller.IsOpen, "Home button opens selector");
                        _skullPosition = Find("Skull_0_1").GetComponent<RectTransform>().anchoredPosition;
                        Capture("selector-390x844");
                        Click("pvpInfo");
                        break;
                    case 2:
                        Require(_controller.IsInfoOpen, "Info opens without selecting or closing");
                        Require(Find("Description").GetComponent<TextMeshProUGUI>().text.Contains("75"), "PVP details include live counter definition");
                        Capture("info-pvp");
                        _controller.Back();
                        Require(_controller.IsOpen && !_controller.IsInfoOpen, "Back first dismisses info");
                        Require(Find("Skull_0_1").GetComponent<RectTransform>().anchoredPosition != _skullPosition, "Skulls move over time");
                        Click("bossInfo");
                        break;
                    case 3:
                        Require(Find("Description").GetComponent<TextMeshProUGUI>().text.Contains("босса"), "Boss has its own description");
                        _controller.CloseInfo();
                        Click("bossCard");
                        Require(SelectedGameMode.Current == GameMode.Boss, "Boss selection is persisted");
                        break;
                    case 4:
                        Require(!_controller.IsOpen, "Selection closes sheet");
                        Require(Find("PvpButton").GetComponentInChildren<TextMeshProUGUI>().text == "BOSS", "Home label follows selected mode");
                        Click("PvpButton");
                        break;
                    case 5:
                        Click("trainingInfo");
                        Require(Find("Description").GetComponent<TextMeshProUGUI>().text.Contains("без соперника"), "Training has its own description");
                        _controller.CloseInfo();
                        Click("trainingCard");
                        Require(SelectedGameMode.Current == GameMode.Training, "Training selectable");
                        break;
                    case 6:
                        Require(Find("BattleButton").GetComponentInChildren<TextMeshProUGUI>().text == "START", "Training action says START");
                        Require(Find("PushupButton").GetComponentInChildren<TextMeshProUGUI>().text == "SETTINGS", "Training settings label");
                        Require(Find("PushupButton").transform.Find("Icon").GetComponent<Image>().sprite.name == "training-settings-icon", "Training uses supplied gear");
                        Require(Find("PvpButton").transform.Find("Icon").GetComponent<RectTransform>().sizeDelta == new Vector2(54, 54), "Training dumbbell is reduced");
                        Capture("training-home");
                        Click("PvpButton");
                        Prepare(320, 568);
                        break;
                    case 7:
                        AssertFits(); Capture("selector-320x568");
                        Prepare(844, 390);
                        break;
                    case 8:
                        AssertFits(); Capture("selector-landscape");
                        _controller.Hide(); _controller.Show(); _controller.Hide(); _controller.Show();
                        Prepare(390, 844);
                        break;
                    case 9:
                        Require(_controller.IsOpen, "Rapid open/close finishes open");
                        Click("pvpCard");
                        Require(SelectedGameMode.Current == GameMode.Pvp, "PVP selectable");
                        break;
                    case 10:
                        Require(Find("BattleButton").GetComponentInChildren<TextMeshProUGUI>().text == "BATTLE" && Find("PushupButton").GetComponentInChildren<TextMeshProUGUI>().text == "PUSHUP", "PVP restores battle and exercise labels");
                        Require(Find("PushupButton").transform.Find("Icon").GetComponent<Image>().sprite.name != "training-settings-icon", "PVP restores exercise icon");
                        Click("PvpButton");
                        break;
                    case 11:
                        Require(Find("OnlineCount").GetComponent<TextMeshProUGUI>().text == "—", "Unavailable backend does not invent online players");
                        Click("Dimmer");
                        break;
                    case 12:
                        Require(!_controller.IsOpen, "Backdrop closes selector");
                        _settings = Object.FindObjectOfType<BattleSettingsController>();
                        Require(_settings != null && !_settings.IsOpen, "Settings start closed");
                        SelectedGameMode.Current = GameMode.Boss;
                        Click("PushupButton");
                        break;
                    case 13:
                        Require(_settings.IsOpen && Find("SettingsTitle").GetComponent<TextMeshProUGUI>().text == "BOSS SETTINGS", "Right button opens boss settings");
                        Require(!Find("SquatsLocked").GetComponent<Button>().IsInteractable() && !Find("PullupLocked").GetComponent<Button>().IsInteractable(), "Squats and pullups remain locked");
                        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                        ExecuteEvents.Execute(Find("SquatsLocked"), pointer, ExecuteEvents.pointerClickHandler);
                        Require(_settings.IsOpen && SelectedGameMode.Current == GameMode.Boss, "Locked card cannot change selection or dismiss settings");
                        _skullPosition = Find("SettingsSkull_0_1").GetComponent<RectTransform>().anchoredPosition;
                        Capture("boss-settings");
                        break;
                    case 14:
                        Require(Find("SettingsSkull_0_1").GetComponent<RectTransform>().anchoredPosition != _skullPosition, "Settings skulls drift");
                        Click("PushupExercise");
                        Require(!_settings.IsOpen && !FightRequest.HasRequest, "Selecting pushups closes settings without starting a battle");
                        break;
                    case 15:
                        SelectedGameMode.Current = GameMode.Pvp;
                        Click("PushupButton");
                        break;
                    case 16:
                        Require(_settings.IsOpen && Find("SettingsTitle").GetComponent<TextMeshProUGUI>().text == "PVP 1V1 SETTINGS", "Same settings support PVP 1v1");
                        Capture("pvp-settings");
                        Prepare(320, 568);
                        break;
                    case 17:
                        AssertFits("SettingsSheet"); Capture("settings-320x568");
                        Prepare(844, 390);
                        break;
                    case 18:
                        AssertFits("SettingsSheet"); Capture("settings-landscape");
                        _settings.Hide(); _settings.Show(); _settings.Hide(); _settings.Show();
                        Prepare(390, 844);
                        break;
                    case 19:
                        Require(_settings.IsOpen, "Settings recover after rapid reopening");
                        Click("SettingsClose");
                        break;
                    case 20:
                        Require(!_settings.IsOpen, "Settings handle closes sheet");
                        Click("PushupButton");
                        break;
                    case 21:
                        Click("SettingsDimmer");
                        break;
                    case 22:
                        Require(!_settings.IsOpen, "Settings backdrop closes sheet");
                        new TrainingPlan(3, 60).Save();
                        _controller.Select(GameMode.Training);
                        _trainingSettings = Object.FindObjectOfType<TrainingSettingsController>();
                        Click("PushupButton");
                        break;
                    case 23:
                        Require(_trainingSettings.IsOpen && !_settings.IsOpen, "Training opens its own settings");
                        Require(Find("TrainingEstimate").GetComponent<TextMeshProUGUI>().text == "~5 min.", "Default 3 sets include two rest intervals");
                        Capture("training-settings");
                        for (int i = 0; i < 20; i++) _trainingSettings.ChangeSets(-1);
                        Require(_trainingSettings.Plan.Sets == 1 && !Find("TrainingMinus").GetComponent<Button>().IsInteractable(), "Set lower bound enforced");
                        for (int i = 0; i < 20; i++) _trainingSettings.ChangeSets(1);
                        Require(_trainingSettings.Plan.Sets == 10 && !Find("TrainingPlus").GetComponent<Button>().IsInteractable(), "Set upper bound enforced");
                        _trainingSettings.ChangeSets(-7);
                        Click("TrainingRest3");
                        Require(!_trainingSettings.Plan.EstimatedSeconds.HasValue, "Manual rest does not invent a duration");
                        Capture("training-settings-manual");
                        _trainingSettings.Hide();
                        break;
                    case 24:
                        Click("PushupButton");
                        break;
                    case 25:
                        Require(_trainingSettings.Plan.IsManualRest && _trainingSettings.Plan.Sets == 3, "Workout settings survive reopening");
                        Require(!Find("TrainingSquats").GetComponent<Button>().IsInteractable() && !Find("TrainingPullup").GetComponent<Button>().IsInteractable(), "Other training exercises stay locked");
                        Click("TrainingRest0");
                        Require(_trainingSettings.Plan.RestSeconds == 30, "Thirty-second rest selectable");
                        Click("TrainingRest2");
                        Require(_trainingSettings.Plan.RestSeconds == 90, "Ninety-second rest selectable");
                        Click("TrainingRest1");
                        Prepare(320, 568);
                        break;
                    case 26:
                        AssertFits("TrainingSheet"); Capture("training-settings-320x568");
                        Prepare(844, 390);
                        break;
                    case 27:
                        AssertFits("TrainingSheet"); Capture("training-settings-landscape");
                        _trainingSettings.Hide(); _trainingSettings.Show();
                        Prepare(390, 844);
                        break;
                    case 28:
                        Require(_trainingSettings.IsOpen, "Training settings recover after reopening");
                        Click("TrainingStart");
                        Require(FightRequest.Mode == FightMode.Training && FightRequest.Workout.Sets == 3 && FightRequest.Workout.RestSeconds == 60,
                            "START snapshots the selected workout into the fight request");
                        _loadDeadline = EditorApplication.timeSinceStartup + 30;
                        break;
                    case 29:
                        var fight = Object.FindObjectOfType<PushStars.Fight.FightController>();
                        if (fight == null && EditorApplication.timeSinceStartup < _loadDeadline) { _step--; break; }
                        Require(fight != null, "START loads the fight controller through the configured scene route");
                        var mode = typeof(PushStars.Fight.FightController).GetField("_mode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        Require((FightMode)mode.GetValue(fight) == FightMode.Training, "Fight initializes the training mode");
                        var trainingScreen = Object.FindObjectOfType<PushStars.Fight.TrainingScreen>();
                        var trainingSetLabel = new SerializedObject(trainingScreen).FindProperty("_set").objectReferenceValue as TextMeshProUGUI;
                        Require(trainingSetLabel.text == "1/3", "Workout starts with the first of three sets");
                        Require(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == FightConfig.TrainingSceneName, "Training has its dedicated scene");
                        _canvas = Object.FindObjectOfType<PushStars.Fight.TrainingScreen>().GetComponent<Canvas>();
                        Prepare(390, 844);
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                        _workout = (TrainingProgress)fight.GetType().GetField("_training", flags).GetValue(fight);
                        // Exercise the real UI/controller bindings without granting test rewards.
                        _workout.CompleteSet();
                        var phase = fight.GetType().GetField("_phase", flags); phase.SetValue(fight, Enum.Parse(phase.FieldType, "Rest"));
                        ((Behaviour)fight.GetType().GetField("_session", flags).GetValue(fight)).enabled = false;
                        break;
                    case 30:
                        Require(Object.FindObjectOfType<PushStars.Fight.TrainingScreen>().CurrentView == PushStars.Fight.TrainingScreen.View.Rest, "Completed set shows the rest screen");
                        float restBefore = _workout.RestRemaining;
                        Click("AddRest"); Require(Mathf.Abs(_workout.RestRemaining - restBefore - 15) < .01f, "+15 sec extends actual rest");
                        Click("Pause"); _pausedRest = _workout.RestRemaining;
                        break;
                    case 31:
                        Require(Mathf.Abs(_workout.RestRemaining - _pausedRest) < .01f, "Pause freezes actual rest countdown");
                        Click("Resume"); Click("StartSet");
                        Require(!_workout.IsResting && _workout.CurrentSet == 2, "START SET resumes the next actual set");
                        Finish(0); return;
                }
                _due = EditorApplication.timeSinceStartup + .65;
            }
            catch (Exception exception)
            {
                File.AppendAllText(Output + "/validation.txt", "FAIL: " + exception + "\n");
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static void AssertFits(string name = "Sheet")
        {
            var sheet = Find(name).GetComponent<RectTransform>();
            Vector3[] corners = new Vector3[4]; sheet.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                Vector3 point = _camera.WorldToViewportPoint(corner);
                Require(point.x >= -.001 && point.x <= 1.001 && point.y >= -.001 && point.y <= 1.001,
                    "Sheet fits " + _texture.width + "x" + _texture.height);
            }
        }

        private static GameObject Find(string name) => _canvas.GetComponentsInChildren<Transform>(true).First(t => t.name == name).gameObject;
        private static void Click(string name)
        {
            var go = Find(name);
            var button = go.GetComponent<Button>();
            Require(go.activeInHierarchy && button.IsInteractable(), name + " is clickable");
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.Execute(go, pointer, ExecuteEvents.pointerClickHandler);
        }

        private static void Prepare(int width, int height)
        {
            if (_canvas == null) _canvas = Object.FindObjectsOfType<Canvas>().First(c => c.isRootCanvas);
            if (_camera == null)
            {
                _camera = new GameObject("ModeCapture", typeof(Camera)).GetComponent<Camera>();
                _camera.transform.position = new Vector3(0, 0, -50);
                _camera.orthographic = true;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = Color.black;
                _camera.cullingMask = 1 << 5;
            }
            if (_texture != null) { _camera.targetTexture = null; _texture.Release(); Object.Destroy(_texture); }
            _texture = new RenderTexture(width, height, 24); _texture.Create();
            _camera.targetTexture = _texture; _camera.orthographicSize = height / 2f;
            _canvas.renderMode = RenderMode.ScreenSpaceCamera; _canvas.worldCamera = _camera; _canvas.planeDistance = 10;
            _canvas.GetComponent<CanvasScaler>().enabled = false; _canvas.scaleFactor = 1;
            Canvas.ForceUpdateCanvases();
        }

        private static void Capture(string name)
        {
            foreach (var rect in _canvas.GetComponentsInChildren<RectTransform>(true)) rect.gameObject.layer = 5;
            Canvas.ForceUpdateCanvases(); _camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = _texture;
            var image = new Texture2D(_texture.width, _texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, _texture.width, _texture.height), 0, 0); image.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            Object.Destroy(image); RenderTexture.active = previous;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            File.AppendAllText(Output + "/validation.txt", "PASS: " + message + "\n");
        }

        private static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CaptureError;
            SessionState.SetBool(Key, false);
            int selection = SessionState.GetInt(Key + ".selection", -1);
            if (selection == -1) PlayerPrefs.DeleteKey("selected_game_mode"); else PlayerPrefs.SetInt("selected_game_mode", selection);
            PlayerPrefs.Save();
            foreach (string preference in new[] { "sets", "rest" })
            {
                int saved = SessionState.GetInt(Key + "." + preference, -100);
                if (saved == -100) PlayerPrefs.DeleteKey("training." + preference); else PlayerPrefs.SetInt("training." + preference, saved);
            }
            PlayerPrefs.Save();
            File.AppendAllText(Output + "/validation.txt", "RESULT: " + (code == 0 ? "PASS" : "FAIL") + "\n");
            EditorApplication.Exit(code);
        }

        private static void CaptureError(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) _runtimeError = message;
        }
    }
}
