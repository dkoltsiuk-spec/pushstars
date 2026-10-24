using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>The bot pool (functions/src/botPool.js): recorded sets the search offers as players
    /// at your level while few people are online. A player meets each bot once — taking its fight
    /// marks it here and on the server, and the search never offers it to them again.</summary>
    public static class PoolBotClient
    {
        private const string KeyFought = "pool-bots-fought";
        [Serializable] private sealed class IdRequest { public string id; }
        [Serializable] private sealed class Downloaded { public BotRecording recording; }
        [Serializable] private sealed class Marked { public bool ok; }

        private static string CacheDir => Path.Combine(Application.persistentDataPath, "pool-bots-v1");

        /// <summary>Bots this phone has fought. Sent with every new search, so a mark that has not
        /// reached the server yet still keeps the bot away.</summary>
        public static string[] Fought =>
            PlayerPrefs.GetString(KeyFought, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        /// <summary>The bot's whole recording, motion included, with the face the search card showed.
        /// Kept on the phone once fetched: a bot's take never changes.</summary>
        public static async Task<BotRecording> Load(LiveQueueGhost offer)
        {
            if (offer == null || !offer.IsBot || !Guid.TryParseExact(offer.botId, "N", out _))
                throw new ArgumentException("Not a pool bot.");
            string path = Path.Combine(CacheDir, offer.botId + ".json");
            BotRecording bot = null;
            try { if (File.Exists(path)) bot = JsonUtility.FromJson<BotRecording>(File.ReadAllText(path)); }
            catch (Exception e) { Debug.LogWarning("[PoolBot] Cached bot unreadable: " + e.Message); }
            if (bot == null || !bot.IsValid)
            {
                await FirebaseAuthService.EnsureReadyAsync();
                var result = await LeagueClient.Call<IdRequest, Downloaded>("getPoolBot", new IdRequest { id = offer.botId }, LeagueClient.Uid);
                bot = result?.recording;
                if (bot == null || !bot.IsValid) throw new InvalidOperationException("Pool bot is invalid.");
                var clip = GhostMotionClip.Decode(bot.fight.motionBase64);
                if (clip == null || !clip.HasPhase(GhostMotionClip.Live)) throw new InvalidOperationException("Pool bot has no animation.");
                try { Directory.CreateDirectory(CacheDir); File.WriteAllText(path, JsonUtility.ToJson(bot)); }
                catch (Exception e) { Debug.LogWarning("[PoolBot] Could not cache bot: " + e.Message); }
            }
            bot.displayName = string.IsNullOrEmpty(offer.name) ? bot.displayName : offer.name;
            bot.countryCode = string.IsNullOrEmpty(offer.country) ? bot.countryCode : offer.country;
            bot.trophies = offer.trophies;
            bot.avatar = offer.hero;
            bot.gender = offer.gender;
            return bot;
        }

        /// <summary><see cref="Load"/> that never throws: null when the recording could not be had.</summary>
        public static async Task<BotRecording> Prefetch(LiveQueueGhost offer)
        {
            try { return await Load(offer); }
            catch (Exception e) { Debug.LogWarning("[PoolBot] Not loaded: " + e.Message); return null; }
        }

        /// <summary>The player took this bot's fight: it leaves their search for good.</summary>
        public static void MarkFought(string botId)
        {
            if (string.IsNullOrEmpty(botId)) return;
            Remember(botId);
            _ = Send(botId);
        }

        /// <summary>The phone's own half of <see cref="MarkFought"/>.</summary>
        public static void Remember(string botId)
        {
            var fought = Fought;
            if (string.IsNullOrEmpty(botId) || fought.Contains(botId)) return;
            PlayerPrefs.SetString(KeyFought, string.Join(",", fought.Append(botId)));
            PlayerPrefs.Save();
        }

        private static async Task Send(string botId)
        {
            try
            {
                await FirebaseAuthService.EnsureReadyAsync();
                await LeagueClient.Call<IdRequest, Marked>("markBotFought", new IdRequest { id = botId }, LeagueClient.Uid);
            }
            catch (Exception e) { Debug.LogWarning("[PoolBot] Fought mark not sent: " + e.Message); }
        }
    }
}
