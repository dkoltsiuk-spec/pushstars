namespace PushStars.CV.AntiCheat
{
    public enum RepVoteKind
    {
        /// <summary>Rep counts at full credit, no FORM penalty.</summary>
        Pass = 0,
        /// <summary>Rep counts, FORM score is multiplied by (1 - <see cref="RepVote.Penalty"/>).</summary>
        SoftDock = 1,
        /// <summary>Rep does NOT count — <see cref="PushupRepCounter.Reps"/> is unchanged.</summary>
        HardVeto = 2,
    }

    /// <summary>
    /// The verdict an <see cref="IRepValidator"/> returns for a single rep, and the aggregated
    /// verdict the <see cref="AntiCheatAuditor"/> returns to <see cref="PushupRepCounter"/>.
    ///
    /// <para>The aggregation rule is "first HardVeto wins; otherwise SoftDock penalties sum,
    /// clamped to <see cref="CVConstants.MaxAggregatedSoftDockPenalty"/>". See
    /// <see cref="AntiCheatAuditor"/> for the loop.</para>
    /// </summary>
    public readonly struct RepVote
    {
        public readonly RepVoteKind Kind;
        public readonly float Penalty;          // 0..1, only meaningful for SoftDock
        public readonly RepRejectReason Reason; // None for Pass

        private RepVote(RepVoteKind kind, float penalty, RepRejectReason reason)
        {
            Kind = kind;
            Penalty = penalty;
            Reason = reason;
        }

        public static readonly RepVote Pass = new RepVote(RepVoteKind.Pass, 0f, RepRejectReason.None);

        public static RepVote HardVeto(RepRejectReason reason)
            => new RepVote(RepVoteKind.HardVeto, 0f, reason);

        public static RepVote Dock(float penalty, RepRejectReason reason)
            => new RepVote(RepVoteKind.SoftDock, penalty, reason);

        public override string ToString()
            => Kind switch
            {
                RepVoteKind.Pass     => "Pass",
                RepVoteKind.SoftDock => $"SoftDock({Penalty:0.00}, {Reason})",
                RepVoteKind.HardVeto => $"HardVeto({Reason})",
                _ => Kind.ToString(),
            };
    }
}
