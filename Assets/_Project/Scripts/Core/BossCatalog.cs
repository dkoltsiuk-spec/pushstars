using System.Collections.Generic;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// A scripted PvE opponent for the boss duel (phase 08.9). The rep timeline is a fixed list of
    /// timestamps (seconds from the duel start) — deliberately bursty, so the boss reads as a live
    /// opponent doing sets with pauses, not a metronome. Lives in Core (no scene dependencies) so
    /// both the fight scene and the search overlay (PushStars.UI) can read it without an assembly
    /// cycle. In phase 12.5 the boss becomes just one implementation behind the same opponent seam
    /// as ghost recordings and pacer bots.
    /// </summary>
    public sealed class BossProfile
    {
        public string Id { get; }
        /// <summary>Never shown as "bot" — the boss is an explicit PvE character.</summary>
        public string DisplayName { get; }
        public int MaxHp { get; }
        public string PrefabId { get; }
        /// <summary>Rep timestamps in seconds from duel start, ascending, all &lt; duel duration.</summary>
        public IReadOnlyList<float> RepTimes { get; }

        public BossProfile(string id, string displayName, IReadOnlyList<float> repTimes, int maxHp = 1000, string prefabId = null)
        {
            Id = id;
            DisplayName = displayName;
            RepTimes = repTimes;
            MaxHp = maxHp;
            PrefabId = prefabId ?? id;
        }

        /// <summary>Builds a timeline from bursts: (start, reps, secondsBetweenReps). Reps that would
        /// land past the duel duration are dropped so the advertised total is honest.</summary>
        public static BossProfile FromBursts(string id, string name, float durationSec,
                                             params (float start, int reps, float interval)[] bursts)
        {
            var times = new List<float>();
            foreach (var (start, reps, interval) in bursts)
                for (int i = 0; i < reps; i++)
                {
                    float t = start + i * interval;
                    if (t <= durationSec) times.Add(t);
                }
            times.Sort();
            return new BossProfile(id, name, times);
        }
    }

    /// <summary>
    /// The boss ladder and the player's progress on it. Progress is a PlayerPrefs int (index of the
    /// current boss): beating the current boss advances to the next, losing keeps you where you are,
    /// beating the last one keeps re-matching them. Server sync of this flag comes with phases 11.5+.
    /// </summary>
    public static class BossCatalog
    {
        private const string ProgressKey = "boss_progress";

        private static BossProfile Goblin(string id, int hp, bool final = false)
        {
            var timeline = BossProfile.FromBursts(id, "", FightConfig.DuelDurationSec,
                (3f, 4, 2.2f), (18f, 4, 2.4f), (34f, 3, 2.6f), (49f, 4, 2.5f));
            return new BossProfile(id, final ? "BOSS GOBLIN KING" : "BOSS GOBLIN ARCH",
                timeline.RepTimes, hp, final ? "goblin-king" : "novice");
        }

        public static BossProfile Find(string id)
        {
            foreach (var boss in Bosses) if (boss.Id == id) return boss;
            return null;
        }

        /// <summary>Five forest fights. +125 HP adds 1–2 reps; the finale adds 200 HP (2–3 reps).</summary>
        public static readonly IReadOnlyList<BossProfile> Bosses = new[]
        {
            Goblin("novice", 450),
            Goblin("athlete", 575),
            Goblin("champion", 700),
            Goblin("goblin-guard", 825),
            Goblin("goblin-king", 1025, true),
        };

        public static int CurrentIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ProgressKey, 0), 0, Bosses.Count - 1);
            private set { PlayerPrefs.SetInt(ProgressKey, value); PlayerPrefs.Save(); }
        }

        public static BossProfile Current => Bosses[CurrentIndex];

        /// <summary>Call once per finished duel. A win unlocks the next boss (the last one repeats).</summary>
        public static void ReportResult(bool won)
        {
            if (won && CurrentIndex < Bosses.Count - 1)
                CurrentIndex = CurrentIndex + 1;
        }
    }
}
