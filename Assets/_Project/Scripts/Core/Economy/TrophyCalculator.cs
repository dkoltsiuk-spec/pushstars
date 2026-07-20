namespace PushStars.Core
{
    /// <summary>Which trophy table a result uses.</summary>
    public enum MatchKind { Pvp, Ghost }

    /// <summary>
    /// Trophy deltas for a finished match. MVP uses flat values (no Elo scaling) to stay aligned with
    /// the server (<c>functions/src/constants.js</c>) and the Ghost-mode spec. Ghost awards half of PvP.
    /// Trophies never drop below 0. See <c>docs/architecture/constants.md</c>.
    /// </summary>
    public static class TrophyCalculator
    {
        public static int WinDelta(MatchKind kind)
            => kind == MatchKind.Ghost ? EconomyConfig.TrophyGhostWin : EconomyConfig.TrophyWin;

        /// <summary>Signed (negative) trophy change for a loss.</summary>
        public static int LossDelta(MatchKind kind)
            => -(kind == MatchKind.Ghost ? EconomyConfig.TrophyGhostLoss : EconomyConfig.TrophyLoss);

        /// <summary>Signed delta for a result (true = won).</summary>
        public static int Delta(bool won, MatchKind kind = MatchKind.Pvp)
            => won ? WinDelta(kind) : LossDelta(kind);

        /// <summary>Applies a delta to a trophy total, clamped at 0 (no negative trophies).</summary>
        public static long ApplyDelta(long trophies, int delta)
        {
            long result = trophies + delta;
            return result < 0 ? 0 : result;
        }
    }
}
