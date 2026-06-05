using System;

namespace PushStars.Core
{
    /// <summary>
    /// One row of match history, from the local player's point of view (already resolved
    /// to "me vs opponent"). Built from a <c>matches/{matchId}</c> document by the repository.
    /// </summary>
    public class MatchRecord
    {
        public string   MatchId;
        public string   Mode;        // "pvp" | "ghost"
        public string   Exercise;    // "pushups"
        public bool     Won;
        public int      MyReps;
        public int      OpponentReps;
        public int      TrophyDelta; // signed (+win / -loss)
        public DateTime CreatedAt;
    }
}
