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
        /// <summary>Extra trophies per active day after the first, on rated duel wins.</summary>
        public const int StreakTrophyBonusPerDay = 1;
        /// <summary>XP bonus added per streak day (0.05 = +5%/day).</summary>
        public const float StreakXpBonusPerDay = 0.05f;
        /// <summary>Cap on the streak XP bonus (0.50 = +50%, reached at 10 days).</summary>
        public const float StreakXpBonusCap = 0.50f;
        public const int StreakMilestoneA = 7;
        public const int StreakMilestoneB = 30;

        // ── Daily goal: push-ups to do today, summed over every mode (DailyGoal) ──────
        /// <summary>Target = best set before today × this, rounded up to 5, clamped to min..max.</summary>
        public const double DailyGoalBestSetShare = 1.5;
        public const int DailyGoalMinReps = 20;
        /// <summary>Never above <see cref="DailyRepXpCap"/> either: the goal must not push past the anti-grind cap.</summary>
        public const int DailyGoalMaxReps = 60;

        // ── Local reminders (ReminderPlanner), minutes after local midnight ───────────
        /// <summary>Habitual hour when there is no history yet: 19:00.</summary>
        public const int ReminderDefaultMinute = 19 * 60;
        /// <summary>The habitual hour is the median first-set time of this many recent active days.</summary>
        public const int ReminderHabitDays = 7;
        public const int ReminderEarliestMinute = 9 * 60;
        public const int ReminderLatestMinute = 21 * 60;
        /// <summary>"Streak is fading" comes this long after the habitual hour, kept within 20:00..21:30.</summary>
        public const int ReminderSaverAfterMinutes = 150;
        public const int ReminderSaverEarliestMinute = 20 * 60;
        public const int ReminderSaverLatestMinute = 21 * 60 + 30;
        public const int ReminderSaverMinStreak = 3;
        /// <summary>Goal reminders for today, tomorrow and the day after; a lapsed player gets no more than that.</summary>
        public const int ReminderDaysAhead = 3;
        /// <summary>A reminder closer than this to now is not scheduled.</summary>
        public const int ReminderMinLeadMinutes = 10;
        /// <summary>Goal reminder dropped when the evening one is closer than this.</summary>
        public const int ReminderMinGapMinutes = 90;

        // ── Duel hour (DuelHour): the daily hour players are gathered for live duels ──
        /// <summary>Starts at 20:00 local time and lasts an hour; one reminder at the start.</summary>
        public const int DuelHourStartMinute = 20 * 60;
        public const int DuelHourMinutes = 60;

        // ── Trophies (PvP) — MVP flat values, mirror constants.md / constants.js ──────
        public const int TrophyWin = 25;
        public const int TrophyLoss = 15;   // applied as -15
        public const int TrophyGhostWin = 12;  // floor(TrophyWin / 2)
        public const int TrophyGhostLoss = 7;  // floor(TrophyLoss / 2)

        // ── Aura — the meme status score ("+1000 aura"). Never spent; fights move it both ways ──
        // Balance floors at 0. Peak (the highest balance ever reached) never drops and unlocks the
        // Aura heroes. Rationale and the player-journey estimates: docs/design/economy.md §3.

        // Duel (PvP and ghost pay the same).
        public const int AuraWin = 1000;
        /// <summary>Win with at least <see cref="AuraCrushRatio"/>× the opponent's reps and a
        /// margin of <see cref="AuraCrushMargin"/>+. Replaces <see cref="AuraWin"/>.</summary>
        public const int AuraCrushWin = 1500;
        public const float AuraCrushRatio = 1.5f;
        public const int AuraCrushMargin = 5;
        /// <summary>Won after trailing by <see cref="AuraComebackDeficit"/>+ reps at some point
        /// from <see cref="AuraComebackAfterSec"/> seconds on.</summary>
        public const int AuraComeback = 1000;
        public const int AuraComebackDeficit = 3;
        public const float AuraComebackAfterSec = 20f;
        /// <summary>Won by exactly one rep.</summary>
        public const int AuraClutch = 500;
        public const int AuraDraw = 250;
        public const int AuraLoss = 500;          // applied as −500
        /// <summary>Lost with the opponent on the crush ratio and margin. Replaces <see cref="AuraLoss"/>.</summary>
        public const int AuraCrushedLoss = 1000;  // applied as −1000
        /// <summary>Left a live duel. The largest penalty on purpose: finishing always beats quitting.</summary>
        public const int AuraQuit = 2000;         // applied as −2000
        /// <summary>"Gave it all": a loss with at least this share of the personal best costs nothing.</summary>
        public const float AuraEffortShare = .9f;
        /// <summary>The first rated fights never cost Aura.</summary>
        public const int AuraRookieFights = 5;

        // Performance moments (win, draw or loss; never on a quit).
        public const int AuraPerfectForm = 500;
        public const float AuraPerfectFormMin = 90f;
        public const int AuraPerfectFormMinReps = 5;
        public const int AuraPerClap = 250;
        public const int AuraMaxClaps = 5;
        public const int AuraNewRecord = 2000;
        public const int AuraStreak3 = 1000;
        public const int AuraStreak5 = 2500;
        /// <summary>Paid at 10 wins in a row and every further 10.</summary>
        public const int AuraStreak10 = 10000;

        // League stakes: every duel moment is multiplied (wins and losses alike).
        public const float AuraStakesBronze = 1f;
        public const float AuraStakesSilver = 1.5f;
        public const float AuraStakesGold = 2f;
        public const float AuraStakesDiamond = 3f;

        // Bosses (PvE): no stakes multiplier and never a minus.
        public const int AuraBossFirstWin = 2000;
        public const int AuraIslandKingFirstWin = 10000;
        public const int AuraBossRepeatWin = 500;

        /// <summary>A finished training of at least <see cref="MinSessionReps"/> reps. PvE rules: no
        /// stakes, never a minus; perfect form pays on top.</summary>
        public const int AuraWorkoutDone = 500;

        // One-off progression rewards (no multiplier).
        /// <summary>Welcome Aura for the first assessment: Home shows "10K" straight away.</summary>
        public const int AuraAssessment = 10000;
        public const int AuraPerLevel = 1000;
        /// <summary>Every level divisible by this grants an extra <see cref="AuraMilestoneBonus"/>.</summary>
        public const int AuraMilestoneInterval = 5;
        public const int AuraMilestoneBonus = 5000;
        // First-time league promotion rewards (one league = one payout, by receipt).
        public const int AuraPromoSilver = 25000;
        public const int AuraPromoGold = 50000;
        public const int AuraPromoDiamond = 100000;
        // Daily rhythm (DailyRewards): the goal once per local day, streak milestones once ever.
        public const int AuraDailyGoal = 1000;
        public const int AuraStreakMilestoneA = 10000;  // 7-day
        public const int AuraStreakMilestoneB = 50000;  // 30-day

        // Visible aura around the hero: peak Aura at which each glow tier switches on (AuraTiers).
        public const long AuraTierGlow = 10000;
        public const long AuraTierBlueFlame = 50000;
        public const long AuraTierVioletStorm = 250000;
        public const long AuraTierInfinite = 1000000;
    }
}
