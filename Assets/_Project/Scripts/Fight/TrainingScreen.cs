using System;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Authored training UI. Gameplay and reward writes stay in FightController.</summary>
    public sealed class TrainingScreen : MonoBehaviour
    {
        public enum View { Loading, Exercise, Rest, Results }
        [SerializeField] private GameObject _loading, _exercise, _rest, _results, _header, _pauseCurtain;
        [SerializeField] private TextMeshProUGUI _mode, _set, _reps, _technique, _timer, _hint, _restTimer, _elapsed;
        [SerializeField] private TextMeshProUGUI _totalReps, _totalSets, _totalTechnique, _xp, _pauseLabel;
        [SerializeField] private Button _exit, _pause, _next, _addRest, _startSet, _more, _home, _resume;
        [SerializeField] private RawImage _livePortrait, _sourcePortrait;
        [SerializeField] private Texture2D _pushupPreview;
        [SerializeField] private Texture2D _femalePushup, _femaleRest, _femaleStanding;
        [SerializeField] private RawImage _resultPortrait;
        [SerializeField] private RectTransform _restPortrait;
        [SerializeField] private RectTransform _content;
        [SerializeField] private bool _useMeasurementLayout;
        [SerializeField] private GameObject _presentationRoot;
        [SerializeField] private CanvasGroup[] _measurementGroups;
        [SerializeField] private TextMeshProUGUI _exerciseMode;
        [SerializeField] private Button _exercisePause, _exerciseExit, _exerciseResume;
        [SerializeField] private Image _progressFill;
        [SerializeField] private Image _background;
        [SerializeField] private Sprite _resultsBackground;
        private Sprite _exerciseBackground;
        [SerializeField] private Image[] _steps;
        [SerializeField] private TextMeshProUGUI[] _stepLabels;
        [SerializeField] private Sprite _check, _dumbbell;
        [SerializeField] private RectTransform[] _bolts, _sleepLetters;
        private Vector2[] _boltOrigins, _sleepOrigins;
        private RawImage _restImage;
        private Texture _restStill;
        private static readonly Color32 StepDone = new Color32(255, 207, 0, 255), StepNext = new Color32(74, 84, 132, 255),
            StepLater = new Color32(38, 43, 70, 255);
        private View _view;
        private bool _preview, _paused;
        private float _shownReps = -1, _repPop;
        private float _readyAt;
        private int _previewSet = 1;
        private Vector3 _repScale;
        private Material _repMaterial;
        public View CurrentView => _view;

        public void Bind(Action exit, Action pause, Action next, Action addRest, Action startSet, Action more)
        {
            _exit.onClick.AddListener(() => exit()); _home.onClick.AddListener(() => exit());
            _pause.onClick.AddListener(() => pause()); _resume.onClick.AddListener(() => pause());
            if (!_useMeasurementLayout || _preview) _next.onClick.AddListener(() => next());
            _addRest.onClick.AddListener(() => addRest());
            _startSet.onClick.AddListener(() => startSet()); _more.onClick.AddListener(() => more());
        }
        private void Awake()
        {
            if (_background != null) _exerciseBackground = _background.sprite;
            _repScale = _reps.rectTransform.localScale;
            _repMaterial = new Material(_reps.fontSharedMaterial) { name = "Training Reps Thin Outline" };
            _repMaterial.SetFloat("_OutlineWidth", .07f);
            _repMaterial.SetFloat("_FaceDilate", .07f);
            _repMaterial.SetFloat("_UnderlayDilate", .05f);
            _repMaterial.SetFloat("_UnderlayOffsetX", 0f);
            _repMaterial.SetFloat("_UnderlayOffsetY", -.2f);
            ShaderUtilities.UpdateShaderRatios(_repMaterial);
            _reps.fontSharedMaterial = _repMaterial;
            _reps.UpdateMeshPadding();
            _readyAt = Time.unscaledTime + .25f;
            _boltOrigins = Origins(_bolts); _sleepOrigins = Origins(_sleepLetters);
            if (PushStars.UI.CharacterRoster.SavedGender == PushStars.UI.CharacterGender.Female)
            {
                if (_femalePushup != null) _pushupPreview = _femalePushup;
                if (_femaleRest != null && _restPortrait != null) _restPortrait.GetComponent<RawImage>().texture = _femaleRest;
                if (_femaleStanding != null) _resultPortrait.texture = _femaleStanding;
            }
            if (_restPortrait != null) { _restImage = _restPortrait.GetComponent<RawImage>(); _restStill = _restImage.texture; }
            ShowLoading();
        }
        private void OnDestroy()
        {
            if (_repMaterial != null) Destroy(_repMaterial);
        }
        private static Vector2[] Origins(RectTransform[] rects)
        { var values = new Vector2[rects.Length]; for (int i = 0; i < rects.Length; i++) values[i] = rects[i].anchoredPosition; return values; }
        private void Switch(View view)
        {
            _view = view;
            if (_background != null && _resultsBackground != null)
                _background.sprite = view == View.Results ? _resultsBackground : _exerciseBackground;
            _reps.gameObject.SetActive(false);
            _loading.SetActive(view == View.Loading); _exercise.SetActive(view == View.Exercise);
            _rest.SetActive(view == View.Rest); _results.SetActive(view == View.Results);
            _header.SetActive(view == View.Exercise || view == View.Rest);
            _mode.text = view == View.Rest ? "REST" : "PUSHUP";
            if (_useMeasurementLayout)
            {
                bool exercise = view == View.Exercise;
                _presentationRoot.SetActive(!exercise);
                foreach (var group in _measurementGroups)
                { group.alpha = exercise ? 1 : 0; group.blocksRaycasts = exercise; group.interactable = exercise; }
                if (exercise) _exerciseMode.text = "PUSHUP";
            }
        }
        public void ShowLoading() => Switch(View.Loading);
        public void ShowExercise(int set, int total, int reps, float technique, float remaining, string hint, bool canFinish, bool setStarted)
        {
            if (!_preview && Time.unscaledTime < _readyAt) return;
            if (_view != View.Exercise)
            {
                GameAudio.SetWorkoutPaused(false);
                Switch(View.Exercise);
            }
            _set.text = $"{set}/{total}";
            _reps.text = reps.ToString();
            _reps.gameObject.SetActive(setStarted);
            if (_shownReps != reps) { _shownReps = reps; _repPop = Time.unscaledTime; }
            _technique.text = technique > 0 ? $"{technique:0}%" : "—";
            if (!_useMeasurementLayout || technique > 0)
                _technique.color = technique >= 85 ? new Color32(63, 255, 0, 255) : technique >= 60 ? Color.yellow : Color.white;
            _timer.text = FormatTime(remaining);
            // The measurement HUD already provides the live camera guidance and countdown.
            if (!_useMeasurementLayout) _hint.text = hint ?? "";
            if (_useMeasurementLayout) _exerciseMode.text = "PUSHUP";
            _next.interactable = canFinish;
        }
        public void ShowRest(int completed, int total, float remaining, float elapsed, bool manual)
        {
            if (_view != View.Rest)
            {
                GameAudio.Play(SoundCue.RewardComplete);
                GameAudio.SetWorkoutPaused(true);
                Switch(View.Rest);
            }
            _restTimer.text = manual ? "∞" : FormatTime(remaining);
            _elapsed.text = FormatTime(elapsed); _addRest.interactable = !manual;
            float progress = total > 1 ? Mathf.Clamp01((float)completed / (total - 1)) : 1;
            // A sliced fill keeps its rounded ends: it grows to the next set's node instead of
            // being clipped by a radial/horizontal fill.
            if (_progressFill.type == Image.Type.Filled) _progressFill.fillAmount = progress;
            else _progressFill.rectTransform.anchorMax = new Vector2(progress, 1);
            int visible = Mathf.Min(total, _steps.Length);
            var track = _progressFill.transform.parent;
            for (int i = 0; i < _steps.Length; i++)
            {
                var step = _steps[i];
                var node = (RectTransform)step.transform;
                while (node.parent != track) node = (RectTransform)node.parent;
                node.gameObject.SetActive(i < visible);
                if (i >= visible) continue;
                node.anchorMin = node.anchorMax = new Vector2(visible == 1 ? .5f : (float)i / (visible - 1), .5f);
                bool done = i < completed;
                step.sprite = done ? _check : _dumbbell;
                step.color = done || i == completed ? Color.white : new Color(1, 1, 1, .45f);
                var disc = step.transform.parent.GetComponent<Image>();
                if (disc != null && disc.transform != track)
                    disc.color = done ? StepDone : i == completed ? StepNext : StepLater;
                _stepLabels[i].text = total > 5 ? (i + 1).ToString() : "";
            }
        }
        public void ShowResults(int reps, int sets, float technique, long xp, bool canAdd)
        {
            if (_view != View.Results) GameAudio.Play(reps > 0 ? SoundCue.Victory : SoundCue.Back);
            Switch(View.Results); SetPaused(false);
            _totalReps.text = reps.ToString(); _totalSets.text = $"TOTAL REPS • {sets} SET{(sets == 1 ? "" : "S")}";
            _totalTechnique.text = technique > 0 ? $"{technique:0}%" : "—"; _xp.text = $"+{xp}";
            _more.interactable = canAdd;
        }
        public void SetPaused(bool paused)
        {
            _paused = paused;
            GameAudio.SetWorkoutPaused(paused || _view == View.Rest);
            if (_useMeasurementLayout && _view == View.Exercise) GetComponent<FightHud>().SetPaused(paused);
            else _pauseCurtain.SetActive(paused);
            _pauseLabel.text = paused ? "▶" : "Ⅱ";
        }
        public static string FormatTime(float seconds)
        { int value = Mathf.Max(0, Mathf.CeilToInt(seconds)); return $"{value / 60:00}:{value % 60:00}"; }
        private void LateUpdate()
        {
            if (_content != null)
            {
                var size = ((RectTransform)_content.parent).rect.size;
                _content.localScale = Vector3.one * Mathf.Min(size.x / 390f, size.y / 844f);
            }
            if (_view == View.Exercise && !_useMeasurementLayout)
            {
                _livePortrait.texture = !_preview && _sourcePortrait != null && _sourcePortrait.texture != null ? _sourcePortrait.texture : _pushupPreview;
                float pop = Mathf.Max(0, 1 - (Time.unscaledTime - _repPop) / .2f);
                _reps.rectTransform.localScale = _repScale * (1 + .08f * Mathf.Sin(pop * Mathf.PI));
            }
            if (!_paused)
            {
                for (int i = 0; i < _bolts.Length; i++) _bolts[i].anchoredPosition = _boltOrigins[i] + Vector2.up * Mathf.Repeat(Time.unscaledTime * 9, 160);
                for (int i = 0; i < _sleepLetters.Length; i++) _sleepLetters[i].anchoredPosition = _sleepOrigins[i] + new Vector2(Mathf.Sin(Time.unscaledTime + i) * 4, Mathf.Sin(Time.unscaledTime * 1.5f + i) * 6);
            }
            if (_view == View.Rest) ShowRestingHero();
        }
        /// <summary>Between sets the live hero stands and gets their breath back (the fight's own stage, framed
        /// standing by the preparation presentation); the baked still stands in where no stage
        /// renders, such as the editor preview.</summary>
        private void ShowRestingHero()
        {
            if (_restImage == null) return;
            var live = !_preview && _sourcePortrait != null ? _sourcePortrait.texture : null;
            if (live == null || !TryFrameHero(live, out var uv))
            {
                _restImage.texture = _restStill;
                _restImage.uvRect = new Rect(0, 0, 1, 1);
                if (_restStill != null) FitRestBox((float)_restStill.width / _restStill.height);
                return;
            }
            _restImage.texture = live;
            _restImage.uvRect = uv;
            FitRestBox(LiveRestAspect);
        }

        private const float LiveRestAspect = .75f;
        private FightAvatar _stageAvatar;

        /// <summary>The fight stage renders a wide strip with the hero small in it. Cut a portrait
        /// around the body: feet at the bottom, a standing height above them (from the rig, not
        /// the animated limbs, so the crop stays still while the arms swing).</summary>
        private bool TryFrameHero(Texture live, out Rect uv)
        {
            uv = default;
            if (_stageAvatar == null || _stageAvatar.StageCamera == null || _stageAvatar.StageCamera.targetTexture != live)
            {
                _stageAvatar = null;
                foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
                    if (avatar.StageCamera != null && avatar.StageCamera.targetTexture == live) _stageAvatar = avatar;
                if (_stageAvatar == null) return false;
            }
            var body = _stageAvatar.Character != null ? _stageAvatar.Character.GetComponentInChildren<Animator>() : null;
            if (body == null || !body.isHuman) return false;
            var camera = _stageAvatar.StageCamera;
            Vector3 feet = body.transform.position;
            // humanScale is the hips height; a standing body with headroom is ~1.8 of it.
            float height = body.humanScale * body.transform.lossyScale.y * 1.8f;
            Vector3 ground = camera.WorldToViewportPoint(feet);
            Vector3 top = camera.WorldToViewportPoint(feet + body.transform.up * height);
            float tall = top.y - ground.y;
            if (ground.z <= 0 || tall <= .01f) return false;
            // The stage lens may squeeze the strip horizontally (AvatarWideCamera): measure how a
            // metre across compares with a metre up, in texture pixels, and widen the cut by it.
            Vector3 across = camera.WorldToViewportPoint(feet + camera.transform.right);
            Vector3 up = camera.WorldToViewportPoint(feet + camera.transform.up);
            float squeeze = (across.x - ground.x) * live.width / ((up.y - ground.y) * live.height);
            if (squeeze <= .01f) return false;
            float uvHeight = tall * 1.1f;
            float uvWidth = uvHeight * LiveRestAspect * live.height / live.width * squeeze;
            uv = new Rect(ground.x - uvWidth * .5f, ground.y - tall * .06f, uvWidth, uvHeight);
            return true;
        }

        private void FitRestBox(float aspect)
        {
            var size = _restPortrait.sizeDelta;
            if (!Mathf.Approximately(size.x, size.y * aspect)) _restPortrait.sizeDelta = new Vector2(size.y * aspect, size.y);
        }

        public void Preview(View view)
        {
            _preview = true;
            if (view == View.Loading) ShowLoading();
            if (view == View.Exercise) ShowExercise(_previewSet, 3, 18, 92, 45, "", true, true);
            if (view == View.Rest) ShowRest(_previewSet, 3, 45, 85, false);
            if (view == View.Results) ShowResults(57, 3, 92, 570, true);
        }
        public void BindPreview()
        {
            _preview = true;
            Bind(() => PushStars.OTA.OtaSceneLoader.LoadScene("Main"), () => SetPaused(!_paused),
                () => Preview(_previewSet >= 3 ? View.Results : View.Rest),
                () => _restTimer.text = "01:00", () => { _previewSet++; Preview(View.Exercise); },
                () => { _previewSet = 1; Preview(View.Exercise); });
            if (_useMeasurementLayout)
            {
                _exercisePause.onClick.AddListener(() => SetPaused(!_paused));
                _exerciseResume.onClick.AddListener(() => SetPaused(false));
                _exerciseExit.onClick.AddListener(() => PushStars.OTA.OtaSceneLoader.LoadScene("Main"));
            }
            Preview(View.Exercise);
        }
    }
}
