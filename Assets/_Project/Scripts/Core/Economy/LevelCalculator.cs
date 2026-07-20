using System;

namespace PushStars.Core
{
    /// <summary>
    /// Pure XP ↔ Level math for the "fast start, then steeper" curve
    /// (<c>cost(L→L+1) = round(LevelCostBase × L^LevelCostExponent)</c>). No Unity dependency, so it is
    /// trivially unit-testable. XP is never lost, so level only ever rises. See <c>docs/design/economy.md</c>.
    /// </summary>
    public static class LevelCalculator
    {
        /// <summary>XP needed to advance from <paramref name="level"/> to <paramref name="level"/>+1.</summary>
        public static long XpForLevelUp(int level)
        {
            if (level < 1) level = 1;
            return (long)Math.Round(
                EconomyConfig.LevelCostBase * Math.Pow(level, EconomyConfig.LevelCostExponent),
                MidpointRounding.AwayFromZero);
        }

        /// <summary>Total cumulative XP required to be exactly at <paramref name="level"/> (level 1 = 0 XP).</summary>
        public static long TotalXpForLevel(int level)
        {
            if (level <= 1) return 0;
            if (level > EconomyConfig.MaxLevel) level = EconomyConfig.MaxLevel;
            long sum = 0;
            for (int l = 1; l < level; l++) sum += XpForLevelUp(l);
            return sum;
        }

        /// <summary>The level a player with <paramref name="xp"/> total XP has reached (clamped to MaxLevel).</summary>
        public static int LevelFromXp(long xp)
        {
            if (xp <= 0) return 1;
            int level = 1;
            long cumulative = 0;
            while (level < EconomyConfig.MaxLevel)
            {
                long next = cumulative + XpForLevelUp(level);
                if (xp < next) break;
                cumulative = next;
                level++;
            }
            return level;
        }

        /// <summary>Progress (0..1) through the current level. Returns 1 at MaxLevel.</summary>
        public static float LevelProgress(long xp)
        {
            int level = LevelFromXp(xp);
            if (level >= EconomyConfig.MaxLevel) return 1f;
            long floor = TotalXpForLevel(level);
            long need = XpForLevelUp(level);
            return need <= 0 ? 0f : (float)(xp - floor) / need;
        }

        /// <summary>XP still required to reach the next level (0 at MaxLevel).</summary>
        public static long XpRemainingToNext(long xp)
        {
            int level = LevelFromXp(xp);
            if (level >= EconomyConfig.MaxLevel) return 0;
            return TotalXpForLevel(level + 1) - xp;
        }
    }
}
