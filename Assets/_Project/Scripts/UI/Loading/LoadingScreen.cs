using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// What the player looks at while Boot brings the app up: the wordmark, a progress bar and one
    /// line saying what is happening. Driven by <c>AppBootstrap</c>, which reports real progress
    /// (services, then the scene load) rather than an animation pretending to be one.
    ///
    /// <para><b>The bar never goes backwards and never teleports.</b> Startup progress arrives in
    /// jumps — a Firebase handshake finishes and half the bar appears at once — which reads as a
    /// glitch. The fill chases the reported value at a bounded speed instead, so every jump becomes
    /// a short sweep, and <see cref="Finished"/> lets the caller wait for the bar to actually reach
    /// the end before the screen is replaced.</para>
    /// </summary>
    public sealed class LoadingScreen : MonoBehaviour
    {
        [SerializeField] private Image _progressFill;
        [SerializeField] private TextMeshProUGUI _status;
        [SerializeField] private TextMeshProUGUI _version;
        [SerializeField] private CanvasGroup _group;

        [Tooltip("Fastest the bar may travel, in fractions of its length per second.")]
        [SerializeField, Range(0.2f, 4f)] private float _fillSpeed = 1.1f;

        private float _target;
        private float _shown;

        /// <summary>True once the bar has caught up with the last reported progress and reached
        /// the end. The bootstrap waits for this so the app never cuts away mid-sweep.</summary>
        public bool Finished => _target >= 1f && _shown >= 0.999f;

        private void Awake()
        {
            if (_version != null) _version.text = $"v{Application.version}";
            if (_progressFill != null) _progressFill.fillAmount = 0f;
        }

        private void Update()
        {
            // The value advances even with no bar to draw it on. AppBootstrap waits for
            // Finished before it swaps scenes, so an unwired fill would otherwise hold the app
            // on the loading screen forever — a missing reference must cost a progress bar, not
            // the launch.
            _shown = Mathf.MoveTowards(_shown, _target, _fillSpeed * Time.unscaledDeltaTime);
            if (_progressFill != null) _progressFill.fillAmount = _shown;
        }

        /// <summary>Reports how far along startup is. Progress only ever moves forward.</summary>
        public void Report(float progress01, string status = null)
        {
            _target = Mathf.Clamp01(Mathf.Max(_target, progress01));
            if (!string.IsNullOrEmpty(status) && _status != null) _status.text = status;
        }

        /// <summary>Fades the screen out. Called with the next scene already loading behind it.</summary>
        public void SetAlpha(float alpha)
        {
            if (_group != null) _group.alpha = Mathf.Clamp01(alpha);
        }
    }
}
