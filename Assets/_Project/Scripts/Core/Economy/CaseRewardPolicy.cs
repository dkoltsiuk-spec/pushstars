using UnityEngine;

namespace PushStars.Core
{
    /// <summary>Eligibility for workout cases. Rewards are granted into inventory independently of UI.</summary>
    [CreateAssetMenu(fileName = "CaseRewardPolicy", menuName = "Push Stars/Rewards/Workout Case Policy")]
    public sealed class CaseRewardPolicy : ScriptableObject
    {
        public const string ResourcePath = "CaseRewardPolicy";
        public const int DefaultDailyLimit = 1;
        public const int DefaultMinimumReps = 1;

        [Tooltip("Maximum workout cases per UTC calendar day. Zero disables this source. Opening or claiming does not reset it.")]
        [SerializeField, Min(0)] private int _dailyWorkoutCaseLimit = DefaultDailyLimit;
        [Tooltip("Minimum counted repetitions in a completed workout before it can qualify for a daily case.")]
        [SerializeField, Min(1)] private int _minimumWorkoutReps = DefaultMinimumReps;

        public int DailyWorkoutCaseLimit => Mathf.Max(0, _dailyWorkoutCaseLimit);
        public int MinimumWorkoutReps => Mathf.Max(1, _minimumWorkoutReps);
    }
}
