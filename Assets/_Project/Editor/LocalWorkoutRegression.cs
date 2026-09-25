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
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/local-user-data-regression.txt", "PASS: new user, assessment, replay, retest, training sets, ghost, draw, loss, restart, detached history, legacy migration, write failure, unsupported save, trophy floor, league thresholds.\n");
            Debug.Log("[LocalWorkoutRegression] PASS");
        }

        private static LocalWorkout Workout(string id, string mode, int reps, long xp) => new LocalWorkout {
            Id = id, Mode = mode, Reps = reps, Xp = xp, UtcTicks = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc).Ticks,
            DurationSec = 60
        };
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    }
}
