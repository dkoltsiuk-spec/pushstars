using System.Collections;
using PushStars.Core;
using PushStars.UI;
using PushStars.OTA;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>A continuous hero stage: selection, coach, permission, placement, live assessment.</summary>
    [DefaultExecutionOrder(-1500)]
    public sealed class GuidedOnboardingController : MonoBehaviour
    {
        public enum Step { Character, Assessment, Camera, Placement, Live }
        [Header("Persistent scene")]
        [SerializeField] private RectTransform _heroLayer;
        [SerializeField] private GenderChoiceCard[] _cards;
        [SerializeField] private CharacterStage[] _stages;
        [SerializeField] private CanvasGroup _selection;
        [SerializeField] private CanvasGroup _selectionBackground;
        [SerializeField] private CanvasGroup _assessmentBackground;
        [SerializeField] private CanvasGroup _assessmentHud;
        [SerializeField] private RectTransform _assessmentTarget;
        [SerializeField] private Button _next;
        [SerializeField] private Button _back;
        [SerializeField] private Button _skip;
        [SerializeField] private Button _dismissSurface;

        [Header("Coach presentation")]
        [SerializeField] private CanvasGroup _wash;
        [SerializeField] private CanvasGroup _coachGroup;
        [SerializeField] private Image _coach;
        [SerializeField] private Sprite[] _coachPoses;
        [SerializeField] private CanvasGroup _bubble;
        [SerializeField] private TextMeshProUGUI _speech;
        [SerializeField] private Button _action;
        [SerializeField] private TextMeshProUGUI _actionLabel;
        [SerializeField] private GameObject _placement;
        [SerializeField] private RectTransform _placementPhone;
        [SerializeField] private TextMeshProUGUI _stepLabel;

        [Header("Authored assessment (same Unity scene)")]
        [SerializeField] private GameObject _workout;
        [SerializeField] private CanvasGroup _workoutCanvas;
        [SerializeField] private FightAvatar _player;
        [SerializeField] private FightHud _hud;
        [SerializeField] private GameObject _unusedPlayerStage;

        public Step CurrentStep { get; private set; }
        public bool IsTransitioning { get; private set; }
        public RawImage SelectedPortrait { get; private set; }
        public GameObject SelectedCharacter => SelectedStage != null && SelectedStage.AvatarRoot.childCount > 0
            ? SelectedStage.AvatarRoot.GetChild(0).gameObject : null;
        private CharacterStage SelectedStage => _stages[_selectedIndex];
        private int _selectedIndex;
        private Vector2 _coachHome, _bubbleHome;
        private Transform _portraitParent;
        private Vector2 _portraitAnchor, _portraitPosition, _portraitSize, _portraitPivot;
        private bool _permissionPending, _denied, _openingSettings, _leaving;
        private Coroutine _sequence;
        private Coroutine _dismiss;
        private bool _greetingDismissed;

        private void Awake()
        {
            _coachHome = _coach.rectTransform.anchoredPosition;
            _bubbleHome = ((RectTransform)_bubble.transform).anchoredPosition;
            _workout.SetActive(false);
            _unusedPlayerStage.SetActive(false);
            SetGroup(_wash, 0); SetGroup(_coachGroup, 0); SetGroup(_bubble, 0);
            SetGroup(_assessmentBackground, 0); SetGroup(_assessmentHud, 0);
        }

        private void Start()
        {
            for (int i = 0; i < _cards.Length; i++)
            {
                int index = i;
                _cards[i].Button.onClick.AddListener(() => SelectCharacter(index));
                if (_cards[i].Gender == CharacterRoster.SavedGender) _selectedIndex = i;
            }
            RefreshSelection();
            _next.onClick.AddListener(Advance);
            _back.onClick.AddListener(GoBack);
            _action.onClick.AddListener(Advance);
            _skip.onClick.AddListener(Skip);
            _dismissSurface.onClick.AddListener(DismissGreeting);
            _sequence = StartCoroutine(EnterStep(Step.Character, true));
        }

        private void Update()
        {
            if (!IsTransitioning && _coachGroup.alpha > .99f)
                _coach.rectTransform.anchoredPosition = _coachHome + new Vector2(0, Mathf.Sin(Time.unscaledTime * 1.8f) * 1.6f);
            if (_placement.activeInHierarchy && _placementPhone != null)
                _placementPhone.localRotation = Quaternion.Euler(0, 0, -8f + Mathf.Sin(Time.unscaledTime * 2f) * 3f);
            if (Input.GetKeyDown(KeyCode.Escape)) GoBack();
        }

        public void DismissGreeting()
        {
            if (CurrentStep != Step.Character || _dismiss != null || _greetingDismissed) return;
            _greetingDismissed = true;
            if (_sequence != null) StopCoroutine(_sequence);
            IsTransitioning = true;
            SetInput(false);
            _dismiss = StartCoroutine(FadeGreeting());
        }

        private IEnumerator FadeGreeting()
        {
            float start = _bubble.alpha;
            yield return Animate(.18f, t => SetGroup(_bubble, start * (1 - t)));
            float coachStart = _coachGroup.alpha, washStart = _wash.alpha;
            Vector2 position = _coach.rectTransform.anchoredPosition;
            yield return Animate(.3f, t =>
            {
                SetGroup(_coachGroup, coachStart * (1 - t));
                SetGroup(_wash, washStart * (1 - t));
                _coach.rectTransform.anchoredPosition = position + Vector2.down * (80 * t);
            });
            _dismissSurface.gameObject.SetActive(false);
            _stepLabel.text = "01 / CHOOSE YOUR HERO";
            IsTransitioning = false;
            SetInput(true);
            _dismiss = null;
        }

        public void SelectCharacter(int index)
        {
            if (IsTransitioning || CurrentStep != Step.Character || index < 0 || index >= _cards.Length) return;
            Haptics.Selection();
            _selectedIndex = index;
            CharacterRoster.SaveGender(_cards[index].Gender);
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < _cards.Length; i++) _cards[i].SetSelected(i == _selectedIndex);
            SelectedPortrait = _cards[_selectedIndex].Portrait;
        }

        public void Advance()
        {
            if (IsTransitioning || _permissionPending || _leaving) return;
            if (CurrentStep == Step.Character && !_greetingDismissed) { DismissGreeting(); return; }
            Haptics.Light();
            switch (CurrentStep)
            {
                case Step.Character: _sequence = StartCoroutine(ToAssessment()); break;
                case Step.Assessment: _sequence = StartCoroutine(EnterStep(Step.Camera)); break;
                case Step.Camera:
                    if (_denied) OpenSettings();
                    else _sequence = StartCoroutine(RequestCamera());
                    break;
                case Step.Placement:
                    if (!HasCamera()) { _sequence = StartCoroutine(EnterStep(Step.Camera)); return; }
                    _sequence = StartCoroutine(BeginAssessment());
                    break;
            }
        }

        private IEnumerator ToAssessment()
        {
            IsTransitioning = true; SetInput(false);
            yield return HideCoach(true);
            var rect = SelectedPortrait.rectTransform;
            _portraitParent = rect.parent;
            _portraitAnchor = rect.anchorMin; _portraitPosition = rect.anchoredPosition;
            _portraitSize = rect.sizeDelta; _portraitPivot = rect.pivot;
            rect.SetParent(_heroLayer, true);
            _cards[_selectedIndex].enabled = false;
            yield return MovePortrait(_assessmentTarget, .95f, t =>
            {
                SetGroup(_selection, 1f - Mathf.Clamp01(t * 2.6f));
                SetGroup(_selectionBackground, 1f - t);
                SetGroup(_assessmentBackground, t);
                SetGroup(_assessmentHud, Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - .35f) / .65f)));
            });
            _stages[1 - _selectedIndex].gameObject.SetActive(false);
            yield return EnterStep(Step.Assessment, true);
        }

        private IEnumerator EnterStep(Step step, bool hidden = false)
        {
            if (_dismiss != null) { StopCoroutine(_dismiss); _dismiss = null; }
            IsTransitioning = true; SetInput(false);
            if (!hidden) yield return HideCoach(false);
            CurrentStep = step;
            _greetingDismissed = false;
            _dismissSurface.gameObject.SetActive(step == Step.Character);
            _placement.SetActive(step == Step.Placement);
            _back.gameObject.SetActive(step != Step.Character);
            _skip.gameObject.SetActive(step == Step.Camera || step == Step.Placement);
            _next.gameObject.SetActive(step == Step.Character);
            _action.gameObject.SetActive(step != Step.Character);
            _coach.sprite = _coachPoses[step == Step.Character ? 0 : step == Step.Assessment ? 1 : 2];
            _stepLabel.text = step == Step.Character ? "TAP ANYWHERE TO CONTINUE" : step == Step.Assessment
                ? "02 / YOUR FIRST ASSESSMENT" : step == Step.Camera ? "03 / CAMERA ACCESS" : "04 / SET UP YOUR PHONE";
            _speech.text = step == Step.Character ? "Hey, I'm your coach!\nChoose your hero.\nI'll show you the ropes."
                : step == Step.Assessment ? "Let's see your strength!\n60 seconds of push-ups.\nYou set the pace."
                : step == Step.Camera ? (_denied ? "Camera access is off.\nEnable it in Settings,\nthen come back here."
                    : "Ready to move?\nAllow camera access\nto track your reps.")
                : "Place your phone on the floor.";
            _actionLabel.text = step == Step.Assessment ? "LET'S GO" : step == Step.Camera
                ? (_denied ? "OPEN SETTINGS" : "ALLOW CAMERA") : "I'M READY";
            var bubbleRect = (RectTransform)_bubble.transform;
            bubbleRect.sizeDelta = step == Step.Placement ? new Vector2(352, 315) : new Vector2(200, 158);
            _bubbleHome = step == Step.Placement ? new Vector2(0, 155) : new Vector2(82, 122);
            _speech.rectTransform.anchorMin = _speech.rectTransform.anchorMax = new Vector2(.5f, 1f);
            _speech.rectTransform.pivot = new Vector2(.5f, 1f);
            _speech.rectTransform.anchoredPosition = new Vector2(0, -17);
            _speech.rectTransform.sizeDelta = step == Step.Placement ? new Vector2(320, 30) : new Vector2(176, 78);
            _speech.maxVisibleCharacters = 0;
            ((RectTransform)_action.transform).anchoredPosition = step == Step.Placement ? new Vector2(40, 63) : new Vector2(0, 43);
            float washStart = _wash.alpha;
            yield return Animate(.22f, t => SetGroup(_wash, Mathf.Lerp(washStart, 1, t)));
            yield return Animate(.42f, t =>
            {
                SetGroup(_coachGroup, t);
                _coach.rectTransform.anchoredPosition = _coachHome + new Vector2(-40 * (1 - t), -92 * (1 - t));
                _coach.rectTransform.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, t);
            });
            yield return Animate(.24f, t =>
            {
                SetGroup(_bubble, _greetingDismissed ? 0 : t);
                bubbleRect.anchoredPosition = _bubbleHome + Vector2.down * (12 * (1 - t));
                bubbleRect.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, t);
            });
            _speech.ForceMeshUpdate();
            int count = _speech.textInfo.characterCount;
            yield return Animate(Mathf.Min(.7f, count * .012f), t => _speech.maxVisibleCharacters = Mathf.CeilToInt(count * t));
            _speech.maxVisibleCharacters = int.MaxValue;
            IsTransitioning = false; SetInput(true);
        }

        private IEnumerator HideCoach(bool clearWash)
        {
            if (_dismiss != null) { StopCoroutine(_dismiss); _dismiss = null; }
            float bubbleStart = _bubble.alpha;
            yield return Animate(.16f, t => SetGroup(_bubble, bubbleStart * (1 - t)));
            float coachStart = _coachGroup.alpha, washStart = _wash.alpha;
            yield return Animate(.28f, t =>
            {
                SetGroup(_coachGroup, coachStart * (1 - t));
                _coach.rectTransform.anchoredPosition = _coachHome + new Vector2(-20 * t, -80 * t);
                if (clearWash) SetGroup(_wash, washStart * (1 - t));
            });
        }

        private IEnumerator RequestCamera()
        {
            _permissionPending = true; SetInput(false);
            if (!HasCamera())
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                bool answered = false;
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ => answered = true;
                callbacks.PermissionDenied += _ => answered = true;
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera, callbacks);
                while (!answered) yield return null;
#else
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
#endif
            }
            _permissionPending = false;
            _denied = !HasCamera();
            yield return EnterStep(_denied ? Step.Camera : Step.Placement);
        }

        private static bool HasCamera()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera);
#else
            return Application.HasUserAuthorization(UserAuthorization.WebCam);
#endif
        }

        private void OpenSettings()
        {
            _openingSettings = true;
#if UNITY_IOS && !UNITY_EDITOR
            Application.OpenURL("app-settings:");
#elif UNITY_ANDROID && !UNITY_EDITOR
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var intent = new AndroidJavaObject("android.content.Intent", "android.settings.APPLICATION_DETAILS_SETTINGS"))
            using (var uri = new AndroidJavaClass("android.net.Uri"))
            using (var parsed = uri.CallStatic<AndroidJavaObject>("parse", "package:" + Application.identifier))
            {
                intent.Call<AndroidJavaObject>("setData", parsed);
                activity.Call("startActivity", intent);
            }
#else
            _openingSettings = false;
            _denied = false;
            _sequence = StartCoroutine(RequestCamera());
#endif
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused || !_openingSettings || IsTransitioning || _leaving) return;
            _openingSettings = false;
            _denied = !HasCamera();
            _sequence = StartCoroutine(EnterStep(_denied ? Step.Camera : Step.Placement));
        }

        public void GoBack()
        {
            if (IsTransitioning || _permissionPending || _leaving || CurrentStep == Step.Live || CurrentStep == Step.Character) return;
            Haptics.Light();
            _sequence = StartCoroutine(CurrentStep == Step.Assessment ? ReturnToSelection()
                : EnterStep(CurrentStep == Step.Placement ? Step.Camera : Step.Assessment));
        }

        private IEnumerator ReturnToSelection()
        {
            IsTransitioning = true; SetInput(false);
            yield return HideCoach(true);
            _stages[1 - _selectedIndex].gameObject.SetActive(true);
            // An invisible target stores the original card rect, so Back reverses the same motion.
            var destination = new GameObject("ReturnTarget", typeof(RectTransform)).GetComponent<RectTransform>();
            destination.SetParent(_portraitParent, false);
            destination.anchorMin = destination.anchorMax = _portraitAnchor;
            destination.pivot = _portraitPivot; destination.sizeDelta = _portraitSize;
            destination.anchoredPosition = _portraitPosition;
            yield return MovePortrait(destination, .7f, t =>
            {
                SetGroup(_selection, t); SetGroup(_selectionBackground, t);
                SetGroup(_assessmentBackground, 1 - t); SetGroup(_assessmentHud, 1 - t);
            });
            var rect = SelectedPortrait.rectTransform;
            rect.SetParent(_portraitParent, false);
            rect.anchorMin = rect.anchorMax = _portraitAnchor;
            rect.pivot = _portraitPivot; rect.sizeDelta = _portraitSize;
            rect.anchoredPosition = _portraitPosition; rect.localScale = Vector3.one;
            Destroy(destination.gameObject);
            _cards[_selectedIndex].enabled = true;
            RefreshSelection();
            yield return EnterStep(Step.Character, true);
        }

        private IEnumerator BeginAssessment()
        {
            IsTransitioning = true; SetInput(false);
            yield return HideCoach(true);
            FightRequest.LevelTest(FightConfig.MainSceneName);
            _player.UseOnboardingStage(SelectedStage);
            SetGroup(_workoutCanvas, 0);
            _workout.SetActive(true);
            // FightController configures its authored solo HUD in Start.
            yield return null;
            Canvas.ForceUpdateCanvases();
            var target = _hud.SoloPortrait;
            if (target == null)
            {
                Debug.LogError("[Onboarding] Assessment portrait is not configured.");
                _workout.SetActive(false);
                yield return EnterStep(Step.Placement, true);
                yield break;
            }
            target.color = Color.clear;
            float portraitHeight = SelectedPortrait.rectTransform.rect.height;
            float startFov = SelectedStage.StageCamera.fieldOfView * Mathf.Deg2Rad;
            yield return MovePortrait(target.rectTransform, .65f, t =>
            {
                // A larger HUD surface pulls the lens back by the same ratio, preserving
                // the hero's on-screen height instead of suddenly doubling its size.
                float ratio = SelectedPortrait.rectTransform.rect.height / portraitHeight;
                SelectedStage.StageCamera.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(startFov * .5f) * ratio) * Mathf.Rad2Deg;
                SetGroup(_workoutCanvas, t);
                SetGroup(_assessmentHud, 1 - t);
                SetGroup(_assessmentBackground, 1 - t);
            });
            _hud.AdoptOnboardingPortrait(SelectedPortrait);
            _player.ReleaseOnboardingCamera();
            OnboardingState.IntroSeen = true;
            OnboardingState.LevelTestSkipped = false;
            CurrentStep = Step.Live;
            SetGroup(_workoutCanvas, 1, true);
            _back.gameObject.SetActive(false); _skip.gameObject.SetActive(false);
            _stepLabel.gameObject.SetActive(false);
            IsTransitioning = false;
            // The hero's stage remains alive; only the guide canvas is retired.
            gameObject.SetActive(false);
        }

        private IEnumerator MovePortrait(RectTransform destination, float duration, System.Action<float> progress)
        {
            var rect = SelectedPortrait.rectTransform;
            var parent = (RectTransform)rect.parent;
            var corners = new Vector3[4]; rect.GetWorldCorners(corners);
            Vector2 startCenter = parent.InverseTransformPoint((corners[0] + corners[2]) * .5f);
            Vector2 startSize = new Vector2(Vector3.Distance(corners[0], corners[3]), Vector3.Distance(corners[0], corners[1]));
            startSize /= parent.lossyScale.x;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.localScale = Vector3.one;
            yield return Animate(duration, t =>
            {
                // Re-measure each frame: resizing / changing safe areas mid-transition stays continuous.
                destination.GetWorldCorners(corners);
                Vector2 endCenter = parent.InverseTransformPoint((corners[0] + corners[2]) * .5f);
                float height = Vector3.Distance(corners[0], corners[1]) / parent.lossyScale.y;
                float aspect = (float)SelectedPortrait.texture.width / SelectedPortrait.texture.height;
                rect.anchoredPosition = Vector2.Lerp(startCenter, endCenter, t);
                rect.sizeDelta = Vector2.Lerp(startSize, new Vector2(height * aspect, height), t);
                progress(t);
            });
        }

        private void Skip()
        {
            if (IsTransitioning || _permissionPending || _leaving) return;
            _leaving = true;
            OnboardingState.IntroSeen = true;
            OnboardingState.LevelTestSkipped = true;
            OtaSceneLoader.LoadScene(FightConfig.MainSceneName);
        }

        private void SetInput(bool enabled)
        {
            _next.interactable = _action.interactable = _back.interactable = _skip.interactable = enabled;
            _next.interactable = enabled && _greetingDismissed;
            _bubble.blocksRaycasts = _bubble.interactable = enabled;
            _selection.blocksRaycasts = _selection.interactable = enabled && CurrentStep == Step.Character;
        }

        private static void SetGroup(CanvasGroup group, float alpha, bool input = false)
        {
            group.alpha = alpha; group.interactable = group.blocksRaycasts = input;
        }

        private static IEnumerator Animate(float seconds, System.Action<float> draw)
        {
            float elapsed = 0; draw(0);
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);
                draw(t * t * (3 - 2 * t));
                yield return null;
            }
            draw(1);
        }
    }
}
