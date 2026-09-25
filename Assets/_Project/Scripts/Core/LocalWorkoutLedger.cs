using System;
using System.Collections.Generic;
using UnityEngine;

namespace PushStars.Core
{
    [Serializable]
    public sealed class LocalWorkout
    {
        public string Id, Mode, OpponentName;
        public int Reps, OpponentReps, TrophyDelta, DurationSec;
        public long Xp, UtcTicks;
        public bool Won, Draw, IsRecord;
    }

    [Serializable]
    public sealed class LocalProgress
    {
        public int Version = 1;
        public int Trophies, BestReps, TotalReps, Wins, Losses, WinStreak;
        public long Xp;
        public bool Seeded;
        public List<LocalWorkout> Workouts = new List<LocalWorkout>();
    }

    /// <summary>One completion receipt atomically saves XP, statistics and history.</summary>
    public sealed class LocalWorkoutLedger
    {
        [Serializable] private sealed class SaveHeader { public int Version; }
        private LocalProgress _state;
        private readonly Action<string> _save;
        public LocalProgress Snapshot => new LocalProgress {
            Trophies = _state.Trophies, BestReps = _state.BestReps, TotalReps = _state.TotalReps,
            Wins = _state.Wins, Losses = _state.Losses, WinStreak = _state.WinStreak,
            Xp = _state.Xp, Seeded = _state.Seeded
        };

        public LocalWorkoutLedger(string json, Action<string> save, LocalProgress legacy = null)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
            if (!string.IsNullOrEmpty(json) && JsonUtility.FromJson<SaveHeader>(json)?.Version != 1)
                throw new InvalidOperationException("Unsupported local workout save. The original save has been preserved.");
            _state = string.IsNullOrEmpty(json) ? Clone(legacy ?? new LocalProgress()) : JsonUtility.FromJson<LocalProgress>(json);
            Validate(_state);
        }

        public List<MatchRecord> History
        {
            get
            {
                var rows = new List<MatchRecord>();
                foreach (var w in _state.Workouts)
                    rows.Add(new MatchRecord {
                        MatchId = w.Id, Mode = w.Mode, Exercise = "pushups", OpponentName = w.OpponentName,
                        MyReps = w.Reps, OpponentReps = w.OpponentReps, Won = w.Won, Draw = w.Draw,
                        TrophyDelta = w.TrophyDelta, DurationSec = w.DurationSec, IsRecord = w.IsRecord,
                        CreatedAt = new DateTime(w.UtcTicks, DateTimeKind.Utc)
                    });
                rows.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
                return rows;
            }
        }

        public int Record(LocalWorkout workout)
        {
            ValidateWorkout(workout);
            foreach (var previous in _state.Workouts)
                if (previous.Id == workout.Id) return previous.TrophyDelta;
            if (workout.Reps == 0 && (workout.Mode == "training" || workout.Mode == "assessment")) return 0;

            var next = Clone(_state);
            var record = JsonUtility.FromJson<LocalWorkout>(JsonUtility.ToJson(workout));
            record.IsRecord = record.Reps > next.BestReps;
            next.BestReps = Math.Max(next.BestReps, record.Reps);
            checked { next.TotalReps += record.Reps; next.Xp += record.Xp; }
            int before = next.Trophies;
            if (record.Mode == "assessment" && !next.Seeded)
            {
                next.Trophies = FitnessTest.StartingTrophiesFor(FitnessTest.TierFor(record.Reps));
                next.Seeded = true;
            }
            else if ((record.Mode == "ghost" || record.Mode == "boss" || record.Mode == "pvp") && !record.Draw)
            {
                bool ghost = record.Mode == "ghost";
                if (record.Won) { checked { next.Wins++; next.WinStreak++; } }
                else { checked { next.Losses++; } next.WinStreak = 0; }
                int delta = record.Won ? (ghost ? EconomyConfig.TrophyGhostWin : EconomyConfig.TrophyWin)
                    : -(ghost ? EconomyConfig.TrophyGhostLoss : EconomyConfig.TrophyLoss);
                next.Trophies = Math.Max(0, checked(next.Trophies + delta));
            }
            record.TrophyDelta = next.Trophies - before;
            next.Workouts.Add(record);
            Commit(next);
            return record.TrophyDelta;
        }

        public void AddXp(long xp)
        {
            if (xp <= 0) return;
            var next = Clone(_state);
            checked { next.Xp += xp; }
            Commit(next);
        }

        private void Commit(LocalProgress next)
        {
            _save(JsonUtility.ToJson(next));
            _state = next;
        }
        private static LocalProgress Clone(LocalProgress state) => JsonUtility.FromJson<LocalProgress>(JsonUtility.ToJson(state));
        private static void Validate(LocalProgress state)
        {
            if (state == null || state.Version != 1 || state.Workouts == null || state.Xp < 0 ||
                state.Trophies < 0 || state.BestReps < 0 || state.TotalReps < 0 || state.Wins < 0 || state.Losses < 0 || state.WinStreak < 0)
                throw new InvalidOperationException("Invalid local workout save. The original save has been preserved.");
            var ids = new HashSet<string>();
            foreach (var w in state.Workouts)
            {
                ValidateWorkout(w);
                if (!ids.Add(w.Id)) throw new InvalidOperationException("Duplicate workout in local save.");
            }
        }
        private static void ValidateWorkout(LocalWorkout w)
        {
            if (w == null || string.IsNullOrWhiteSpace(w.Id) || w.Reps < 0 || w.OpponentReps < 0 || w.Xp < 0 ||
                w.DurationSec < 0 || w.UtcTicks <= 0 || w.UtcTicks > DateTime.MaxValue.Ticks ||
                (w.Mode != "training" && w.Mode != "assessment" && w.Mode != "boss" && w.Mode != "ghost" && w.Mode != "pvp") ||
                (w.Won && w.Draw)) throw new ArgumentException("Invalid workout receipt.");
        }
    }
}
