using PushStars.Core;
using PushStars.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Fills an authored preparation scene; its button chooses where the player goes next.</summary>
    [DefaultExecutionOrder(350)]
    public sealed class PreparationScreen : MonoBehaviour
    {
        [SerializeField] private DuelReadyPanel _panel;
        [SerializeField] private Button _homeButton;
        [SerializeField] private FightScreen _readyDestination = FightScreen.Battle;

        private void Start()
        {
            FightScreenNavigation.BeginPreparation();
            foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
            {
                if (avatar.gameObject.scene != gameObject.scene) continue;
                avatar.SetPreparationPresentation(true);
                if (avatar.StageCamera != null) avatar.StageCamera.enabled = true;
            }
            if (_panel == null) return;
            _panel.OnReady += Ready;
            if (_homeButton != null) _homeButton.onClick.AddListener(Home);
            bool preview = FightScreenNavigation.IsPreview;
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            var player = new DuelReadyPanel.Side(ProfileIdentityEditor.ResolveName("YOU"),
                LocalProfile.Trophies, LocalProfile.BestReps,
                LocalProfile.Games > 0 ? LocalProfile.WinRatePercent : DuelReadyPanel.Side.Unknown);
            DuelReadyPanel.Side opponent;
            Sprite opponentFlag;
            if (!preview && FightRequest.Mode == FightMode.Boss)
            {
                var boss = BossCatalog.Current;
                opponent = new DuelReadyPanel.Side(boss.DisplayName, DuelReadyPanel.Side.Unknown,
                    boss.RepTimes.Count, DuelReadyPanel.Side.Unknown);
                opponentFlag = null;
                FightScreenNavigation.PreparedOpponent = null;
            }
            else if (preview)
            {
                var identity = FightScreenNavigation.PreparedOpponent ??
                    new MockupProfile.Opponent("OSKAT009", MockupProfile.Flag.Germany, 98, 32, 52);
                FightScreenNavigation.PreparedOpponent = identity;
                opponent = new DuelReadyPanel.Side(identity.Name, identity.Trophies, identity.BestReps, identity.WinRate);
                opponentFlag = MockupProfile.FlagSprite(identity.Flag, theme);
            }
            else
            {
                opponent = new DuelReadyPanel.Side(FightConfig.GhostOpponentName, DuelReadyPanel.Side.Unknown,
                    GhostStore.Load()?.reps ?? 0, DuelReadyPanel.Side.Unknown);
                opponentFlag = null;
            }
            _panel.Show(player, opponent, null, opponentFlag, !preview);
        }

        private void OnDestroy()
        {
            if (_panel != null) _panel.OnReady -= Ready;
            if (_homeButton != null) _homeButton.onClick.RemoveListener(Home);
        }

        private void Ready() => FightScreenNavigation.Navigate(_readyDestination);
        private void Home() => FightScreenNavigation.Navigate(FightScreen.Home);
    }
}
