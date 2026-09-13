using System.Collections;
using PushStars.Core;
using PushStars.OTA;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed class TrainingSettingsController : MonoBehaviour
    {
        [SerializeField] private GameObject _overlay;
        [SerializeField] private RectTransform _sheet;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Button _backdrop, _close, _minus, _plus, _start;
        [SerializeField] private Button[] _restButtons;
        [SerializeField] private TextMeshProUGUI _setCount, _summary, _estimate, _error;
        [SerializeField] private Sprite _selectedSprite, _optionSprite;
        private static readonly int[] RestOptions = { 30, 60, 90, TrainingPlan.ManualRest };
        private TrainingPlan _plan;
        private Coroutine _transition;
        private bool _open, _launching;
        private float _shown;
        private Vector2 _lastSize;
        public bool IsOpen => _open;
        public TrainingPlan Plan => _plan;

        private void Start()
        {
            _backdrop.onClick.AddListener(Hide); _close.onClick.AddListener(Hide);
            _minus.onClick.AddListener(() => ChangeSets(-1)); _plus.onClick.AddListener(() => ChangeSets(1));
            _start.onClick.AddListener(StartWorkout);
            for (int i = 0; i < _restButtons.Length; i++)
            { int value = RestOptions[i]; _restButtons[i].onClick.AddListener(() => SelectRest(value)); }
            _overlay.SetActive(false);
        }

        public void Show()
        {
            if (_open || _launching) return;
            _plan = TrainingPlan.Load();
            Refresh(); _error.text = "";
            _open = true;
            _overlay.SetActive(true); _overlay.transform.SetAsLastSibling();
            _lastSize = Vector2.zero; Fit(); Animate(1);
        }
        public void Hide() { if (!_open) return; _open = false; Animate(0); }
        public void ChangeSets(int delta) { _plan = new TrainingPlan(_plan.Sets + delta, _plan.RestSeconds); SaveAndRefresh(); }
        public void SelectRest(int seconds) { _plan = new TrainingPlan(_plan.Sets, seconds); SaveAndRefresh(); }
        private void SaveAndRefresh() { _plan.Save(); Refresh(); }
        private void Refresh()
        {
            _setCount.text = _plan.Sets.ToString();
            _minus.interactable = _plan.Sets > TrainingPlan.MinSets;
            _plus.interactable = _plan.Sets < TrainingPlan.MaxSets;
            for (int i = 0; i < _restButtons.Length; i++)
                _restButtons[i].GetComponent<Image>().sprite = _plan.RestSeconds == RestOptions[i] ? _selectedSprite : _optionSprite;
            string rest = _plan.IsManualRest ? "manual rest" : $"{_plan.RestSeconds} sec. rest";
            _summary.text = $"Total workout:\n{_plan.Sets} {(_plan.Sets == 1 ? "set" : "sets")}, {rest}";
            _estimate.text = _plan.EstimatedSeconds.HasValue ? $"~{Mathf.CeilToInt(_plan.EstimatedSeconds.Value / 60f)} min." : "∞";
        }

        public void StartWorkout()
        {
            if (!_open || _launching) return;
            if (!Application.CanStreamedLevelBeLoaded(FightConfig.TrainingSceneName))
            { _error.text = "Training scene is unavailable"; return; }
            _launching = true; _start.interactable = false;
            _plan.Save(); FightRequest.Training(_plan);
            OtaSceneLoader.LoadScene(FightConfig.TrainingSceneName);
        }

        private void Update()
        {
            if (!_open && _shown <= 0) return;
            Fit();
            if (_open && Input.GetKeyDown(KeyCode.Escape)) Hide();
        }
        private void Fit()
        {
            var size = ((RectTransform)_sheet.parent).rect.size;
            if (size == _lastSize) return;
            _lastSize = size;
            _sheet.localScale = Vector3.one * Mathf.Min(size.x / 390f, size.y * .92f / 540f);
            Position();
        }
        private void Animate(float target)
        { if (_transition != null) StopCoroutine(_transition); _transition = StartCoroutine(Slide(target)); }
        private IEnumerator Slide(float target)
        {
            float from = _shown;
            for (float time = 0; time < .26f; time += Time.unscaledDeltaTime)
            {
                _shown = Mathf.Lerp(from, target, 1 - Mathf.Pow(1 - time / .26f, 3));
                Position(); yield return null;
            }
            _shown = target; Position(); _transition = null;
            if (target == 0) _overlay.SetActive(false);
        }
        private void Position()
        { _sheet.anchoredPosition = new Vector2(0, -(1 - _shown) * (540 * _sheet.localScale.x + 20)); _group.alpha = _shown; }
        private void OnDisable()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = null; _open = false; _shown = 0;
            if (_overlay != null) _overlay.SetActive(false);
        }
    }
}
