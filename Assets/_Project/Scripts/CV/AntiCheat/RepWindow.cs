using System;
using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Read-only view over the per-rep sample window accumulated by <see cref="AntiCheatAuditor"/>.
    /// Validators consume this; they do NOT mutate the underlying buffer.
    ///
    /// <para>Common derived values (torso scale, body-axis projection, mean visibility) are exposed
    /// as helpers so each validator doesn't reimplement them. All math is image-space — Stage 2
    /// doesn't need world coordinates.</para>
    /// </summary>
    public readonly struct RepWindow
    {
        private readonly RepSample[] _items;
        private readonly int _count;

        public RepWindow(RepSample[] items, int count)
        {
            _items = items;
            _count = count;
        }

        public int Count => _count;
        public RepSample this[int i] => _items[i];

        public float DurationSec => _count >= 2
            ? _items[_count - 1].TimestampSec - _items[0].TimestampSec
            : 0f;

        /// <summary>Average of (shoulderMid − hipMid) over the first <paramref name="leadFrames"/>
        /// "Top"-phase frames — captures the body axis at the start of the rep, before any descent
        /// would have shifted it. Returns (false, zero) if neither midpoint is reliably visible.</summary>
        public bool TryComputeBodyAxis(int leadFrames, out Vector2 axis, out float scale)
        {
            axis = Vector2.zero;
            scale = 0f;
            int used = 0;
            Vector2 sum = Vector2.zero;
            int limit = Mathf.Min(leadFrames, _count);
            for (int i = 0; i < limit; i++)
            {
                var s = _items[i];
                if (!s.HasShoulderMid || !s.HasHipMid) continue;
                sum += (s.ShoulderMidImg - s.HipMidImg);
                used++;
            }
            if (used == 0)
            {
                // Fallback: scan the whole window — better noisy axis than no axis.
                for (int i = 0; i < _count; i++)
                {
                    var s = _items[i];
                    if (!s.HasShoulderMid || !s.HasHipMid) continue;
                    sum += (s.ShoulderMidImg - s.HipMidImg);
                    used++;
                }
                if (used == 0) return false;
            }
            Vector2 mean = sum / used;
            float mag = mean.magnitude;
            if (mag < 1e-4f) return false;
            axis = mean / mag;   // unit vector pointing shoulder → hip (the "head→feet" direction on a plank)
            scale = mag;          // average |shoulder − hip| over Top frames; the body-relative unit
            return true;
        }

        /// <summary>Mean of <see cref="RepSample.KeyJointVisAvg"/> across the window.</summary>
        public float MeanKeyJointVisibility
        {
            get
            {
                if (_count == 0) return 0f;
                float sum = 0f;
                for (int i = 0; i < _count; i++) sum += _items[i].KeyJointVisAvg;
                return sum / _count;
            }
        }

        /// <summary>Fraction of frames in which the left arm chain (shoulder+elbow+wrist) was visible.</summary>
        public float LeftArmVisibilityFraction => CountFraction(true);
        /// <summary>Fraction of frames in which the right arm chain was visible.</summary>
        public float RightArmVisibilityFraction => CountFraction(false);

        private float CountFraction(bool leftSide)
        {
            if (_count == 0) return 0f;
            int n = 0;
            for (int i = 0; i < _count; i++)
                if (leftSide ? _items[i].LeftArmVisible : _items[i].RightArmVisible) n++;
            return (float)n / _count;
        }

        /// <summary>Project (point − origin) onto <paramref name="axis"/> (unit vector). Used to
        /// turn 2D shoulder/hip positions into a 1D "depth along the body axis" time series.</summary>
        public static float Project(Vector2 point, Vector2 origin, Vector2 axis)
            => Vector2.Dot(point - origin, axis);

        public ReadOnlySpan<RepSample> AsSpan() => new ReadOnlySpan<RepSample>(_items, 0, _count);
    }
}
