using System;
using System.Collections.Generic;
using System.IO;
using PushStars.Core;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Daily goal target/progress, local-day streak and the daily Aura receipts.</summary>
    public static class DailyGoalRegression
    {
        [MenuItem("Tools/Push Stars/Validate Daily Goal")]
        public static void Run()
        {
            Require(DailyGoal.TargetFor(0) == 20 && DailyGoal.TargetFor(10) == 20 && DailyGoal.TargetFor(14) == 25 &&
                DailyGoal.TargetFor(30) == 45 && DailyGoal.TargetFor(50) == 60 && DailyGoal.TargetFor(500) == 60,
                "Daily goal target off the 1.5× best set, 20..60 rule.");

            // Moscow: 21:30 UTC is already the next local day.
            var msk = TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");
            var now = new DateTime(2026, 10, 18, 12, 0, 0);
            var history = new List<MatchRecord> {
                Match(20, new DateTime(2026, 10, 17, 10, 0, 0)),   // yesterday: best set 20 → target 30
                Match(35, new DateTime(2026, 10, 18, 9, 0, 0)),    // today, beats the record: target stays 30
                Match(12, new DateTime(2026, 10, 16, 21, 30, 0)),  // local 17th, not today
                Match(0,  new DateTime(2026, 10, 18, 10, 0, 0)),
            };
            var goal = DailyGoal.Evaluate(history, now, msk);
            Require(goal.Target == 30 && goal.Progress == 35 && goal.Done && goal.DayKey == "2026-10-18",
                $"Goal evaluated wrong: {goal.Progress}/{goal.Target} {goal.DayKey}");
            goal = DailyGoal.Evaluate(new List<MatchRecord> { Match(10, new DateTime(2026, 10, 17, 21, 30, 0)) }, now, msk);
            Require(goal.Target == 20 && goal.Progress == 10 && !goal.Done, "Late-evening UTC set not counted on the local day.");
            Require(DailyGoal.Evaluate(null, now, msk).Target == EconomyConfig.DailyGoalMinReps, "First day has no minimum target.");

            // Daily Aura: goal once per day, milestones once ever, "reached" rather than "exactly on".
            var paid = new HashSet<string>();
            var done = new DailyGoalStatus("2026-10-18", 20, 25);
            var pending = DailyRewards.Pending(done, 8, paid.Contains);
            Require(pending.Count == 2 && pending[0].Grant.Delta == EconomyConfig.AuraDailyGoal &&
                pending[1].Grant.Delta == EconomyConfig.AuraStreakMilestoneA, "Daily Aura pending list wrong.");
            foreach (var p in pending) paid.Add(p.Grant.Receipt);
            Require(DailyRewards.Pending(done, 9, paid.Contains).Count == 0, "Daily Aura paid twice.");
            Require(DailyRewards.Pending(new DailyGoalStatus("2026-10-19", 20, 25), 30, paid.Contains).Count == 2,
                "Next day goal or 30-day milestone missing.");
            Require(DailyRewards.Pending(new DailyGoalStatus("2026-10-20", 20, 19), 3, paid.Contains).Count == 0,
                "Unfinished goal paid Aura.");

            // The ledger's active days are local days: two sets on one Moscow evening are one day.
            var ledger = new LocalWorkoutLedger(null, _ => { }, null, msk);
            ledger.Record(Workout("a", new DateTime(2026, 10, 17, 22, 0, 0)));  // 01:00 on the 18th
            ledger.Record(Workout("b", new DateTime(2026, 10, 18, 20, 0, 0)));  // 23:00 on the 18th
            Require(ledger.Snapshot.DailyStreak == 1, "One local day counted as two.");
            ledger.Record(Workout("c", new DateTime(2026, 10, 18, 21, 30, 0))); // 00:30 on the 19th
            Require(ledger.Snapshot.DailyStreak == 2, "Next local day did not extend the streak.");

            ValidateReminders(msk);

            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/daily-goal-regression.txt", "PASS: target rule, local-day progress, daily Aura receipts, local-day streak, reminder plan.\n");
            Debug.Log("[DailyGoalRegression] PASS");
        }

        private static void ValidateReminders(TimeZoneInfo msk)
        {
            // A set started at local 18:00 / 19:00 / 20:00 on three days (Moscow = UTC+3, 60-second sets).
            MatchRecord StartedAt(int day, int hour) => new MatchRecord {
                Mode = "ghost", Exercise = "pushups", MyReps = 10, DurationSec = 60,
                CreatedAt = new DateTime(2026, 10, day, hour - 3, 1, 0, DateTimeKind.Utc) };
            var history = new List<MatchRecord> { StartedAt(14, 18), StartedAt(15, 19), StartedAt(15, 22), StartedAt(16, 20) };
            Require(ReminderPlanner.HabitualMinute(history, msk) == 19 * 60, "Habitual hour is not the median first set.");
            Require(ReminderPlanner.HabitualMinute(null, msk) == EconomyConfig.ReminderDefaultMinute, "No default habitual hour.");
            Require(ReminderPlanner.HabitualMinute(new List<MatchRecord> { StartedAt(16, 6) }, msk) == EconomyConfig.ReminderEarliestMinute,
                "Early bird reminder not moved into the day.");
            Require(ReminderPlanner.SaverMinute(19 * 60) == 21 * 60 + 30 && ReminderPlanner.SaverMinute(10 * 60) == 20 * 60,
                "Evening reminder outside 20:00..21:30.");

            var noon = new DateTime(2026, 10, 18, 12, 0, 0);
            int Count(List<PlannedReminder> plan, ReminderKind kind) { int n = 0; foreach (var r in plan) if (r.Kind == kind) n++; return n; }

            // No set yet today, 4-day streak: tonight's duel hour and "fading"; the next days' goal
            // (habitually 19:00) rides along with the 20:00 duel hour.
            var plan = ReminderPlanner.Plan(history, new DailyGoalStatus("2026-10-18", 30, 0), 4, noon, msk);
            Require(plan.Count == 4 && Count(plan, ReminderKind.StreakSaver) == 1 && Count(plan, ReminderKind.DuelHour) == 3 &&
                plan[0].LocalTime == noon.Date.AddHours(20) && plan[1].LocalTime == noon.Date.AddHours(21.5) &&
                plan[2].Body.Contains("goal") && plan[2].Body.Contains("30"),
                "Untrained-day reminder plan wrong.");
            // Trained today: no "fading" and no duel hour tonight, one tomorrow; today's goal says what is left.
            plan = ReminderPlanner.Plan(history, new DailyGoalStatus("2026-10-18", 30, 10), 4, noon, msk);
            Require(plan.Count == 4 && Count(plan, ReminderKind.StreakSaver) == 1 && Count(plan, ReminderKind.DuelHour) == 2 &&
                plan[0].Kind == ReminderKind.DailyGoal && plan[0].Body.Contains("left") && plan[0].Body.Contains("20") &&
                plan.Exists(r => r.Kind == ReminderKind.StreakSaver && r.LocalTime.Date == noon.Date.AddDays(1)),
                "Trained-day reminder plan wrong.");
            // Goal done: nothing more today.
            plan = ReminderPlanner.Plan(history, new DailyGoalStatus("2026-10-18", 30, 35), 1, noon, msk);
            Require(plan.Count == 2 && plan.TrueForAll(r => r.LocalTime.Date > noon.Date), "Reminder after a finished goal.");
            // Past the habitual hour: today's goal reminder is gone, never fired late.
            plan = ReminderPlanner.Plan(history, new DailyGoalStatus("2026-10-18", 30, 0), 0, noon.Date.AddHours(20), msk);
            Require(plan.Count == 2 && plan.TrueForAll(r => r.LocalTime > noon.Date.AddHours(20)), "Late reminder scheduled in the past.");
            // Never more than two a day.
            plan = ReminderPlanner.Plan(history, new DailyGoalStatus("2026-10-18", 30, 0), 40, noon, msk);
            var perDay = new Dictionary<DateTime, int>();
            foreach (var r in plan) perDay[r.LocalTime.Date] = perDay.TryGetValue(r.LocalTime.Date, out int n) ? n + 1 : 1;
            foreach (var n in perDay.Values) Require(n <= 2, "More than two reminders in a day.");
        }

        private static MatchRecord Match(int reps, DateTime utc) => new MatchRecord {
            Mode = "ghost", Exercise = "pushups", MyReps = reps, CreatedAt = DateTime.SpecifyKind(utc, DateTimeKind.Utc)
        };

        private static LocalWorkout Workout(string id, DateTime utc) => new LocalWorkout {
            Id = id, Mode = "training", Reps = 10, Xp = 100, DurationSec = 60,
            UtcTicks = DateTime.SpecifyKind(utc, DateTimeKind.Utc).Ticks
        };

        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
