namespace PushStars.Core
{
    /// <summary>A competitive league tier defined by an inclusive trophy range.</summary>
    public readonly struct League
    {
        public readonly string Id;          // matches UserProfile.Rank ("bronze" | "silver" | "gold" | "diamond")
        public readonly string DisplayName;
        public readonly long   MinTrophies;
        public readonly long   MaxTrophies; // long.MaxValue for the top league

        public League(string id, string displayName, long min, long max)
        {
            Id = id; DisplayName = displayName; MinTrophies = min; MaxTrophies = max;
        }

        public bool IsTop => MaxTrophies == long.MaxValue;
    }

    /// <summary>
    /// The MVP league ladder (4 tiers), mirroring the thresholds in <c>docs/architecture/constants.md</c>.
    /// Pure lookup, no Unity dependency.
    /// </summary>
    public static class Leagues
    {
        public static readonly League Bronze  = new League("bronze",  "Bronze",  0,    399);
        public static readonly League Silver  = new League("silver",  "Silver",  400,  799);
        public static readonly League Gold    = new League("gold",    "Gold",    800,  1199);
        public static readonly League Diamond = new League("diamond", "Diamond", 1200, long.MaxValue);

        /// <summary>Ordered low → high.</summary>
        public static readonly League[] All = { Bronze, Silver, Gold, Diamond };

        public static League ForTrophies(long trophies)
        {
            for (int i = All.Length - 1; i >= 0; i--)
                if (trophies >= All[i].MinTrophies) return All[i];
            return Bronze;
        }

        public static League ById(string id)
        {
            for (int i = 0; i < All.Length; i++)
                if (All[i].Id == id) return All[i];
            return Bronze;
        }

        /// <summary>Progress (0..1) toward the next league. Returns 1 for the top league.</summary>
        public static float Progress(long trophies)
        {
            League l = ForTrophies(trophies);
            if (l.IsTop) return 1f;
            long span = l.MaxTrophies - l.MinTrophies + 1;
            return span <= 0 ? 0f : (float)(trophies - l.MinTrophies) / span;
        }
    }
}
