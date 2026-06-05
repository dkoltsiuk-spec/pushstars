namespace PushStars.Core
{
    /// <summary>
    /// Plain snapshot of a <c>users/{uid}</c> document. Lives in Core so both the data layer
    /// (Firestore repository) and the UI can share it without a Firebase dependency.
    /// Defaults match a brand-new player, so a missing document renders correctly too.
    /// </summary>
    public class UserProfile
    {
        public bool   Exists;
        public string DisplayName = "Игрок";
        public string Rank = "bronze";
        public long   Trophies;
        public long   Xp;
        public long   WinStreak;
        public long   TotalWins;
        public long   TotalLosses;
        public long   TotalReps;
        public double WinRate;

        public long Games => TotalWins + TotalLosses;

        /// <summary>Server-side winRate if present, else computed from W/L (0 when no games).</summary>
        public int WinRatePercent
        {
            get
            {
                if (WinRate > 0) return (int)System.Math.Round(WinRate);
                return Games > 0 ? (int)System.Math.Round((double)TotalWins / Games * 100.0) : 0;
            }
        }
    }
}
