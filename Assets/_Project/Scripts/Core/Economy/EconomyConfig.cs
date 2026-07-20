namespace PushStars.Core
{
    /// <summary>
    /// Single source of truth (client) for the game economy: XP, levels, trophies, leagues, Aura,
    /// streaks and the XP anti-cheat caps. Mirrors <c>docs/architecture/constants.md</c> and
    /// <c>functions/src/constants.js</c> — when a value here is also server-side, the doc is canonical
    /// and the two must stay in sync. Values not present server-side (level curve, Aura, streak) are
    /// owned by this file and documented in <c>docs/design/economy.md</c>.
    /// </summary>
    public static class EconomyConfig
    {
        // ── XP from reps (== XP_PER_REP server-side) ─────────────────────────────────
        /// <summary>Base XP for one valid rep at perfect form. Mirrors <c>XP_PER_REP</c>.</summary>
        public const int XpPerRep = 10;
        /// <summary>FORM (0..100) below which a rep earns no XP (junk / anti-cheat). See PushStars.CV form scoring.</summary>
        public const float FormXpThreshold = 40f;
        /// <summary>Form multiplier at the threshold/floor — a barely-valid rep is worth half.</summary>
        public const float FormMultiplierMin = 0.5f;
        /// <summary>Form multiplier at FORM = 100 — a perfect rep earns a 50% bonus.</summary>
        public const float FormMultiplierMax = 1.5f;
        /// <summary>Reps per day that still earn XP (anti-grind / anti-injury). Reps beyond this still count toward stats.</summary>
        public const int DailyRepXpCap = 150;

        // ── XP from matches / session / login ────────────────────────────────────────
        public const int VsWinXp = 150;
        public const int VsLossXp = 50;
        /// <summary>One-off XP for finishing a training session of at least <see cref="MinSessionReps"/> reps.</summary>
        public const int SessionCompleteXp = 100;
        public const int MinSessionReps = 20;
        /// <summary>XP for the first app open of the day.</summary>
        public const int DailyLoginXp = 50;

        // ── Level curve: cost(L→L+1) = round(LevelCostBase × L^LevelCostExponent) ─────
        /// <summary>Scales the whole level curve. Bigger = every level costs more.</summary>
        public const double LevelCostBase = 120.0;
        /// <summary>Steepness. 1.0 = linear, 1.5 = "fast start, then steeper" (chosen for Push Stars).</summary>
        public const double LevelCostExponent = 1.5;
        /// <summary>Hard cap on player level.</summary>
        public const int MaxLevel = 100;

        // ── Streak (consecutive active days) ─────────────────────────────────────────
        /// <summary>XP bonus added per streak day (0.05 = +5%/day).</summary>
        public const float StreakXpBonusPerDay = 0.05f;
        /// <summary>Cap on the streak XP bonus (0.50 = +50%, reached at 10 days).</summary>
        public const float StreakXpBonusCap = 0.50f;
        public const int StreakMilestoneA = 7;
        public const int StreakMilestoneB = 30;

        // ── Trophies (PvP) — MVP flat values, mirror constants.md / constants.js ──────
        public const int TrophyWin = 25;
        public const int TrophyLoss = 15;   // applied as -15
        public const int TrophyGhostWin = 12;  // floor(TrophyWin / 2)
        public const int TrophyGhostLoss = 7;  // floor(TrophyLoss / 2)

        // ── Aura (premium currency) — earned only via progression, never raw reps ─────
        public const int AuraPerLevel = 5;
        /// <summary>Every level divisible by this grants an extra <see cref="AuraMilestoneBonus"/>.</summary>
        public const int AuraMilestoneInterval = 5;
        public const int AuraMilestoneBonus = 25;
        // First-time league promotion rewards (one league = one payout, tracked by the caller).
        public const int AuraPromoSilver = 50;
        public const int AuraPromoGold = 75;
        public const int AuraPromoDiamond = 100;
        // Streak milestone rewards.
        public const int AuraStreakMilestoneA = 30;  // 7-day
        public const int AuraStreakMilestoneB = 150; // 30-day
    }
}
