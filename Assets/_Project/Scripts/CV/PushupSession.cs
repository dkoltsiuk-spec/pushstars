using System;
using UnityEngine;
using PushStars.CV.AntiCheat;

namespace PushStars.CV
{
    /// <summary>
    /// Runtime glue between a pose backend and the pushup analytics. Subscribes to an
    /// <see cref="IPoseSource"/>, feeds each frame into a <see cref="PushupRepCounter"/> and computes
    /// the live <see cref="FormReading"/>. This is the single component the duel HUD (phase 14) and
    /// training screen bind to for REPS / FORM / TEMPO.
    ///
    /// Drop a <c>MockPoseSource</c> in <see cref="_poseSourceBehaviour"/> to validate counting in the
    /// editor with no camera; swap to <c>MediaPipePoseSource</c> once the plugin is installed. Counting
    /// is gated on tracking quality so a lost skeleton never produces phantom reps.
    /// </summary>
    public sealed class PushupSession : MonoBehaviour
    {
        [Header("Pose source (must implement IPoseSource)")]
        [SerializeField] private MonoBehaviour _poseSourceBehaviour;

        [Header("Debug")]
        [SerializeField] private bool _logReps;

        public PushupRepCounter Counter { get; } = new PushupRepCounter();
        public FormReading LastForm { get; private set; }
        /// <summary>The most recent pose frame received from the source. Default (IsValid=false)
        /// until the first frame arrives. Exposed for the debug HUD and the anti-cheat layer.</summary>
        public PoseFrame LastFrame { get; private set; }
        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;

        // ── Anti-cheat (phase 08.1) ────────────────────────────────────────────────────────────
        // Owned here so PushupSession is the single integration point. Per-frame tick order is
        // strict: knee → anchor → armer → audit-record → counter (predicate reads anchor.LastVerdict
        // and knee.Classification, which must be fresh; auditor records the frame BEFORE the
        // counter potentially fires CreditRep so the sample is in the window when audit runs).
        public WristAnchorMonitor WristAnchor { get; } = new WristAnchorMonitor();
        public KneeBendDetector   KneeBend    { get; } = new KneeBendDetector();
        public PlankArmer         Armer       { get; private set; }
        public AntiCheatAuditor   Auditor     { get; } = new AntiCheatAuditor();

        public int Reps => Counter.Reps;
        public PushupPhase Phase => Counter.Phase;
        public float TempoRpm => Counter.TempoRpm;
        public float Form => LastForm.Form;

        /// <summary>Status/error from the pose source (for the on-screen debug HUD).</summary>
        public string SourceStatus => _source != null ? _source.StatusMessage : "(no source)";

        /// <summary>Forwarded from the rep counter (new total each completed rep).</summary>
        public event Action<int> OnRep;
        /// <summary>Forwarded from the rep counter — fires when the per-rep auditor vetoes a rep
        /// candidate. The HUD (and Stage 3 reject-feedback) subscribe to this to show the user
        /// why the rep didn't count.</summary>
        public event Action<RepVote> OnRepRejected;
        /// <summary>Raised every processed frame with the latest form reading.</summary>
        public event Action<FormReading> OnFormUpdated;

        private IPoseSource _source;

        private void Awake()
        {
            _source = _poseSourceBehaviour as IPoseSource;
            if (_source == null)
            {
                Debug.LogError("[PushupSession] Pose source is missing or does not implement IPoseSource.");
                return;
            }
            Armer = new PlankArmer(WristAnchor, KneeBend);
            Armer.OnArmed       += HandleArmed;
            Armer.OnDisarmed    += HandleDisarmed;
            Counter.OnRep       += HandleRep;
            Counter.OnRepRejected += HandleRepRejected;
            // Plug the auditor into the counter. The lambda is the seam between the pure-C# counter
            // and the MonoBehaviour-owned auditor — counter calls back when it would credit a rep.
            Counter.RepAuditor   = () => Auditor.AuditPendingRep();
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
            Counter.OnRep         -= HandleRep;
            Counter.OnRepRejected -= HandleRepRejected;
            Counter.RepAuditor     = null;
            if (Armer != null)
            {
                Armer.OnArmed    -= HandleArmed;
                Armer.OnDisarmed -= HandleDisarmed;
            }
        }

        public void ResetSession()
        {
            Counter.Reset();
            WristAnchor.Reset();
            KneeBend.Reset();
            Armer?.Reset();
            Auditor.Clear();
        }

        private void HandleFrame(PoseFrame frame)
        {
            LastFrame = frame;
            bool trackingOk = Quality == TrackingQuality.Good;

            // Anti-cheat per-frame chain. Order matters: PlankArmer's predicate reads the verdicts
            // that the monitor + detector just produced.
            KneeBend.Tick(frame);
            WristAnchor.Tick(frame);
            Armer.Tick(frame, trackingOk, Time.time);

            // Record into the per-rep audit window — but only when armed AND tracking. Garbage
            // frames (disarmed pose, lost skeleton) must NOT leak into the next rep's window or
            // they'll bias the validators.
            if (Armer.IsArmed && trackingOk)
            {
                float elbowL = PoseMath.SideElbowAngle(frame, left: true);
                float elbowR = PoseMath.SideElbowAngle(frame, left: false);
                Auditor.RecordSample(RepSample.From(frame, Counter.Phase, elbowL, elbowR));
            }

            // Counter is gated on (good tracking) AND (plank armer is armed). When not armed, the
            // counter clears its in-progress descent and parks at Top — no false credit when the
            // user finally re-enters plank. When a rep arc completes inside Process, Counter calls
            // back into the auditor via the RepAuditor delegate wired in Awake.
            Counter.Process(frame, trackingOk, Armer.IsArmed);

            // Form/HUD can still update on any non-lost frame so the user sees live feedback.
            if (Quality != TrackingQuality.Lost)
            {
                LastForm = FormScoreCalculator.Evaluate(frame);
                OnFormUpdated?.Invoke(LastForm);
            }
        }

        private void HandleArmed()
        {
            // Give the user a moment to settle their hand position before the anchor monitor starts
            // judging drift — see CVConstants.WristAnchorGraceFramesAfterArm. Also clear the audit
            // window so frames captured before arming don't contaminate the first rep.
            WristAnchor.Reset(CVConstants.WristAnchorGraceFramesAfterArm);
            Auditor.Clear();
        }

        private void HandleDisarmed(PlankRejectReason reason)
        {
            // Audit window from a previously-armed session is now stale — drop it.
            Auditor.Clear();
        }

        private void HandleRepRejected(RepVote vote)
        {
            if (_logReps)
                Debug.Log($"[PushupSession] Rep VETOED ({vote})  src={Auditor.LastVoteSource}  frames={Auditor.LastWindowFrameCount}  dur={Auditor.LastWindowDurationSec:0.00}s");
            OnRepRejected?.Invoke(vote);
        }

        private void HandleRep(int reps)
        {
            // Grace the wrist anchor monitor so the user can shift hands between reps without
            // tripping the airborne verdict.
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
