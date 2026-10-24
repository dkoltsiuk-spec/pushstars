using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.Services;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// A bot from the search's pool (functions/src/botPool.js) is fought as the player it plays: the
    /// search's offer carries its id and face, the fight request keeps its hero, flag and motion
    /// like a stranger's set, and the phone remembers it was met so the next search skips it.
    /// No network: the offer is parsed the way the server sends it and the recording is built here.
    /// </summary>
    public static class PoolBotRegression
    {
        private const string KeyFought = "pool-bots-fought";

        [MenuItem("Tools/Push Stars/Bots/Validate Pool Bots")]
        public static void Run()
        {
            string savedFought = PlayerPrefs.GetString(KeyFought, null);
            try
            {
                Offer();
                Request();
                Fought();
            }
            finally
            {
                if (savedFought == null) PlayerPrefs.DeleteKey(KeyFought); else PlayerPrefs.SetString(KeyFought, savedFought);
                FightRequest.Clear();
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/pool-bot-regression.txt", "PASS " + DateTime.UtcNow.ToString("O"));
            Debug.Log("[PoolBotRegression] PASS");
        }

        private static void Offer()
        {
            // The enqueue answer as botPool.offerOf writes it.
            var status = JsonUtility.FromJson<LiveQueueStatus>("{\"status\":\"waiting\",\"ghost\":{\"uid\":\"\",\"name\":\"SwiftOtter\"," +
                "\"trophies\":191,\"repTimes\":[1,2.5,4],\"clapTimes\":[],\"botId\":\"" + Id(1) + "\",\"country\":\"US\",\"hero\":12,\"gender\":0}}");
            Require(status.HasGhost && status.ghost.IsBot && status.ghost.botId == Id(1) && status.ghost.hero == 12
                && status.ghost.country == "US" && status.ghost.trophies == 191, "Bot offer did not parse with its face.");
            // A stranger's season set has no bot id.
            var stranger = JsonUtility.FromJson<LiveQueueStatus>("{\"status\":\"waiting\",\"ghost\":{\"uid\":\"u1\",\"name\":\"ZED\",\"repTimes\":[1,2]}}");
            Require(stranger.HasGhost && !stranger.ghost.IsBot, "A stranger's set read as a bot.");
        }

        private static void Request()
        {
            var bot = Bot();
            FightRequest.PoolBot(bot);
            Require(FightRequest.Mode == FightMode.Ghost && FightRequest.Stranger == bot.fight && FightRequest.StrangerName == "SwiftOtter"
                && FightRequest.StrangerBot == bot && !FightRequest.IsBotTest && !FightRequest.IsLiveDuel,
                "Pool bot request is not a stranger duel with rewards.");
            Require(FightRequest.TryGetOpponentLook(out int hero, out int gender) && hero == 12 && gender == 1,
                "Opponent stage would not build the bot's hero.");
            Require(!string.IsNullOrEmpty(FightRequest.Stranger.motionBase64), "The bot's motion did not reach the fight.");
            FightRequest.StrangerGhost(GhostRecord.From(new[] { 1f, 2f }, 0, "stranger"), "ZED");
            Require(FightRequest.StrangerBot == null && !FightRequest.TryGetOpponentLook(out _, out _), "A later stranger kept the bot's face.");
            FightRequest.PoolBot(bot);
            FightRequest.Ghost();
            Require(FightRequest.StrangerBot == null && FightRequest.Stranger == null, "Own ghost duel kept the bot.");
            bool threw = false;
            try { FightRequest.PoolBot(new BotRecording()); } catch (ArgumentException) { threw = true; }
            Require(threw, "An empty bot was accepted.");
        }

        private static void Fought()
        {
            PlayerPrefs.DeleteKey(KeyFought);
            Require(PoolBotClient.Fought.Length == 0, "Fresh phone remembers bots.");
            PoolBotClient.Remember(Id(1));
            PoolBotClient.Remember(Id(2));
            PoolBotClient.Remember(Id(1));
            Require(PoolBotClient.Fought.SequenceEqual(new[] { Id(1), Id(2) }), "Fought bots not remembered once each.");
        }

        private static BotRecording Bot()
        {
            var fight = GhostRecord.From(new[] { 1f, 2.5f, 4f }, 0, "bot-recording");
            fight.motionBase64 = "H4sIAAAAAAAAAwMAAAAAAAAAAAA=";   // any non-empty text: IsValid only checks presence
            return new BotRecording { id = Id(1), displayName = "SwiftOtter", countryCode = "US", avatar = 12, gender = 1, trophies = 191, fight = fight };
        }

        private static string Id(int n) => n.ToString().PadLeft(32, 'a');

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception("[PoolBotRegression] " + message);
        }
    }
}
