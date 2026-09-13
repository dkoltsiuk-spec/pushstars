using System;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>One cancellable, unscaled entrance timeline; authored transforms remain the resting pose.</summary>
    public sealed class LeagueEntrance : MonoBehaviour
    {
        [Serializable]
        public sealed class Beat
        {
            public CanvasGroup Group;
            public float Delay, Duration = .3f, FromScale = 1f, Rise;
            public bool Spring;
            [NonSerialized] public Vector3 RestScale;
            [NonSerialized] public Vector2 RestPosition;
        }

        public LeagueView View;
        public ScrollRect Leaderboard;
        public Beat[] Beats;
        public float ProgressStart = .46f, ProgressDuration = .65f;
        private bool _captured, _playing;
        private float _elapsed, _duration;
        public bool IsPlaying => _playing;

        public void Play()
        {
            if (View == null || Beats == null) return;
            if (!_captured)
            {
                _duration = ProgressStart + ProgressDuration;
                foreach (var beat in Beats)
                {
                    if (beat?.Group == null) continue;
                    var rect = (RectTransform)beat.Group.transform;
                    beat.RestScale = rect.localScale;
                    beat.RestPosition = rect.anchoredPosition;
                    _duration = Mathf.Max(_duration, beat.Delay + beat.Duration);
                }
                _captured = true;
            }
            if (Leaderboard != null)
            {
                Leaderboard.StopMovement();
                Leaderboard.verticalNormalizedPosition = 1;
                Leaderboard.enabled = false;
            }
            _elapsed = 0;
            _playing = true;
            Sample(0);
        }

        private void LateUpdate()
        {
            if (!_playing) return;
            _elapsed += Time.unscaledDeltaTime;
            Sample(_elapsed);
            if (_elapsed >= _duration) Finish();
        }

        private void Sample(float time)
        {
            foreach (var beat in Beats)
            {
                if (beat?.Group == null) continue;
                float t = Mathf.Clamp01((time - beat.Delay) / Mathf.Max(.01f, beat.Duration));
                var rect = (RectTransform)beat.Group.transform;
                beat.Group.alpha = Mathf.Clamp01(t * 4);
                float ease = beat.Spring ? UITween.EaseOutBack(t) : UITween.EaseOutCubic(t);
                rect.localScale = beat.RestScale * Mathf.LerpUnclamped(beat.FromScale, 1, ease);
                rect.anchoredPosition = beat.RestPosition + Vector2.down * (beat.Rise * (1 - UITween.EaseOutCubic(t)));
            }
            if (View.ProgressBar != null)
            {
                float p = UITween.EaseOutCubic(Mathf.Clamp01((time - ProgressStart) / Mathf.Max(.01f, ProgressDuration)));
                View.ProgressBar.Fill = View.Progress * p;
                View.ProgressBar.Refresh();
            }
        }

        private void Finish()
        {
            _playing = false;
            if (_captured)
                foreach (var beat in Beats)
                {
                    if (beat?.Group == null) continue;
                    beat.Group.alpha = 1;
                    var rect = (RectTransform)beat.Group.transform;
                    rect.localScale = beat.RestScale;
                    rect.anchoredPosition = beat.RestPosition;
                }
            if (View != null && View.ProgressBar != null)
            {
                View.ProgressBar.Fill = View.Progress;
                View.ProgressBar.Refresh();
            }
            if (Leaderboard != null) { Leaderboard.enabled = true; Leaderboard.StopMovement(); }
        }

        private void OnDisable() => Finish();
    }
}
