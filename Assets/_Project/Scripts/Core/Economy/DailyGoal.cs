using System;
using System.Collections.Generic;
using System.Globalization;

namespace PushStars.Core
{
    /// <summary>Today's goal: target, reps done so far, and the local date it belongs to.</summary>
    public readonly struct DailyGoalStatus
    {
        public readonly string DayKey;
        public readonly int Target, Progress;
        public bool Done => Target > 0 && Progress >= Target;
        public float Fraction => Target > 0 ? Math.Min(1f, (float)Progress / Target) : 0f;

        public DailyGoalStatus(string dayKey, int target, int progress)
        { DayKey = dayKey; Target = target; Progress = progress; }
    }

    /// <summary>
    /// The daily goal: a number of push-ups to do today, summed over every mode (fights, bosses,
    /// training, the assessment). It is a separate thing from the streak flame, which any
    /// workout keeps alive. The target follows the player's best set before today, so it is fixed
    /// for the whole day and grows only as the player gets stronger, and it never asks for more
    /// than the reps that still earn XP (<see cref="EconomyConfig.DailyRepXpCap"/>). Pure function
    /// of the history: nothing is saved here. See <c>docs/design/economy.md</c> §4.
    /// </summary>
    public static class DailyGoal
    {
        public static string AuraReceipt(string dayKey) => "daily-goal:" + dayKey + ":aura:v1";

        public static string DayKey(DateTime localDay) => localDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Target for a player whose best single set before today was <paramref name="bestSetBefore"/>.</summary>
        public static int TargetFor(int bestSetBefore)
        {
            int target = (int)Math.Ceiling(bestSetBefore * EconomyConfig.DailyGoalBestSetShare / 5.0) * 5;
            int max = Math.Min(EconomyConfig.DailyGoalMaxReps, EconomyConfig.DailyRepXpCap);
            return Math.Max(EconomyConfig.DailyGoalMinReps, Math.Min(max, target));
        }

        /// <summary>Goal for the local day containing <paramref name="localNow"/>.</summary>
        public static DailyGoalStatus Evaluate(IReadOnlyList<MatchRecord> history, DateTime localNow, TimeZoneInfo zone = null)
        {
            zone = zone ?? TimeZoneInfo.Local;
            DateTime today = localNow.Date;
            int bestBefore = 0, progress = 0;
            if (history != null)
                foreach (var match in history)
                {
                    if (match == null || match.MyReps <= 0) continue;
                    if (!string.IsNullOrEmpty(match.Exercise) && match.Exercise != "pushups") continue;
                    DateTime day = LocalDay(match.CreatedAt, zone);
                    if (day < today) bestBefore = Math.Max(bestBefore, match.MyReps);
                    else if (day == today) progress += match.MyReps;
                }
            return new DailyGoalStatus(DayKey(today), TargetFor(bestBefore), progress);
        }

        /// <summary>Local calendar day of a UTC timestamp.</summary>
        public static DateTime LocalDay(DateTime utc, TimeZoneInfo zone = null)
        {
            if (utc.Kind != DateTimeKind.Utc) utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return TimeZoneInfo.ConvertTimeFromUtc(utc, zone ?? TimeZoneInfo.Local).Date;
        }
    }
}
