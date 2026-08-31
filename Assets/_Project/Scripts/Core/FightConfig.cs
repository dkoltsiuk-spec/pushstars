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
        /// <summary>First-run intro pages. Only ever loaded by <c>AppBootstrap</c>.</summary>
        public const string OnboardingSceneName = "Onboarding";
        /// <summary>Loading screen and router; build index 0.</summary>
        public const string BootSceneName = "Boot";

        /// <summary>What the player's own recording is called on screen. Never "бот" and never the
        /// player's own name — the opponent is explicitly their past self, which is the appeal of the
        /// mode. Lives in Core because both the search overlay (UI) and the duel (Fight) show it.</summary>
        public const string GhostOpponentName = "ТВОЯ ТЕНЬ";
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
