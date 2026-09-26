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
        /// <summary>Player HP removed by each rep on <see cref="RepTimes"/>.</summary>
        public int AttackDamage { get; }

        public BossProfile(string id, string displayName, IReadOnlyList<float> repTimes, int maxHp = 1000,
                           string prefabId = null, int attackDamage = 75)
        {
            Id = id;
            DisplayName = displayName;
            RepTimes = repTimes;
            MaxHp = maxHp;
            PrefabId = prefabId ?? id;
            AttackDamage = attackDamage;
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
    /// One island of the boss map: its bosses in order, a gem islet reached after
    /// <see cref="GemsAfterBoss"/> and a case chest after the island's main (last) boss.
    /// Unplayable chapters are authored ahead of their 3D models and stay COMING SOON: they are
    /// not part of the <see cref="BossCatalog.Bosses"/> ladder until <see cref="Playable"/> is set.
    /// </summary>
    public sealed class BossChapter
    {
        public string Id { get; }
        public string Title { get; }
        public bool Playable { get; }
        public IReadOnlyList<BossProfile> Bosses { get; }
        /// <summary>Chapter-local index of the boss whose defeat opens the gem islet.</summary>
        public int GemsAfterBoss { get; }
        public int GemReward { get; }
        public string GemReceipt => "boss-map:" + Id + ":gems:v1";
        public string ChestReceipt => "boss-map:" + Id + ":chest:v1";

        public BossChapter(string id, string title, bool playable, int gemsAfterBoss, int gemReward,
                           params BossProfile[] bosses)
        {
            Id = id; Title = title; Playable = playable;
            GemsAfterBoss = gemsAfterBoss; GemReward = gemReward; Bosses = bosses;
        }
    }

    /// <summary>
    /// The boss ladder and the player's progress on it. Progress is a PlayerPrefs int counting
    /// defeated ladder bosses: beating the current boss advances to the next, losing keeps you where
    /// you are, and after the last playable boss it keeps re-matching them. Because the count can
    /// reach <see cref="Bosses"/>.Count, a player who has cleared everything moves straight on to
    /// the next island the moment it becomes playable. Server sync comes with phases 11.5+.
    /// </summary>
    public static class BossCatalog
    {
        private const string ProgressKey = "boss_progress";

        /// <summary>One bursty 15-hit timeline; at 75 damage the player falls on hit 14 (~54 s).</summary>
        private static readonly (float, int, float)[] StandardBursts =
            { (3f, 4, 2.2f), (18f, 4, 2.4f), (34f, 3, 2.6f), (49f, 4, 2.5f) };

        private static BossProfile Boss(string id, string name, int hp, string prefabId = null, int attackDamage = 75)
        {
            var timeline = BossProfile.FromBursts(id, name, FightConfig.DuelDurationSec, StandardBursts);
            return new BossProfile(id, name, timeline.RepTimes, hp, prefabId ?? id, attackDamage);
        }

        private static BossProfile Goblin(string id, int hp, bool final = false)
            => Boss(id, final ? "BOSS GOBLIN KING" : "BOSS GOBLIN ARCH", hp, final ? "goblin-king" : "novice");

        /// <summary>
        /// Perfect-form kills — forest: 5 / 6 / 7 / 9 / 11 (+125 HP per step, +200 for the king);
        /// ice: 12 / 14 / 15 / 17 / 19; lava: 20 / 22 / 24 / 26 / 28 / 30, where each hit costs 80 HP
        /// so the player falls on hit 13 (~51.5 s). IDs of COMING SOON bosses name their future
        /// prefabs in Resources/Bosses. The gem islet opens after the third boss of each island.
        /// </summary>
        public static readonly IReadOnlyList<BossChapter> Chapters = new[]
        {
            new BossChapter("forest", "FOREST ISLAND", true, 2, 50,
                Goblin("novice", 450),
                Goblin("athlete", 575),
                Goblin("champion", 700),
                Goblin("goblin-guard", 825),
                Goblin("goblin-king", 1025, true)),
            new BossChapter("ice", "ICE ISLAND", false, 2, 80,
                Boss("ice-golem-1", "BOSS FROST GOLEM", 1200),
                Boss("ice-golem-2", "BOSS FROST GOLEM", 1400),
                Boss("ice-golem-3", "BOSS FROST GOLEM", 1500),
                Boss("ice-golem-4", "BOSS FROST GOLEM", 1700),
                Boss("ice-golem-king", "BOSS GLACIER KING", 1900)),
            new BossChapter("lava", "LAVA ISLAND", false, 2, 120,
                Boss("lava-golem-1", "BOSS MAGMA GOLEM", 2000, attackDamage: 80),
                Boss("lava-golem-2", "BOSS MAGMA GOLEM", 2200, attackDamage: 80),
                Boss("lava-golem-3", "BOSS MAGMA GOLEM", 2400, attackDamage: 80),
                Boss("lava-golem-4", "BOSS MAGMA GOLEM", 2600, attackDamage: 80),
                Boss("lava-golem-5", "BOSS MAGMA GOLEM", 2800, attackDamage: 80),
                Boss("lava-golem-king", "BOSS VOLCANO KING", 3000, attackDamage: 80)),
        };

        /// <summary>The playable ladder: every boss of every playable chapter, in map order.</summary>
        public static readonly IReadOnlyList<BossProfile> Bosses = BuildLadder();

        private static IReadOnlyList<BossProfile> BuildLadder()
        {
            var ladder = new List<BossProfile>();
            foreach (var chapter in Chapters)
            {
                if (!chapter.Playable) break; // islands unlock in order; a COMING SOON island ends the ladder
                ladder.AddRange(chapter.Bosses);
            }
            return ladder;
        }

        public static BossProfile Find(string id)
        {
            foreach (var chapter in Chapters)
                foreach (var boss in chapter.Bosses) if (boss.Id == id) return boss;
            return null;
        }

        public static BossChapter FindChapter(string chapterId)
        {
            foreach (var chapter in Chapters) if (chapter.Id == chapterId) return chapter;
            return null;
        }

        public static BossChapter ChapterOf(string bossId)
        {
            foreach (var chapter in Chapters)
                foreach (var boss in chapter.Bosses) if (boss.Id == bossId) return chapter;
            return null;
        }

        /// <summary>Ladder index of the chapter's first boss (chapters are contiguous on the ladder).</summary>
        public static int LadderStart(BossChapter chapter)
        {
            int start = 0;
            foreach (var c in Chapters)
            {
                if (c == chapter) return start;
                start += c.Bosses.Count;
            }
            return -1;
        }

        /// <summary>1-based position of a boss inside its island (difficulty stars).</summary>
        public static int StageInChapter(string bossId)
        {
            var chapter = ChapterOf(bossId);
            if (chapter == null) return 1;
            for (int i = 0; i < chapter.Bosses.Count; i++) if (chapter.Bosses[i].Id == bossId) return i + 1;
            return 1;
        }

        /// <summary>How many ladder bosses have been defeated (0..<see cref="Bosses"/>.Count).</summary>
        public static int ClearedCount
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(ProgressKey, 0), 0, Bosses.Count);
            private set { PlayerPrefs.SetInt(ProgressKey, value); PlayerPrefs.Save(); }
        }

        public static int CurrentIndex => Mathf.Min(ClearedCount, Bosses.Count - 1);

        public static BossProfile Current => Bosses[CurrentIndex];

        public static bool IsCleared(int ladderIndex) => ladderIndex < ClearedCount;

        /// <summary>Every boss of a playable chapter has been defeated: its chest is open.</summary>
        public static bool IsChapterCleared(BossChapter chapter)
            => chapter != null && chapter.Playable && ClearedCount >= LadderStart(chapter) + chapter.Bosses.Count;

        /// <summary>The boss before a chapter's gem islet has been defeated.</summary>
        public static bool IsGemIsletReached(BossChapter chapter)
            => chapter != null && chapter.Playable && ClearedCount > LadderStart(chapter) + chapter.GemsAfterBoss;

        /// <summary>Call once per finished duel. A win over the current boss unlocks the next one;
        /// repeat wins over the last playable boss change nothing.</summary>
        public static void ReportResult(bool won)
        {
            if (won && ClearedCount < Bosses.Count)
                ClearedCount = ClearedCount + 1;
        }
    }
}
