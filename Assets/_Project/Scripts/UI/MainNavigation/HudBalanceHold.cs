using System.Collections.Generic;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Lets a reward flight show a HUD balance behind its saved value, so the counter counts up as
    /// the flying icons land instead of jumping before they arrive. While a pill is held,
    /// <see cref="MainShellView"/> displays the held value. Keys are the pill names
    /// ("AuraPill", "GemPill", "TrophyPill"). Display only: balances are never changed here.
    /// </summary>
    public static class HudBalanceHold
    {
        private static readonly Dictionary<string, long> Held = new Dictionary<string, long>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Held.Clear();

        public static void Set(string pill, long shown) => Held[pill] = System.Math.Max(0, shown);
        public static void Release(string pill) => Held.Remove(pill);
        public static void ReleaseAll() => Held.Clear();
        public static bool TryGet(string pill, out long shown) => Held.TryGetValue(pill, out shown);
    }
}
