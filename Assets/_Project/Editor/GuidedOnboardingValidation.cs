using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.CV;
using PushStars.Fight;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Exercises the real guide and handoff. OS permission UI and live CV are excluded.</summary>
    [InitializeOnLoad]
    public static class GuidedOnboardingValidation
    {
        const string Key = "PushStars.CoachQA", Output = "output/onboarding/";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly string[] Prefs = { "character.gender", "onboarding.intro_seen", "onboarding.level_test_skipped" };
        static GuidedOnboardingController _guide;
        static int _step, _modelId, _portraitId;
        static double _due, _deadline;
        static string _error;
        static Camera _camera;
        static RenderTexture _target;
        static Vector2Int _size;
        static bool _background;

        static GuidedOnboardingValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    _step = 0; _error = null; _due = EditorApplication.timeSinceStartup + 2;
                    _deadline = _due + 150;
                    _background = Application.runInBackground; Application.runInBackground = true;
                    Application.logMessageReceived += Log;
                    EditorApplication.update += Tick;
                }
                if (state == PlayModeStateChange.EnteredEditMode) Restore();
            };
        }

        [MenuItem("Tools/Push Stars/Onboarding/Validate Coach Flow")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            Directory.CreateDirectory(Output);
            foreach (var key in Prefs)
            {
                SessionState.SetBool(Key + key + ".exists", PlayerPrefs.HasKey(key));
                SessionState.SetInt(Key + key, PlayerPrefs.GetInt(key));
            }
            PlayerPrefs.SetInt("character.gender", 0);
            SessionState.SetBool(Key, true);
            File.WriteAllText(Output + "validation.txt", "Coach onboarding Play Mode validation\n");
            EditorSceneManager.OpenScene(OnboardingSceneSetup.ScenePath);
            EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _due) return;
            _due = EditorApplication.timeSinceStartup + .35;
            try
            {
                Require(_error == null, _error);
                Require(EditorApplication.timeSinceStartup < _deadline, "Timed out waiting for guide.");
                if (_guide != null && _guide.IsTransitioning) return;
                switch (_step++)
                {
                    case 0:
                        _guide = Object.FindFirstObjectByType<GuidedOnboardingController>();
                        Require(_guide != null, "Guide starts");
                        Prepare(new Vector2Int(390, 844));
                        _due += 2;
                        break;
                    case 1:
                        Require(_guide.CurrentStep == GuidedOnboardingController.Step.Character, "Character comes first");
                        NoCamera(); Capture("01-male");
                        Prepare(new Vector2Int(320, 568)); Capture("01-male-compact");
                        Prepare(new Vector2Int(430, 932)); Capture("01-male-large");
                        Prepare(new Vector2Int(390, 844));
                        _guide.SelectCharacter(1); _guide.SelectCharacter(0); _guide.SelectCharacter(1);
                        _guide.DismissGreeting();
                        _due += .5;
                        break;
                    case 2:
                        var cards = Get<GenderChoiceCard[]>(_guide, "_cards");
                        foreach (var ellipse in Object.FindObjectsByType<HardEllipseGraphic>(FindObjectsSortMode.None))
                            Require(ellipse.GetComponent<CanvasRenderer>() != null && ellipse.rectTransform.rect.width > 0, "Contact shadow has a renderable ellipse");
                        Require(cards.All(c => Mathf.Abs(c.transform.localScale.x - 1) < .01f), "Card layout has no inherited canvas scale");
                        var bubble = (RectTransform)Get<CanvasGroup>(_guide, "_bubble").transform;
                        foreach (var card in cards)
                        {
                            var ring = (RectTransform)card.transform.Find("DotRing");
                            Require(!bubble.rect.Contains(bubble.InverseTransformPoint(ring.TransformPoint(ring.rect.center))), "Both selection buttons remain visible below the characters");
                        }
                        Require(cards[1].Portrait.rectTransform.localScale.x > .99f && cards[0].Portrait.rectTransform.localScale.x > .85f && cards[0].Portrait.rectTransform.localScale.x < .91f,
                            "Female selected: full size; male smaller");
                        Require(cards[1].Portrait.material.GetFloat("_Saturation") > .99f && cards[0].Portrait.material.GetFloat("_Saturation") < .01f,
                            "Female selected: full color; male grayscale");
                        Capture("02-female"); RememberHero();
                        _guide.Advance(); _guide.Advance(); _guide.GoBack();
                        Require(_guide.IsTransitioning, "Rapid navigation is gated");
                        break;
                    case 3:
                        SameHero(); NoCamera();
                        Require(_guide.CurrentStep == GuidedOnboardingController.Step.Assessment, "Selection transitions to assessment");
                        Capture("03-female-assessment"); _guide.GoBack();
                        break;
                    case 4:
                        SameHero(); Require(_guide.CurrentStep == GuidedOnboardingController.Step.Character, "Back restores selection");
                        _guide.SelectCharacter(0); _guide.DismissGreeting(); _due += .5;
                        break;
                    case 5:
                        Require(Get<CanvasGroup>(_guide, "_bubble").alpha < .01f, "Greeting dismisses on a selection-screen tap");
                        Require(Get<CanvasGroup>(_guide, "_coachGroup").alpha < .01f && Get<CanvasGroup>(_guide, "_wash").alpha < .01f,
                            "Coach and dim disappear before NEXT is enabled");
                        Require(Mathf.Abs(((RectTransform)Get<Button>(_guide, "_next").transform).anchoredPosition.x) < .01f, "NEXT is centered");
                        Require(Get<CanvasGroup>(_guide, "_assessmentHud").transform.Find("FinishPreview") == null, "FINISH is absent from the guide");
                        RememberHero(); _guide.Advance(); break;
                    case 6: SameHero(); Capture("04-male-assessment"); _guide.Advance(); break;
                    case 7:
                        NoCamera(); Capture("05-camera");
                        Set(_guide, "_denied", true);
                        StartPrivate("EnterStep", GuidedOnboardingController.Step.Camera, false);
                        break;
                    case 8:
                        Require(Get<TextMeshProUGUI>(_guide, "_actionLabel").text == "OPEN SETTINGS", "Denied camera offers recovery");
                        Require(Get<Button>(_guide, "_skip").gameObject.activeInHierarchy, "Denied camera can be postponed");
                        Capture("06-camera-denied");
                        StartPrivate("EnterStep", GuidedOnboardingController.Step.Placement, false);
                        break;
                    case 9: NoCamera(); Capture("07-placement"); Prepare(new Vector2Int(320, 568)); break;
                    case 10: Capture("08-placement-compact"); Prepare(new Vector2Int(430, 932)); break;
                    case 11: Capture("09-placement-large"); Prepare(new Vector2Int(390, 844)); break;
                    case 12:
                        var workout = Get<GameObject>(_guide, "_workout");
                        foreach (var component in workout.GetComponentsInChildren<MonoBehaviour>(true))
                            if (component is IPoseSource || component is PushupSession) component.enabled = false;
                        // Exercise handoff after a simulated permission result, without touching OS camera access.
                        StartPrivate("BeginAssessment");
                        break;
                    case 13:
                        var player = Get<FightAvatar>(_guide, "_player");
                        Require(player.Character != null && player.Character.GetInstanceID() == _modelId, "Live assessment owns the exact selected model");
                        var portrait = Get<FightHud>(_guide, "_hud").SoloPortrait;
                        Require(portrait.GetInstanceID() == _portraitId && portrait.gameObject.activeInHierarchy, "Live HUD owns the exact portrait");
                        Require(_guide.CurrentStep == GuidedOnboardingController.Step.Live, "Assessment starts");
                        Require(OnboardingState.IntroSeen && !OnboardingState.LevelTestSkipped, "Intro persisted only after setup");
                        NoCamera(); Prepare(new Vector2Int(390, 844));
                        break;
                    case 14:
                        var liveShadow = _guide.SelectedPortrait.GetComponent<AvatarContactShadow>();
                        var ellipseRect = Get<RectTransform>(liveShadow, "_shadow");
                        Require(ellipseRect.rect.width < _guide.SelectedPortrait.rectTransform.rect.width && ellipseRect.rect.height < 100,
                            "Live shadow remains a foot-sized ellipse after HUD adoption");
                        var livePlayer = Get<FightAvatar>(_guide, "_player");
                        var feet = Get<Transform[]>(livePlayer, "_guidedFeet");
                        var rotations = Get<Quaternion[]>(livePlayer, "_guidedFootRotations");
                        for (int i = 0; i < feet.Length; i++)
                            if (feet[i] != null) Require(Quaternion.Angle(feet[i].rotation, rotations[i]) < 1,
                                "Idle soles retain their selected standing orientation");
                        Capture("10-live-assessment"); Finish(null);
                        break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        static void Prepare(Vector2Int size)
        {
            _size = size;
            if (_camera == null)
            {
                _camera = new GameObject("CoachQACapture", typeof(Camera)).GetComponent<Camera>();
                _camera.transform.position = new Vector3(0, 0, -50); _camera.orthographic = true;
                _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = Color.black; _camera.cullingMask = 1 << 5;
            }
            if (_target != null) { _camera.targetTexture = null; _target.Release(); Object.Destroy(_target); }
            _target = new RenderTexture(size.x, size.y, 24); _target.Create();
            _camera.targetTexture = _target; _camera.orthographicSize = size.y / 2f;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.isRootCanvas))
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = _camera; canvas.planeDistance = 10;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.enabled = false;
                    canvas.scaleFactor = Mathf.Pow(size.x / scaler.referenceResolution.x, 1 - scaler.matchWidthOrHeight)
                        * Mathf.Pow(size.y / scaler.referenceResolution.y, scaler.matchWidthOrHeight);
                }
                foreach (var fix in canvas.GetComponentsInChildren<DeviceSimulatorMirrorFix>(true))
                { fix.enabled = false; fix.transform.localRotation = Quaternion.identity; }
                foreach (var fitter in canvas.GetComponentsInChildren<SafeAreaFitter>(true))
                {
                    fitter.enabled = false;
                    float bottom = size.y > 700 ? 34 : 0, top = size.y > 700 ? 59 : 0;
                    SafeAreaFitter.TryGetAnchors(new Rect(0, bottom, size.x, size.y - bottom - top), size, out var min, out var max);
                    var r = (RectTransform)fitter.transform; r.anchorMin = min; r.anchorMax = max; r.offsetMin = r.offsetMax = Vector2.zero;
                }
                foreach (var rect in canvas.GetComponentsInChildren<RectTransform>(true)) rect.gameObject.layer = 5;
            }
            Canvas.ForceUpdateCanvases();
            foreach (var label in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)) label.ForceMeshUpdate(false, true);
        }

        static void Capture(string name)
        {
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (cam != _camera && cam.targetTexture != null && cam.enabled) cam.Render();
            Canvas.ForceUpdateCanvases(); _camera.Render();
            foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None))
                Require(!System.Text.RegularExpressions.Regex.IsMatch(text.text ?? "", "[\\u0400-\\u04ff]"), "English text: " + text.name);
            var previous = RenderTexture.active; var image = new Texture2D(_target.width, _target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = _target;
                image.ReadPixels(new Rect(0, 0, image.width, image.height), 0, 0); image.Apply();
                File.WriteAllBytes(Output + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.Destroy(image); }
        }
        static void RememberHero() { _modelId = _guide.SelectedCharacter.GetInstanceID(); _portraitId = _guide.SelectedPortrait.GetInstanceID(); }
        static void SameHero() { Require(_guide.SelectedCharacter.GetInstanceID() == _modelId && _guide.SelectedPortrait.GetInstanceID() == _portraitId, "Hero identity persists through transition"); }
        static void NoCamera() { Require(!Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Any(c => c is IPoseSource && c.isActiveAndEnabled), "Camera stays off during guide and isolated QA"); }
        static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
        static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
        static void StartPrivate(string method, params object[] args) => _guide.StartCoroutine((IEnumerator)_guide.GetType().GetMethod(method, Private).Invoke(_guide, args));
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception) _error = message; }
        static void Finish(string error)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            Application.runInBackground = _background;
            File.AppendAllText(Output + "validation.txt", error == null
                ? "PASS: male/female size + saturation, rapid taps, forward/reverse transitions, exact model and portrait identity through live handoff, denied-camera recovery UI, placement at 320x568/390x844/430x932, English copy. No camera capture or rewards. OS permission results require device QA.\n"
                : "FAIL: " + error + "\n");
            EditorApplication.isPlaying = false;
        }
        static void Restore()
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            foreach (var key in Prefs)
                if (SessionState.GetBool(Key + key + ".exists", false)) PlayerPrefs.SetInt(key, SessionState.GetInt(Key + key, 0));
                else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save(); FightRequest.Clear(); SessionState.EraseBool(Key);
        }
    }
}
