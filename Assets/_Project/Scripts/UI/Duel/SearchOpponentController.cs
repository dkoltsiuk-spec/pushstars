using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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
    /// Lives on a small always-active GameObject so it keeps running while the overlay
    /// itself is toggled on/off. Phase 14 swaps the cosmetic spinner for a real matchmaking
    /// state machine (searching → found → countdown) — the open/close plumbing here stays.
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
        }

        /// <summary>Closes the overlay (quick scale-down + fade); the main screen returns.</summary>
        public void Hide()
        {
            if (!_open || _overlay == null) return;
            _open = false;

            if (_juicy != null) _juicy.PlayOut(() => { if (_overlay != null) _overlay.SetActive(false); });
            else                _overlay.SetActive(false);

            RecedeMain(false);
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
