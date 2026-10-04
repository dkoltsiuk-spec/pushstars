using System;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>One immutable choice per match. Online adapters supply a confirmed result, never a client roll.</summary>
    public static class ArenaMatch
    {
        public static string PlayerId { get; private set; }
        public static string OpponentId { get; private set; }
        public static string WinnerId { get; private set; }
        public static string MatchId { get; private set; }
        public static bool HasChoices => PlayerId != null;
        public static bool IsResolved => WinnerId != null;
        public static bool SameChoice => HasChoices && PlayerId == OpponentId;
        public static bool PresentationComplete { get; set; }
        public static void BeginLocal(string player, string opponent)
        {
            Clear();
            PlayerId = ArenaCatalog.Normalize(player); OpponentId = ArenaCatalog.Normalize(opponent);
            if (SameChoice) WinnerId = PlayerId;
        }
        public static string ResolveLocal(Func<int> coin = null)
        {
            if (!HasChoices) BeginLocal(ArenaProfile.SelectedId, ArenaCatalog.DefaultId);
            if (!IsResolved) WinnerId = (coin != null ? coin() : UnityEngine.Random.Range(0, 2)) == 0 ? PlayerId : OpponentId;
            return WinnerId;
        }
        public static bool AcceptConfirmed(string matchId, string player, string opponent, string winner)
        {
            var catalog = ArenaCatalog.Instance;
            if (string.IsNullOrEmpty(matchId) || catalog == null || catalog.Find(player) == null || catalog.Find(opponent) == null
                || (winner != player && winner != opponent)) return false;
            if (MatchId == matchId) return PlayerId == player && OpponentId == opponent && WinnerId == winner;
            Clear(); MatchId = matchId; PlayerId = player; OpponentId = opponent; WinnerId = winner; return true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            PlayerId = OpponentId = WinnerId = MatchId = null; PresentationComplete = false;
        }
    }
}
