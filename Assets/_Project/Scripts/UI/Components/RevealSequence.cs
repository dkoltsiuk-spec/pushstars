using System;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Brings a set of elements up one at a time, on a timeline, then holds the finished picture
    /// and does it again.
    ///
    /// <para><b>Why a diagram needs this at all.</b> Three pictures with arrows between them state
    /// a sequence; they do not show one. A reader has to work out that the left one happens first,
    /// and on a page whose whole job is to say "the video stops here and only these numbers leave",
    /// the order <i>is</i> the argument. Revealed in order, the diagram makes it without being
    /// read.</para>
    ///
    /// <para><b>Absolute times, not durations.</b> Each beat says when it happens, counted from the
    /// start of the cycle, so the timeline can be read down the Inspector like a storyboard and one
    /// beat can be nudged without shifting every beat after it. The cycle's length is derived from
    /// the last beat rather than configured, which is one fewer number able to disagree with the
    /// others.</para>
    ///
    /// <para>Plays once by default and then stands still — it is an explanation, not decoration,
    /// and a diagram that keeps rebuilding itself under a paragraph of text pulls the eye off the
    /// text forever. Restarts on enable, so a page shown a second time plays it again rather than
    /// arriving already finished.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RevealSequence : MonoBehaviour
    {
        [Serializable]
        public struct Beat
        {
            [Tooltip("Faded up when this beat arrives.")]
            public CanvasGroup target;

            [Tooltip("Seconds from the start of the cycle.")]
            public float at;
        }

        [SerializeField] private Beat[] _beats;

        [Tooltip("How long each element takes to come up.")]
        [SerializeField, Range(0.05f, 1f)] private float _fadeSeconds = 0.14f;

        [Tooltip("Only used when looping: seconds the completed picture stands before the cycle " +
                 "starts over.")]
        [SerializeField, Range(0f, 8f)] private float _holdSeconds = 1.8f;

        [SerializeField] private bool _loop = false;

        private float _time;
        private float _cycle;

        private void OnEnable()
        {
            _cycle = 0f;
            if (_beats != null)
                foreach (var beat in _beats)
                    if (beat.target != null) _cycle = Mathf.Max(_cycle, beat.at);
            _cycle += _fadeSeconds + _holdSeconds;

            _time = 0f;
            Apply();
        }

        private void Update()
        {
            // Unscaled: this is onboarding chrome, and the one thing on screen explaining itself
            // must not freeze because something else paused the game.
            _time += Time.unscaledDeltaTime;

            if (_time >= _cycle)
            {
                // Clamped rather than switched off when it does not loop: disabling the component
                // means OnEnable never runs again, and the page would come back already finished.
                if (_loop) _time -= _cycle;
                else _time = _cycle;
            }

            Apply();
        }

        private void Apply()
        {
            if (_beats == null) return;

            foreach (var beat in _beats)
            {
                if (beat.target == null) continue;
                beat.target.alpha = _fadeSeconds <= 0f
                    ? (_time >= beat.at ? 1f : 0f)
                    : Mathf.Clamp01((_time - beat.at) / _fadeSeconds);
            }
        }
    }
}
