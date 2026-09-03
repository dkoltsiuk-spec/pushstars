using System;
using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>
    /// The dark bolts that close in on the two far corners when the set goes live.
    ///
    /// <para><b>Why they are animated and not just drawn.</b> The level test has no starting gun
    /// beyond a number changing — the countdown ends, the clock starts, and nothing about the
    /// screen says the thing being measured is now the thing you are doing. These arriving is that
    /// signal, and it is one the player catches from a plank, at the bottom of the screen, out of
    /// the corner of an eye that cannot read a caption.</para>
    ///
    /// <para>Each one comes in along its own diagonal, and which diagonal is read off its anchor
    /// rather than configured: a corner piece slides from its corner, and a second number saying
    /// which one is a second number able to disagree with where the thing actually sits.</para>
    ///
    /// <para>Plays on a call, not on enable — the cue is a phase change in the set, not the frame
    /// this object happened to be switched on. It holds the finished picture afterwards; these are
    /// part of the screen once they have arrived, not a flourish that undoes itself.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CornerAccents : MonoBehaviour
    {
        [Serializable]
        public struct Accent
        {
            public RectTransform target;
            public CanvasGroup group;
        }

        [SerializeField] private Accent[] _accents;

        [Tooltip("How far out along its diagonal each piece starts, in canvas units.")]
        [SerializeField] private float _travel = 120f;

        [Tooltip("How long one piece takes to arrive.")]
        [SerializeField, Range(0.1f, 1.5f)] private float _seconds = 0.45f;

        [Tooltip("Delay between the first piece and the next, so they land as two beats rather " +
                 "than one flash.")]
        [SerializeField, Range(0f, 0.5f)] private float _stagger = 0.09f;

        private Vector2[] _home;
        private float _time = -1f;

        private void Awake()
        {
            if (_accents == null) return;

            _home = new Vector2[_accents.Length];
            for (int i = 0; i < _accents.Length; i++)
                if (_accents[i].target != null) _home[i] = _accents[i].target.anchoredPosition;

            Apply(0f);
        }

        /// <summary>Brings them in. Calling it again replays from the start, which is what a set
        /// restarted after a broken plank should look like.</summary>
        public void Play() => _time = 0f;

        private void Update()
        {
            if (_time < 0f) return;

            _time += Time.deltaTime;
            Apply(_time);

            if (_time >= Total) _time = -1f;
        }

        private float Total => _seconds + Mathf.Max(0, (_accents?.Length ?? 1) - 1) * _stagger;

        private void Apply(float time)
        {
            if (_accents == null || _home == null) return;

            for (int i = 0; i < _accents.Length; i++)
            {
                var accent = _accents[i];
                if (accent.target == null) continue;

                float t = Mathf.Clamp01((time - i * _stagger) / _seconds);
                // Out-cubic: fastest at the start, so the piece reads as having been thrown into
                // place rather than easing in politely.
                float e = 1f - Mathf.Pow(1f - t, 3f);

                var anchor = accent.target.anchorMin;
                var outward = new Vector2(anchor.x <= 0.5f ? -1f : 1f, anchor.y <= 0.5f ? -1f : 1f);

                accent.target.anchoredPosition = _home[i] + outward * (_travel * (1f - e));
                if (accent.group != null) accent.group.alpha = e;
            }
        }
    }
}
