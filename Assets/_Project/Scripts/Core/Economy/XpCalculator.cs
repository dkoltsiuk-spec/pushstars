using System;
using System.Collections.Generic;

namespace PushStars.Core
{
    /// <summary>
    /// Pure XP-earning math: per-rep XP (form-weighted, daily-capped), match/session/login awards, and
    /// the streak multiplier. Server (<c>syncOfflineXp</c>) is authoritative for persisted XP; this is the
    /// client-side mirror used for live HUD and prediction. See <c>docs/design/economy.md</c>.
    /// </summary>
    public static class XpCalculator
    {
        /// <summary>Maps FORM (0..100) to an XP multiplier. Below the threshold the rep earns nothing.</summary>
        public static float RepFormMultiplier(float form)
        {
            if (form < EconomyConfig.FormXpThreshold) return 0f;
            float t = Clamp01(form / 100f);
            return EconomyConfig.FormMultiplierMin +
                   t * (EconomyConfig.FormMultiplierMax - EconomyConfig.FormMultiplierMin);
        }

        /// <summary>XP for a single rep at the given form.</summary>
        public static long XpForRep(float form)
            => (long)Math.Round(EconomyConfig.XpPerRep * RepFormMultiplier(form), MidpointRounding.AwayFromZero);

        /// <summary>
        /// XP for a list of reps (one FORM value each), respecting the per-day rep XP cap.
        /// Pass how many XP-earning reps the player already logged today so the cap carries across sessions.
        /// </summary>
        public static long XpForReps(IReadOnlyList<float> repForms, int repsAlreadyCountedToday = 0)
        {
            if (repForms == null) return 0;
            long xp = 0;
            int counted = repsAlreadyCountedToday;
            for (int i = 0; i < repForms.Count; i++)
            {
                if (counted >= EconomyConfig.DailyRepXpCap) break;
                if (repForms[i] < EconomyConfig.FormXpThreshold) continue; // junk rep: no XP, no cap usage
                xp += XpForRep(repForms[i]);
                counted++;
            }
            return xp;
        }

        /// <summary>Streak XP multiplier: 1 + min(days × perDay, cap).</summary>
        public static float StreakMultiplier(int streakDays)
        {
            if (streakDays < 0) streakDays = 0;
            float bonus = Math.Min(streakDays * EconomyConfig.StreakXpBonusPerDay, EconomyConfig.StreakXpBonusCap);
            return 1f + bonus;
        }

        /// <summary>Applies the streak multiplier to a base (training) XP amount.</summary>
        public static long ApplyStreak(long baseXp, int streakDays)
            => (long)Math.Round(baseXp * StreakMultiplier(streakDays), MidpointRounding.AwayFromZero);

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
