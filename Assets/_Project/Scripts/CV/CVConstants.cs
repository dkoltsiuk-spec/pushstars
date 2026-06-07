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
    }
}
