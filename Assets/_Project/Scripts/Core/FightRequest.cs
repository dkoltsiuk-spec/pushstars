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
        // Snapshot the chosen boss: winning advances the ladder before the result scene opens.
        public static string BossId { get; private set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            ArenaMatch.Clear();
            HasRequest = false;
            IsBotRecording = false;
            TestBot = null;
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

        private static void Set(FightMode mode, string returnScene)
        {
            IsBotRecording = false;
            TestBot = null;
            ArenaMatch.Clear();
            if (mode == FightMode.Ghost) ArenaMatch.BeginLocal(ArenaProfile.SelectedId, GhostStore.Load()?.arenaId);
            HasRequest = true;
            Mode = mode;
            BossId = mode == FightMode.Boss ? BossCatalog.Current.Id : null;
            ReturnScene = string.IsNullOrEmpty(returnScene) ? FightConfig.MainSceneName : returnScene;
        }
    }
}
