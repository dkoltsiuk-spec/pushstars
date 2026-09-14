using System.Collections;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Exercise settings shared by boss battles, 1v1 and solo training.</summary>
    public sealed class BattleSettingsController : MonoBehaviour
    {
        [SerializeField] private Button _openButton;
        [SerializeField] private GameObject _overlay;
        [SerializeField] private RectTransform _sheet;
        [SerializeField] private CanvasGroup _group;
        [SerializeField] private Button _backdrop;
        [SerializeField] private Button _close;
        [SerializeField] private Button _pushup;
        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _homeLabel;
        [SerializeField] private RectTransform[] _cards;
        [SerializeField] private CanvasGroup[] _cardGroups;
        [SerializeField] private TrainingSettingsController _trainingSettings;
        private Vector2[] _rest;
        private Coroutine _transition;
        private bool _open;
        private float _shown;
        private Vector2 _lastSize;
        private bool _friendDuelActive;
        private bool _selecting;

        public void SetFriendDuelActive(bool active) => _friendDuelActive = active;

        public bool IsOpen => _open;

        private void Start()
        {
            _rest = new Vector2[_cards.Length];
            for (int i = 0; i < _cards.Length; i++) _rest[i] = _cards[i].anchoredPosition;
            _openButton.onClick.AddListener(Show);
            _backdrop.onClick.AddListener(Hide);
            _close.onClick.AddListener(Hide);
            _pushup.onClick.AddListener(SelectPushup);
            _overlay.SetActive(false);
        }

        public void Show()
        {
            if (_open) return;
            var mode = _friendDuelActive ? GameMode.Pvp : SelectedGameMode.Current;
            if (mode == GameMode.Training && _trainingSettings != null)
            { _trainingSettings.Show(); return; }
            _title.text = mode == GameMode.Boss ? "BOSS SETTINGS" :
                mode == GameMode.Pvp ? "PVP 1V1 SETTINGS" : "TRAINING SETTINGS";
            _open = true;
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
            _lastSize = Vector2.zero;
            Fit();
            Animate(true);
        }

        public void Hide()
        {
            if (!_open) return;
            _open = false;
            Animate(false);
        }

        public void SelectPushup()
        {
            // Push-ups are the sole supported exercise in every current fight route.
            // The two locked entries intentionally have no selection callbacks.
            if (!_open || _selecting) return;
            StartCoroutine(PressThenSelectPushup());
        }

        private IEnumerator PressThenSelectPushup()
        {
            _selecting = true;
            Transform target = _pushup.transform;
            Vector3 restScale = target.localScale;
            _pushup.interactable = false;

            // The card settles back first; only then does the settings sheet collapse.
            yield return UITween.Scale(target, restScale, restScale * .94f,
                .07f, 0f, UITween.EaseOutQuad);
            yield return UITween.Scale(target, restScale * .94f, restScale,
                .12f, 0f, UITween.EaseOutBackSoft);

            _pushup.interactable = true;
            if (_open)
            {
                _homeLabel.text = "PUSHUP";
                Hide();
            }
            _selecting = false;
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

        private void Animate(bool opening)
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(Transition(opening));
        }

        private IEnumerator Transition(bool opening)
        {
            float start = _shown;
            float duration = opening ? .44f : .2f;
            for (float time = 0; time < duration; time += Time.unscaledDeltaTime)
            {
                float t = Mathf.Clamp01(time / .24f);
                _shown = Mathf.Lerp(start, opening ? 1 : 0, 1 - Mathf.Pow(1 - t, 3));
                Position();
                if (opening)
                    for (int i = 0; i < _cards.Length; i++)
                    {
                        float progress = Mathf.Clamp01((time - .06f - i * .065f) / .25f);
                        CardPose(i, 1 - Mathf.Pow(1 - progress, 3));
                    }
                yield return null;
            }
            _shown = opening ? 1 : 0;
            Position();
            for (int i = 0; i < _cards.Length; i++) CardPose(i, 1);
            _transition = null;
            if (!opening) _overlay.SetActive(false);
        }

        private void CardPose(int i, float progress)
        {
            _cards[i].anchoredPosition = _rest[i] + Vector2.down * (18 * (1 - progress));
            _cardGroups[i].alpha = progress;
            _cardGroups[i].blocksRaycasts = progress >= .65f;
            _cardGroups[i].interactable = progress >= .65f;
        }

        private void Position()
        {
            _sheet.anchoredPosition = new Vector2(0, -(1 - _shown) * (540 * _sheet.localScale.x + 20));
            _group.alpha = _shown;
        }

        private void OnDisable()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            _open = false;
            _selecting = false;
            _shown = 0;
            if (_overlay != null) _overlay.SetActive(false);
            if (_rest != null) for (int i = 0; i < _cards.Length; i++) CardPose(i, 1);
        }

        private void OnDestroy()
        {
            if (_openButton != null) _openButton.onClick.RemoveListener(Show);
            if (_backdrop != null) _backdrop.onClick.RemoveListener(Hide);
            if (_close != null) _close.onClick.RemoveListener(Hide);
            if (_pushup != null) _pushup.onClick.RemoveListener(SelectPushup);
        }
    }
}
