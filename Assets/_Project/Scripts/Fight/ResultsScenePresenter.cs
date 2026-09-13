using PushStars.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Shows already recorded results. Scene preview never grants or recalculates rewards.</summary>
    [DefaultExecutionOrder(350)]
    public sealed class ResultsScenePresenter : MonoBehaviour
    {
        [SerializeField] private FightResultScreen _screen;
        [SerializeField] private Button _homeButton;
        [SerializeField] private FightScreen _continueDestination = FightScreen.RewardSummary;

        private void Start()
        {
            if (_screen == null) return;
            _screen.ContinueRequested = Continue;
            if (_homeButton != null) _homeButton.onClick.AddListener(Home);
            foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
            {
                if (avatar.gameObject.scene != gameObject.scene) continue;
                avatar.SetPreparationPresentation(true);
                if (avatar.StageCamera != null) avatar.StageCamera.enabled = true;
                var animator = avatar.Character != null ? avatar.Character.GetComponentInChildren<Animator>() : null;
                if (animator != null && animator.HasState(0, Animator.StringToHash("WarriorIdle")))
                {
                    animator.Play("WarriorIdle", 0, 0f);
                    animator.Update(0f);
                }
            }
            var result = FightScreenNavigation.Result ?? new FightResultData
            {
                Mode = FightMode.Ghost, Win = true, MyReps = 21, OppReps = 18,
                MyForm = 90f, OppForm = 85f, MyRepsPerMinute = 33.3f, OppSecondsPerRep = 1.6f,
                Xp = 570, Trophies = 21, PlayerName = "BEASTCORE_DEV", OpponentName = "OSKAT009"
            };
            if (result.Mode == FightMode.Training)
                _screen.ShowTraining(result.MyReps, result.TrainingSets, result.Xp, result.NewRecord);
            else if (result.Mode == FightMode.LevelTest)
                _screen.ShowLevelTest(result.MyReps, result.FitnessTier, result.Xp, result.MyReps > 0);
            else
                _screen.ShowDuel(result.Win, result.Draw, result.MyReps, result.OppReps, result.MyForm, result.OppForm,
                    result.MyRepsPerMinute, result.OppSecondsPerRep, result.Xp, result.Trophies,
                    result.OpponentName, result.PlayerName, result.NewRecord);
        }

        private void OnDestroy()
        {
            if (_screen != null) _screen.ContinueRequested = null;
            if (_homeButton != null) _homeButton.onClick.RemoveListener(Home);
        }

        private void Continue() => FightScreenNavigation.Navigate(_continueDestination);
        private void Home() => FightScreenNavigation.Navigate(FightScreen.Home);
    }
}
