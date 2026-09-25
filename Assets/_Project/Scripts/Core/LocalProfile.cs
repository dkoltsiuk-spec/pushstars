using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>Device progress. Cloud authentication does not replace this unsynced save.</summary>
    public static class LocalProfile
    {
        private static LocalWorkoutLedger _ledger;
        private static string SavePath => Path.Combine(Application.persistentDataPath, "workouts_v1.json");
        private static LocalWorkoutLedger Ledger => _ledger ?? (_ledger = new LocalWorkoutLedger(
            File.Exists(SavePath) ? File.ReadAllText(SavePath) : null, Save, LegacySnapshot()));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reload() => _ledger = null;

        public static int Trophies => Ledger.Snapshot.Trophies;
        public static int BestReps => Ledger.Snapshot.BestReps;
        public static int TotalReps => Ledger.Snapshot.TotalReps;
        public static int Wins => Ledger.Snapshot.Wins;
        public static int Losses => Ledger.Snapshot.Losses;
        public static int WinStreak => Ledger.Snapshot.WinStreak;
        public static long Xp => Ledger.Snapshot.Xp;
        public static int Games => Wins + Losses;
        public static int WinRatePercent => Games > 0 ? Mathf.RoundToInt(100f * Wins / Games) : 0;
        public static League League => Leagues.ForTrophies(Trophies);
        public static List<MatchRecord> History => Ledger.History;

        public static UserProfile Profile => new UserProfile {
            Exists = true, Trophies = Trophies, Rank = League.Id, Xp = Xp,
            TotalWins = Wins, TotalLosses = Losses, TotalReps = TotalReps, WinStreak = WinStreak
        };

        public static int RecordWorkout(string id, string mode, int reps, long xp, int opponentReps = 0,
            string opponentName = null, bool win = false, bool draw = false, int durationSec = 60)
            => Ledger.Record(new LocalWorkout {
                Id = id, Mode = mode, Reps = reps, Xp = xp, OpponentReps = opponentReps,
                OpponentName = opponentName, Won = win, Draw = draw, DurationSec = durationSec,
                UtcTicks = DateTime.UtcNow.Ticks
            });

        public static void AddXp(long xp) => Ledger.AddXp(xp);

        private static LocalProgress LegacySnapshot() => new LocalProgress {
            Trophies = Mathf.Max(0, PlayerPrefs.GetInt("profile.trophies", 0)),
            BestReps = Mathf.Max(0, PlayerPrefs.GetInt("profile.best_reps", 0)),
            TotalReps = Mathf.Max(0, PlayerPrefs.GetInt("profile.total_reps", 0)),
            Wins = Mathf.Max(0, PlayerPrefs.GetInt("profile.wins", 0)),
            Losses = Mathf.Max(0, PlayerPrefs.GetInt("profile.losses", 0)),
            Seeded = PlayerPrefs.GetInt("profile.seeded", 0) != 0,
            Xp = long.TryParse(PlayerPrefs.GetString("pending_xp", "0"), out var xp) ? Math.Max(0, xp) : 0
        };

        private static void Save(string json)
        {
            string path = SavePath, temp = path + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(temp, json);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        /// <summary>Explicit debug reset, never invoked during migration or sign-in.</summary>
        public static void Reset()
        {
            Save(JsonUtility.ToJson(new LocalProgress()));
            _ledger = null;
        }
    }
}
