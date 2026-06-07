using System;
using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    public enum PlankArmerState
    {
        /// <summary>No valid plank — counter is fully gated off.</summary>
        Disarmed = 0,

        /// <summary>Valid plank started; counting time until armed.</summary>
        Arming = 1,

        /// <summary>Valid plank held long enough — counter runs.</summary>
        Armed = 2,

        /// <summary>Plank just broke; counter still runs for a grace period to absorb skeleton
        /// jitter mid-rep.</summary>
        Cooling = 3,
    }

    /// <summary>
    /// FSM that gates <see cref="PushupRepCounter"/> behind "user is in a valid plank for
    /// <see cref="CVConstants.PlankArmHoldSec"/> seconds". Prevents the counter from crediting reps
    /// when the user is in any pose other than a real plank (lying on back waving arms, sitting up,
    /// kneeling, mid-rep paused, etc.) — the known weakness of the original phase-08 detector.
    ///
    /// <para><b>Lifecycle:</b></para>
    /// <code>
    ///   Disarmed → Arming  : a frame passes IsValidPlank
    ///   Arming   → Armed   : valid plank held for PlankArmHoldSec → OnArmed fires
    ///   Arming   → Disarmed: validity broken before arm completes → OnDisarmed(reason)
    ///   Armed    → Cooling : a single bad frame; counter STILL runs
    ///   Cooling  → Armed   : validity returned; cooling cancelled
    ///   Cooling  → Disarmed: invalid continuously for PlankDisarmGraceSec → OnDisarmed(reason)
    /// </code>
    /// <para><see cref="IsArmed"/> = (State == Armed || State == Cooling). Counter consumes this
    /// flag; the grace window is what lets a real rep finish even if the skeleton glitches at the
    /// bottom of the descent.</para>
    ///
    /// <para><b>Dependencies:</b> <see cref="WristAnchorMonitor"/> and <see cref="KneeBendDetector"/>
    /// are read but NOT ticked by this class. The owner (PushupSession) is responsible for ticking
    /// them BEFORE calling <see cref="Tick"/>, otherwise the predicate reads stale state.</para>
    /// </summary>
    public sealed class PlankArmer
    {
        private readonly WristAnchorMonitor _anchor;
        private readonly KneeBendDetector _knee;

        public PlankArmerState State { get; private set; } = PlankArmerState.Disarmed;

        /// <summary>True while the rep counter should run (covers Armed + Cooling).</summary>
        public bool IsArmed => State == PlankArmerState.Armed || State == PlankArmerState.Cooling;

        /// <summary>Why the most recent frame's plank was rejected (or <see cref="PlankRejectReason.Ok"/>
        /// if it passed). For HUD / telemetry.</summary>
        public PlankRejectReason LastRejectReason { get; private set; } = PlankRejectReason.LowerBodyNotVisible;

        /// <summary>[0..1] — progress of the arming hold. Drives the calibration-screen progress
        /// ring. 1.0 means we just armed.</summary>
        public float ArmingProgress01 { get; private set; }

        /// <summary>Seconds left before a Cooling → Disarmed transition. 0 outside Cooling.</summary>
        public float CoolingTimeLeftSec { get; private set; }

        public event Action OnArmed;
        public event Action<PlankRejectReason> OnDisarmed;

        private float _stateEnteredAt;

        public PlankArmer(WristAnchorMonitor anchor, KneeBendDetector knee)
        {
            _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
            _knee   = knee   ?? throw new ArgumentNullException(nameof(knee));
        }

        public void Reset()
        {
            State = PlankArmerState.Disarmed;
            LastRejectReason = PlankRejectReason.LowerBodyNotVisible;
            ArmingProgress01 = 0f;
            CoolingTimeLeftSec = 0f;
            _stateEnteredAt = 0f;
        }

        /// <summary>Advance the FSM with the current frame. Caller MUST have ticked
        /// <see cref="WristAnchorMonitor"/> and <see cref="KneeBendDetector"/> first.</summary>
        public void Tick(in PoseFrame frame, bool trackingOk, float nowSec)
        {
            if (!trackingOk || !frame.IsValid)
            {
                ApplyInvalid(PlankRejectReason.TrackingLost, nowSec);
                return;
            }

            if (IsValidPlank(frame, out PlankRejectReason reason))
                ApplyValid(nowSec);
            else
                ApplyInvalid(reason, nowSec);
        }

        private void ApplyValid(float nowSec)
        {
            LastRejectReason = PlankRejectReason.Ok;

            switch (State)
            {
                case PlankArmerState.Disarmed:
                    EnterState(PlankArmerState.Arming, nowSec);
                    ArmingProgress01 = 0f;
                    break;

                case PlankArmerState.Arming:
                {
                    float held = nowSec - _stateEnteredAt;
                    ArmingProgress01 = Mathf.Clamp01(held / Mathf.Max(1e-3f, CVConstants.PlankArmHoldSec));
                    if (held >= CVConstants.PlankArmHoldSec)
                    {
                        EnterState(PlankArmerState.Armed, nowSec);
                        ArmingProgress01 = 1f;
                        OnArmed?.Invoke();
                    }
                    break;
                }

                case PlankArmerState.Armed:
                    // hold
                    break;

                case PlankArmerState.Cooling:
                    // Recovered before grace expired — back to Armed without firing OnArmed (we
                    // never actually disarmed).
                    EnterState(PlankArmerState.Armed, nowSec);
                    CoolingTimeLeftSec = 0f;
                    break;
            }
        }

        private void ApplyInvalid(PlankRejectReason reason, float nowSec)
        {
            LastRejectReason = reason;

            switch (State)
            {
                case PlankArmerState.Disarmed:
                    // stay
                    break;

                case PlankArmerState.Arming:
                    EnterState(PlankArmerState.Disarmed, nowSec);
                    ArmingProgress01 = 0f;
                    OnDisarmed?.Invoke(reason);
                    break;

                case PlankArmerState.Armed:
                    EnterState(PlankArmerState.Cooling, nowSec);
                    CoolingTimeLeftSec = CVConstants.PlankDisarmGraceSec;
                    break;

                case PlankArmerState.Cooling:
                {
                    float since = nowSec - _stateEnteredAt;
                    CoolingTimeLeftSec = Mathf.Max(0f, CVConstants.PlankDisarmGraceSec - since);
                    if (since >= CVConstants.PlankDisarmGraceSec)
                    {
                        EnterState(PlankArmerState.Disarmed, nowSec);
                        CoolingTimeLeftSec = 0f;
                        OnDisarmed?.Invoke(reason);
                    }
                    break;
                }
            }
        }

        private void EnterState(PlankArmerState s, float nowSec)
        {
            State = s;
            _stateEnteredAt = nowSec;
        }

        /// <summary>The plank predicate. Public so tests can probe it directly without driving the FSM.</summary>
        public bool IsValidPlank(in PoseFrame f, out PlankRejectReason reason)
        {
            // 1) Lower body at least partially visible. Loose on purpose — side-camera framing
            // often hides one side; we just need SOMETHING below the hips to confirm we're not
            // looking at a head-and-arms wave.
            bool lowerOk =
                f.Visibility(PoseLandmark.LeftAnkle)       >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.RightAnkle)      >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.LeftFootIndex)   >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.RightFootIndex)  >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.LeftKnee)        >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.RightKnee)       >= CVConstants.PlankLowerBodyVisibility;
            if (!lowerOk) { reason = PlankRejectReason.LowerBodyNotVisible; return false; }

            // 2) Body line straight enough — stricter than the legacy MinPlankBodyLine (140°) used
            // by PoseMath.LooksLikePushup, because we're ARMING, not just sanity-checking a frame
            // mid-rep.
            float bodyLine = PoseMath.BodyLineAngle(f);
            if (bodyLine < CVConstants.ArmingBodyLineAngle) { reason = PlankRejectReason.BodySagging; return false; }

            // 3) Knees not bent — the user is NOT in a knee push-up.
            if (_knee.Classification == KneeClassification.Bent) { reason = PlankRejectReason.KneesBent; return false; }

            // 4) Elbows extended — arming starts from the top of a rep, not the middle.
            float elbow = PoseMath.ElbowAngle(f);
            if (elbow < CVConstants.ArmingElbowTopAngle) { reason = PlankRejectReason.NotAtTop; return false; }

            // 5) Wrists not provably airborne. Anchored / Drifting / Unknown all OK — only the hard
            // verdict blocks arming. Unknown is permissive because a fresh window legitimately hasn't
            // filled yet.
            if (_anchor.LastVerdict == AnchorVerdict.Airborne) { reason = PlankRejectReason.WristsAirborne; return false; }

            // 6) (Stage 2 / S10: BodyHorizontal world-only — reserved hook.)

            reason = PlankRejectReason.Ok;
            return true;
        }
    }
}
