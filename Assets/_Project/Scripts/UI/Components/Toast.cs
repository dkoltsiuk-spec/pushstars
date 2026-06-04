using System.Collections;
using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Lightweight transient hint banner. Call <see cref="Show"/> to fade a message in,
    /// hold it, then fade it back out. Self-contained — no tween dependency, uses unscaled
    /// time so it still animates if the game is paused.
    ///
    /// Used on the main screen for "coming soon" hints (e.g. squats / приседания).
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class Toast : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private CanvasGroup     _group;

        [Header("Timing (seconds)")]
        [SerializeField] private float _fadeIn  = 0.15f;
        [SerializeField] private float _hold    = 1.8f;
        [SerializeField] private float _fadeOut = 0.35f;

        private Coroutine _routine;

        private void Reset() => _group = GetComponent<CanvasGroup>();

        private void Awake()
        {
            if (_group == null) _group = GetComponent<CanvasGroup>();
            if (_group != null)
            {
                _group.alpha          = 0f;
                _group.blocksRaycasts = false; // purely informational — never eats taps
                _group.interactable   = false;
            }
        }

        /// <summary>Shows <paramref name="message"/>, then auto-dismisses.</summary>
        public void Show(string message)
        {
            if (_label != null) _label.text = message;
            if (!isActiveAndEnabled) return; // can't run a coroutine while disabled

            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            yield return Fade(_group != null ? _group.alpha : 0f, 1f, _fadeIn);
            yield return new WaitForSecondsRealtime(_hold);
            yield return Fade(1f, 0f, _fadeOut);
            _routine = null;
        }

        private IEnumerator Fade(float from, float to, float dur)
        {
            if (_group == null) yield break;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = Mathf.Lerp(from, to, dur <= 0f ? 1f : t / dur);
                yield return null;
            }
            _group.alpha = to;
        }
    }
}
