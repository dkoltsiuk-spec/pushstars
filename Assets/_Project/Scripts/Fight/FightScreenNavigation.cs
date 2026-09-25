using System;
using PushStars.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Fight
{
    public enum FightScreen { Home, Preparation, Battle, Results, RewardSummary, CaseAward, CaseOpening, CaseReward }

    [Serializable]
    public sealed class FightResultData
    {
        public FightMode Mode;
        public bool Win, Draw, NewRecord;
        public int MyReps, OppReps, Trophies;
        public float MyForm, OppForm, MyRepsPerMinute, OppSecondsPerRep;
        public long Xp;
        public string OpponentName, PlayerName;
        public FitnessTier FitnessTier;
        public int TrainingSets;
    }

    /// <summary>
    /// Transports presentation data between independently authored scenes. It never grants a
    /// reward and contains no sequence of screens: each scene's actions choose their destination.
    /// Durable case receipts remain in CaseRewards; a fresh editor launch uses harmless samples.
    /// </summary>
    public static class FightScreenNavigation
    {
        public static bool IsPreview { get; private set; } = Application.isEditor;
        public static FightResultData Result { get; private set; } = Application.isEditor ? DemoResult() : null;
        public static FightRewardFlow.Summary RewardSummary { get; private set; } = Application.isEditor ? DemoSummary() : default;
        public static string CaseId { get; set; }
        public static string AwardedCaseId { get; private set; }
        public static string ReturnScene { get; private set; } = FightConfig.MainSceneName;
        public static CaseRarity PreviewRarity { get; set; } = CaseRarity.Common;
        public static int PreviewGems { get; set; } = 100;
        public static int PreviewAura { get; set; }
        internal static MockupProfile.Opponent? PreparedOpponent { get; set; }
        private static bool _loading;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            IsPreview = Application.isEditor;
            Result = Application.isEditor ? DemoResult() : null;
            RewardSummary = Application.isEditor ? DemoSummary() : default;
            CaseId = AwardedCaseId = null;
            ReturnScene = FightConfig.MainSceneName;
            PreviewRarity = CaseRarity.Common;
            PreviewGems = 100;
            PreviewAura = 0;
            PreparedOpponent = null;
            _loading = false;
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneLoaded += SceneLoaded;
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode) => _loading = false;

        public static string SceneName(FightScreen screen)
        {
            switch (screen)
            {
                case FightScreen.Preparation: return "FightPreparation";
                case FightScreen.Battle: return FightConfig.FightSceneName;
                case FightScreen.Results: return "FightResults";
                case FightScreen.RewardSummary: return "RewardSummary";
                case FightScreen.CaseAward: return "CaseAward";
                case FightScreen.CaseOpening: return "CaseOpening";
                case FightScreen.CaseReward: return "CaseReward";
                default: return FightConfig.MainSceneName;
            }
        }

        public static string ScenePath(FightScreen screen) => "Assets/_Project/Scenes/" + SceneName(screen) + ".unity";

        public static void BeginPreparation()
        {
            HomeRewardFlight.Clear();
            IsPreview = Application.isEditor && !FightRequest.HasRequest;
            ReturnScene = FightRequest.ReturnScene;
            CaseId = AwardedCaseId = null;
        }

        public static void ShowResults(FightResultData result, FightRewardFlow.Summary summary,
            string awardedCaseId, string returnScene)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            SetSummary(summary, awardedCaseId, returnScene);
            Navigate(result.Mode == FightMode.LevelTest && result.MyReps > 0
                ? FightScreen.RewardSummary : FightScreen.Results);
        }

        public static void ShowSummary(FightRewardFlow.Summary summary, string returnScene = "Main")
        {
            SetSummary(summary, null, returnScene);
            Navigate(FightScreen.RewardSummary);
        }

        private static void SetSummary(FightRewardFlow.Summary summary, string awardedCaseId, string returnScene)
        {
            IsPreview = false;
            // Never retain a Unity object owned by the scene we are about to unload.
            summary.AvatarSource = null;
            summary.HasCase = !string.IsNullOrEmpty(awardedCaseId);
            RewardSummary = summary;
            AwardedCaseId = awardedCaseId;
            CaseId = awardedCaseId;
            ReturnScene = string.IsNullOrWhiteSpace(returnScene) ? FightConfig.MainSceneName : returnScene;
        }

        public static bool OpenCase(string caseId = null, string returnScene = "Main")
        {
            var receipt = string.IsNullOrEmpty(caseId) ? CaseRewards.Pending : CaseRewards.Find(caseId);
            if (receipt == null) return false;
            IsPreview = false;
            CaseId = receipt.Id;
            AwardedCaseId = null;
            ReturnScene = string.IsNullOrWhiteSpace(returnScene) ? FightConfig.MainSceneName : returnScene;
            Navigate(receipt.Opened ? FightScreen.CaseReward : FightScreen.CaseOpening);
            return true;
        }

        /// <summary>Present an already granted case from any source; showing this scene grants nothing.</summary>
        public static bool ShowCaseAward(string caseId, string returnScene = "Main")
        {
            if (CaseRewards.Find(caseId) == null) return false;
            IsPreview = false;
            AwardedCaseId = CaseId = caseId;
            ReturnScene = string.IsNullOrWhiteSpace(returnScene) ? FightConfig.MainSceneName : returnScene;
            Navigate(FightScreen.CaseAward);
            return true;
        }

        public static void Preview(FightScreen screen)
        {
            if (!Application.isEditor) return;
            HomeRewardFlight.Clear();
            FightRequest.Clear();
            IsPreview = true;
            Result = DemoResult();
            RewardSummary = DemoSummary();
            CaseId = AwardedCaseId = null;
            PreviewRarity = CaseRarity.Common;
            PreviewGems = 100;
            PreviewAura = 0;
            Navigate(screen);
        }

#if UNITY_EDITOR
        /// <summary>Wallet-free preview of the completed assessment and its fixed reward.</summary>
        public static void PrepareAssessmentPreview()
        {
            IsPreview = true;
            Result = new FightResultData { Mode = FightMode.LevelTest, MyReps = 8, MyForm = 92, Xp = 80, PlayerName = "BEASTCORE_DEV" };
            RewardSummary = new FightRewardFlow.Summary { PlayerName = Result.PlayerName, TotalReps = 8, Technique = .92f, EnergyXp = 80, HasCase = true };
            PreviewAura = CaseRewards.AssessmentAura; PreviewGems = 0; PreviewRarity = CaseRarity.Legendary;
            CaseId = AwardedCaseId = null;
        }
#endif

        /// <summary>Exit to the caller and clear any prepared opponent or outstanding fight request.</summary>
        public static void ReturnTo(string returnScene)
        {
            ReturnScene = string.IsNullOrWhiteSpace(returnScene) ? FightConfig.MainSceneName : returnScene;
            Navigate(FightScreen.Home);
        }

        public static void Navigate(FightScreen screen)
        {
            if (_loading) return;
            if (!IsPreview && (screen == FightScreen.CaseOpening || screen == FightScreen.CaseReward || screen == FightScreen.CaseAward))
            {
                if (string.IsNullOrEmpty(CaseId)) CaseId = AwardedCaseId;
                var receipt = CaseRewards.Find(CaseId);
                if (receipt == null) screen = FightScreen.Home;
                else if (screen == FightScreen.CaseReward && !receipt.Opened) screen = FightScreen.CaseOpening;
                else if (screen == FightScreen.CaseOpening && receipt.Opened) screen = FightScreen.CaseReward;
            }

            string destination = screen == FightScreen.Home ? ReturnScene : SceneName(screen);
            if (!Application.CanStreamedLevelBeLoaded(destination))
            {
                Debug.LogError("Screen scene is missing from Build Settings: " + destination);
                return;
            }
            _loading = true;
            if (screen == FightScreen.Home)
            {
                HomeRewardFlight.ReturnTo(destination);
                PreparedOpponent = null;
                FightRequest.Clear();
                SceneManager.LoadScene(destination);
            }
            else SceneManager.LoadScene(destination);
        }

        private static FightResultData DemoResult() => new FightResultData
        {
            Mode = FightMode.Ghost, Win = true, MyReps = 21, OppReps = 18,
            MyForm = 90, OppForm = 85, MyRepsPerMinute = 33.3f, OppSecondsPerRep = 1.6f,
            Xp = 570, Trophies = 21, PlayerName = "BEASTCORE_DEV", OpponentName = "OSKAT009",
            FitnessTier = FitnessTest.TierFor(21)
        };

        private static FightRewardFlow.Summary DemoSummary() => new FightRewardFlow.Summary
        {
            PlayerName = "BEASTCORE_DEV", TotalReps = 57, Technique = .92f,
            Trophies = 21, EnergyXp = 570, Aura = 32, HasCase = true
        };
    }
}
