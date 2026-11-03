using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>A row of the blocked list: an account (<see cref="uid"/>) or a bot of the pool
    /// (<see cref="botId"/>), which has no account and is kept on this phone only.</summary>
    [Serializable] public sealed class BlockedPlayer { public string uid, name, botId; }
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
        [Serializable] private sealed class SavedBot { public string id, name; }
        [Serializable] private sealed class SavedList
        {
            public List<string> ids = new List<string>();
            /// <summary>Blocked pool bots with the name they showed, for the blocked list.</summary>
            public List<SavedBot> bots = new List<SavedBot>();
        }

        public const string ReasonName = "offensive_name", ReasonCheating = "cheating";
        private const string Region = "europe-west1";
        private const string SaveKey = "moderation.blocked.v1";
        private static HashSet<string> _blocked;
        private static Dictionary<string, string> _bots;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _blocked = null; _bots = null; }

        private static HashSet<string> Blocked { get { Read(); return _blocked; } }
        private static Dictionary<string, string> Bots { get { Read(); return _bots; } }

        private static void Read()
        {
            if (_blocked != null) return;
            _blocked = new HashSet<string>(StringComparer.Ordinal);
            _bots = new Dictionary<string, string>(StringComparer.Ordinal);
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var saved = JsonUtility.FromJson<SavedList>(json);
                _blocked.UnionWith(saved.ids);
                if (saved.bots != null) foreach (var bot in saved.bots) _bots[bot.id] = bot.name ?? "";
            }
            catch (ArgumentException e) { Debug.LogWarning("[Moderation] blocked list: " + e.Message); }
        }

        private static void Save()
        {
            var saved = new SavedList { ids = new List<string>(Blocked) };
            foreach (var bot in Bots) saved.bots.Add(new SavedBot { id = bot.Key, name = bot.Value });
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(saved));
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
        /// <param name="name">The name shown in the blocked list; a bot has no account to ask it from.</param>
        public static async Task SetBlocked(string targetUid, string targetBot, bool blocked, string name = "")
        {
            if (!string.IsNullOrEmpty(targetUid))
            {
                await FirebaseAuthService.EnsureReadyAsync();
                await LeagueClient.Call<BlockRequest, Empty>("setPlayerBlocked", new BlockRequest { targetUid = targetUid, blocked = blocked }, LeagueClient.Uid, Region);
            }
            string id = string.IsNullOrEmpty(targetUid) ? targetBot : targetUid;
            if (string.IsNullOrEmpty(id)) return;
            if (blocked) Blocked.Add(id); else Blocked.Remove(id);
            if (string.IsNullOrEmpty(targetUid))
            {
                if (blocked) Bots[id] = string.IsNullOrWhiteSpace(name) ? "PLAYER" : name;
                else Bots.Remove(id);
            }
            Save();
        }

        /// <summary>The bots blocked on this phone: no account, no server to ask.</summary>
        public static BlockedPlayer[] LocalPlayers()
        {
            var players = new List<BlockedPlayer>();
            foreach (var bot in Bots) players.Add(new BlockedPlayer { uid = "", botId = bot.Key, name = bot.Value });
            return players.ToArray();
        }

        /// <summary>The account's list from the server, which also refreshes this phone's copy (a
        /// reinstall, another phone), then the bots blocked on this phone.</summary>
        public static async Task<BlockedPlayer[]> Load()
        {
            await FirebaseAuthService.EnsureReadyAsync();
            var list = await LeagueClient.Call<Empty, BlockedPlayers>("getBlockedPlayers", new Empty(), LeagueClient.Uid, Region);
            var players = new List<BlockedPlayer>(list?.players ?? Array.Empty<BlockedPlayer>());
            foreach (var player in players) Blocked.Add(player.uid);
            Save();
            players.AddRange(LocalPlayers());
            return players.ToArray();
        }
    }
}
