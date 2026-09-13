using UnityEngine;

namespace PushStars.Core
{
    public readonly struct TrainingPlan
    {
        public const int MinSets = 1, MaxSets = 10, SetSeconds = 60, ManualRest = -1;
        public int Sets { get; }
        public int RestSeconds { get; }
        public bool IsManualRest => RestSeconds == ManualRest;
        public int? EstimatedSeconds => IsManualRest && Sets > 1 ? (int?)null :
            Sets * SetSeconds + (Sets - 1) * Mathf.Max(0, RestSeconds);
        public TrainingPlan(int sets, int restSeconds)
        {
            Sets = Mathf.Clamp(sets, MinSets, MaxSets);
            RestSeconds = restSeconds == 30 || restSeconds == 60 || restSeconds == 90 || restSeconds == ManualRest
                ? restSeconds : 60;
        }
        public static TrainingPlan Load() => new TrainingPlan(PlayerPrefs.GetInt("training.sets", 3), PlayerPrefs.GetInt("training.rest", 60));
        public void Save()
        { PlayerPrefs.SetInt("training.sets", Sets); PlayerPrefs.SetInt("training.rest", RestSeconds); PlayerPrefs.Save(); }
    }

    /// <summary>Workout progression independent of camera, UI and scene lifetime.</summary>
    public sealed class TrainingProgress
    {
        public TrainingPlan Plan { get; private set; }
        public int CompletedSets { get; private set; }
        public int CurrentSet => Mathf.Min(CompletedSets + 1, Plan.Sets);
        public bool IsResting { get; private set; }
        public bool IsComplete => CompletedSets >= Plan.Sets;
        public float RestRemaining { get; private set; }
        public TrainingProgress(TrainingPlan plan) { Plan = plan; }
        public void CompleteSet()
        {
            if (IsComplete || IsResting) return;
            CompletedSets++;
            IsResting = !IsComplete;
            RestRemaining = Plan.RestSeconds;
        }
        public bool TickRest(float seconds)
        {
            if (!IsResting || Plan.IsManualRest) return false;
            RestRemaining = Mathf.Max(0, RestRemaining - Mathf.Max(0, seconds));
            return RestRemaining <= 0 && Continue();
        }
        public bool Continue()
        { if (!IsResting) return false; IsResting = false; RestRemaining = 0; return true; }
        public void AddRest(int seconds)
        { if (IsResting && !Plan.IsManualRest) RestRemaining += Mathf.Max(0, seconds); }
        public bool AddSet()
        {
            if (!IsComplete || Plan.Sets >= TrainingPlan.MaxSets) return false;
            Plan = new TrainingPlan(Plan.Sets + 1, Plan.RestSeconds);
            return true;
        }
    }
}
