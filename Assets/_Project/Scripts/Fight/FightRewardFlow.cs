using System;
using PushStars.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Compatibility entry point for the independently authored reward scenes.</summary>
    public sealed class FightRewardFlow : MonoBehaviour
    {
        [Serializable]
        public struct Summary
        {
            public string PlayerName;
            public int TotalReps;
            public float Technique;
            public int Trophies;
            public long EnergyXp;
            public long Aura;
            public bool HasCase;
            public RawImage AvatarSource;
        }

        public bool IsShowing => FindFirstObjectByType<RewardScreen>() != null;
        public void Show(Summary summary, Action onComplete)
        {
            FightScreenNavigation.ShowSummary(summary, FightRequest.ReturnScene);
        }
        public void OpenPendingCases(Action onComplete) =>
            FightScreenNavigation.OpenCase(null, SceneManager.GetActiveScene().name);
        public void PreviewSummary() => FightScreenNavigation.Preview(FightScreen.RewardSummary);
        public void PreviewCaseAward() => FightScreenNavigation.Preview(FightScreen.CaseAward);
        public void PreviewCaseOpening() => FightScreenNavigation.Preview(FightScreen.CaseOpening);
        public void PreviewCaseReward() => FightScreenNavigation.Preview(FightScreen.CaseReward);
        // Authored scene content is never destroyed by the former overlay API.
        public void Hide() { }
    }
}
