using System;
using System.Collections.Generic;
using PushStars.Core;

namespace PushStars.UI
{
    /// <summary>Calendar-day activity, derived from real local workouts without writing progress.</summary>
    public sealed class ProfileInsights
    {
        public readonly int[] Reps = new int[7];
        public int WeekReps, ActiveDays, StreakDays, BestSet;
        public static ProfileInsights From(IReadOnlyList<MatchRecord> history, DateTime today, int savedBest)
        {
            var value = new ProfileInsights { BestSet = Math.Max(0, savedBest) };
            var days = new HashSet<DateTime>();
            foreach (var match in history)
            {
                if (!string.IsNullOrEmpty(match.Exercise) && match.Exercise != "pushups") continue;
                DateTime day = match.CreatedAt.ToLocalTime().Date;
                if (day > today.Date || match.MyReps <= 0) continue;
                value.BestSet = Math.Max(value.BestSet, match.MyReps);
                days.Add(day);
                int age = (today.Date - day).Days;
                if (age < 7) { value.Reps[6-age] += match.MyReps; value.WeekReps += match.MyReps; }
            }
            foreach (int reps in value.Reps) if (reps > 0) value.ActiveDays++;
            var cursor = days.Contains(today.Date) ? today.Date : today.Date.AddDays(-1);
            while (days.Contains(cursor)) { value.StreakDays++; cursor = cursor.AddDays(-1); }
            return value;
        }
    }
}
