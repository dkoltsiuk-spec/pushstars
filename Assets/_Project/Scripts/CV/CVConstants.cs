namespace PushStars.CV
{
    /// <summary>
    /// Tuning constants for pushup detection and form scoring. Starting values from biomechanics
    /// rules of thumb — refine against the phase-08 test recordings (see acceptance criteria). The
    /// match-level cap mirrors <c>MAX_REPS_PER_MATCH</c> in docs/architecture/constants.md.
    /// </summary>
    public static class CVConstants
    {
        // ── Rep FSM (average elbow angle, degrees) ──────────────────────────────────
        /// <summary>Elbow angle at/above which the arms count as "locked out" (top of the pushup).</summary>
        public const float TopElbowAngle = 160f;
        /// <summary>Elbow angle at/below which the rep counts as having reached the bottom.</summary>
        public const float BottomElbowAngle = 95f;

        // ── Anti-cheat / match ───────────────────────────────────────────────────────
        public const int MaxRepsPerMatch = 65; // == MAX_REPS_PER_MATCH

        // ── Pushup-pose gate (rejects phantom reps from non-pushup motion, e.g. waving arms) ──
        /// <summary>Body-line angle (shoulder–hip–knee/ankle) must be at least this for the pose to
        /// count as a plank — rejects sitting/lying/curled poses where the body isn't extended.</summary>
        public const float MinPlankBodyLine = 140f;
        /// <summary>A rep must take at least this long (bottom→top) — rejects fast arm-flapping.</summary>
        public const float MinRepSeconds = 0.45f;

        // ── Tracking quality gates (visibility ∈ [0,1]) ────────────────────────────────
        /// <summary>Below this, a single key joint is treated as not visible.</summary>
        public const float MinJointVisibility = 0.5f;
        /// <summary>Average key-joint visibility above this → <see cref="TrackingQuality.Good"/>.</summary>
        public const float GoodVisibility = 0.7f;

        // ── Form scoring ───────────────────────────────────────────────────────────────
        /// <summary>Body-line angle (shoulder–hip–ankle) considered perfectly straight.</summary>
        public const float StraightBodyAngle = 180f;
        /// <summary>Deviation (deg) from straight at which the body-line score hits 0 (heavy sag/pike).</summary>
        public const float BodyLineZeroAt = 35f;
        /// <summary>Elbow angle at the bottom that earns a full depth score (deeper = better, clamped).</summary>
        public const float FullDepthElbowAngle = 80f;
        /// <summary>Elbow angle at the bottom that earns zero depth score (too shallow).</summary>
        public const float ShallowDepthElbowAngle = 120f;

        /// <summary>Weights for the combined FORM score (sum to 1).</summary>
        public const float DepthWeight = 0.5f;
        public const float BodyLineWeight = 0.5f;

        // ── Anti-cheat (phase 08.1) — see docs/plan/phase-08.1-pushup-anticheat.md ─────────────

        // ── Plank arming (PlankArmer) ──
        /// <summary>How long the user must hold a valid plank before the rep counter arms.</summary>
        public const float PlankArmHoldSec = 1.0f;
        /// <summary>Grace period — Armed → Cooling → Disarmed transitions over this many seconds
        /// of continuous invalid frames. Covers skeleton glitches mid-rep.</summary>
        public const float PlankDisarmGraceSec = 2.5f;
        /// <summary>Body-line angle required to count as a plank for arming. Stricter than the
        /// legacy <see cref="MinPlankBodyLine"/> which gates mid-rep sanity.</summary>
        public const float ArmingBodyLineAngle = 160f;
        /// <summary>Elbow angle required at arming — user must start from the top of a push-up.</summary>
        public const float ArmingElbowTopAngle = 150f;
        /// <summary>Lower-body landmark visibility threshold for the plank-armer "lower body visible"
        /// check. Higher than <see cref="MinJointVisibility"/> because false-arming from a partially-
        /// occluded leg is worse than rejecting a side-camera framing.</summary>
        public const float PlankLowerBodyVisibility = 0.7f;

        // ── Knee bend detector (KneeBendDetector) ──
        /// <summary>Hip-knee-ankle angle at or below which the raw classification is "Bent" (knee
        /// push-up). Lowered from 150° to 145° to account for image-space perspective and slight
        /// natural knee softness in legitimate reps.</summary>
        public const float KneeBentMaxAngle = 145f;
        /// <summary>Hip-knee-ankle angle at or above which the raw classification is "Straight".
        /// Gap with <see cref="KneeBentMaxAngle"/> = hysteresis dead zone.</summary>
        public const float KneeStraightMinAngle = 160f;
        /// <summary>Consecutive frames a raw classification must hold before the smoothed
        /// classification changes. Prevents single-frame flicker.</summary>
        public const int KneeClassificationRibbonFrames = 5;

        // ── Wrist anchor monitor (WristAnchorMonitor) ──
        /// <summary>Sliding window length (frames) for wrist drift detection. 12 ≈ 400ms @ 30fps.</summary>
        public const int WristAnchorWindowFrames = 12;
        /// <summary>Wrist drift below this fraction of torso length → <see cref="AntiCheat.AnchorVerdict.Anchored"/>.</summary>
        public const float WristAnchorSoftFrac = 0.10f;
        /// <summary>Wrist drift at or above this fraction of torso length → <see cref="AntiCheat.AnchorVerdict.Airborne"/>
        /// (hard veto). Drift between the two thresholds → <see cref="AntiCheat.AnchorVerdict.Drifting"/>.</summary>
        public const float WristAnchorHardFrac = 0.20f;
        /// <summary>Grace frames after a credited rep — the monitor returns <see cref="AntiCheat.AnchorVerdict.Unknown"/>
        /// so a user adjusting hand position between reps is not falsely flagged. ~1.5s @ 30fps.</summary>
        public const int WristAnchorGraceFramesAfterRep = 45;
        /// <summary>Grace frames after the armer transitions to Armed — gives the user a moment to
        /// settle hand position before drift starts being measured. ~1s @ 30fps.</summary>
        public const int WristAnchorGraceFramesAfterArm = 30;

        // ── Per-rep auditor (Stage 2, phase 08.1) ──────────────────────────────────────────────

        /// <summary>Ring-buffer capacity for per-rep samples. 512 frames ≈ 17s @ 30fps — safe
        /// headroom above <see cref="MaxRepSeconds"/>.</summary>
        public const int RepWindowMaxFrames = 512;

        /// <summary>How many initial samples to average for the rep's body-axis estimate
        /// (shoulder→hip direction at the top of the rep, before descent rotates the torso).</summary>
        public const int RepBodyAxisLeadFrames = 5;

        /// <summary>Maximum rep duration before <see cref="AntiCheat.TempoSanityGate"/> HardVetos
        /// — user probably rested mid-rep. Raised from 8s after adversarial review (slow controlled
        /// reps + Bottom pause variants).</summary>
        public const float MaxRepSeconds = 12f;

        /// <summary>Per-rep mean key-joint visibility floor; below = HardVeto LowVisibility.</summary>
        public const float RepWindowMinVisibilityAvg = 0.60f;
        /// <summary>Soft dock cap on visibility — between this and the hard floor → SoftDock.</summary>
        public const float RepWindowSoftDockVisibilityAvg = 0.70f;
        public const float PoorTrackingPenalty = 0.15f;

        /// <summary>Required chest travel along the body axis as a fraction of torso length. A
        /// real push-up travels ~0.6-1.0× shoulder-hip distance; setting the floor at 0.30 admits
        /// short-range pushers but rejects elbow-only fakes.</summary>
        public const float MinChestTravelFracBody = 0.30f;

        /// <summary>Minimum ratio of weaker arm's ROM to stronger arm's. Below + both arms visible
        /// → HardVeto Asymmetric.</summary>
        public const float MinBilateralAmplitudeRatio = 0.50f;
        /// <summary>Fraction of rep frames each arm chain must be visible for symmetry to apply at
        /// all. Side-camera framings legitimately occlude one arm — we skip rather than veto.</summary>
        public const float SymmetryArmVisibilityThreshold = 0.75f;
        /// <summary>Mean |left − right| elbow angle across the rep — above → SoftDock SlightAsymmetry.</summary>
        public const float MaxBilateralMeanAbsDiffDeg = 20f;
        public const float SlightAsymmetryPenalty = 0.20f;

        /// <summary>Pearson correlation between hip-projection and shoulder-projection along body
        /// axis. Below → SoftDock HipDecoupled (gentle "worm").</summary>
        public const float MinHipShoulderCorrelation = 0.60f;
        public const float HipDecouplingPenalty = 0.25f;

        /// <summary>Aggregated soft-dock penalty cap — a single rep can have multiple sub-par
        /// signals but FORM never goes to zero from soft-docks alone.</summary>
        public const float MaxAggregatedSoftDockPenalty = 0.80f;
    }
}
