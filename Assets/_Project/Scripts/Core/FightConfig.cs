namespace PushStars.Core
{
    /// <summary>
    /// Client constants for the boss duel (phase 08.9). Mirrors the table in
    /// <c>docs/plan/phase-08.9-boss-fight.md</c>; values that later gain a server twin move to
    /// <c>constants.md</c> like the rest of <see cref="EconomyConfig"/>.
    /// </summary>
    public static class FightConfig
    {
        /// <summary>How long the search ring spins before the boss is "found".</summary>
        public const float SearchDelaySec = 2.5f;
        /// <summary>How long the "СОПЕРНИК НАЙДЕН" card stays before the fight scene loads.</summary>
        public const float FoundPauseSec = 1.6f;
        /// <summary>Fixed duel length (the 60 СЕК mode; МАКС comes later).</summary>
        public const int DuelDurationSec = 60;
        /// <summary>One-off XP bonus for beating a boss (on top of per-rep XP).</summary>
        public const int BossWinXpBonus = 50;
        /// <summary>Countdown after the plank is confirmed, before reps start counting.</summary>
        public const int CountdownSec = 3;

        public const string FightSceneName = "Fight";
        public const string MainSceneName = "Main";
    }

    /// <summary>
    /// Local accumulator for XP earned offline (boss duels, later trainings). Phase 11.5's
    /// <c>syncOfflineXp</c> drains it to the server; until then it makes earned XP survive restarts
    /// instead of evaporating. PlayerPrefs is fine at this scale (one long).
    /// </summary>
    public static class OfflineXpBank
    {
        private const string Key = "pending_xp";

        public static long Pending
        {
            get => long.TryParse(UnityEngine.PlayerPrefs.GetString(Key, "0"), out var v) ? v : 0L;
            private set { UnityEngine.PlayerPrefs.SetString(Key, value.ToString()); UnityEngine.PlayerPrefs.Save(); }
        }

        public static void Add(long xp)
        {
            if (xp <= 0) return;
            Pending += xp;
        }
    }
}
