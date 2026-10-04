using System;
using System.Collections.Generic;
using System.Linq;
using PushStars.Core;

namespace PushStars.UI
{
    public static class ProfileAchievementCatalog
    {
        public enum Metric { FirstSet, TotalReps, BestSet, Wins, ActiveDays, Modes, BossWins }

        public sealed class Entry
        {
            public readonly string Id, Title, Description;
            public readonly Metric ProgressMetric;
            public readonly long Goal;
            public Entry(string id, string title, string description, Metric metric, long goal)
            { Id=id; Title=title; Description=description; ProgressMetric=metric; Goal=goal; }
        }

        // Stable order: the first eight form the profile's compact starter collection.
        public static readonly Entry[] Entries = {
            new Entry("first-set", "FIRST SET", "Complete your first set with at least one repetition.", Metric.FirstSet, 1),
            new Entry("reps-100", "FIRST HUNDRED", "Complete 100 repetitions across all workouts.", Metric.TotalReps, 100),
            new Entry("set-10", "STRONG START", "Complete 10 repetitions in one set.", Metric.BestSet, 10),
            new Entry("win-1", "FIRST VICTORY", "Win your first battle or boss fight.", Metric.Wins, 1),
            new Entry("days-5", "FIND YOUR RHYTHM", "Train on 5 different days. Rest days do not reset progress.", Metric.ActiveDays, 5),
            new Entry("reps-500", "GAINING MOMENTUM", "Complete 500 repetitions across all workouts.", Metric.TotalReps, 500),
            new Entry("set-25", "POWER SET", "Complete 25 repetitions in one set.", Metric.BestSet, 25),
            new Entry("wins-10", "TEN VICTORIES", "Win 10 battles or boss fights in total.", Metric.Wins, 10),
            new Entry("reps-1000", "THOUSAND CLUB", "Complete 1,000 repetitions across all workouts.", Metric.TotalReps, 1000),
            new Entry("set-50", "IRON ARMS", "Complete 50 repetitions in one set.", Metric.BestSet, 50),
            new Entry("days-20", "BUILT A HABIT", "Train on 20 different days. Rest days do not reset progress.", Metric.ActiveDays, 20),
            new Entry("modes-3", "ALL-ROUNDER", "Complete a training set, a battle and a boss fight with at least one repetition each.", Metric.Modes, 3),
            new Entry("boss-1", "BOSS BREAKER", "Defeat a boss for the first time.", Metric.BossWins, 1),
            new Entry("wins-50", "ARENA VETERAN", "Win 50 battles or boss fights in total.", Metric.Wins, 50),
            new Entry("reps-5000", "FIVE THOUSAND", "Complete 5,000 repetitions across all workouts.", Metric.TotalReps, 5000),
            new Entry("wins-100", "CHAMPION", "Win 100 battles or boss fights in total.", Metric.Wins, 100)
        };

        public static long[] Progress(UserProfile profile, IReadOnlyList<MatchRecord> history, int savedBest, DateTime today)
        {
            var days = new HashSet<DateTime>();
            var modes = new HashSet<string>();
            var receipts = new HashSet<string>();
            long historyReps=0, wins=0, bossWins=0;
            int best=Math.Max(0,savedBest);
            foreach(var match in history)
            {
                if(match==null || match.MyReps<=0 || (!string.IsNullOrEmpty(match.Exercise) && match.Exercise!="pushups")) continue;
                var day=match.CreatedAt.ToLocalTime().Date;
                if(day>today.Date) continue;
                if(!string.IsNullOrEmpty(match.MatchId) && !receipts.Add(match.MatchId)) continue;
                days.Add(day); historyReps+=match.MyReps; best=Math.Max(best,match.MyReps);
                if(match.Mode=="training") modes.Add("training");
                else if(match.Mode=="pvp" || match.Mode=="ghost") modes.Add("battle");
                else if(match.Mode=="boss") modes.Add("boss");
                if((match.Mode=="pvp" || match.Mode=="ghost" || match.Mode=="boss") && match.Won && !match.Draw)
                { wins++; if(match.Mode=="boss") bossWins++; }
            }
            // The aggregate also retains progress imported from older device saves.
            long reps=Math.Max(Math.Max(0,profile.TotalReps),historyReps);
            wins=Math.Max(wins,Math.Max(0,profile.TotalWins));
            var values=new long[Entries.Length];
            for(int i=0;i<Entries.Length;i++)
            {
                switch(Entries[i].ProgressMetric)
                {
                    case Metric.FirstSet: values[i]=reps>0 || best>0 ? 1 : 0; break;
                    case Metric.TotalReps: values[i]=reps; break;
                    case Metric.BestSet: values[i]=best; break;
                    case Metric.Wins: values[i]=wins; break;
                    case Metric.ActiveDays: values[i]=days.Count; break;
                    case Metric.Modes: values[i]=modes.Count; break;
                    case Metric.BossWins: values[i]=bossWins; break;
                }
            }
            return values;
        }

        // Curated difficulty, not a statistical rarity claim. Suppress lower stages of the same metric.
        private static readonly int[] Prestige={1,12,10,8,15,25,28,30,40,55,45,35,32,65,70,90};
        public static int[] BestEarned(IReadOnlyList<long> progress,int limit=3)
        {
            var used=new HashSet<Metric>();
            return Enumerable.Range(0,Math.Min(progress.Count,Entries.Length))
                .Where(i=>progress[i]>=Entries[i].Goal).OrderByDescending(i=>Prestige[i])
                .Where(i=>used.Add(Entries[i].ProgressMetric)).Take(Math.Max(0,Math.Min(3,limit))).ToArray();
        }
    }
}
