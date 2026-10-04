using System.Linq;
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
            if (_panel == null) return;
            _panel.OnReady += Ready;
            if (_homeButton != null) _homeButton.onClick.AddListener(Home);
            bool preview = FightScreenNavigation.IsPreview;
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            var achievements = ProfileAchievementCatalog.Progress(LocalProfile.Profile, LocalProfile.History, LocalProfile.BestReps, System.DateTime.Today);
            var player = new DuelReadyPanel.Side(ProfileIdentityEditor.ResolveName("YOU"),
                LocalProfile.Trophies, LocalProfile.BestReps,
                LocalProfile.Games > 0 ? LocalProfile.WinRatePercent : DuelReadyPanel.Side.Unknown,
                ProfileCountry.Code, ProfileAchievementCatalog.BestEarned(achievements).Select(i => ProfileAchievementCatalog.Entries[i].Id).ToArray());
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
            else if (FightRequest.TestBot != null)
            {
                var bot = FightRequest.TestBot;
                opponent = new DuelReadyPanel.Side(bot.displayName, bot.trophies, bot.fight.reps,
                    DuelReadyPanel.Side.Unknown, bot.countryCode, bot.achievementIds);
                opponentFlag = CountryFlags.Get(bot.countryCode);
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
            AttachEmotes(FightRequest.TestBot == null && (FightRequest.Mode != FightMode.Boss || preview));
        }

        /// <summary>The emote button over the card. The two stages are told apart by which
        /// portrait shows their render, the same test <see cref="DuelReadyPanel"/> uses.</summary>
        private void AttachEmotes(bool opponentEmotes)
        {
            var host = _panel.Root.transform as RectTransform;
            if (host == null) return;
            FightAvatar Side(bool opponent) => System.Array.Find(
                FindObjectsByType<FightAvatar>(FindObjectsSortMode.None), a => a.gameObject.scene == gameObject.scene
                    && a.StageCamera != null && _panel.OpponentAvatarSource != null
                    && (a.StageCamera.targetTexture == _panel.OpponentAvatarSource.texture) == opponent);
            Animator Body(FightAvatar a) => a != null && a.Character != null ? a.Character.GetComponentInChildren<Animator>() : null;
            var bar = EmoteBar.Attach(host, () => Body(Side(false)),
                opponentEmotes ? () => Body(Side(true)) : (System.Func<Animator>)null,
                () => Side(false)?.Character, EmoteLayout.PreparationCorner, EmoteLayout.PreparationOffset);
            if (bar != null && opponentEmotes && Random.value < .5f) bar.OpponentOpens(Random.Range(2.6f, 4f), null);
        }

        private void OnDestroy()
        {
            if (_panel != null) _panel.OnReady -= Ready;
            if (_homeButton != null) _homeButton.onClick.RemoveListener(Home);
        }

        private bool _selectingArena;
        private void Ready()
        {
            if (_selectingArena) return;
            if (FightRequest.Mode != FightMode.Ghost && !FightScreenNavigation.IsPreview)
            { FightScreenNavigation.Navigate(_readyDestination); return; }
            _selectingArena = true;
            StartCoroutine(SelectArenaAndContinue());
        }
        private System.Collections.IEnumerator SelectArenaAndContinue()
        {
            _panel.Root.SetActive(true);
            if (!ArenaMatch.HasChoices) ArenaMatch.BeginLocal(ArenaProfile.SelectedId, GhostStore.Load()?.arenaId);
            ArenaMatch.ResolveLocal();
            if (!ArenaMatch.PresentationComplete && !ArenaMatch.SameChoice)
            {
                var label = _panel.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                var popup = ArenaSelectionPopup.Create(_panel.Root.transform.parent, label != null ? label.font : null);
                yield return popup.Play();
                Destroy(popup.gameObject);
            }
            ArenaMatch.PresentationComplete = true;
            FightScreenNavigation.Navigate(_readyDestination);
        }
        private void Home() => FightScreenNavigation.Navigate(FightScreen.Home);
    }
}
