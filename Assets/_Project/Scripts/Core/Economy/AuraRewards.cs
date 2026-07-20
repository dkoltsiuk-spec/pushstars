namespace PushStars.Core
{
    /// <summary>
    /// Aura (premium currency) faucets. Aura is intentionally rare: it is earned only from progression
    /// milestones — never from raw reps — and otherwise bought via IAP. Spending (cosmetics, boosts) is
    /// handled by the shop once it exists. Each payout here is one-shot; the caller is responsible for
    /// not double-awarding (e.g. tracking which league promotions were already paid). See
    /// <c>docs/design/economy.md</c>.
    /// </summary>
    public static class AuraRewards
    {
        /// <summary>
        /// Aura granted for advancing from <paramref name="oldLevel"/> to <paramref name="newLevel"/>.
        /// Handles multi-level jumps and adds the milestone bonus on every level divisible by the interval.
        /// </summary>
        public static long ForLevelUp(int oldLevel, int newLevel)
        {
            if (newLevel <= oldLevel) return 0;
            long aura = 0;
            for (int l = oldLevel + 1; l <= newLevel; l++)
            {
                aura += EconomyConfig.AuraPerLevel;
                if (l % EconomyConfig.AuraMilestoneInterval == 0)
                    aura += EconomyConfig.AuraMilestoneBonus;
            }
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
