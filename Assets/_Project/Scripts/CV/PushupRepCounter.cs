using System;
using PushStars.CV.AntiCheat;

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

        /// <summary>Vote returned by the per-rep auditor for the most recent rep CANDIDATE — set on
        /// both accepted and rejected reps. Default <see cref="RepVote.Pass"/> until first rep
        /// arc completes. Phase-08.1, Stage 2.</summary>
        public RepVote LastRepVote { get; private set; } = RepVote.Pass;

        /// <summary>How many rep candidates were vetoed by the auditor across this session. For
        /// telemetry / HUD diagnostics.</summary>
        public int VetoedReps { get; private set; }

        /// <summary>Optional per-rep auditor hook. Phase-08.1 Stage 2 wiring — when set, called
        /// once per rep arc completion. Return Pass/SoftDock to credit the rep (with the penalty
        /// stored on <see cref="LastRepVote"/>), HardVeto to reject. Unset → all reps pass (Stage 1
        /// behaviour, preserved for unit tests that don't construct an auditor).</summary>
        public Func<RepVote> RepAuditor { get; set; }

        /// <summary>Fires with the new rep count each time a rep is CREDITED (Pass or SoftDock).</summary>
        public event Action<int> OnRep;

        /// <summary>Fires when a rep candidate is vetoed by the auditor. Argument is the verdict
        /// (with <see cref="RepVote.Reason"/> for HUD feedback).</summary>
        public event Action<RepVote> OnRepRejected;

        /// <summary>Fires whenever the movement phase changes.</summary>
        public event Action<PushupPhase> OnPhaseChanged;

        private bool _reachedBottom;
        private float _bottomTime = -1f;   // when the current rep reached the bottom (for min-duration gate)
        private float _lastAngle = 180f;
        private float _firstRepTime = -1f;
        private float _lastRepTime = -1f;

        public void Reset()
        {
            Reps = 0;
            VetoedReps = 0;
            Phase = PushupPhase.Top;
            CurrentElbowAngle = 180f;
            TempoRpm = 0f;
            LastRepVote = RepVote.Pass;
            _reachedBottom = false;
            _bottomTime = -1f;
            _lastAngle = 180f;
            _firstRepTime = -1f;
            _lastRepTime = -1f;
        }

        /// <summary>Feed one tracked frame. <paramref name="trackingOk"/> should be false when the
        /// source reports <see cref="TrackingQuality.Lost"/> so the FSM holds its state.
        /// <paramref name="isArmed"/> is the <see cref="AntiCheat.PlankArmer.IsArmed"/> gate — when
        /// false the descent state is cleared so a half-rep from a kneeling/sitting pose doesn't
        /// carry across into the next valid plank. Defaults to true for backwards compatibility
        /// with callers (and unit tests) that pre-date the anti-cheat layer.</summary>
        public void Process(in PoseFrame frame, bool trackingOk, bool isArmed = true)
        {
            if (!trackingOk || !frame.IsValid) return;

            // PlankArmer (phase 08.1) is now the primary gate. When the user is not in a valid plank
            // we forget any in-progress descent — otherwise a "half-rep" started before disarming
            // could complete the moment they re-arm and credit a false rep.
            if (!isArmed)
            {
                _reachedBottom = false;
                _bottomTime = -1f;
                SetPhase(PushupPhase.Top);
                return;
            }

            // LEGACY redundant sanity gate. PlankArmer is strictly stronger; this stays as a belt-
            // and-braces during the Stage 1 rollout. Stage 2 (per-rep auditor) will remove it.
            if (!PoseMath.LooksLikePushup(frame))
            {
                _reachedBottom = false;
                _bottomTime = -1f;
                return;
            }

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
                if (!_reachedBottom) _bottomTime = timeSec;
                _reachedBottom = true;
                SetPhase(PushupPhase.Bottom);
            }
            // Returned to the top after a valid descent → credit one rep.
            else if (angle >= CVConstants.TopElbowAngle)
            {
                // Reject reps that happened too fast to be a real rep (anti arm-flapping).
                bool longEnough = _bottomTime >= 0f && (timeSec - _bottomTime) >= CVConstants.MinRepSeconds;
                if (_reachedBottom && longEnough)
                {
                    _reachedBottom = false;
                    _bottomTime = -1f;
                    CreditRep(timeSec);
                }
                else if (_reachedBottom)
                {
                    _reachedBottom = false; // too fast — discard this attempt without crediting
                    _bottomTime = -1f;
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
            // Phase-08.1 Stage 2: per-rep audit hook. When unset (legacy / tests), behaves as if
            // every rep passes — preserves Stage 1 semantics. When set, the auditor runs all
            // registered IRepValidators and short-circuits on the first HardVeto.
            RepVote vote = RepAuditor != null ? RepAuditor.Invoke() : RepVote.Pass;
            LastRepVote = vote;

            if (vote.Kind == RepVoteKind.HardVeto)
            {
                VetoedReps++;
                OnRepRejected?.Invoke(vote);
                return;
            }

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
