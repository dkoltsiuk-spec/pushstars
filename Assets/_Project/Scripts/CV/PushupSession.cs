using System;
using UnityEngine;
using PushStars.CV.AntiCheat;

namespace PushStars.CV
{
    /// <summary>
    /// Runtime glue between a pose backend and the pushup analytics. Subscribes to an
    /// <see cref="IPoseSource"/>, runs the per-frame anti-cheat chain, feeds the
    /// <see cref="AmplitudeTracker"/> and <see cref="PushupRepCounter"/>, and computes the live
    /// <see cref="FormReading"/>. This is the single integration point the duel HUD (phase 14)
    /// and training screen bind to for REPS / FORM / TEMPO.
    ///
    /// <para><b>Per-frame tick order is strict</b> (frontal addendum):
    /// ViewClassifier → KneeBend → WristAnchor → KneeDrop → FootMonitor → Armer → Tracker →
    /// Auditor.RecordSample → Counter.Process. The armer's predicate reads verdicts produced
    /// earlier in the chain; the tracker's latches must exist before the counter consumes them;
    /// the audit sample must be in the window before the counter can fire the audit.</para>
    /// </summary>
    public sealed class PushupSession : MonoBehaviour
    {
        [Header("Pose source (must implement IPoseSource)")]
        [SerializeField] private MonoBehaviour _poseSourceBehaviour;

        [Header("Debug")]
        [SerializeField] private bool _logReps;

        public PushupRepCounter Counter { get; private set; }
        public FormReading LastForm { get; private set; }
        /// <summary>The most recent pose frame received from the source. Default (IsValid=false)
        /// until the first frame arrives. Exposed for the debug HUD and the anti-cheat layer.</summary>
        public PoseFrame LastFrame { get; private set; }
        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;

        // ── Anti-cheat + precision components (phase 08.1 + frontal addendum) ───────────────────
        public ViewClassifier    View        { get; } = new ViewClassifier();
        public WristAnchorMonitor WristAnchor { get; } = new WristAnchorMonitor();
        public KneeBendDetector  KneeBend    { get; } = new KneeBendDetector();
        public KneeDropDetector  KneeDrop    { get; } = new KneeDropDetector();
        public FootEventMonitor  FootMonitor { get; } = new FootEventMonitor();
        public AmplitudeTracker  Tracker     { get; } = new AmplitudeTracker();
        public WorkoutSetTracker SetTracker  { get; } = new WorkoutSetTracker();
        public PlankArmer        Armer       { get; private set; }
        public AntiCheatAuditor  Auditor     { get; private set; }

        public int Reps => Counter != null ? Counter.Reps : 0;
        public PushupPhase Phase => Counter != null ? Counter.Phase : PushupPhase.Top;
        public float TempoRpm => Counter != null ? Counter.TempoRpm : 0f;
        public float Form => LastForm.Form;

        /// <summary>Status/error from the pose source (for the on-screen debug HUD).</summary>
        public string SourceStatus => _source != null ? _source.StatusMessage : "(no source)";

        /// <summary>Forwarded from the rep counter (new total each completed rep).</summary>
        public event Action<int> OnRep;
        /// <summary>Forwarded from the rep counter — fires when the per-rep auditor vetoes a rep
        /// candidate. The HUD (and phase-14 reject-feedback) subscribe to this to show the user
        /// why the rep didn't count.</summary>
        public event Action<RepVote> OnRepRejected;
        /// <summary>Raised every processed frame with the latest form reading.</summary>
        public event Action<FormReading> OnFormUpdated;

        private IPoseSource _source;
        private float _lastKappa = float.NaN; // per-frame κ, captured as baseline at OnArmed

        /// <summary>Pose-frame arrival rate (inference results/sec) — NOT the render FPS. If this
        /// sits well below 30, the model/delegate is starving fast-rep detection.</summary>
        public float PoseFps { get; private set; }
        private int _poseFrameCount;
        private float _poseFpsWindowStart;

        private void Awake()
        {
            _source = _poseSourceBehaviour as IPoseSource;
            if (_source == null)
            {
                Debug.LogError("[PushupSession] Pose source is missing or does not implement IPoseSource.");
                return;
            }

            Counter = new PushupRepCounter(Tracker);
            Auditor = new AntiCheatAuditor(KneeDrop, FootMonitor);
            Armer = new PlankArmer(WristAnchor, KneeBend, KneeDrop);

            Armer.OnArmed         += HandleArmed;
            Armer.OnDisarmed      += HandleDisarmed;
            Counter.OnRep         += HandleRep;
            Counter.OnRepRejected += HandleRepRejected;
            // The audit seam: the pure-C# counter calls back when it would credit a rep.
            Counter.RepAuditor     = () => Auditor.AuditPendingRep();
        }

        private void OnEnable()
        {
            if (_source == null) return;
            _source.OnFrame += HandleFrame;
            _source.OnQualityChanged += HandleQuality;
            Quality = _source.Quality;
        }

        private void OnDisable()
        {
            if (_source == null) return;
            _source.OnFrame -= HandleFrame;
            _source.OnQualityChanged -= HandleQuality;
        }

        private void HandleQuality(TrackingQuality q) => Quality = q;

        private void OnDestroy()
        {
            if (Counter != null)
            {
                Counter.OnRep         -= HandleRep;
                Counter.OnRepRejected -= HandleRepRejected;
                Counter.RepAuditor     = null;
            }
            if (Armer != null)
            {
                Armer.OnArmed    -= HandleArmed;
                Armer.OnDisarmed -= HandleDisarmed;
            }
        }

        public void ResetSession()
        {
            Counter?.Reset();
            View.Reset();
            WristAnchor.Reset();
            KneeBend.Reset();
            KneeDrop.Reset();
            FootMonitor.Reset();
            Tracker.Reset();
            SetTracker.Reset();
            Armer?.Reset();
            Auditor?.Clear();
        }

        private void HandleFrame(PoseFrame frame)
        {
            LastFrame = frame;
            bool trackingOk = Quality == TrackingQuality.Good;
            float now = Time.time;

            _poseFrameCount++;
            float nowRt = Time.realtimeSinceStartup;
            if (nowRt - _poseFpsWindowStart >= 1f)
            {
                PoseFps = _poseFrameCount / (nowRt - _poseFpsWindowStart);
                _poseFrameCount = 0;
                _poseFpsWindowStart = nowRt;
            }

            // ── Single-computation pass: values several components consume this frame ──
            bool hasElbow = PoseMath.TryElbowAngle(frame, out float rawElbow);
            ComputeFrontalScalars(frame, out _lastKappa, out float kneeRel);

            // ── 1. View classification (never switches mid-rep) ──
            bool canSwitchView = (Counter == null || Counter.Phase == PushupPhase.Top)
                                 || Armer == null || !Armer.IsArmed;
            View.Tick(frame, trackingOk, canSwitchView, now);
            if (Auditor != null) Auditor.CurrentView = View.View;

            // ── 2. Per-frame monitors ──
            KneeBend.Tick(frame);
            WristAnchor.Tick(frame);
            bool elbowExtended = hasElbow && rawElbow >= CVConstants.ArmingElbowTopAngle;
            // KneeDrop is a frontal-family signal (kneeRel divides by shoulder width, degenerate
            // side-on) — feed NaN in the side view so it stays silent there.
            float kneeRelForDetector = View.View == ViewKind.Side ? float.NaN : kneeRel;
            KneeDrop.Tick(kneeRelForDetector, elbowExtended, Armer != null && Armer.IsArmed);
            FootMonitor.Tick(frame, trackingOk, Armer != null && Armer.IsArmed, now);

            // Rep in flight (tracker state from the previous tick): mid-rep the plank predicate
            // legitimately fails (elbows bent) and wrist landmarks jitter at the bottom — several
            // guards below relax while an arc is being completed.
            bool repInFlight =
                Tracker.ArcState == DepthArcState.AwaitTop ||
                (Tracker.ArcState == DepthArcState.AwaitBottom && !Tracker.InTopZone);

            // ── 3. Armer (reads the monitors above) ──
            if (Armer != null)
            {
                Armer.PhonePitchDeg = ReadPhonePitchDeg();
                Armer.Tick(frame, trackingOk, now, View.View, repInFlight);
            }

            bool isArmed = Armer != null && Armer.IsArmed;
            bool anchorOk = WristAnchor.LastVerdict == AnchorVerdict.Anchored;

            // Immediate counting suspension on a confident wrist-off-floor verdict (owner's
            // request: "both wrists lifted → counting stops"), but NOT mid-rep — at the bottom the
            // wrist landmarks jitter and a spurious Airborne window must not kill an honest arc.
            // An air "rep" that somehow started armed still faces the per-rep auditor.
            bool countingLive = isArmed
                && !(WristAnchor.LastVerdict == AnchorVerdict.Airborne && !repInFlight);

            // ── 4. Depth tracker (owns the top/bottom latches) ──
            Tracker.Tick(frame, trackingOk, countingLive, now, hasElbow, rawElbow, anchorOk);

            // ── 5. Audit window ──
            if (countingLive && trackingOk)
            {
                float elbowL = PoseMath.SideElbowAngle(frame, left: true);
                float elbowR = PoseMath.SideElbowAngle(frame, left: false);
                Auditor.RecordSample(RepSample.From(frame, Counter.Phase, elbowL, elbowR,
                    Tracker.SmoothedElbowDeg, Tracker.MedianElbowDeg));
            }

            // ── 6. Counter (consumes the tracker's latch pulses; fires the audit on rep arcs) ──
            Counter?.Process(frame, trackingOk, countingLive);

            // ── 7. Set / rest semantics for the UI ──
            SetTracker.Tick(isArmed, Counter != null ? Counter.Reps : 0, now);

            // Form/HUD can still update on any non-lost frame so the user sees live feedback.
            if (Quality != TrackingQuality.Lost)
            {
                LastForm = FormScoreCalculator.Evaluate(frame);
                OnFormUpdated?.Invoke(LastForm);
            }
        }

        /// <summary>κ = (hipMid_y − shoulderMid_y)/sw and kneeRel = (kneeMid_y − hipMid_y)/sw —
        /// square space, NaN when inputs are missing. Computed once per frame here (single-source
        /// rule) and consumed by KneeDrop / the armed-baseline capture.</summary>
        private static void ComputeFrontalScalars(in PoseFrame f, out float kappa, out float kneeRel)
        {
            kappa = float.NaN;
            kneeRel = float.NaN;
            if (!f.IsValid) return;

            float aspect = f.Aspect;
            bool ls = f.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = f.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            bool lh = f.Visibility(PoseLandmark.LeftHip)  >= CVConstants.MinJointVisibility;
            bool rh = f.Visibility(PoseLandmark.RightHip) >= CVConstants.MinJointVisibility;
            if (!ls || !rs || (!lh && !rh)) return;

            Vector2 lsp = PoseMath.ToSquare(f.Get(PoseLandmark.LeftShoulder).Pos2D, aspect);
            Vector2 rsp = PoseMath.ToSquare(f.Get(PoseLandmark.RightShoulder).Pos2D, aspect);
            float sw = Vector2.Distance(lsp, rsp);
            if (sw < 1e-3f) return;
            float shoulderMidY = 0.5f * (lsp.y + rsp.y);

            float hipMidY = (lh && rh)
                ? 0.5f * (f.Get(PoseLandmark.LeftHip).Y + f.Get(PoseLandmark.RightHip).Y)
                : f.Get(lh ? PoseLandmark.LeftHip : PoseLandmark.RightHip).Y;
            kappa = (hipMidY - shoulderMidY) / sw;

            bool lk = f.Visibility(PoseLandmark.LeftKnee)  >= CVConstants.MinJointVisibility;
            bool rk = f.Visibility(PoseLandmark.RightKnee) >= CVConstants.MinJointVisibility;
            if (lk || rk)
            {
                float kneeMidY = (lk && rk)
                    ? 0.5f * (f.Get(PoseLandmark.LeftKnee).Y + f.Get(PoseLandmark.RightKnee).Y)
                    : f.Get(lk ? PoseLandmark.LeftKnee : PoseLandmark.RightKnee).Y;
                kneeRel = (kneeMidY - hipMidY) / sw;
            }
        }

        /// <summary>Phone tilt from the IMU for the F0 SetupGate. A phone propped near-vertical
        /// (screen to the user) reads gravity ≈ (0,−1,0); lying flat reads ≈ 90°. NaN when there
        /// is no usable accelerometer (editor / mocks) — the gate passes.</summary>
        private static float ReadPhonePitchDeg()
        {
            Vector3 acc = Input.acceleration;
            if (acc.sqrMagnitude < 0.25f) return float.NaN;
            return Vector3.Angle(new Vector3(0f, -1f, 0f), acc);
        }

        private void HandleArmed()
        {
            // Freeze the knee/κ baselines the moment the plank is confirmed, grace the anchor
            // monitor so hand settling isn't flagged, and drop any pre-arm audit contamination.
            KneeDrop.CaptureBaseline(_lastKappa);
            WristAnchor.Reset(CVConstants.WristAnchorGraceFramesAfterArm);
            Auditor.Clear();
        }

        private void HandleDisarmed(PlankRejectReason reason)
        {
            Auditor.Clear();
        }

        private void HandleRepRejected(RepVote vote)
        {
            Tracker.CommitRepRejected();
            if (_logReps)
                Debug.Log($"[PushupSession] Rep VETOED ({vote})  src={Auditor.LastVoteSource}  frames={Auditor.LastWindowFrameCount}  dur={Auditor.LastWindowDurationSec:0.00}s");
            OnRepRejected?.Invoke(vote);
        }

        private void HandleRep(int reps)
        {
            Tracker.CommitRepAccepted();
            // Grace the wrist anchor monitor so the user can shift hands between reps.
            WristAnchor.Reset(CVConstants.WristAnchorGraceFramesAfterRep);

            if (_logReps)
            {
                var vote = Counter.LastRepVote;
                string voteTag = vote.Kind == RepVoteKind.Pass ? "Pass" : $"{vote}";
                Debug.Log($"[PushupSession] Rep {reps} | phase={Phase} | form={Form:0} | tempo={TempoRpm:0} rpm | vote={voteTag}");
            }
            OnRep?.Invoke(reps);
        }
    }
}
