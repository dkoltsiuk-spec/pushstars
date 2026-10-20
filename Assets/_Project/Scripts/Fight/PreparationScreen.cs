using System.Linq;
using PushStars.Core;
using PushStars.Services;
using PushStars.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Fills an authored preparation scene; its button chooses where the player goes next.
    /// For a live duel from the queue the screen is a ready check: READY tells the other phone,
    /// and the fight opens on both once both have pressed it.</summary>
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
            var player = new DuelReadyPanel.Side(ProfileIdentityEditor.PlayerName(),
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
            else if (FightRequest.IsLiveDuel)
            {
                _live = FightRequest.LiveDuel;
                var peer = _live.Peer;
                opponent = new DuelReadyPanel.Side(string.IsNullOrEmpty(peer?.Name) ? "OPPONENT" : peer.Name,
                    DuelReadyPanel.Side.Unknown, DuelReadyPanel.Side.Unknown, DuelReadyPanel.Side.Unknown, peer?.Country);
                opponentFlag = null;
                _liveDeadline = Time.realtimeSinceStartup + LiveReadySec;
                LiveDuelDiagnostics.Mark("prep");
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
            else if (FightRequest.Stranger != null)
            {
                opponent = new DuelReadyPanel.Side(FightRequest.StrangerName, DuelReadyPanel.Side.Unknown,
                    FightRequest.Stranger.reps, DuelReadyPanel.Side.Unknown);
                opponentFlag = null;
            }
            else
            {
                opponent = new DuelReadyPanel.Side(FightConfig.GhostOpponentName, DuelReadyPanel.Side.Unknown,
                    GhostStore.Load()?.reps ?? 0, DuelReadyPanel.Side.Unknown);
                opponentFlag = null;
            }
            _panel.Show(player, opponent, null, opponentFlag, !preview);
            AttachEmotes(FightRequest.TestBot == null && _live == null && (FightRequest.Mode != FightMode.Boss || preview));
        }

        /// <summary>The emote button over the card. The two stages are told apart by which
        /// portrait shows their render, the same test <see cref="DuelReadyPanel"/> uses.</summary>
        private void AttachEmotes(bool opponentEmotes)
        {
            if (!EmoteCatalog.Enabled) return;
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

        // ── Live ready check ──────────────────────────────────────────────────────────────────
        private const float LiveReadySec = 40f;
        private LiveDuelLink _live;
        private float _liveDeadline;
        private bool _liveLeaving;

        private void Update()
        {
            if (_live == null || _liveLeaving) return;
            var peer = _live.Peer;
            bool gone = _live.Closed || _live.Status == DuelLinkStatus.Closed || _live.PeerGone || _live.VersionMismatch;
            if (gone || Time.realtimeSinceStartup >= _liveDeadline)
            {
                Debug.Log($"[LiveDuel] Ready check ended without a fight (gone={gone}).");
                LeaveLive();
                return;
            }
            if (!_live.Local.Ready) return;
            if (peer == null || !peer.Ready)
            {
                _panel.SetReadyCaption("WAITING…", false);
                return;
            }
            _liveLeaving = true;   // both ready: the fight takes the link over
            _panel.SetReadyCaption("GO!", false);
            FightScreenNavigation.Navigate(_readyDestination);
        }

        /// <summary>The opponent left, never confirmed, or the player went home: the match is
        /// released and nobody is charged for it.</summary>
        private void LeaveLive()
        {
            if (_live == null || _liveLeaving) return;
            _liveLeaving = true;
            var link = _live;
            link.Leave();
            CancelMatch(link.MatchId);
            FightScreenNavigation.Navigate(FightScreen.Home);
        }

        private static async void CancelMatch(string matchId)
        {
            try { await LiveDuelClient.Cancel(matchId); }
            catch (System.Exception e) { Debug.LogWarning("[LiveDuel] cancel: " + e.Message); }
        }

        private bool _selectingArena;
        private void Ready()
        {
            if (_live != null)
            {
                if (_liveLeaving) return;
                _live.SetReady(true);
                _panel.SetReadyCaption("WAITING…", false);
                LiveDuelDiagnostics.Mark("prep:ready");
                return;
            }
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
        private void Home()
        {
            if (_live != null) { LeaveLive(); return; }
            FightScreenNavigation.Navigate(FightScreen.Home);
        }
    }
}
