using System;

namespace PushStars.Core
{
    /// <summary>
    /// The daily duel hour (20:00–21:00 local): while few people play, a reminder at its start
    /// gathers them at one time so the live queue has someone in it. The main screen shows a badge
    /// while it runs and the search screen says so; nothing else changes (no extra rewards).
    /// </summary>
    public static class DuelHour
    {
        public static DateTime StartOf(DateTime localDay) => localDay.Date.AddMinutes(EconomyConfig.DuelHourStartMinute);
        public static DateTime EndOf(DateTime localDay) => StartOf(localDay).AddMinutes(EconomyConfig.DuelHourMinutes);

        public static bool IsOn(DateTime localNow) => localNow >= StartOf(localNow) && localNow < EndOf(localNow);

        /// <summary>Time left in today's duel hour; zero outside it.</summary>
        public static TimeSpan Left(DateTime localNow) => IsOn(localNow) ? EndOf(localNow) - localNow : TimeSpan.Zero;
    }
}
