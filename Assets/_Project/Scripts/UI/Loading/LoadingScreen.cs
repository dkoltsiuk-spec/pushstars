using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// What the player looks at while Boot brings the app up: the poster art, a progress bar and
    /// the percentage on it. Driven by <c>AppBootstrap</c>, which reports real progress (services,
    /// then the scene load) rather than an animation pretending to be one.
    ///
    /// <para><b>The bar never goes backwards and never teleports.</b> Startup progress arrives in
    /// jumps — a Firebase handshake finishes and half the bar appears at once — which reads as a
    /// glitch. The fill chases the reported value at a bounded speed instead, so every jump becomes
    /// a short sweep, and <see cref="Finished"/> lets the caller wait for the bar to actually reach
    /// the end before the screen is replaced. The number counts the drawn value, not the reported
    /// one, so it never shows a total the bar has not reached.</para>
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        [SerializeField] private Image _progressFill;
        [SerializeField] private TextMeshProUGUI _percent;
        [SerializeField] private TextMeshProUGUI _status;
        [SerializeField] private TextMeshProUGUI _version;
        [SerializeField] private CanvasGroup _group;

        [Tooltip("Seconds the bar takes to travel its whole length. AppBootstrap overwrites this " +
                 "with its own minimum display time, so the bar is still moving for the whole of " +
                 "a fast launch instead of arriving early and sitting full.")]
        [SerializeField, Range(0.2f, 4f)] private float _sweepSec = 1.5f;

        /// <summary>Below this the fill has less width than its own two end caps and renders as a
        /// pinched lozenge, so it is hidden instead of drawn squashed.</summary>
        private const float MinVisibleFill = 0.02f;

        /// <summary>Fractions of the bar per second. Guarded rather than clamped to the field's
        /// Range: <see cref="PaceOver"/> takes its number from elsewhere and a zero there would
        /// divide the sweep into infinity.</summary>
        private float FillSpeed => 1f / Mathf.Max(_sweepSec, 0.05f);

        private float _target;
        private float _shown;
        private int _shownPercent = -1;
        private string _statusText = "";
        private float _statusSetAt;

        /// <summary>True once the bar has caught up with the last reported progress and reached
        /// the end. The bootstrap waits for this so the app never cuts away mid-sweep.</summary>
        public bool Finished => _target >= 1f && _shown >= 0.999f;

        private void Awake()
        {
            if (_version != null) _version.text = $"v{Application.version}";
            ApplyFill(0f);
        }

        private void Update()
        {
            // The value advances even with no bar to draw it on. AppBootstrap waits for
            // Finished before it swaps scenes, so an unwired fill would otherwise hold the app
            // on the loading screen forever — a missing reference must cost a progress bar, not
            // the launch.
            _shown = Mathf.MoveTowards(_shown, _target, FillSpeed * Time.unscaledDeltaTime);
            ApplyFill(_shown);

            // A phase that outstays a second starts counting out loud. Startup is a sequence of
            // named steps, so the one the number is attached to IS the diagnosis — no profiler,
            // no cable, no guessing which of them is slow.
            if (_status == null || _statusText.Length == 0) return;
            float held = Time.realtimeSinceStartup - _statusSetAt;
            _status.text = held > 1f ? $"{_statusText}  {held:0.0}s" : _statusText;
        }

        /// <summary>
        /// Stretches a full sweep of the bar over <paramref name="seconds"/>.
        ///
        /// <para>The pace and the launch's minimum length are the same number said twice, so the
        /// bootstrap hands its own over rather than letting a second one be tuned here. Tuned
        /// apart they produce the two ways this screen looks wrong: a bar that fills early and
        /// then sits at 100 % waiting to be dismissed, or one still climbing when the app cuts
        /// away from it.</para>
        /// </summary>
        public void PaceOver(float seconds) => _sweepSec = Mathf.Max(seconds, 0.05f);

        /// <summary>Reports how far along startup is. Progress only ever moves forward.</summary>
        public void Report(float progress01, string status = null)
        {
            _target = Mathf.Clamp01(Mathf.Max(_target, progress01));

            // Progress-only reports must not restart the clock — the timer belongs to the phase,
            // not to the last thing that happened to tick.
            if (string.IsNullOrEmpty(status) || status == _statusText) return;
            _statusText = status;
            _statusSetAt = Time.realtimeSinceStartup;
            if (_status != null) _status.text = status;
        }

        /// <summary>Fades the screen out. Called with the next scene already loading behind it.</summary>
        public void SetAlpha(float alpha)
        {
            if (_group != null) _group.alpha = Mathf.Clamp01(alpha);
        }

        /// <summary>
        /// Draws the bar at <paramref name="t"/> of its track and puts the same value on the label.
        ///
        /// <para>The width is driven through the fill's right anchor rather than
        /// <c>Image.fillAmount</c>: a filled image crops the sprite with a hard vertical edge,
        /// while the design's fill carries a round cap that has to travel with the leading edge.
        /// Only a real width change keeps it, and a sliced sprite is what makes the width free to
        /// change without smearing the caps.</para>
        /// </summary>
        private void ApplyFill(float t)
        {
            t = Mathf.Clamp01(t);

            if (_progressFill != null)
            {
                var rt = _progressFill.rectTransform;
                var max = rt.anchorMax;
                // Only the x anchor is touched: the scene sets the rest, and rewriting all of it
                // every frame would quietly overwrite any layout tweak made in the Inspector.
                if (!Mathf.Approximately(max.x, t)) rt.anchorMax = new Vector2(t, max.y);
                _progressFill.enabled = t >= MinVisibleFill;
            }

            if (_percent == null) return;
            int whole = Mathf.RoundToInt(t * 100f);
            if (whole == _shownPercent) return;
            _shownPercent = whole;
            _percent.text = $"{whole}%";
        }
    }
}
