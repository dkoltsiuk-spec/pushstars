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
        public string   Mode;         // "pvp" | "ghost" | "boss" | "training" | "assessment"
        public string   Exercise;     // "pushups"
        public string   OpponentName; // opponent name at completion; absent for solo sets
        public bool     Won;
        public bool     Draw;
        public bool IsSolo => Mode == "training" || Mode == "assessment";
        public int      MyReps;
        public int      OpponentReps;
        public int      TrophyDelta;  // signed (+win / -loss)
        public int      DurationSec;  // 60
        public bool     IsRecord;     // "NEW RECORD" tag
        public DateTime CreatedAt;
    }
}
