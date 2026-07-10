using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// One-Euro filter (Casiez et al. 2012) — an adaptive low-pass: heavy smoothing when the signal
    /// is slow (clean values at the rep's turnaround points), light smoothing when it moves fast
    /// (lag stays under ~70ms during the descent). Chosen over plain EMA for the elbow-angle
    /// pipeline because EMA cannot deliver both at once.
    ///
    /// Struct, zero allocation, no Unity lifecycle. dt is clamped inside <see cref="Filter"/> to
    /// [<see cref="CVConstants.FilterDtClampMinSec"/>, <see cref="CVConstants.FilterDtClampMaxSec"/>]
    /// so a hitch or a duplicated timestamp can't destabilize the derivative estimate.
    /// </summary>
    public struct OneEuroFilter
    {
        public float MinCutoffHz;
        public float Beta;
        public float DerivCutoffHz;

        private bool _initialized;
        private float _prevValue;
        private float _prevDeriv;

        public bool IsInitialized => _initialized;

        /// <summary>|v̂| of the last Filter call — smoothed signal speed in units/sec (HUD/debug).</summary>
        public float LastSpeed { get; private set; }

        public OneEuroFilter(float minCutoffHz, float beta, float derivCutoffHz)
        {
            MinCutoffHz = minCutoffHz;
            Beta = beta;
            DerivCutoffHz = derivCutoffHz;
            _initialized = false;
            _prevValue = 0f;
            _prevDeriv = 0f;
            LastSpeed = 0f;
        }

        /// <summary>Forget history — the next <see cref="Filter"/> call re-seeds from its input.
        /// Called after tracking gaps longer than <see cref="CVConstants.TrackerRebaseAfterLostSec"/>
        /// so the filter doesn't "glide" from a stale pre-gap value.</summary>
        public void Reset()
        {
            _initialized = false;
            LastSpeed = 0f;
        }

        public float Filter(float raw, float dtSec)
        {
            float dt = Mathf.Clamp(dtSec, CVConstants.FilterDtClampMinSec, CVConstants.FilterDtClampMaxSec);

            if (!_initialized)
            {
                _initialized = true;
                _prevValue = raw;
                _prevDeriv = 0f;
                LastSpeed = 0f;
                return raw;
            }

            // Derivative, low-passed at DerivCutoffHz.
            float rawDeriv = (raw - _prevValue) / dt;
            float aD = Alpha(DerivCutoffHz, dt);
            _prevDeriv += aD * (rawDeriv - _prevDeriv);
            LastSpeed = Mathf.Abs(_prevDeriv);

            // Adaptive cutoff: faster motion → higher cutoff → less lag.
            float cutoff = MinCutoffHz + Beta * LastSpeed;
            float a = Alpha(cutoff, dt);
            _prevValue += a * (raw - _prevValue);
            return _prevValue;
        }

        private static float Alpha(float cutoffHz, float dt)
        {
            float tau = 1f / (2f * Mathf.PI * Mathf.Max(cutoffHz, 1e-3f));
            return 1f / (1f + tau / dt);
        }
    }
}
