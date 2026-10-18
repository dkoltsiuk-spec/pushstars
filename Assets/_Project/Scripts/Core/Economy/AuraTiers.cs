namespace PushStars.Core
{
    /// <summary>One step of the visible aura around the hero.</summary>
    public sealed class AuraTier
    {
        public readonly int Index;
        public readonly string Name;
        public readonly long MinPeak;

        public AuraTier(int index, string name, long minPeak) { Index = index; Name = name; MinPeak = minPeak; }
    }

    /// <summary>
    /// Peak Aura → the glow drawn around the player's hero: none, white GLOW, BLUE FLAME,
    /// VIOLET STORM, INFINITE. Driven by the peak, not the balance, so a lost fight never
    /// dims the hero (same rule as the Aura heroes; docs/design/economy.md §3).
    /// </summary>
    public static class AuraTiers
    {
        public static readonly AuraTier[] All =
        {
            new AuraTier(0, "NO AURA", 0),
            new AuraTier(1, "GLOW", EconomyConfig.AuraTierGlow),
            new AuraTier(2, "BLUE FLAME", EconomyConfig.AuraTierBlueFlame),
            new AuraTier(3, "VIOLET STORM", EconomyConfig.AuraTierVioletStorm),
            new AuraTier(4, "INFINITE", EconomyConfig.AuraTierInfinite),
        };

        public static AuraTier ForPeak(long peak)
        {
            for (int i = All.Length - 1; i > 0; i--)
                if (peak >= All[i].MinPeak) return All[i];
            return All[0];
        }

        /// <summary>The next tier to reach, or null at the top.</summary>
        public static AuraTier Next(long peak)
        {
            int index = ForPeak(peak).Index + 1;
            return index < All.Length ? All[index] : null;
        }
    }
}
