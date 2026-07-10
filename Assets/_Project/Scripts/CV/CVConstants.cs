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
        /// <summary>A rep must take at least this long (bottom→top) — rejects fast arm-flapping.
        /// Lowered 0.45 → 0.30 (frontal addendum): 0.45 silently cut honest 1.5 rep/s athletes,
        /// and flapping is now killed by the 95↔160 envelope + PlankArmer + SupportGeometryGate.</summary>
        public const float MinRepSeconds = 0.30f;

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

        // ═════════════════════════════════════════════════════════════════════════════════════
        // Frontal addendum (docs/plan/phase-08.1-frontal-addendum.md). All spatial thresholds in
        // aspect-corrected "square" space unless noted. TopElbowAngle=160 / BottomElbowAngle=95
        // remain the immutable anti-cheat envelope — the ONLY latch source this release.
        // ═════════════════════════════════════════════════════════════════════════════════════

        // ── AmplitudeTracker: One-Euro filter + spike gating ──
        /// <summary>One-Euro cutoff at rest. Raised 1.5 → 2.5 after the fast-tempo review (at
        /// 1.5 rep/s a 1.5Hz cutoff lifted the smoothed minimum above the bottom zone).</summary>
        public const float ElbowFilterMinCutoffHz = 2.5f;
        public const float ElbowFilterBeta = 0.05f;
        public const float ElbowFilterDerivCutoffHz = 1.0f;
        public const float FilterDtClampMinSec = 0.0167f;
        public const float FilterDtClampMaxSec = 0.10f;
        /// <summary>Hampel clamp — a raw-vs-smoothed jump above this per frame is an outlier; the
        /// filtered signals hold their previous value for that frame.</summary>
        public const float ElbowSpikeClampDegPerFrame = 40f;
        /// <summary>Tracking gap longer than this → re-seed the filter, depth arc → Idle.</summary>
        public const float TrackerRebaseAfterLostSec = 0.5f;

        // ── AmplitudeTracker: zones & latching (median-of-3 raw signal, NOT the smoothed one) ──
        /// <summary>Zone latch debounce measured by timestamps (≈2 frames @30fps, robust at 25fps).</summary>
        public const float ZoneLatchSec = 0.07f;
        /// <summary>Single-frame instant latch when this deep past the zone edge.</summary>
        public const float ZoneDeepLatchMarginDeg = 4f;
        /// <summary>Enter→Exit hysteresis (BottomExit = BottomEnter + 6, TopExit = TopEnter − 6).</summary>
        public const float ZoneExitHysteresisDeg = 6f;
        /// <summary>Retro bottom-latch after a tracking dropout near the bottom: max gap length.</summary>
        public const float GraceLatchMaxGapSec = 0.5f;
        /// <summary>...and how close to the bottom zone the last valid frame must have been.</summary>
        public const float GraceLatchNearZoneDeg = 3f;

        // ── AmplitudeTracker: adaptive zones (HUD-ONLY this release — ratchet-deadlock review) ──
        public const bool  AdaptiveZonesAffectLatch = false;
        public const float AdaptiveMarginDeg = 8f;
        public const float AdaptiveMaxTightenTopDeg = 7f;
        public const float AdaptiveMaxTightenBottomDeg = 3f;
        public const int   AdaptiveMinReps = 3;
        public const int   AdaptiveWindowReps = 5;
        public const float AdaptiveDecayStepDeg = 3f;
        public const int   AdaptiveDecayAfterMissedAttempts = 2;

        // ── AmplitudeTracker: fixed HUD depth scale (the gauge must not "breathe") ──
        public const float AmplitudeGaugeTopDeg = 175f;    // d01 = 0
        public const float AmplitudeGaugeBottomDeg = 75f;  // d01 = 1

        // ── Bottom latch channel B (tucked elbows / wide grip) — OFF until acceptance recordings ──
        public const bool  BottomAltChannelEnabled = false;
        public const float BottomAltMaxElbowDeg = 120f;
        public const float BottomAltShoulderDropFracSw = 0.6f;
        public const float BottomAltNoseWristBandFracSw = 0.15f;

        // ── Audio feedback ──
        public const float BottomTickFreqHz = 1320f;  // E6 — perceptually distinct from the 880Hz rep beep
        public const float BottomTickDurSec = 0.04f;
        public const float RejectBuzzFreqHz = 220f;
        public const float RejectBuzzDurSec = 0.25f;

        // ── ViewClassifier ──
        public const float ViewFrontalMaxRatio = 0.7f;
        public const float ViewSideMinRatio = 1.6f;
        public const int   ViewMedianWindowFrames = 9;
        public const int   ViewSwitchVotes = 20;
        public const int   ViewSwitchWindow = 30;
        /// <summary>Hip visibility for a frame to "vote" (ONE hip suffices) — frontal hip vis
        /// routinely sits at 0.4–0.75, requiring 0.5 on both starved the classifier.</summary>
        public const float ViewHipVoteVisibility = 0.35f;

        // ── PlankArmer frontal branch F0–F6 ──
        /// <summary>F3: |κ| = |(hipMid_y − shoulderMid_y)/sw| ceiling for arming. Raised 0.28 →
        /// 0.35 (narrow shoulders ×1.2–1.3 and moderate tilt broke honest users).</summary>
        public const float FrontalMaxBodyInclineKappa = 0.35f;
        public const float FrontalMinBodyInclineKappa = -0.35f;
        public const float FrontalWristBelowShoulderFrac = 0.4f;   // F1
        public const float FrontalElbowBelowShoulderFrac = 0.25f;  // F1 fallback via elbows
        public const float FrontalWristSpreadMinFrac = 0.4f;       // F2 (diamond grip un-banned)
        public const float FrontalNarrowGripWristDropFrac = 0.6f;  // F2 narrow-grip strict branch
        public const float FrontalNoseBetweenPalmsFrac = 0.5f;     // F6
        /// <summary>F0 hip fail-closed: hipMid must be available this fraction of arming-window
        /// frames (arming only — per-rep gates stay fail-open).</summary>
        public const float FrontalArmingHipAvailabilityMin = 0.7f;

        // ── PlankArmer F0 SetupGate (framing / distance / phone tilt) ──
        public const float SetupMinShoulderWidthImg = 0.17f;  // ~2.3m
        public const float SetupMaxShoulderWidthImg = 0.38f;  // ~1.3m
        public const float SetupMaxNoseY = 0.85f;             // head would exit frame at the bottom
        public const float SetupMaxPhonePitchDeg = 30f;       // IMU gate

        // ── FullRomGate v2 ──
        /// <summary>travelFrac below → HardVeto ChestNotLowered (0.25/0.45 conflict resolved).</summary>
        public const float MinChestTravelFracHard = 0.25f;
        /// <summary>travelFrac in [Hard, Soft) → SoftDock ShallowTravel.</summary>
        public const float MinChestTravelFracSoft = 0.40f;
        /// <summary>BodySwing veto: shoulder-width growth ≥ this with travelFrac below
        /// BodySwingMaxTravelFrac = "approaching the camera without descending" signature.</summary>
        public const float BodySwingWidthRatioMin = 1.15f;
        public const float BodySwingMaxTravelFrac = 0.30f;

        // ── WristAnchorMonitor (scale fix is unconditional, view-independent) ──
        /// <summary>Absolute drift deadband (square-space norm): below this RMS the wrists are
        /// Anchored regardless of body-scale normalization — protects small/far silhouettes where
        /// pixel jitter doesn't scale with the body.</summary>
        public const float WristDriftAbsDeadband = 0.008f;

        // ── S-KNEE-1 KneeDropDetector / KneeCheatGate ──
        public const float KneeDropDeltaDisarm = 0.12f;    // per-frame ribbon (10 frames) → disarm
        public const float KneeDropDeltaRelease = 0.06f;
        public const float KneeDropDeltaHardVeto = 0.15f;  // per-rep mean over Top frames
        public const float KneeDropDeltaSoftDock = 0.10f;
        public const int   KneeDropDisarmRibbonFrames = 10;
        /// <summary>κ-drift from the arming baseline (works WITHOUT knee landmarks — catches
        /// "armed honestly, then dropped to knees" even with knees invisible).</summary>
        public const float KappaDriftSoftDock = 0.08f;
        public const float KappaDriftHardVeto = 0.15f;

        // ── S-KNEE-2 FootEventMonitor ──
        public const float FootVanishHighVis = 0.6f;
        public const float FootVanishLowVis = 0.35f;
        public const float FootVanishMinHeldSec = 2f;
        public const float FootVanishMinLostSec = 1f;
        /// <summary>Ankle RMS drift relative to wristMid (camera-shake subtracted) / sw.</summary>
        public const float FootDriftEventFrac = 0.25f;

        // ── S-AIR-1 SupportGeometryGate (ordering checks — phone-tilt invariant) ──
        public const float SupportWristBelowShoulderFrac = 0.15f;  // P1
        public const float SupportWristBelowHipFrac = 0.15f;       // P2 (kills table/wall pushups)
        public const float SupportWristVsLegMarginFrac = 0.10f;    // P3 (soft)

        // ── HipDecouplingGate frontal branch ──
        /// <summary>Pearson floor lowered 0.6 → 0.45 frontally (perspective squeezes hip travel).</summary>
        public const float FrontalMinHipShoulderCorr = 0.45f;
        public const float HipDropRatioMin = 0.15f;  // below → soft "worm"/knee hint
        public const float HipDropRatioMax = 1.1f;

        // ── KneeBendDetector view gating ──
        /// <summary>KneeBend is a HARD signal only when View==Side AND the leg is in the image
        /// plane: |hip→ankle| / sw ≥ this. Frontally the knee angle is uninformative (sagittal
        /// bend projects collinear).</summary>
        public const float KneeBendSideProjMinFrac = 0.8f;
    }
}
