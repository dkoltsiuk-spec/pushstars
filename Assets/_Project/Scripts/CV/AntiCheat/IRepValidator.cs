namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// One validator stage in the per-rep audit pipeline. <see cref="AntiCheatAuditor"/> runs each
    /// registered validator in declared order on every rep candidate; the FIRST HardVeto short-
    /// circuits the audit (no further validators run), otherwise SoftDock penalties accumulate.
    /// </summary>
    public interface IRepValidator
    {
        /// <summary>Short label for HUD and telemetry — e.g. "FullRom", "Symmetry".</summary>
        string Name { get; }

        /// <summary>Inspect the per-rep window and return a verdict. Implementations must be
        /// pure (no side effects, no allocation in the hot path) and tolerant to missing data
        /// (degraded samples should generally return <see cref="RepVote.Pass"/>, not HardVeto —
        /// failing closed on noisy data would frustrate honest users).</summary>
        RepVote Validate(in RepWindow window);
    }
}
