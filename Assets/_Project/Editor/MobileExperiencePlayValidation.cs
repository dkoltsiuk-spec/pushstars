using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Onboarding, main tabs, profile and settings using real scene presenters.
    /// OS camera permission and cloud account actions are intentionally not invoked.</summary>
    [InitializeOnLoad]
    public static class MobileExperiencePlayValidation
    {
        const string Key = "PushStars.MobileExperienceQA", Output = "output/mobile-audit/experience";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] class Preference { public string key, value; public int number; public bool exists, isInt; }
        [Serializable] class Backup { public List<Preference> items = new List<Preference>(); }
        static int _step, _page, _size;
        static double _due, _deadline;
        static string _error;
        static bool _background;
        static OnboardingController _intro;
        static PushStars.Fight.GuidedOnboardingController _guide;
        static MainShellView _shell;
        static ProfileIdentityEditor _identity;
        static SettingsScreen _settings;
        static ProfileSettingsActions _actions;
        static Canvas _canvas;
        static Camera _camera;
        static RenderTexture _texture;
        static readonly Vector2Int[] Sizes = { new Vector2Int(320, 568), new Vector2Int(390, 844), new Vector2Int(430, 932) };

        static MobileExperiencePlayValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                _step = _page = _size = 0; _error = null;
                _due = EditorApplication.timeSinceStartup + 2;
                _deadline = EditorApplication.timeSinceStartup + 180;
                _background = Application.runInBackground;
                Application.runInBackground = true;
                Application.logMessageReceived += OnLog;
                EditorApplication.update += Tick;
            };
        }

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/validation.txt", "Mobile experience Play validation\n");
            var backup = new Backup();
            foreach (var key in new[] { "character.gender", "onboarding.intro_seen", "onboarding.level_test_done",
                "onboarding.level_test_reps", "onboarding.level_test_skipped", "settings.sound", "settings.vibration",
                "settings.notifications", "selected_game_mode" })
                backup.items.Add(new Preference { key = key, exists = PlayerPrefs.HasKey(key), isInt = true, number = PlayerPrefs.GetInt(key) });
            backup.items.Add(new Preference { key = "settings.language", exists = PlayerPrefs.HasKey("settings.language"), value = PlayerPrefs.GetString("settings.language") });
            SessionState.SetString(Key + ".prefs", JsonUtility.ToJson(backup));
            new PlayerPrefsSettingsStore().Language = PlayerPrefsSettingsStore.LangEn;
            SessionState.SetBool(Key, true);
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Onboarding.unity");
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _due) return;
            _due = EditorApplication.timeSinceStartup + .65;
            try
            {
                Require(_error == null, _error ?? "Runtime has no errors");
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Experience validation timed out");
                switch (_step)
                {
                    case 0:
                        _guide = Object.FindFirstObjectByType<PushStars.Fight.GuidedOnboardingController>();
                        if (_guide != null) { Prepare(Sizes[1]); _step = 20; break; }
                        _intro = Object.FindFirstObjectByType<OnboardingController>();
                        Require(_intro != null, "Onboarding starts");
                        Prepare(Sizes[_size]); Invoke(_intro, "ShowPage", _page); _step = 1; break;
                    case 1:
                        var pages = Get<GameObject[]>(_intro, "_pages");
                        Require(pages.Count(p => p.activeSelf) == 1 && pages[_page].activeSelf, "Only the selected onboarding page is visible");
                        Capture("onboarding-" + _page);
                        if (++_page < pages.Length) { Invoke(_intro, "Next"); break; }
                        _page = 0;
                        if (++_size < Sizes.Length) { Prepare(Sizes[_size]); Invoke(_intro, "ShowPage", 0); break; }
                        Invoke(_intro, "Back");
                        Require(Get<int>(_intro, "_page") == pages.Length - 2, "Onboarding back navigation");
                        Invoke(_intro, "Next");
                        Click(Get<Button>(_intro, "_skipButton"));
                        _step = 2; break;
                    case 20:
                        if (_guide.IsTransitioning) break;
                        Capture("onboarding-hero"); _guide.DismissGreeting(); _step = 19; break;
                    case 19:
                        if (_guide.IsTransitioning) break;
                        _guide.Advance(); _step = 21; break;
                    case 21:
                        if (_guide.IsTransitioning) break;
                        Capture("onboarding-assessment"); _guide.Advance(); _step = 22; break;
                    case 22:
                        if (_guide.IsTransitioning) break;
                        Capture("onboarding-camera");
                        Click(Get<Button>(_guide, "_skip")); _step = 2; break;
                    case 2:
                        if (SceneManager.GetActiveScene().name != "Main") break;
                        Require(OnboardingState.IntroSeen && OnboardingState.LevelTestSkipped, "Skipping assessment opens home without marking it complete");
                        _shell = Object.FindFirstObjectByType<MainShellView>();
                        Prepare(Sizes[1]); _shell.SwitchTab(TabId.Duel); _step = 3; break;
                    case 3: Capture("home"); _shell.SwitchTab(TabId.League); _step = 4; break;
                    case 4: Capture("league"); _shell.SwitchTab(TabId.Profile); _step = 5; break;
                    case 5:
                        Capture("profile");
                        _identity = Object.FindFirstObjectByType<ProfileIdentityEditor>();
                        Require(_identity != null, "Profile identity editor is bound");
                        _identity.OpenName(); _step = 6; break;
                    case 6:
                        _identity.NameInput.SetTextWithoutNotify("!"); _identity.Save();
                        Require(Get<GameObject>(_identity, "_modal").activeSelf, "Invalid nickname stays in the editor");
                        Capture("name-validation"); _identity.Cancel(); _identity.OpenAvatar(); _step = 7; break;
                    case 7:
                        Capture("avatars"); _identity.Cancel();
                        _settings = Object.FindFirstObjectByType<SettingsScreen>();
                        _settings.Show(); _step = 8; break;
                    case 8:
                        _actions = Object.FindFirstObjectByType<ProfileSettingsActions>();
                        Require(_actions != null && _actions.isActiveAndEnabled, "Settings opens from profile");
                        Click(_actions.Language);
                        foreach (string language in new[] { "ru", "pt-BR", "en" })
                        {
                            _actions.SelectLanguage(language);
                            Require(_actions.LanguageLabel.text == Localization.NativeName(language), "Selected language is displayed");
                            Require(new PlayerPrefsSettingsStore().Language == language, "Selected language persists");
                        }
                        var sound = Get<Toggle>(_settings, "_soundToggle");
                        sound.isOn = !sound.isOn;
                        Require(new PlayerPrefsSettingsStore().SoundEnabled == sound.isOn, "Sound toggle persists");
                        _size = 0; Prepare(Sizes[_size]); _step = 9; break;
                    case 9:
                        Capture("settings");
                        if (++_size < Sizes.Length) { Prepare(Sizes[_size]); break; }
                        Click(_actions.Home); _step = 10; break;
                    case 10:
                        Require(Get<GameObject>(_shell, "_duelPanel").activeInHierarchy, "Settings HOME returns to Play tab");
                        Require(!Get<GameObject>(_settings, "_overlay").activeSelf, "Settings closes after HOME");
                        Finish(null); break;
                }
            }
            catch (Exception e) { Finish(e.ToString()); }
        }

        static void Prepare(Vector2Int size)
        {
            if (_camera == null)
            {
                _camera = new GameObject("MobileExperienceCapture", typeof(Camera)).GetComponent<Camera>();
                _camera.transform.position = new Vector3(0, 0, -50); _camera.orthographic = true;
                _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = Color.black; _camera.cullingMask = 1 << 5;
            }
            if (_texture != null) { _camera.targetTexture = null; _texture.Release(); Object.Destroy(_texture); }
            _texture = new RenderTexture(size.x, size.y, 24); _texture.Create();
            _camera.targetTexture = _texture; _camera.orthographicSize = size.y / 2f;
            _canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).First(c => c.isRootCanvas && c.gameObject.scene == SceneManager.GetActiveScene());
            _canvas.renderMode = RenderMode.ScreenSpaceCamera; _canvas.worldCamera = _camera; _canvas.planeDistance = 10;
            var scaler = _canvas.GetComponent<CanvasScaler>();
            scaler.enabled = false;
            // Reproduce the authored CanvasScaler using this render target's dimensions,
            // rather than using the editor Game view's unrelated screen size.
            _canvas.scaleFactor = Mathf.Pow(size.x / scaler.referenceResolution.x, 1 - scaler.matchWidthOrHeight)
                * Mathf.Pow(size.y / scaler.referenceResolution.y, scaler.matchWidthOrHeight);
            foreach (var fix in _canvas.GetComponentsInChildren<DeviceSimulatorMirrorFix>(true))
            { fix.enabled = false; fix.transform.localRotation = Quaternion.identity; }
            foreach (var fitter in _canvas.GetComponentsInChildren<SafeAreaFitter>(true))
            {
                fitter.enabled = false;
                float bottom = size.y > 700 ? 34 : 0, top = size.x == 430 ? 59 : size.y > 700 ? 47 : 0;
                SafeAreaFitter.TryGetAnchors(new Rect(0, bottom, size.x, size.y - bottom - top), size, out var min, out var max);
                var rect = (RectTransform)fitter.transform; rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            Canvas.ForceUpdateCanvases();
        }

        static void Capture(string name)
        {
            foreach (var rect in _canvas.GetComponentsInChildren<RectTransform>(true)) rect.gameObject.layer = 5;
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (camera != _camera && camera.targetTexture != null && camera.enabled) camera.Render();
            Canvas.ForceUpdateCanvases(); _camera.Render();
            foreach (var text in _canvas.GetComponentsInChildren<TMP_Text>())
                Require(!System.Text.RegularExpressions.Regex.IsMatch(text.text ?? "", "[\\u0400-\\u04ff]"), "English runtime text: " + text.name);
            var previous = RenderTexture.active; var image = new Texture2D(_texture.width, _texture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = _texture;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                Require(image.GetPixels32().Count(p => p.r + p.g + p.b > 30) > image.width * image.height / 4, "Visible UI: " + name);
                File.WriteAllBytes(Output + "/" + name + "-" + image.width + "x" + image.height + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }

        static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        static void Click(Button b) { Require(b != null && b.gameObject.activeInHierarchy && b.IsInteractable(), "Button enabled: " + b?.name); b.onClick.Invoke(); }
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception) _error = message; }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog;
            Application.runInBackground = _background;
            var backup = JsonUtility.FromJson<Backup>(SessionState.GetString(Key + ".prefs", ""));
            foreach (var item in backup.items)
                if (!item.exists) PlayerPrefs.DeleteKey(item.key);
                else if (item.isInt) PlayerPrefs.SetInt(item.key, item.number);
                else PlayerPrefs.SetString(item.key, item.value);
            PlayerPrefs.Save(); SessionState.EraseBool(Key);
            if (_texture != null) { _texture.Release(); Object.Destroy(_texture); }
            File.AppendAllText(Output + "/validation.txt", error == null
                ? "PASS: onboarding pages at 320x568/390x844/430x932 with safe insets; Back and Skip; all three home tabs; invalid name and avatar dialogs; settings, language, persisted sound, and HOME. Preferences restored.\n"
                : "FAIL: " + error + "\nPreferences restored.\n");
            EditorApplication.isPlaying = false;
        }
    }
}
