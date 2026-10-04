using System.Collections;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Authored home bottom sheet. All artwork and positions remain editable in Main.</summary>
    public sealed class ModeSelectionController : MonoBehaviour
    {
        [SerializeField] private Button _openButton;
        [SerializeField] private GameObject _overlay;
        [SerializeField] private RectTransform _sheet;
        [SerializeField] private CanvasGroup _overlayGroup;
        [SerializeField] private Button _backdropButton;
        [SerializeField] private Button _closeButton;
        [SerializeField] private Button[] _cards;
        [SerializeField] private Button[] _infoButtons;
        [SerializeField] private GameObject _infoPanel;
        [SerializeField] private Button _infoClose;
        [SerializeField] private TextMeshProUGUI _infoTitle;
        [SerializeField] private TextMeshProUGUI _infoBody;
        [SerializeField] private Image _infoIcon;
        [SerializeField] private TextMeshProUGUI _online;
        [SerializeField] private Image _onlineDot;
        [SerializeField] private TextMeshProUGUI _homeLabel;
        [SerializeField] private Image _homeIcon;
        [SerializeField] private Image _homeBackground;
        [SerializeField] private Sprite _trainingBackground;
        [SerializeField] private Sprite[] _icons;
        [SerializeField] private TextMeshProUGUI _actionLabel;
        [SerializeField] private TextMeshProUGUI _settingsLabel;
        [SerializeField] private Image _settingsIcon;
        [SerializeField] private Sprite _trainingSettingsIcon;
        private Sprite _exerciseIcon;
        private Sprite _defaultHomeBackground;
        private Color _defaultHomeBackgroundColor;
        private Vector3 _homeIconScale, _settingsIconScale;
        private Vector2 _homeIconPosition, _settingsIconPosition, _homeLabelPosition, _settingsLabelPosition;
        private Vector2 _homeIconSize, _settingsIconSize;
        private OnlinePresence _presence;
        private Coroutine _transition;
        private Coroutine _cardEntrance;
        private RectTransform[] _entranceRects;
        private CanvasGroup[] _entranceGroups;
        private Vector2[] _restPositions;
        private Vector3[] _restScales;
        private bool _open;
        private float _shown;
        private Vector2 _lastSize;
        private Image[] _selectionOutlines;
        private Image[] _selectionWaves;
        private const int SelectionWaveCount = 2;
        private int _highlightedMode = -1;
        private float _selectionPulseStarted;
        private bool _friendDuelActive, _started;
        private bool _selecting;

        public void SetFriendDuelActive(bool active)
        {
            if (_friendDuelActive == active) return;
            _friendDuelActive = active;
            if (_started) ApplySelection();
        }

        public bool IsOpen => _open;
        public bool IsInfoOpen => _infoPanel != null && _infoPanel.activeSelf;
        public static string Title(GameMode mode) => mode == GameMode.Pvp ? "PVP" : mode == GameMode.Boss ? "BOSS" : "TRAINING";
        public static string Description(GameMode mode)
        {
            switch (mode)
            {
                case GameMode.Boss:
                    return "Defeat the boss in 60 seconds!\n\nDo push-ups with good form and complete more reps than your opponent. The camera counts your reps automatically.\n\nEach victory unlocks the next boss. Every new challenge gets tougher.";
                case GameMode.Training:
                    return "Train without an opponent. Choose sets and rest time in the settings to the right of START.\n\nEach set lasts 60 seconds. Position your phone so the camera sees your whole body to count push-ups and check your form.\n\nRest for 30, 60, or 90 seconds, or until you tap CONTINUE. Your best set is saved for future duels.";
                default:
                    return "A 60-second duel: complete more proper push-ups than your opponent.\n\nThe camera counts reps automatically. The higher score wins.\n\nYou can currently duel a recording of your best set. Complete an assessment first if you don't have one. Live PvP is coming later.\n\nThe green counter shows players active in the last 75 seconds. A dash means the server is unavailable.";
            }
        }

        private void Start()
        {
            CacheHomeLayout();
            _started = true;
            ArenaProfile.Changed += ApplySelection;
            var arenaHome = gameObject.GetComponent<ArenaHomeController>() ?? gameObject.AddComponent<ArenaHomeController>();
            arenaHome.Configure(_openButton.transform.parent.parent, _overlay.transform.parent, _homeLabel.font);
            _openButton.onClick.AddListener(Show);
            _backdropButton.onClick.AddListener(Back);
            _closeButton.onClick.AddListener(Hide);
            _infoClose.onClick.AddListener(CloseInfo);
            for (int i = 0; i < _cards.Length; i++)
            {
                var mode = (GameMode)i;
                _cards[i].onClick.AddListener(() => Select(mode));
                _infoButtons[i].onClick.AddListener(() => ShowInfo(mode));
            }
            CacheCardPoses();
            _overlay.SetActive(false);
            _infoPanel.SetActive(false);
            ApplySelection();
            RefreshOnline(null);
        }

        private void Update()
        {
            if (_presence == null && OnlinePresence.Instance != null)
            {
                _presence = OnlinePresence.Instance;
                _presence.Changed += RefreshOnline;
                RefreshOnline(_presence.Count);
            }
            if (!_open && _shown <= 0f) return;
            PulseSelection();
            FitSheet();
            if (Input.GetKeyDown(KeyCode.Escape)) Back();
        }

        private void FitSheet()
        {
            var parent = (RectTransform)_sheet.parent;
            var size = parent.rect.size;
            if (size == _lastSize) return;
            _lastSize = size;
            // Entire sheet remains reachable on small devices and in landscape.
            float scale = Mathf.Min(size.x / 390f, size.y * .92f / 540f);
            _sheet.localScale = Vector3.one * scale;
            SetPosition();
        }

        public void Show()
        {
            if (_friendDuelActive) return;
            if (_open) return;
            _open = true;
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
            RefreshSelectionOutline();
            _infoPanel.SetActive(false);
            _lastSize = Vector2.zero;
            FitSheet();
            Animate(1f);
            if (_cardEntrance != null) StopCoroutine(_cardEntrance);
            _cardEntrance = StartCoroutine(RevealCards());
        }

        public void Hide()
        {
            if (!_open) return;
            _open = false;
            if (_cardEntrance != null) StopCoroutine(_cardEntrance);
            _cardEntrance = null;
            _infoPanel.SetActive(false);
            Animate(0f);
        }

        public void Back() { if (IsInfoOpen) CloseInfo(); else Hide(); }
        public void CloseInfo() => _infoPanel.SetActive(false);
        public void ShowInfo(GameMode mode)
        {
            if (!_open) return;
            _infoTitle.text = Title(mode);
            _infoBody.text = Description(mode);
            _infoIcon.sprite = _icons[(int)mode];
            _infoPanel.SetActive(true);
        }

        public void Select(GameMode mode)
        {
            if (_friendDuelActive || _selecting || !_open) return;
            SelectedGameMode.Current = mode;
            ApplySelection();
            StartCoroutine(PressThenSelect(mode));
        }

        private IEnumerator PressThenSelect(GameMode mode)
        {
            _selecting = true;
            Button button = _cards[(int)mode];
            Transform target = button.transform;
            Vector3 restScale = target.localScale;
            button.interactable = false;

            // Let the card visibly sink before it returns and the sheet starts closing.
            yield return UITween.Scale(target, restScale, restScale * .94f,
                .07f, 0f, UITween.EaseOutQuad);
            yield return UITween.Scale(target, restScale * .94f, restScale,
                .12f, 0f, UITween.EaseOutBackSoft);

            button.interactable = true;
            if (_open)
            {
                Hide();
            }
            _selecting = false;
        }

        private void ApplySelection()
        {
            var mode = _friendDuelActive ? GameMode.Pvp : SelectedGameMode.Current;
            RefreshSelectionOutline();
            _homeLabel.text = Title(mode);
            _homeIcon.sprite = _icons[(int)mode];
            bool training = mode == GameMode.Training;
            ApplyHomeBackground(training);
            if (_settingsIcon == null || _trainingSettingsIcon == null) return;
            _actionLabel.text = training ? "START" : mode == GameMode.Boss ? "FIGHT" : "BATTLE";
            _settingsLabel.text = training ? "SETTINGS" : "PUSHUP";
            _settingsIcon.sprite = training ? _trainingSettingsIcon : _exerciseIcon;
            _homeIcon.rectTransform.localScale = training ? Vector3.one : _homeIconScale;
            _homeIcon.rectTransform.sizeDelta = training ? new Vector2(54, 54) : _homeIconSize;
            _homeIcon.rectTransform.anchoredPosition = training ? new Vector2(3, -19) : _homeIconPosition;
            if (mode == GameMode.Boss)
            {
                _homeIcon.rectTransform.localScale = _homeIconScale * 0.88f;
                _homeIcon.rectTransform.anchoredPosition = _homeIconPosition + new Vector2(4, 0);
            }
            _settingsIcon.rectTransform.localScale = training ? Vector3.one : _settingsIconScale;
            _settingsIcon.rectTransform.sizeDelta = training ? new Vector2(46, 50) : _settingsIconSize;
            _settingsIcon.rectTransform.anchoredPosition = training ? new Vector2(3, -17) : _settingsIconPosition;
            _homeLabel.rectTransform.anchoredPosition = training ? new Vector2(0, 3) : _homeLabelPosition;
            _settingsLabel.rectTransform.anchoredPosition = training ? new Vector2(0, 3) : _settingsLabelPosition;
        }

        private void ApplyHomeBackground(bool training)
        {
            if (_homeBackground == null || _trainingBackground == null || _defaultHomeBackground == null) return;
            _homeBackground.sprite = training ? _trainingBackground : (ArenaCatalog.Get(ArenaProfile.SelectedId)?.Home ?? _defaultHomeBackground);
            _homeBackground.color = training || ArenaProfile.SelectedId != ArenaCatalog.DefaultId ? Color.white : _defaultHomeBackgroundColor;
        }

        private void RefreshSelectionOutline()
        {
            if (_selectionOutlines == null)
            {
                var shader = Resources.Load<Shader>("ModeSelectionOutline");
                if (shader == null)
                {
                    Debug.LogError("Mode selection outline shader is missing.", this);
                    return;
                }
                _selectionOutlines = new Image[_cards.Length];
                _selectionWaves = new Image[_cards.Length * SelectionWaveCount];
                for (int i = 0; i < _cards.Length; i++)
                {
                    var source = _cards[i].GetComponent<Image>();
                    var outline = new GameObject("SelectedModeOutline", typeof(RectTransform),
                        typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
                    var rect = outline.rectTransform;
                    rect.SetParent(source.transform, false);
                    rect.SetAsFirstSibling(); // Above the card artwork, below its label and icon.
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    outline.sprite = source.sprite;
                    outline.raycastTarget = false;
                    outline.type = source.type;
                    outline.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
                    outline.material = new Material(shader) { name = "SelectedModeOutline" };
                    var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(source.sprite);
                    var size = source.rectTransform.rect.size;
                    outline.material.SetVector("_OuterUV", uv);
                    outline.material.SetVector("_OutlineUV", new Vector4(
                        4f * (uv.z - uv.x) / Mathf.Max(1f, size.x),
                        4f * (uv.w - uv.y) / Mathf.Max(1f, size.y), 0f, 0f));
                    outline.enabled = false;
                    _selectionOutlines[i] = outline;
                    for (int wave = 0; wave < SelectionWaveCount; wave++)
                    {
                        var ripple = new GameObject("SelectedModeWave" + wave, typeof(RectTransform),
                            typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
                        var rippleRect = ripple.rectTransform;
                        rippleRect.SetParent(source.transform, false);
                        rippleRect.SetAsFirstSibling(); // Keep waves behind the steady outline and lettering.
                        rippleRect.anchorMin = Vector2.zero;
                        rippleRect.anchorMax = Vector2.one;
                        rippleRect.offsetMin = rippleRect.offsetMax = Vector2.zero;
                        ripple.sprite = source.sprite;
                        ripple.type = source.type;
                        ripple.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
                        ripple.raycastTarget = false;
                        ripple.material = new Material(shader) { name = "SelectedModeWave" };
                        ripple.material.SetVector("_OuterUV", uv);
                        ripple.enabled = false;
                        _selectionWaves[i * SelectionWaveCount + wave] = ripple;
                    }
                }
            }

            _highlightedMode = (int)SelectedGameMode.Current;
            _selectionPulseStarted = Time.unscaledTime;
            for (int i = 0; i < _selectionOutlines.Length; i++)
            {
                _selectionOutlines[i].enabled = i == _highlightedMode;
                for (int wave = 0; wave < SelectionWaveCount; wave++)
                    _selectionWaves[i * SelectionWaveCount + wave].enabled = i == _highlightedMode;
            }
            PulseSelection();
        }

        private void PulseSelection()
        {
            if (_selectionOutlines == null || _highlightedMode < 0
                || _highlightedMode >= _selectionOutlines.Length) return;

            // A steady silhouette with a gentle 1.6-second brightness cycle; never blinks off.
            float pulse = .5f + .5f * Mathf.Cos(
                (Time.unscaledTime - _selectionPulseStarted) * (Mathf.PI * 2f / 1.6f));
            _selectionOutlines[_highlightedMode].color = Color.Lerp(
                new Color(1f, .82f, 0f, .9f), new Color(1f, 1f, .42f, 1f), pulse);
            UpdateSelectionWaves(Time.unscaledTime - _selectionPulseStarted);
        }

        private void UpdateSelectionWaves(float elapsed)
        {
            // Two thin silhouettes travel only ten design units beyond the selected card.
            // Start each wave at the border, fade it in briefly and dissolve as it expands.
            for (int wave = 0; wave < SelectionWaveCount; wave++)
            {
                var ripple = _selectionWaves[_highlightedMode * SelectionWaveCount + wave];
                float age = elapsed - wave * .75f;
                float progress = age < 0f ? 0f : Mathf.Repeat(age, 1.5f) / 1.5f;
                float expansion = Mathf.Lerp(1f, 10f, progress);
                var rect = ripple.rectTransform;
                rect.offsetMin = -Vector2.one * expansion;
                rect.offsetMax = Vector2.one * expansion;
                float alpha = age < 0f ? 0f : .48f * Mathf.Clamp01(progress / .12f)
                    * Mathf.Pow(1f - progress, 1.5f);
                ripple.color = new Color(1f, .94f, .08f, alpha);
                var uv = UnityEngine.Sprites.DataUtility.GetOuterUV(ripple.sprite);
                var size = rect.rect.size;
                ripple.material.SetVector("_OutlineUV", new Vector4(
                    1.5f * (uv.z - uv.x) / Mathf.Max(1f, size.x),
                    1.5f * (uv.w - uv.y) / Mathf.Max(1f, size.y), 0f, 0f));
            }
        }

        private void CacheHomeLayout()
        {
            if (_homeBackground != null)
            {
                _defaultHomeBackground = _homeBackground.sprite;
                _defaultHomeBackgroundColor = _homeBackground.color;
            }
            if (_settingsIcon == null) return;
            _exerciseIcon = _settingsIcon.sprite;
            _homeIconScale = _homeIcon.rectTransform.localScale;
            _homeIconSize = _homeIcon.rectTransform.sizeDelta;
            _homeIconPosition = _homeIcon.rectTransform.anchoredPosition;
            _settingsIconScale = _settingsIcon.rectTransform.localScale;
            _settingsIconSize = _settingsIcon.rectTransform.sizeDelta;
            _settingsIconPosition = _settingsIcon.rectTransform.anchoredPosition;
            _homeLabelPosition = _homeLabel.rectTransform.anchoredPosition;
            _settingsLabelPosition = _settingsLabel.rectTransform.anchoredPosition;
        }

        private void RefreshOnline(int? count)
        {
            _online.text = count.HasValue ? count.Value.ToString("N0") : "—";
            var color = count.HasValue ? new Color32(48, 255, 24, 255) : new Color32(225, 222, 218, 255);
            _online.color = color;
            _onlineDot.color = color;
        }

        private void Animate(float target)
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(Slide(target));
        }

        private void CacheCardPoses()
        {
            int count = _cards.Length * 2;
            _entranceRects = new RectTransform[count];
            _entranceGroups = new CanvasGroup[count];
            _restPositions = new Vector2[count];
            _restScales = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                var button = i % 2 == 0 ? _cards[i / 2] : _infoButtons[i / 2];
                var rect = (RectTransform)button.transform;
                _entranceRects[i] = rect;
                _restPositions[i] = rect.anchoredPosition;
                _restScales[i] = rect.localScale;
                var group = button.GetComponent<CanvasGroup>();
                if (group == null) group = button.gameObject.AddComponent<CanvasGroup>();
                _entranceGroups[i] = group;
            }
        }

        private IEnumerator RevealCards()
        {
            // PVP, BOSS, TRAINING rise and fade in with a short stagger. The sibling info
            // buttons follow their cards, so their artwork never floats ahead of the row.
            const float leadIn = .06f, stagger = .065f, duration = .25f;
            float total = leadIn + (_cards.Length - 1) * stagger + duration;
            for (float time = 0f; time < total; time += Time.unscaledDeltaTime)
            {
                for (int i = 0; i < _cards.Length; i++)
                {
                    float progress = Mathf.Clamp01((time - leadIn - i * stagger) / duration);
                    float eased = 1f - Mathf.Pow(1f - progress, 3f);
                    SetCardPose(i * 2, eased);
                    SetCardPose(i * 2 + 1, eased);
                }
                yield return null;
            }
            RestoreCardPoses();
            _cardEntrance = null;
        }

        private void SetCardPose(int index, float progress)
        {
            _entranceRects[index].anchoredPosition = _restPositions[index] + Vector2.down * (18f * (1f - progress));
            _entranceRects[index].localScale = _restScales[index] * Mathf.Lerp(.975f, 1f, progress);
            _entranceGroups[index].alpha = progress;
            _entranceGroups[index].interactable = progress >= .65f;
            _entranceGroups[index].blocksRaycasts = progress >= .65f;
        }

        private void RestoreCardPoses()
        {
            if (_entranceRects == null) return;
            for (int i = 0; i < _entranceRects.Length; i++) SetCardPose(i, 1f);
        }

        private IEnumerator Slide(float target)
        {
            float start = _shown;
            for (float time = 0; time < .24f; time += Time.unscaledDeltaTime)
            {
                float t = 1f - Mathf.Pow(1f - time / .24f, 3f);
                _shown = Mathf.Lerp(start, target, t);
                SetPosition();
                yield return null;
            }
            _shown = target;
            SetPosition();
            _transition = null;
            if (target == 0f) _overlay.SetActive(false);
        }

        private void SetPosition()
        {
            _sheet.anchoredPosition = new Vector2(0, -(1f - _shown) * (540f * _sheet.localScale.x + 20f));
            _overlayGroup.alpha = _shown;
        }

        private void OnDisable()
        {
            if (_transition != null) StopCoroutine(_transition);
            if (_cardEntrance != null) StopCoroutine(_cardEntrance);
            _cardEntrance = null;
            RestoreCardPoses();
            _transition = null;
            _shown = 0f;
            _open = false;
            _selecting = false;
            if (_overlay != null) _overlay.SetActive(false);
        }

        private void OnDestroy()
        {
            ArenaProfile.Changed -= ApplySelection;
            if (_selectionOutlines != null)
                foreach (var outline in _selectionOutlines)
                    if (outline != null)
                    {
                        if (Application.isPlaying) Destroy(outline.material);
                        else DestroyImmediate(outline.material);
                    }
            if (_selectionWaves != null)
                foreach (var wave in _selectionWaves)
                    if (wave != null)
                    {
                        if (Application.isPlaying) Destroy(wave.material);
                        else DestroyImmediate(wave.material);
                    }
            if (_presence != null) _presence.Changed -= RefreshOnline;
            if (_openButton != null) _openButton.onClick.RemoveListener(Show);
        }
    }
}
