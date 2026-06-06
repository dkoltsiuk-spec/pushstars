using System;

namespace PushStars.CV
{
    public enum PushupPhase
    {
        /// <summary>Arms locked out — top of the movement (or not yet started).</summary>
        Top = 0,
        /// <summary>Lowering (eccentric).</summary>
        Descending = 1,
        /// <summary>Chest down, elbows bent past the bottom threshold.</summary>
        Bottom = 2,
        /// <summary>Pushing up (concentric).</summary>
        Ascending = 3,
    }

    /// <summary>
    /// Counts pushups from a stream of <see cref="PoseFrame"/>s by tracking the average elbow angle
    /// through a hysteresis FSM: a rep is credited when the lifter returns to the top
    /// (<see cref="CVConstants.TopElbowAngle"/>) <b>after</b> having reached the bottom
    /// (<see cref="CVConstants.BottomElbowAngle"/>). This rejects partial reps and bouncing.
    ///
    /// Frames whose key joints are not visible are ignored (no phantom counts when the skeleton is
    /// lost). Counting stops at <see cref="CVConstants.MaxRepsPerMatch"/> (anti-cheat cap). TEMPO is
    /// the average reps-per-minute over the completed reps.
    ///
    /// Pure C#, frame-driven — feed it from any <see cref="IPoseSource"/>. No Unity lifecycle, so it
    /// is unit-testable against recorded/mock frames.
    /// </summary>
    public sealed class PushupRepCounter
    {
        public int Reps { get; private set; }
        public PushupPhase Phase { get; private set; } = PushupPhase.Top;
        public float CurrentElbowAngle { get; private set; } = 180f;

        /// <summary>Average reps per minute across all completed reps (0 until the 2nd rep).</summary>
        public float TempoRpm { get; private set; }

        /// <summary>Fires with the new rep count each time a rep completes.</summary>
        public event Action<int> OnRep;

        /// <summary>Fires whenever the movement phase changes.</summary>
        public event Action<PushupPhase> OnPhaseChanged;

        private bool _reachedBottom;
        private float _lastAngle = 180f;
        private float _firstRepTime = -1f;
        private float _lastRepTime = -1f;

        public void Reset()
        {
            Reps = 0;
            Phase = PushupPhase.Top;
            CurrentElbowAngle = 180f;
            TempoRpm = 0f;
            _reachedBottom = false;
            _lastAngle = 180f;
            _firstRepTime = -1f;
            _lastRepTime = -1f;
        }

        /// <summary>Feed one tracked frame. <paramref name="trackingOk"/> should be false when the
        /// source reports <see cref="TrackingQuality.Lost"/> so the FSM holds its state.</summary>
        public void Process(in PoseFrame frame, bool trackingOk)
        {
            if (!trackingOk || !frame.IsValid) return;

            float angle = PoseMath.ElbowAngle(frame);
            CurrentElbowAngle = angle;

            UpdatePhase(angle, frame.TimestampSec);

            _lastAngle = angle;
        }

        private void UpdatePhase(float angle, float timeSec)
        {
            // Bottom reached?
            if (angle <= CVConstants.BottomElbowAngle)
            {
                _reachedBottom = true;
                SetPhase(PushupPhase.Bottom);
            }
            // Returned to the top after a valid descent → credit one rep.
            else if (angle >= CVConstants.TopElbowAngle)
            {
                if (_reachedBottom)
                {
                    _reachedBottom = false;
                    CreditRep(timeSec);
                }
                SetPhase(PushupPhase.Top);
            }
            // In between — report direction from the angle delta.
            else
            {
                SetPhase(angle < _lastAngle ? PushupPhase.Descending : PushupPhase.Ascending);
            }
        }

        private void CreditRep(float timeSec)
        {
            if (Reps >= CVConstants.MaxRepsPerMatch) return;

            Reps++;
            if (_firstRepTime < 0f) _firstRepTime = timeSec;
            _lastRepTime = timeSec;

            // Average tempo across completed reps (needs ≥2 reps for a meaningful interval).
            float span = _lastRepTime - _firstRepTime;
            if (Reps >= 2 && span > 1e-3f)
                TempoRpm = (Reps - 1) / span * 60f;

            OnRep?.Invoke(Reps);
        }

        private void SetPhase(PushupPhase phase)
        {
            if (phase == Phase) return;
            Phase = phase;
            OnPhaseChanged?.Invoke(phase);
        }
    }
}
