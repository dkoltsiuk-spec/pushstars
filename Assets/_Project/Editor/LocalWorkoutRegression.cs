using System;
using System.IO;
using PushStars.Core;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    public static class LocalWorkoutRegression
    {
        [MenuItem("Tools/Push Stars/Validate Local User Data")]
        public static void Run()
        {
            string saved = null;
            int writes = 0;
            Action<string> persist = json => { saved = json; writes++; };
            var ledger = new LocalWorkoutLedger(null, persist);
            Require(ledger.Snapshot.Xp == 0 && ledger.History.Count == 0, "New user contains demo progress.");
            ledger.Record(Workout("empty", "assessment", 0, 0));
            Require(!ledger.Snapshot.Seeded && writes == 0, "Empty assessment was stored.");
            ledger.Record(Workout("test", "assessment", 20, 200));
            Require(ledger.Snapshot.Trophies == 150 && ledger.Snapshot.TotalReps == 20, "Assessment does not seed progress.");
            ledger = new LocalWorkoutLedger(saved, persist);
            ledger.Record(Workout("test", "assessment", 20, 200));
            Require(ledger.Snapshot.Xp == 200 && writes == 1, "Replay awarded XP twice after restart.");
            ledger.Record(Workout("test2", "assessment", 50, 500));
            Require(ledger.Snapshot.Trophies == 150 && ledger.Snapshot.BestReps == 50, "Retest reseeded trophies.");
            ledger.Record(Workout("training:set:1", "training", 10, 100));
            ledger.Record(Workout("training:set:2", "training", 12, 120));
            Require(ledger.Snapshot.Wins == 0 && ledger.Snapshot.Losses == 0 && ledger.Snapshot.TotalReps == 92,
                "Training changed competitive results or lost a set.");
            var win = Workout("ghost", "ghost", 22, 220); win.Won = true; win.OpponentReps = 20;
            Require(ledger.Record(win) == EconomyConfig.TrophyGhostWin, "Ghost trophy award incorrect.");
            var draw = Workout("draw", "boss", 20, 200); draw.Draw = true; draw.OpponentReps = 20;
            Require(ledger.Record(draw) == 0 && ledger.Snapshot.WinStreak == 1 && ledger.Snapshot.Losses == 0,
                "Draw counted as defeat.");
            ledger.Record(Workout("loss", "boss", 10, 100));
            Require(ledger.Snapshot.Wins == 1 && ledger.Snapshot.Losses == 1 && ledger.Snapshot.WinStreak == 0,
                "Win/loss/streak statistics incorrect.");
            ledger = new LocalWorkoutLedger(saved, persist);
            Require(ledger.History.Count == 7 && ledger.Snapshot.TotalReps == 144 && ledger.Snapshot.Xp == 1440,
                "Restart lost history, reps or XP.");
            Require(ledger.History.Exists(m => m.Draw) && ledger.History.Exists(m => m.IsSolo), "History lost outcome types.");
            var detached = ledger.History; detached.Clear();
            Require(ledger.History.Count == 7, "UI can mutate saved history.");

            var legacy = new LocalProgress { Trophies = 955, Xp = 12345, TotalReps = 300, BestReps = 35, Wins = 8, Losses = 2, Seeded = true };
            var migrated = new LocalWorkoutLedger(null, persist, legacy);
            migrated.Record(Workout("new", "training", 10, 100));
            migrated = new LocalWorkoutLedger(saved, persist, legacy);
            Require(migrated.Snapshot.Trophies == 955 && migrated.Snapshot.Xp == 12445 && migrated.Snapshot.TotalReps == 310 &&
                migrated.Snapshot.Wins == 8 && migrated.History.Count == 1, "Migration lost or fabricated history.");

            var failure = new LocalWorkoutLedger(saved, _ => throw new IOException("Disk full"));
            try { failure.Record(Workout("failed", "training", 10, 100)); throw new Exception("Write failure swallowed."); }
            catch (IOException) { }
            Require(failure.Snapshot.TotalReps == 310 && failure.History.Count == 1, "Failed write committed memory.");
            bool rejected = false;
            try { new LocalWorkoutLedger("{\"Version\":99}", persist); } catch (Exception) { rejected = true; }
            Require(rejected, "Unsupported save accepted.");
            foreach (string invalid in new[] { "{}", " ", "not json", "{\"Version\":1,\"Xp\":-1}" })
            {
                rejected = false;
                try { new LocalWorkoutLedger(invalid, persist); } catch (Exception) { rejected = true; }
                Require(rejected, "Malformed save accepted: " + invalid);
            }
            var zero = new LocalWorkoutLedger(null, persist);
            Require(zero.Record(Workout("zero-loss", "ghost", 0, 0)) == 0 && zero.Snapshot.Losses == 1,
                "Loss below zero trophies was mishandled.");
            Require(Leagues.ForTrophies(399).Id == "bronze" && Leagues.ForTrophies(400).Id == "silver" &&
                Leagues.Progress(1200) == 1, "League thresholds differ from actual trophies.");
            ValidateDailyStreak();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/local-user-data-regression.txt", "PASS: new user, assessment, replay, retest, training sets, ghost, draw, loss, restart, detached history, legacy migration, write failure, unsupported save, trophy floor, league thresholds.\n");
            Debug.Log("[LocalWorkoutRegression] PASS");
        }

        private static LocalWorkout Workout(string id, string mode, int reps, long xp) => new LocalWorkout {
            Id = id, Mode = mode, Reps = reps, Xp = xp, UtcTicks = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc).Ticks,
            DurationSec = 60
        };

        private static void ValidateDailyStreak()
        {
            string saved = null;
            int writes = 0;
            Action<string> persist = json => { saved = json; writes++; };
            var ledger = new LocalWorkoutLedger(null, persist);
            LocalWorkout Day(string id, int day, string mode = "ghost", bool won = true, bool draw = false)
            {
                var workout = Workout(id, mode, 12, 120);
                workout.UtcTicks += day * TimeSpan.TicksPerDay;
                workout.Won = won; workout.Draw = draw;
                return workout;
            }
            Require(ledger.Record(Day("day1", 0)) == EconomyConfig.TrophyGhostWin, "First day granted a streak bonus.");
            Require(ledger.Record(Day("day2", 1)) == EconomyConfig.TrophyGhostWin + 1, "Second day missing +1 trophy.");
            Require(ledger.FindWorkout("day2").StreakBonusTrophies == 1 && ledger.Snapshot.DailyStreak == 2,
                "Streak receipt does not match awarded trophies.");
            ledger = new LocalWorkoutLedger(saved, persist);
            int before = ledger.Snapshot.Trophies, savedWrites = writes;
            Require(ledger.Record(Day("day2", 1)) == EconomyConfig.TrophyGhostWin + 1 &&
                ledger.Snapshot.Trophies == before && writes == savedWrites, "Replaying a receipt paid the streak twice.");
            ledger.Record(Day("same-day", 1));
            Require(ledger.Snapshot.DailyStreak == 2, "Multiple fights advanced the day streak.");
            Require(ledger.Record(Day("day3", 2, "pvp")) == EconomyConfig.TrophyWin + 2, "Third day missing +2 trophies.");
            Require(ledger.Record(Day("boss", 2, "boss")) == 0 && ledger.FindWorkout("boss").StreakBonusTrophies == 0,
                "Boss granted trophy bonus.");
            Require(ledger.Record(Day("draw", 2, "ghost", false, true)) == 0, "Draw granted trophy bonus.");
            Require(ledger.Record(Day("loss", 2, "ghost", false)) == -EconomyConfig.TrophyGhostLoss &&
                ledger.FindWorkout("loss").StreakBonusTrophies == 0 && ledger.Snapshot.DailyStreak == 3,
                "Loss paid a bonus or reset the active-day streak.");
            Require(ledger.Record(Day("gap", 4)) == EconomyConfig.TrophyGhostWin && ledger.Snapshot.DailyStreak == 1,
                "Missed day did not reset the streak.");
            Require(ledger.Record(Day("training", 5, "training", false)) == 0 && ledger.Snapshot.DailyStreak == 2,
                "Training should extend active days without awarding trophies.");
            var empty = Day("empty", 6, "training", false); empty.Reps = 0;
            ledger.Record(empty);
            Require(ledger.Snapshot.DailyStreak == 2, "Empty workout extended the streak.");
            var old = new LocalProgress { Workouts = new System.Collections.Generic.List<LocalWorkout> {
                Day("old2", 1), Day("old1", 0) } };
            var migrated = new LocalWorkoutLedger(JsonUtility.ToJson(old), _ => { });
            Require(migrated.Snapshot.DailyStreak == 2 && migrated.Snapshot.Trophies == 0 &&
                migrated.Record(Day("after-migration", 2)) == EconomyConfig.TrophyGhostWin + 2,
                "Old workout history did not restore daily streak safely.");
            var failure = new LocalWorkoutLedger(saved, _ => throw new IOException("Disk full"));
            before = failure.Snapshot.DailyStreak;
            try { failure.Record(Day("failed-day", 6)); throw new Exception("Write failure swallowed."); }
            catch (IOException) { }
            Require(failure.Snapshot.DailyStreak == before, "Failed save committed daily streak.");
            Debug.Log("[LocalWorkoutRegression] Daily streak: PASS");
        }
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
