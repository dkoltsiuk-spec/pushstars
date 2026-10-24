using System;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>
    /// A live set that has begun and not yet ended, kept on disk. The fight screen opens it at the
    /// bell and closes it however the set ends — at the final whistle, a walkover either way, a
    /// duel called off. One still open means the set was walked out of some other way: the app
    /// was killed, crashed, or the screen torn down. That is a defeat like the exit button's, and
    /// <see cref="SettleOpen"/> books it — on the next launch at the latest — so closing the app
    /// is no way out of a duel going badly. The opponent's phone has long since won by walkover.
    /// </summary>
    public static class LiveDuelGuard
    {
        [Serializable]
        private sealed class OpenSet { public string matchId, sessionId, opponent; public bool ranked; }

        private const string Key = "liveduel.open";

        public static void Open(string matchId, string sessionId, bool ranked, string opponent)
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(new OpenSet { matchId = matchId, sessionId = sessionId, ranked = ranked, opponent = opponent }));
            PlayerPrefs.Save();
        }

        public static void Close()
        {
            if (!PlayerPrefs.HasKey(Key)) return;
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void SettleOnLaunch() => SettleOpen();

        /// <summary>Books the open set, if there is one, as a forfeit: a loss in the ledger (queue
        /// trophies), the RAGE QUIT Aura for a queue duel, and the forfeit told to the server.</summary>
        public static void SettleOpen()
        {
            OpenSet open = null;
            try { open = JsonUtility.FromJson<OpenSet>(PlayerPrefs.GetString(Key, "")); } catch (Exception) { }
            Close();
            if (open == null || string.IsNullOrEmpty(open.matchId) || string.IsNullOrEmpty(open.sessionId)) return;
            Debug.Log($"[LiveDuel] A live set was left open ({open.matchId}) — booking it as a forfeit.");
            try
            {
                // Aura first: it is priced against the player as they were before this fight.
                if (open.ranked) FightController.ChargeRageQuit(open.sessionId);
                LocalProfile.RecordWorkout(open.sessionId, open.ranked ? "pvp" : "friend", 0, 0, 0, open.opponent, false, false, 0);
            }
            catch (Exception exception) { Debug.LogException(exception); }
            FightController.SendLiveResult(open.matchId, Array.Empty<float>(), Array.Empty<float>(), 1f, 0, false, forfeit: true);
        }
    }
}
