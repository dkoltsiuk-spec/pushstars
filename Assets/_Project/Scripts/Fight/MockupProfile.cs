using UnityEngine;
using PushStars.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// Placeholder identities for the pre-duel card, standing in for systems that do not exist yet:
    /// the player's country (no <c>country</c> field on the profile), a real ladder for the opponent
    /// (a ghost has none), and named human opponents (async PvP — phase 12.5).
    ///
    /// <para>Nothing here is persisted or scored. The player's real record from
    /// <see cref="PushStars.Core.LocalProfile"/> replaces the player numbers the moment they have
    /// played a set; the opponent side stays theatre, and the duel underneath a mock opponent is
    /// still the player's own ghost recording. Delete once the phase-11.5 sync and phase-12.5 async
    /// PvP land.</para>
    /// </summary>
    internal static class MockupProfile
    {
        public enum Flag { Germany, Moldova }

        // ── Player card, only while LocalProfile is still empty (before the first real set) ──
        public const int PlayerTrophies = 120;
        public const int PlayerBestReps = 32;
        public const int PlayerWinRate  = 52;

        /// <summary>The player's flag is always a placeholder — the profile carries no country.</summary>
        public const Flag PlayerFlag = Flag.Moldova;

        public readonly struct Opponent
        {
            public readonly string Name;
            public readonly Flag Flag;
            public readonly int Trophies;
            public readonly int BestReps;
            public readonly int WinRate;

            public Opponent(string name, Flag flag, int trophies, int bestReps, int winRate)
            {
                Name = name;
                Flag = flag;
                Trophies = trophies;
                BestReps = bestReps;
                WinRate = winRate;
            }
        }

        private static readonly Opponent[] Pool =
        {
            new Opponent("OSKAT009",   Flag.Germany, 98,  32, 52),
            new Opponent("IRONVASILE", Flag.Moldova, 143, 41, 61),
            new Opponent("PUSHZILLA",  Flag.Germany, 71,  26, 44),
            new Opponent("KOVApower",  Flag.Moldova, 187, 47, 66),
        };

        /// <summary>One opponent identity for this duel. Random so the card is not the same face
        /// every launch; the pick is made once per fight and reused by the HUD and result screen.</summary>
        public static Opponent PickOpponent() => Pool[Random.Range(0, Pool.Length)];

        public static Sprite FlagSprite(Flag flag, PushStarsTheme theme)
        {
            if (theme == null) return null;
            return flag == Flag.Germany ? theme.FlagGermany : theme.FlagMoldova;
        }
    }
}
