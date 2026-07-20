using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PushStars.Core;

namespace PushStars.UI
{
    /// <summary>
    /// Drives the full-screen "ПОИСК СОПЕРНИКА" (Search Opponent) overlay on the main screen.
    ///
    ///   • НАЙТИ СОПЕРНИКА (the Duel-panel primary CTA) opens the overlay with a calm,
    ///     staggered Brawl-Stars-style entrance (see <see cref="JuicyScreen"/>) while the
    ///     main screen (character + UI) recedes back and fades — one screen leaves, the
    ///     other arrives.
    ///   • ВЫЙТИ (the red <see cref="ExitButton"/>) snaps it shut and brings the main screen
    ///     back forward.
    ///   • While open, <see cref="LoadingVsRing"/> spins the dashed VS ring.
    ///
    /// Phase 08.9: the spinner is no longer pure cosmetics — after <see cref="FightConfig.SearchDelaySec"/>
    /// the "opponent" is found (the current <see cref="BossCatalog"/> boss), the card flips to
    /// СОПЕРНИК НАЙДЕН and the Fight scene loads. The found-state UI is built at runtime from the
    /// overlay's own Title label, so the serialized Main scene needs no regeneration. Phase 12.5
    /// replaces the fixed delay with the real ladder (live queue → ghost → bot).
    ///
    /// Lives on a small always-active GameObject so it keeps running while the overlay
    /// itself is toggled on/off.
    /// </summary>
    public class SearchOpponentController : MonoBehaviour
    {
        [Header("Overlay (toggled root)")]
        [SerializeField] private GameObject  _overlay;
        [SerializeField] private JuicyScreen _juicy; // transition choreography

        [Header("Main screen (recedes behind the overlay)")]
        [SerializeField] private RectTransform _mainContent; // scaled back on open
        [SerializeField] private CanvasGroup   _mainGroup;   // faded on open
        [SerializeField] private float _mainRecedeScale = 0.92f;
        [SerializeField] private float _mainRecedeTime  = 0.34f;
        [SerializeField] private float _mainReturnTime  = 0.30f;

        [Header("Triggers")]
        [SerializeField] private Button _findButton; // НАЙТИ СОПЕРНИКА — opens
        [SerializeField] private Button _exitButton; // ВЫЙТИ — closes

        [Header("Animation")]
        [SerializeField] private LoadingVsRing _ring;

        private bool      _open;
        private Coroutine _mainAnim;

        // ── Boss matchmaking state (phase 08.9) ──
        private Coroutine _searchRoutine;
        private TextMeshProUGUI _title;      // the overlay's "ПОИСК СОПЕРНИКА" label (found by name)
        private string _titleOriginalText;
        private TextMeshProUGUI _bossLabel;  // runtime clone of the title showing the boss name

        // Wire in Start (not Awake) so every Button/ExitButton has finished its own Awake,
        // mirroring DuelModeController's ordering safeguard.
        private void Start()
        {
            if (_findButton != null) _findButton.onClick.AddListener(Show);
            if (_exitButton != null) _exitButton.onClick.AddListener(Hide);

            // Default closed — the main screen is visible first.
            if (_overlay != null) _overlay.SetActive(false);
        }

        /// <summary>Opens the matchmaking overlay; the main screen recedes behind it.</summary>
        public void Show()
        {
            if (_open || _overlay == null) return;
            _open = true;

            _overlay.SetActive(true);
            _ring?.SetVisible(true);

            if (_juicy != null) _juicy.PlayIn();
            RecedeMain(true);

            ResetFoundCard();
            _searchRoutine = StartCoroutine(SearchRoutine());
        }

        /// <summary>Closes the overlay (quick scale-down + fade); the main screen returns.</summary>
        public void Hide()
        {
            if (!_open || _overlay == null) return;
            _open = false;

            if (_searchRoutine != null) { StopCoroutine(_searchRoutine); _searchRoutine = null; }
            ResetFoundCard();

            if (_juicy != null) _juicy.PlayOut(() => { if (_overlay != null) _overlay.SetActive(false); });
            else                _overlay.SetActive(false);

            RecedeMain(false);
        }

        // ── Boss matchmaking (phase 08.9): delay → "СОПЕРНИК НАЙДЕН: <босс>" → Fight scene ──────
        private IEnumerator SearchRoutine()
        {
            yield return new WaitForSeconds(FightConfig.SearchDelaySec);

            if (!Application.CanStreamedLevelBeLoaded(FightConfig.FightSceneName))
            {
                // Fight scene absent from this build (stale build settings) — stay in the
                // searching state instead of hard-failing; ВЫЙТИ still works.
                Debug.LogError($"[SearchOpponent] Scene '{FightConfig.FightSceneName}' is not in the build — cannot start the boss duel.");
                _searchRoutine = null;
                yield break;
            }

            ShowFoundCard(BossCatalog.Current.DisplayName);
            yield return new WaitForSeconds(FightConfig.FoundPauseSec);

            _searchRoutine = null;
            SceneManager.LoadScene(FightConfig.FightSceneName);
        }

        private void ShowFoundCard(string bossName)
        {
            EnsureTitleRef();
            if (_title == null) return;

            _title.text = "СОПЕРНИК НАЙДЕН";

            // The boss name is a runtime clone of the title label (inherits the Rubik font and
            // styling), parked under the VS ring.
            if (_bossLabel == null)
            {
                var go = Instantiate(_title.gameObject, _title.transform.parent);
                go.name = "BossName";
                _bossLabel = go.GetComponent<TextMeshProUGUI>();
                var rt = _bossLabel.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, -60f); // below the ring (ring centre sits at +70)
                rt.sizeDelta = new Vector2(340f, 40f);
                _bossLabel.fontSize = 26f;
                _bossLabel.color = AppColors.AccentYellow;
                _bossLabel.alignment = TextAlignmentOptions.Center;
            }
            _bossLabel.gameObject.SetActive(true);
            _bossLabel.text = bossName;
        }

        private void ResetFoundCard()
        {
            if (_title != null && !string.IsNullOrEmpty(_titleOriginalText))
                _title.text = _titleOriginalText;
            if (_bossLabel != null)
                _bossLabel.gameObject.SetActive(false);
        }

        private void EnsureTitleRef()
        {
            if (_title != null || _overlay == null) return;
            foreach (var tmp in _overlay.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp.gameObject.name != "Title") continue;
                _title = tmp;
                _titleOriginalText = tmp.text;
                break;
            }
            if (_title == null)
                Debug.LogWarning("[SearchOpponent] Title label not found in the overlay — the found-card will not render.");
        }

        // Pushes the main screen back (scale + fade) when opening, restores it when closing.
        private void RecedeMain(bool recede)
        {
            if (_mainContent == null && _mainGroup == null) return;
            if (_mainAnim != null) StopCoroutine(_mainAnim);
            _mainAnim = StartCoroutine(RecedeRoutine(recede));
        }

        private IEnumerator RecedeRoutine(bool recede)
        {
            float dur   = recede ? _mainRecedeTime : _mainReturnTime;
            float scale = recede ? _mainRecedeScale : 1f;
            float alpha = recede ? 0f : 1f;

            if (_mainContent != null)
                StartCoroutine(UITween.Scale(_mainContent, _mainContent.localScale,
                                             Vector3.one * scale, dur, 0f, UITween.EaseOutCubic));
            if (_mainGroup != null)
                yield return UITween.Fade(_mainGroup, _mainGroup.alpha, alpha, dur, 0f);
            else
                yield return null;

            _mainAnim = null;
        }

        private void OnDestroy()
        {
            if (_findButton != null) _findButton.onClick.RemoveListener(Show);
            if (_exitButton != null) _exitButton.onClick.RemoveListener(Hide);
        }
    }
}
