namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Why a per-rep <see cref="RepVote"/> was a HardVeto or SoftDock. Surfaced on the HUD
    /// ("CHEST NOT LOWERED", "TOO SLOW"...) and logged to telemetry for threshold tuning.
    /// </summary>
    public enum RepRejectReason
    {
        /// <summary>The rep passed — no rejection or penalty.</summary>
        None = 0,

        // ── Hard veto reasons (rep does NOT count) ────────────────────────────────────────────

        /// <summary><see cref="FullRomGate"/>: middle of the shoulders didn't travel far enough
        /// along the body-frame gravity axis. User bent elbows but chest stayed put.</summary>
        ChestNotLowered,

        /// <summary><see cref="BilateralSymmetryGate"/>: with BOTH arms visible most of the rep,
        /// one arm's range of motion was less than half the other's. One-arm fake.</summary>
        Asymmetric,

        /// <summary><see cref="TempoSanityGate"/>: rep took longer than
        /// <see cref="CVConstants.MaxRepSeconds"/>. User probably rested mid-rep.</summary>
        TooSlow,

        /// <summary><see cref="RepVisibilityGate"/>: average key-joint visibility across the rep
        /// window was below <see cref="CVConstants.RepWindowMinVisibilityAvg"/>. Skeleton was
        /// hallucinated — we can't trust this rep.</summary>
        LowVisibility,

        // ── Soft dock reasons (rep counts but FORM penalty applies) ───────────────────────────

        /// <summary><see cref="BilateralSymmetryGate"/>: arms moved in sync but with noticeable
        /// angle difference. Real rep, sloppy form.</summary>
        SlightAsymmetry,

        /// <summary><see cref="HipDecouplingGate"/>: hip and shoulder didn't move in sync along
        /// the body axis. Hint of a "worm" pattern — not the full cheat (FullRomGate would catch
        /// that), but form is off.</summary>
        HipDecoupled,

        /// <summary><see cref="RepVisibilityGate"/>: visibility was OK on average but soft below
        /// <see cref="CVConstants.RepWindowSoftDockVisibilityAvg"/>.</summary>
        PoorTracking,

        // ── Frontal addendum ──

        /// <summary><see cref="KneeCheatGate"/>: knees dropped relative to the arming baseline
        /// (KneeDropDelta) or the body incline drifted from the arming κ — knee push-up.</summary>
        KneeCheat,

        /// <summary><see cref="FullRomGate"/> BodySwing rule: shoulder width grew ≥15% while the
        /// projected travel stayed low — "approaching the camera without descending" (lean-in or
        /// knee-rocking cheat signature).</summary>
        BodySwing,

        /// <summary><see cref="SupportGeometryGate"/>: wrists are not below shoulders/hips in the
        /// image — the hands are not the support (air / table / wall push-ups).</summary>
        SupportGeometry,

        /// <summary><see cref="FullRomGate"/> soft band: travel in [0.25, 0.40) of body scale —
        /// counted, but the form score is docked.</summary>
        ShallowTravel,
    }
}
