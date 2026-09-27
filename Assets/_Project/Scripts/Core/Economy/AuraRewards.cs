namespace PushStars.Core
{
    /// <summary>
    /// One-off Aura for progression: level-ups, first league promotions, streak milestones. Each
    /// payout is keyed by a stable receipt (<see cref="LevelReceipt"/>, <see cref="LeagueReceipt"/>)
    /// so the ledger pays it exactly once, whatever screen notices it first. Fight Aura is priced by
    /// <see cref="AuraCalculator"/>. See <c>docs/design/economy.md</c> §3.
    /// </summary>
    public static class AuraRewards
    {
        public static string LevelReceipt(int level) => "level:" + level + ":aura:v1";
        public static string LeagueReceipt(string leagueId) => "league:" + leagueId + ":aura:v1";

        /// <summary>Aura for reaching exactly <paramref name="level"/> (0 for level 1).</summary>
        public static long ForLevel(int level)
        {
            if (level <= 1) return 0;
            long aura = EconomyConfig.AuraPerLevel;
            if (level % EconomyConfig.AuraMilestoneInterval == 0) aura += EconomyConfig.AuraMilestoneBonus;
            return aura;
        }

        /// <summary>
        /// Aura granted for advancing from <paramref name="oldLevel"/> to <paramref name="newLevel"/>.
        /// Handles multi-level jumps and adds the milestone bonus on every level divisible by the interval.
        /// </summary>
        public static long ForLevelUp(int oldLevel, int newLevel)
        {
            long aura = 0;
            for (int l = System.Math.Max(1, oldLevel) + 1; l <= newLevel; l++) aura += ForLevel(l);
            return aura;
        }

        /// <summary>First-time reward for being promoted into a league (0 for Bronze / unknown).</summary>
        public static long ForPromotion(string leagueId)
        {
            switch (leagueId)
            {
                case "silver":  return EconomyConfig.AuraPromoSilver;
                case "gold":    return EconomyConfig.AuraPromoGold;
                case "diamond": return EconomyConfig.AuraPromoDiamond;
                default:        return 0;
            }
        }

        /// <summary>Reward when a streak reaches a milestone day (0 on non-milestone days).</summary>
        public static long ForStreakDay(int streakDay)
        {
            if (streakDay == EconomyConfig.StreakMilestoneA) return EconomyConfig.AuraStreakMilestoneA;
            if (streakDay == EconomyConfig.StreakMilestoneB) return EconomyConfig.AuraStreakMilestoneB;
            return 0;
        }
    }
}
