using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    [Serializable] public sealed class BlockedPlayer { public string uid, name; }
    [Serializable] public sealed class BlockedPlayers { public BlockedPlayer[] players = Array.Empty<BlockedPlayer>(); }

    /// <summary>Reports and blocks between players (functions/src/moderation.js). The server keeps
    /// the two apart in matchmaking; this phone keeps a copy of its own list so a blocked player's
    /// name stays out of the league table and their search stays off the main screen. A bot of the
    /// pool has no account: blocking one stays on this phone, and the search never offers a bot
    /// twice anyway.</summary>
    public static class ModerationClient
    {
        [Serializable] private sealed class Empty { }
        [Serializable] private sealed class ReportRequest { public string targetUid, targetBot, name, reason, where; }
        [Serializable] private sealed class BlockRequest { public string targetUid; public bool blocked; }
        [Serializable] private sealed class SavedList { public List<string> ids = new List<string>(); }

        public const string ReasonName = "offensive_name", ReasonCheating = "cheating";
        private const string Region = "europe-west1";
        private const string SaveKey = "moderation.blocked.v1";
        private static HashSet<string> _blocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => _blocked = null;

        private static HashSet<string> Blocked
        {
            get
            {
                if (_blocked != null) return _blocked;
                _blocked = new HashSet<string>(StringComparer.Ordinal);
                string json = PlayerPrefs.GetString(SaveKey, "");
                if (string.IsNullOrEmpty(json)) return _blocked;
                try { _blocked.UnionWith(JsonUtility.FromJson<SavedList>(json).ids); }
                catch (ArgumentException e) { Debug.LogWarning("[Moderation] blocked list: " + e.Message); }
                return _blocked;
            }
        }

        private static void Save()
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(new SavedList { ids = new List<string>(Blocked) }));
            PlayerPrefs.Save();
        }

        public static bool IsBlocked(string id) => !string.IsNullOrEmpty(id) && Blocked.Contains(id);

        public static async Task Report(string targetUid, string targetBot, string name, string reason, string where)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            await LeagueClient.Call<ReportRequest, Empty>("reportPlayer", new ReportRequest
            { targetUid = targetUid ?? "", targetBot = targetBot ?? "", name = name ?? "", reason = reason, where = where ?? "" }, LeagueClient.Uid, Region);
        }

        /// <summary>Saved here only once the server has it: a block that failed must not look done.</summary>
        public static async Task SetBlocked(string targetUid, string targetBot, bool blocked)
        {
            if (!string.IsNullOrEmpty(targetUid))
            {
                await FirebaseAuthService.EnsureReadyAsync();
                await LeagueClient.Call<BlockRequest, Empty>("setPlayerBlocked", new BlockRequest { targetUid = targetUid, blocked = blocked }, LeagueClient.Uid, Region);
            }
            string id = string.IsNullOrEmpty(targetUid) ? targetBot : targetUid;
            if (string.IsNullOrEmpty(id)) return;
            if (blocked) Blocked.Add(id); else Blocked.Remove(id);
            Save();
        }

        /// <summary>The account's list, which also refreshes this phone's copy (a reinstall, another phone).</summary>
        public static async Task<BlockedPlayer[]> Load()
        {
            await FirebaseAuthService.EnsureReadyAsync();
            var list = await LeagueClient.Call<Empty, BlockedPlayers>("getBlockedPlayers", new Empty(), LeagueClient.Uid, Region);
            var players = list?.players ?? Array.Empty<BlockedPlayer>();
            foreach (var player in players) Blocked.Add(player.uid);
            Save();
            return players;
        }
    }
}
