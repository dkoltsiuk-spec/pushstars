namespace PushStars.Core
{
    /// <summary>Which duel the fight screen should run when it loads.</summary>
    public enum FightMode
    {
        /// <summary>Onboarding's 60-second level test: no opponent, the score IS the result.</summary>
        LevelTest = 0,
        /// <summary>Against the player's own best recorded set (<see cref="GhostStore"/>).</summary>
        Ghost = 1,
        /// <summary>Against a scripted PvE boss from <see cref="BossCatalog"/>.</summary>
        Boss = 2,
        Training = 3,
    }

    /// <summary>
    /// What the fight screen is about to be asked for. A scene load carries no arguments, so the
    /// caller parks its intent here and <c>FightController</c> reads it in Start — the same pattern
    /// a later phase will use to hand it a downloaded opponent instead of a local one.
    ///
    /// <para>Static state survives scene loads inside one domain, and every entry point sets the
    /// mode before loading, so a stale value can only appear if a scene is opened directly in the
    /// editor — which is exactly when the <see cref="Ghost"/> default (falling back to the boss with
    /// no record) is the useful behaviour.</para>
    /// </summary>
    public static class FightRequest
    {
        public static bool HasRequest { get; private set; }
        public static bool IsBotRecording { get; private set; }
        public static BotRecording TestBot { get; private set; }
        public static bool IsBotTest => IsBotRecording || TestBot != null;
        /// <summary>A live duel against another phone (a friend's, or a stranger's from the random
        /// queue): the opponent is <see cref="LiveDuelLink"/>'s peer, not a recording. Runs as a <see cref="FightMode.Ghost"/> duel so every duel screen
        /// treats it as one.</summary>
        public static LiveDuelLink LiveDuel { get; private set; }
        public static bool IsLiveDuel => LiveDuel != null;
        /// <summary>The live duel came from the random queue: a stranger, ranked (mode "pvp"),
        /// rather than a friendly duel by code.</summary>
        public static bool IsRankedLive { get; private set; }
        /// <summary>Another player's best set to fight when the live queue found nobody; null for
        /// the player's own ghost.</summary>
        public static GhostRecord Stranger { get; private set; }
        public static string StrangerName { get; private set; }
        /// <summary>The stranger is a bot from the pool: its hero, flag, trophies and recorded
        /// motion stand in for a player's. Fought for real — rewards and rating as any stranger.</summary>
        public static BotRecording StrangerBot { get; private set; }
        // Snapshot the chosen boss: winning advances the ladder before the result scene opens.
        public static string BossId { get; private set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            ArenaMatch.Clear();
            HasRequest = false;
            IsBotRecording = false;
            TestBot = null;
            LiveDuel = null;
            IsRankedLive = false;
            Stranger = null;
            StrangerName = null;
            StrangerBot = null;
            BossId = null;
            Mode = FightMode.Ghost;
            ReturnScene = FightConfig.MainSceneName;
            Workout = new TrainingPlan(3, 60);
        }
        public static FightMode Mode { get; private set; } = FightMode.Ghost;
        public static TrainingPlan Workout { get; private set; } = new TrainingPlan(3, 60);

        public static void Training(TrainingPlan plan, string returnScene = FightConfig.MainSceneName)
        { Workout = new TrainingPlan(plan.Sets, plan.RestSeconds); Set(FightMode.Training, returnScene); }

        /// <summary>Scene to return to when the duel ends.</summary>
        public static string ReturnScene { get; private set; } = FightConfig.MainSceneName;

        public static void LevelTest(string returnScene = FightConfig.MainSceneName)
            => Set(FightMode.LevelTest, returnScene);

        public static void Ghost(string returnScene = FightConfig.MainSceneName)
            => Set(FightMode.Ghost, returnScene);

        public static void Boss(string returnScene = FightConfig.MainSceneName)
            => Set(FightMode.Boss, returnScene);

        public static void RecordBot()
        {
            Set(FightMode.Ghost, FightConfig.MainSceneName);
            IsBotRecording = true;
        }

        public static void ReplayBot(BotRecording bot)
        {
            if (bot == null || !bot.IsValid) throw new System.ArgumentException("Invalid test bot.");
            Set(FightMode.Ghost, FightConfig.MainSceneName);
            TestBot = bot;
            ArenaMatch.BeginLocal(ArenaProfile.SelectedId, bot.fight.arenaId);
        }

        /// <summary>Another player's best set (the live queue found nobody). Fought like the
        /// player's own ghost, under the stranger's name.</summary>
        public static void StrangerGhost(GhostRecord record, string name, string returnScene = FightConfig.MainSceneName)
        {
            if (record == null || !record.IsValid) throw new System.ArgumentException("Invalid ghost.");
            Set(FightMode.Ghost, returnScene);
            Stranger = record;
            StrangerName = string.IsNullOrEmpty(name) ? "PLAYER" : name;
            ArenaMatch.BeginLocal(ArenaProfile.SelectedId, ArenaProfile.SelectedId);
        }

        /// <summary>A bot from the pool, offered by the search as a player at this level.</summary>
        public static void PoolBot(BotRecording bot, string returnScene = FightConfig.MainSceneName)
        {
            if (bot == null || !bot.IsValid) throw new System.ArgumentException("Invalid pool bot.");
            StrangerGhost(bot.fight, bot.displayName, returnScene);
            StrangerBot = bot;
        }

        /// <param name="ranked">A stranger from the random queue (trophies) rather than a friend.</param>
        public static void Live(LiveDuelLink link, bool ranked = false)
        {
            if (link == null) throw new System.ArgumentNullException(nameof(link));
            Set(FightMode.Ghost, FightConfig.MainSceneName);
            LiveDuel = link;
            IsRankedLive = ranked;
            // Both phones show the arena their own player picked; no coin flip against a ghost's.
            ArenaMatch.BeginLocal(ArenaProfile.SelectedId, ArenaProfile.SelectedId);
        }

        /// <summary>A queue duel was called off before its set (the stranger walked away): the main
        /// screen reopens the search on arrival, since the player came to fight someone.</summary>
        public static void SearchAgainOnReturn() => _searchAgain = true;
        public static bool ConsumeSearchAgain() { bool again = _searchAgain; _searchAgain = false; return again; }
        // Not part of Clear(): the way home clears the request, and this is what home reads.
        private static bool _searchAgain;
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSearchAgain() => _searchAgain = false;

        /// <summary>The hero and body the opponent's stage should build, when the opponent is a
        /// person (a test bot, a pool bot or a live friend) rather than the player's own shadow.</summary>
        public static bool TryGetOpponentLook(out int hero, out int gender)
        {
            hero = gender = 0;
            var bot = TestBot ?? StrangerBot;
            if (bot != null) { hero = bot.avatar; gender = bot.gender; return true; }
            var peer = LiveDuel?.Peer;
            if (peer == null) return false;
            hero = peer.Hero; gender = peer.Gender;
            return true;
        }

        private static void Set(FightMode mode, string returnScene)
        {
            IsBotRecording = false;
            TestBot = null;
            LiveDuel = null;
            IsRankedLive = false;
            Stranger = null;
            StrangerName = null;
            StrangerBot = null;
            ArenaMatch.Clear();
            if (mode == FightMode.Ghost) ArenaMatch.BeginLocal(ArenaProfile.SelectedId, GhostStore.Load()?.arenaId);
            HasRequest = true;
            Mode = mode;
            BossId = mode == FightMode.Boss ? BossCatalog.Current.Id : null;
            ReturnScene = string.IsNullOrEmpty(returnScene) ? FightConfig.MainSceneName : returnScene;
        }
    }
}
