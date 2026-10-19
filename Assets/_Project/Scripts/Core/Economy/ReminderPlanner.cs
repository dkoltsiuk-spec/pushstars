using System;
using System.Collections.Generic;

namespace PushStars.Core
{
    public enum ReminderKind { DailyGoal, StreakSaver }

    /// <summary>One local reminder to schedule. Copy is English source text, localized by the caller.</summary>
    public readonly struct PlannedReminder
    {
        public readonly DateTime LocalTime;
        public readonly ReminderKind Kind;
        public readonly string Title, Body;
        public PlannedReminder(DateTime localTime, ReminderKind kind, string title, string body)
        { LocalTime = localTime; Kind = kind; Title = title; Body = body; }
    }

    /// <summary>
    /// Which local reminders to keep scheduled, decided from the history alone (no server). The
    /// daily goal reminder comes at the player's habitual hour — the median start of their last
    /// active days — on any day the goal is not done yet. From a 3-day streak, an evening
    /// "streak is fading" reminder covers a day with no workout. At most two a day, and the whole
    /// set is replaced every time the app is opened or closed. See <c>docs/plan/phase-11.5-daily-challenge.md</c>.
    /// </summary>
    public static class ReminderPlanner
    {
        public const string GoalTitle = "Daily goal";
        public const string GoalBodyFresh = "Push-ups to today's goal: {0}. Reward: +{1} Aura.";
        public const string GoalBodyLeft = "Push-ups left to today's goal: {0}. Reward: +{1} Aura.";
        public const string SaverTitle = "Your streak is fading";
        public const string SaverBody = "Days in a row: {0}. One set today keeps it going.";

        /// <summary>Minutes after midnight when the player usually starts; default when there is no history.</summary>
        public static int HabitualMinute(IReadOnlyList<MatchRecord> history, TimeZoneInfo zone = null)
        {
            // Earliest set of each active day, ordered by day.
            var firstOfDay = new SortedDictionary<DateTime, int>();
            if (history != null)
                foreach (var match in history)
                {
                    if (match == null || match.MyReps <= 0) continue;
                    var start = DateTime.SpecifyKind(match.CreatedAt, DateTimeKind.Utc).AddSeconds(-Math.Max(0, match.DurationSec));
                    var local = TimeZoneInfo.ConvertTimeFromUtc(start, zone ?? TimeZoneInfo.Local);
                    int minute = local.Hour * 60 + local.Minute;
                    if (!firstOfDay.TryGetValue(local.Date, out int known) || minute < known) firstOfDay[local.Date] = minute;
                }
            if (firstOfDay.Count == 0) return EconomyConfig.ReminderDefaultMinute;
            var recent = new List<int>(firstOfDay.Values);
            if (recent.Count > EconomyConfig.ReminderHabitDays) recent.RemoveRange(0, recent.Count - EconomyConfig.ReminderHabitDays);
            recent.Sort();
            int median = recent[recent.Count / 2];
            return Math.Max(EconomyConfig.ReminderEarliestMinute, Math.Min(EconomyConfig.ReminderLatestMinute, median));
        }

        /// <summary>Evening "streak is fading" time for a given habitual minute.</summary>
        public static int SaverMinute(int habitualMinute) => Math.Min(EconomyConfig.ReminderSaverLatestMinute,
            Math.Max(EconomyConfig.ReminderSaverEarliestMinute, habitualMinute + EconomyConfig.ReminderSaverAfterMinutes));

        public static List<PlannedReminder> Plan(IReadOnlyList<MatchRecord> history, DailyGoalStatus today, int streakDays,
            DateTime localNow, TimeZoneInfo zone = null)
        {
            var plan = new List<PlannedReminder>();
            int habit = HabitualMinute(history, zone), saver = SaverMinute(habit);
            bool trainedToday = today.Progress > 0;
            string aura = EconomyConfig.AuraDailyGoal.ToString(System.Globalization.CultureInfo.InvariantCulture);

            for (int day = 0; day < EconomyConfig.ReminderDaysAhead; day++)
            {
                DateTime date = localNow.Date.AddDays(day);
                DateTime goalTime = date.AddMinutes(habit), saverTime = date.AddMinutes(saver);
                // The streak can only fade today (no set yet) or tomorrow (kept alive by today's set).
                bool saverDue = streakDays >= EconomyConfig.ReminderSaverMinStreak &&
                                (day == 0 ? !trainedToday : day == 1 && trainedToday);
                bool goalDue = day > 0 || !today.Done;
                if (saverDue && saverTime > localNow.AddMinutes(EconomyConfig.ReminderMinLeadMinutes))
                {
                    plan.Add(new PlannedReminder(saverTime, ReminderKind.StreakSaver, SaverTitle,
                        string.Format(SaverBody, streakDays)));
                    // Too close to the evening one: that one is the more urgent, skip the goal.
                    if ((saverTime - goalTime).TotalMinutes < EconomyConfig.ReminderMinGapMinutes) goalDue = false;
                }
                if (!goalDue || goalTime <= localNow.AddMinutes(EconomyConfig.ReminderMinLeadMinutes)) continue;
                int left = day == 0 ? Math.Max(0, today.Target - today.Progress) : today.Target;
                plan.Add(new PlannedReminder(goalTime, ReminderKind.DailyGoal, GoalTitle,
                    string.Format(day == 0 && trainedToday ? GoalBodyLeft : GoalBodyFresh, left, aura)));
            }
            plan.Sort((a, b) => a.LocalTime.CompareTo(b.LocalTime));
            return plan;
        }
    }
}
