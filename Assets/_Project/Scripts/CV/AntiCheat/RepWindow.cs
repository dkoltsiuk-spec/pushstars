using System;
using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Read-only view over the per-rep sample window accumulated by <see cref="AntiCheatAuditor"/>.
    /// Validators consume this; they do NOT mutate the underlying buffer.
    ///
    /// <para>Carries the camera <see cref="View"/> snapshotted at audit time so gates can pick
    /// their view-adaptive branch (frontal vertical axis vs side perpendicular vs PCA). Positions
    /// in samples are square-space (see <see cref="RepSample"/>).</para>
    /// </summary>
    public readonly struct RepWindow
    {
        private readonly RepSample[] _items;
        private readonly int _count;

        /// <summary>Camera view at audit time — never changes mid-rep (ViewClassifier defers
        /// switches to top-of-rep), so one snapshot is representative of the whole window.</summary>
        public readonly ViewKind View;

        public RepWindow(RepSample[] items, int count, ViewKind view)
        {
            _items = items;
            _count = count;
            View = view;
        }

        public int Count => _count;
        public RepSample this[int i] => _items[i];

        public float DurationSec => _count >= 2
            ? _items[_count - 1].TimestampSec - _items[0].TimestampSec
            : 0f;

        /// <summary>Average of (shoulderMid − hipMid) over the first <paramref name="leadFrames"/>
        /// frames — the body axis at the start of the rep. Also returns the robust body scale
        /// S = max(torsoLen, shoulderWidth) over the same lead frames (frontal fix: torso alone
        /// collapses frontally). Returns false if nothing computable.</summary>
        public bool TryComputeBodyAxis(int leadFrames, out Vector2 axis, out float scale)
        {
            axis = Vector2.zero;
            scale = 0f;
            int used = 0;
            Vector2 sum = Vector2.zero;
            float swSum = 0f;
            int limit = Mathf.Min(leadFrames, _count);
            for (int i = 0; i < limit; i++)
            {
                var s = _items[i];
                if (!s.HasShoulderMid || !s.HasHipMid) continue;
                sum += (s.ShoulderMidSq - s.HipMidSq);
                swSum += s.ShoulderWidthSq;
                used++;
            }
            if (used == 0)
            {
                for (int i = 0; i < _count; i++)
                {
                    var s = _items[i];
                    if (!s.HasShoulderMid || !s.HasHipMid) continue;
                    sum += (s.ShoulderMidSq - s.HipMidSq);
                    swSum += s.ShoulderWidthSq;
                    used++;
                }
                if (used == 0) return false;
            }
            Vector2 mean = sum / used;
            float torsoLen = mean.magnitude;
            float sw = swSum / used;
            scale = Mathf.Max(torsoLen, sw);
            if (scale < 1e-4f) return false;
            axis = torsoLen > 1e-4f ? mean / torsoLen : Vector2.up;
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

        /// <summary>Mean of <see cref="RepSample.UpperBodyVisAvg"/> — the frontal trust metric
        /// (legs/hips excluded; they legitimately vanish at the bottom of a frontal rep).</summary>
        public float MeanUpperBodyVisibility
        {
            get
            {
                if (_count == 0) return 0f;
                float sum = 0f;
                for (int i = 0; i < _count; i++) sum += _items[i].UpperBodyVisAvg;
                return sum / _count;
            }
        }

        public float LeftArmVisibilityFraction => CountFraction(true);
        public float RightArmVisibilityFraction => CountFraction(false);

        private float CountFraction(bool leftSide)
        {
            if (_count == 0) return 0f;
            int n = 0;
            for (int i = 0; i < _count; i++)
                if (leftSide ? _items[i].LeftArmVisible : _items[i].RightArmVisible) n++;
            return (float)n / _count;
        }

        /// <summary>Shoulder-width extremes across the whole window (BodySwing rule input).</summary>
        public void ShoulderWidthRange(out float min, out float max)
        {
            min = float.PositiveInfinity;
            max = 0f;
            for (int i = 0; i < _count; i++)
            {
                float sw = _items[i].ShoulderWidthSq;
                if (sw <= 0f) continue;
                if (sw < min) min = sw;
                if (sw > max) max = sw;
            }
            if (float.IsPositiveInfinity(min)) min = 0f;
        }

        public static float Project(Vector2 point, Vector2 origin, Vector2 axis)
            => Vector2.Dot(point - origin, axis);

        public ReadOnlySpan<RepSample> AsSpan() => new ReadOnlySpan<RepSample>(_items, 0, _count);
    }
}
