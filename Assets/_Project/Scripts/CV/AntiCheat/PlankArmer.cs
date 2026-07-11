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
        private readonly KneeDropDetector _kneeDrop; // frontal knee condition; null in legacy tests

        // Rolling hip-availability window for the frontal F0 fail-closed check (~1s @30fps).
        private readonly Util.RingBuffer<bool> _hipAvail = new Util.RingBuffer<bool>(30);

        /// <summary>Phone pitch from the IMU, degrees from the expected propped-up orientation.
        /// Set each frame by PushupSession (PlankArmer stays pure C#). NaN = no IMU data
        /// (editor / mocks) — the tilt gate passes.</summary>
        public float PhonePitchDeg { get; set; } = float.NaN;

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
        private float _coolingFirstEnteredAt;

        public PlankArmer(WristAnchorMonitor anchor, KneeBendDetector knee)
            : this(anchor, knee, null) { }

        public PlankArmer(WristAnchorMonitor anchor, KneeBendDetector knee, KneeDropDetector kneeDrop)
        {
            _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
            _knee   = knee   ?? throw new ArgumentNullException(nameof(knee));
            _kneeDrop = kneeDrop;
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
        /// <see cref="WristAnchorMonitor"/>, <see cref="KneeBendDetector"/> and (if present)
        /// <see cref="KneeDropDetector"/> first. <paramref name="view"/> comes from the
        /// ViewClassifier and picks the predicate branch.
        ///
        /// <para><paramref name="repInFlight"/>: the plank predicate is only satisfiable at the TOP
        /// (mid-rep the elbows are bent → NotAtTop, and the shoulders sit near the wrists → the F1
        /// margin collapses), so during every rep the armer sits in Cooling. A rep slower than the
        /// grace window would disarm the user AT THE BOTTOM of an honest rep. While a rep arc is in
        /// flight the Cooling timer is frozen — capped at MaxRepSeconds so a genuine abandonment
        /// (dropped to all fours and stayed) still disarms.</para></summary>
        public void Tick(in PoseFrame frame, bool trackingOk, float nowSec, ViewKind view = ViewKind.Side,
                         bool repInFlight = false)
        {
            if (!trackingOk || !frame.IsValid)
            {
                _hipAvail.Push(false);
                ApplyInvalid(PlankRejectReason.TrackingLost, nowSec, repInFlight);
                return;
            }

            _hipAvail.Push(
                frame.Visibility(PoseLandmark.LeftHip)  >= CVConstants.MinJointVisibility ||
                frame.Visibility(PoseLandmark.RightHip) >= CVConstants.MinJointVisibility);

            if (IsValidPlank(frame, view, out PlankRejectReason reason))
                ApplyValid(nowSec);
            else
                ApplyInvalid(reason, nowSec, repInFlight);
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

        private void ApplyInvalid(PlankRejectReason reason, float nowSec, bool repInFlight = false)
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
                    _coolingFirstEnteredAt = nowSec;
                    CoolingTimeLeftSec = CVConstants.PlankDisarmGraceSec;
                    break;

                case PlankArmerState.Cooling:
                {
                    // Rep in flight → the predicate is EXPECTED to fail (elbows bent mid-rep).
                    // Freeze the grace timer, capped at MaxRepSeconds since the Cooling episode
                    // began so a true abandonment still disarms.
                    if (repInFlight && nowSec - _coolingFirstEnteredAt < CVConstants.MaxRepSeconds)
                    {
                        _stateEnteredAt = nowSec;
                        CoolingTimeLeftSec = CVConstants.PlankDisarmGraceSec;
                        break;
                    }

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

        /// <summary>The plank predicate — view-adaptive (frontal addendum). Public so tests can
        /// probe it directly without driving the FSM. Ambiguous/Unknown views take the OR of the
        /// two branches: fail-open on arming (the per-rep auditor picks up the slack), because the
        /// product's camera is frontal and a false refusal to arm is the worse failure.</summary>
        public bool IsValidPlank(in PoseFrame f, ViewKind view, out PlankRejectReason reason)
        {
            switch (view)
            {
                case ViewKind.Side:
                    return SidePlank(f, out reason);
                case ViewKind.Frontal:
                    return FrontalPlank(f, out reason);
                default:
                    if (FrontalPlank(f, out PlankRejectReason frontalReason)) { reason = PlankRejectReason.Ok; return true; }
                    if (SidePlank(f, out _)) { reason = PlankRejectReason.Ok; return true; }
                    reason = frontalReason; // report the frontal reason — the product's primary view
                    return false;
            }
        }

        /// <summary>Legacy signature — side branch (kept for existing tests).</summary>
        public bool IsValidPlank(in PoseFrame f, out PlankRejectReason reason)
            => IsValidPlank(f, ViewKind.Side, out reason);

        // ── Side branch: phase-08.1 Stage 1 predicate, unchanged except the knee in-plane gate ──
        private bool SidePlank(in PoseFrame f, out PlankRejectReason reason)
        {
            // 0) Verticality sanity (on-device find): a person STANDING upright with arms down
            // passes every original side check (body line ≈ 180°, knees straight, elbows extended,
            // wrists still) — the armer briefly armed while the user was walking into position,
            // polluting the κ baseline and starting a phantom set. When the shoulders are wide
            // enough for κ to be reliable (frontal-ish facing), a standing body reads κ ≈ 1.2+ →
            // reject. True side-view planks have tiny sw → the check is skipped there.
            if (TryReliableKappa(f, out float kappaSanity)
                && (kappaSanity > CVConstants.FrontalMaxBodyInclineKappa
                    || kappaSanity < CVConstants.FrontalMinBodyInclineKappa))
            { reason = PlankRejectReason.BodyIncline; return false; }

            // 1) Lower body at least partially visible.
            bool lowerOk =
                f.Visibility(PoseLandmark.LeftAnkle)       >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.RightAnkle)      >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.LeftFootIndex)   >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.RightFootIndex)  >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.LeftKnee)        >= CVConstants.PlankLowerBodyVisibility ||
                f.Visibility(PoseLandmark.RightKnee)       >= CVConstants.PlankLowerBodyVisibility;
            if (!lowerOk) { reason = PlankRejectReason.LowerBodyNotVisible; return false; }

            // 2) Body line straight enough.
            float bodyLine = PoseMath.BodyLineAngle(f);
            if (bodyLine < CVConstants.ArmingBodyLineAngle) { reason = PlankRejectReason.BodySagging; return false; }

            // 3) Knees not bent — HARD only when the leg is actually in the image plane
            // (|hip→ankle| ≥ 0.8·sw); a foreshortened leg's knee angle is projection noise.
            if (_knee.Classification == KneeClassification.Bent && LegInImagePlane(f))
            { reason = PlankRejectReason.KneesBent; return false; }

            // 4) Elbows extended.
            float elbow = PoseMath.ElbowAngle(f);
            if (elbow < CVConstants.ArmingElbowTopAngle) { reason = PlankRejectReason.NotAtTop; return false; }

            // 5) Wrists not provably airborne.
            if (_anchor.LastVerdict == AnchorVerdict.Airborne) { reason = PlankRejectReason.WristsAirborne; return false; }

            reason = PlankRejectReason.Ok;
            return true;
        }

        // ── Frontal branch F0–F6 (docs/plan/phase-08.1-frontal-addendum.md) ──
        private bool FrontalPlank(in PoseFrame f, out PlankRejectReason reason)
        {
            float aspect = f.Aspect;

            bool ls = f.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = f.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            if (!ls || !rs) { reason = PlankRejectReason.TrackingLost; return false; }

            Vector2 lsp = PoseMath.ToSquare(f.Get(PoseLandmark.LeftShoulder).Pos2D, aspect);
            Vector2 rsp = PoseMath.ToSquare(f.Get(PoseLandmark.RightShoulder).Pos2D, aspect);
            Vector2 shoulderMid = (lsp + rsp) * 0.5f;
            float sw = Vector2.Distance(lsp, rsp);
            if (sw < 1e-3f) { reason = PlankRejectReason.TrackingLost; return false; }

            // ── F0: SetupGate — framing / distance / tilt / hip availability ──
            if (sw < CVConstants.SetupMinShoulderWidthImg || sw > CVConstants.SetupMaxShoulderWidthImg)
            { reason = PlankRejectReason.TooCloseOrFar; return false; }

            bool noseVisible = f.Visibility(PoseLandmark.Nose) >= CVConstants.MinJointVisibility;
            if (noseVisible && f.Get(PoseLandmark.Nose).Y > CVConstants.SetupMaxNoseY)
            { reason = PlankRejectReason.BadFraming; return false; } // head would exit at the bottom

            if (!float.IsNaN(PhonePitchDeg) && Mathf.Abs(PhonePitchDeg) > CVConstants.SetupMaxPhonePitchDeg)
            { reason = PlankRejectReason.PhoneTilted; return false; }

            if (HipAvailabilityFrac() < CVConstants.FrontalArmingHipAvailabilityMin)
            { reason = PlankRejectReason.HipNotVisible; return false; } // fail-closed: κ/table checks need hips

            bool lw = f.Visibility(PoseLandmark.LeftWrist)  >= CVConstants.MinJointVisibility;
            bool rw = f.Visibility(PoseLandmark.RightWrist) >= CVConstants.MinJointVisibility;
            bool le = f.Visibility(PoseLandmark.LeftElbow)  >= CVConstants.MinJointVisibility;
            bool re = f.Visibility(PoseLandmark.RightElbow) >= CVConstants.MinJointVisibility;
            bool wristsOk = lw && rw;
            if (!wristsOk && !(le && re)) { reason = PlankRejectReason.BadFraming; return false; }

            // ── F1: hands (or elbows as fallback) planted BELOW the shoulders ──
            if (wristsOk)
            {
                Vector2 lwp = PoseMath.ToSquare(f.Get(PoseLandmark.LeftWrist).Pos2D, aspect);
                Vector2 rwp = PoseMath.ToSquare(f.Get(PoseLandmark.RightWrist).Pos2D, aspect);
                float wristMidY = 0.5f * (lwp.y + rwp.y);
                if (wristMidY - shoulderMid.y < CVConstants.FrontalWristBelowShoulderFrac * sw)
                { reason = PlankRejectReason.WristsAirborne; return false; }

                // ── F2: hand spread — diamond grip is allowed via the strict-anchor branch ──
                float spread = Mathf.Abs(lwp.x - rwp.x);
                bool wideEnough = spread >= CVConstants.FrontalWristSpreadMinFrac * sw;
                bool narrowButAnchored =
                    _anchor.LastVerdict == AnchorVerdict.Anchored &&
                    wristMidY - shoulderMid.y >= CVConstants.FrontalNarrowGripWristDropFrac * sw;
                if (!wideEnough && !narrowButAnchored)
                { reason = PlankRejectReason.WristsAirborne; return false; }

                // ── F6: head between the palms ──
                if (noseVisible)
                {
                    float noseX = PoseMath.ToSquare(f.Get(PoseLandmark.Nose).Pos2D, aspect).x;
                    float palmsMidX = 0.5f * (lwp.x + rwp.x);
                    if (Mathf.Abs(noseX - palmsMidX) > CVConstants.FrontalNoseBetweenPalmsFrac * sw)
                    { reason = PlankRejectReason.BadFraming; return false; }
                }
            }
            else
            {
                // F1 fallback: wrists occluded, elbows visible — softer geometric requirement.
                Vector2 lep = PoseMath.ToSquare(f.Get(PoseLandmark.LeftElbow).Pos2D, aspect);
                Vector2 rep2 = PoseMath.ToSquare(f.Get(PoseLandmark.RightElbow).Pos2D, aspect);
                float elbowMidY = 0.5f * (lep.y + rep2.y);
                if (elbowMidY - shoulderMid.y < CVConstants.FrontalElbowBelowShoulderFrac * sw)
                { reason = PlankRejectReason.WristsAirborne; return false; }
            }

            // ── F3: body incline κ — rejects kneeling-tall / sitting / standing / piked starts ──
            bool lh = f.Visibility(PoseLandmark.LeftHip)  >= CVConstants.MinJointVisibility;
            bool rh = f.Visibility(PoseLandmark.RightHip) >= CVConstants.MinJointVisibility;
            if (lh || rh)
            {
                Vector2 hipMid = (lh && rh)
                    ? (PoseMath.ToSquare(f.Get(PoseLandmark.LeftHip).Pos2D, aspect)
                     + PoseMath.ToSquare(f.Get(PoseLandmark.RightHip).Pos2D, aspect)) * 0.5f
                    : PoseMath.ToSquare(f.Get(lh ? PoseLandmark.LeftHip : PoseLandmark.RightHip).Pos2D, aspect);
                float kappa = (hipMid.y - shoulderMid.y) / sw;
                if (kappa > CVConstants.FrontalMaxBodyInclineKappa
                    || kappa < CVConstants.FrontalMinBodyInclineKappa)
                { reason = PlankRejectReason.BodyIncline; return false; }
            }
            // Chronic hip absence is already blocked by F0's fail-closed gate; a single missing
            // frame just skips κ.

            // (Knee-drop disarm REMOVED, policy change 2026-07-10: knee push-ups count as full
            // reps. Dropping to the knees mid-set is now a legal transition; only the all-fours
            // posture is rejected — by the κ ceiling above (F3) and per-rep by KneeCheatGate.)

            // ── F4: elbows extended (arming from the top) ──
            float elbow = PoseMath.ElbowAngle(f);
            if (elbow < CVConstants.ArmingElbowTopAngle) { reason = PlankRejectReason.NotAtTop; return false; }

            // ── F5: wrists not provably airborne (on the FIXED body scale — see WristAnchorMonitor) ──
            if (_anchor.LastVerdict == AnchorVerdict.Airborne)
            { reason = PlankRejectReason.WristsAirborne; return false; }

            reason = PlankRejectReason.Ok;
            return true;
        }

        /// <summary>κ = (hipMid_y − shoulderMid_y)/sw when the shoulders are wide enough in the
        /// image for the ratio to be meaningful (sw ≥ KappaReliableMinSw). False otherwise.</summary>
        private static bool TryReliableKappa(in PoseFrame f, out float kappa)
        {
            kappa = 0f;
            float aspect = f.Aspect;
            bool ls = f.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = f.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            bool lh = f.Visibility(PoseLandmark.LeftHip)  >= CVConstants.MinJointVisibility;
            bool rh = f.Visibility(PoseLandmark.RightHip) >= CVConstants.MinJointVisibility;
            if (!ls || !rs || (!lh && !rh)) return false;

            Vector2 lsp = PoseMath.ToSquare(f.Get(PoseLandmark.LeftShoulder).Pos2D, aspect);
            Vector2 rsp = PoseMath.ToSquare(f.Get(PoseLandmark.RightShoulder).Pos2D, aspect);
            float sw = Vector2.Distance(lsp, rsp);
            if (sw < CVConstants.KappaReliableMinSw) return false;

            float shoulderMidY = 0.5f * (lsp.y + rsp.y);
            float hipMidY = (lh && rh)
                ? 0.5f * (f.Get(PoseLandmark.LeftHip).Y + f.Get(PoseLandmark.RightHip).Y)
                : f.Get(lh ? PoseLandmark.LeftHip : PoseLandmark.RightHip).Y;
            kappa = (hipMidY - shoulderMidY) / sw;
            return true;
        }

        private float HipAvailabilityFrac()
        {
            int n = _hipAvail.Count;
            if (n == 0) return 1f; // no history yet — don't block the very first frames
            int ok = 0;
            for (int i = 0; i < n; i++)
                if (_hipAvail[i]) ok++;
            return (float)ok / n;
        }

        /// <summary>Side-view refinement: the knee angle is trustworthy only when the leg segment
        /// is long in the image (|hipMid→ankleMid| ≥ 0.8·sw in square space).</summary>
        private static bool LegInImagePlane(in PoseFrame f)
        {
            float aspect = f.Aspect;
            bool lh = f.Visibility(PoseLandmark.LeftHip)  >= CVConstants.MinJointVisibility;
            bool rh = f.Visibility(PoseLandmark.RightHip) >= CVConstants.MinJointVisibility;
            bool la = f.Visibility(PoseLandmark.LeftAnkle)  >= CVConstants.MinJointVisibility;
            bool ra = f.Visibility(PoseLandmark.RightAnkle) >= CVConstants.MinJointVisibility;
            bool ls = f.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = f.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            if (!(lh || rh) || !(la || ra)) return true; // can't judge — keep the legacy behaviour

            Vector2 hip = (lh && rh)
                ? (PoseMath.ToSquare(f.Get(PoseLandmark.LeftHip).Pos2D, aspect)
                 + PoseMath.ToSquare(f.Get(PoseLandmark.RightHip).Pos2D, aspect)) * 0.5f
                : PoseMath.ToSquare(f.Get(lh ? PoseLandmark.LeftHip : PoseLandmark.RightHip).Pos2D, aspect);
            Vector2 ankle = (la && ra)
                ? (PoseMath.ToSquare(f.Get(PoseLandmark.LeftAnkle).Pos2D, aspect)
                 + PoseMath.ToSquare(f.Get(PoseLandmark.RightAnkle).Pos2D, aspect)) * 0.5f
                : PoseMath.ToSquare(f.Get(la ? PoseLandmark.LeftAnkle : PoseLandmark.RightAnkle).Pos2D, aspect);

            float legLen = Vector2.Distance(hip, ankle);
            float sw = 0f;
            if (ls && rs)
                sw = Vector2.Distance(
                    PoseMath.ToSquare(f.Get(PoseLandmark.LeftShoulder).Pos2D, aspect),
                    PoseMath.ToSquare(f.Get(PoseLandmark.RightShoulder).Pos2D, aspect));
            if (sw < 1e-3f) return true;
            return legLen / sw >= CVConstants.KneeBendSideProjMinFrac;
        }
    }
}
