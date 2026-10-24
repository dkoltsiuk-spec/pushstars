using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PushStars.Core;
using PushStars.OTA;
using PushStars.Services;

namespace PushStars.UI
{
    /// <summary>
    /// Drives the full-screen "ПОИСК СОПЕРНИКА" (Search Opponent) overlay on the main screen.
    ///
    ///   • НАЙТИ СОПЕРНИКА (the Duel-panel primary CTA) opens the overlay with a calm,
    ///     staggered Brawl-Stars-style entrance (see <see cref="JuicyScreen"/>) while the
    ///     main screen (character + UI) recedes back and fades — one screen leaves, the
    ///     other arrives.
    ///   • ВЫЙТИ (the red <see cref="ExitButton"/>) snaps it shut and brings the main screen
    ///     back forward.
    ///   • While open, <see cref="LoadingVsRing"/> spins the dashed VS ring.
    ///
    /// The spinner is not pure cosmetics — it is the search: with a live transport in the build
    /// (<see cref="LiveDuelService.Available"/>) the player waits in the random live queue for as
    /// long as they like, and two players searching at the same time meet in a ranked live duel.
    /// While few people play, the screen keeps the wait comfortable (<see cref="SearchLobbyView"/>):
    /// after a few seconds it offers a fight right away — another player's best set at the
    /// player's level, shown as that player, else the player's own shadow — and "notify me" lets
    /// the player close the game: the next player who searches calls them with a push
    /// (functions/src/push.js), and the app opened on that call goes straight back into the search.
    /// A call that arrives while the app is open on the main screen shows a callout above START instead,
    /// and so does anyone searching right now (the server's lobby) for every player on the main screen.
    /// The found-state UI is built at runtime from the overlay's own Title label, so the
    /// serialized Main scene needs no regeneration.
    ///
    /// Lives on a small always-active GameObject so it keeps running while the overlay
    /// itself is toggled on/off.
    /// </summary>
    public class SearchOpponentController : MonoBehaviour
    {
        [Header("Overlay (toggled root)")]
        [SerializeField] private GameObject  _overlay;
        [SerializeField] private JuicyScreen _juicy; // transition choreography

        [Header("Main screen (recedes behind the overlay)")]
        [SerializeField] private RectTransform _mainContent; // scaled back on open
        [SerializeField] private CanvasGroup   _mainGroup;   // faded on open
        [SerializeField] private float _mainRecedeScale = 0.92f;
        [SerializeField] private float _mainRecedeTime  = 0.34f;
        [SerializeField] private float _mainReturnTime  = 0.30f;

        [Header("Triggers")]
        [SerializeField] private Button _findButton; // НАЙТИ СОПЕРНИКА — opens
        [SerializeField] private Button _exitButton; // ВЫЙТИ — closes
        [SerializeField] private FriendDuelController _friendDuel;

        [Header("Animation")]
        [SerializeField] private LoadingVsRing _ring;

        /// <summary>The offer card comes up after this long (or at once without a connection).</summary>
        private const float OfferDelaySec = 4f, OfferLatestSec = 8f;
        /// <summary>How long FIGHT waits for an offered bot's recording before it fights the set alone.</summary>
        private const float BotLoadSec = 6f;
        /// <summary>"Players today" shows from this many: a handful reads as an empty game.</summary>
        private const int PlayersTodayMin = 20;
        /// <summary>A call older than this is not acted on: the caller has stopped waiting.</summary>
        private const long CallWindowMs = 90_000;
        /// <summary>How long "notify me" keeps the player callable after leaving (LIVE_QUEUE_STANDBY_SEC).</summary>
        private const long StandbyMs = 1_800_000;
        private const string KeyNotify = "search.notify", KeyStandbyUntil = "queue.standbyUntil", KeyHandledCall = "queue.handledCall";

        private bool      _open;
        private Coroutine _mainAnim;

        // ── Boss matchmaking state (phase 08.9) ──
        private Coroutine _searchRoutine;
        private LiveQueueSearch _liveSearch; // the live queue part of the search, while it runs
        private TextMeshProUGUI _title;      // the overlay's "ПОИСК СОПЕРНИКА" label (found by name)
        private string _titleOriginalText;
        private TextMeshProUGUI _bossLabel;  // runtime clone of the title showing the boss name

        // ── Slow-launch comfort: offer, "notify me", calls ──
        private SearchLobbyView _lobby;
        private bool _notify, _offerTaken, _unreachable, _asking;
        private LiveQueueGhost _offer;       // a stranger at the player's level; null → own shadow
        private System.Threading.Tasks.Task<BotRecording> _botLoad;   // the offered pool bot's recording
        private string _caller;              // the player whose call opened this search
        private IDisposable _callWatch;
        private CallBanner _banner;
        private DuelHourBadge _duelHour;

        // ── Someone searching right now (the server's lobby), shown to every main screen ──
        private IDisposable _lobbyWatch;
        private string _lobbyUid;            // the search whose callout was shown (once per search)
        private long _lobbySince = -1, _lobbyUntil;
        private bool _bannerFromLobby;

        // Wire in Start (not Awake) so every Button/ExitButton has finished its own Awake,
        // mirroring DuelModeController's ordering safeguard.
        private void Start()
        {
            if (_findButton != null) _findButton.onClick.AddListener(Show);
            if (_exitButton != null) _exitButton.onClick.AddListener(Hide);

            // Default closed — the main screen is visible first.
            if (_overlay != null) _overlay.SetActive(false);

            if (_findButton != null) _duelHour = DuelHourBadge.Attach(_findButton.transform as RectTransform);
            PushTokens.Received += OnPush;
            WatchCalls();
            CheckCall();
            StartCoroutine(WatchLobby());
            // Back from a queue duel the stranger walked out of before it began: keep searching.
            if (FightRequest.ConsumeSearchAgain() && LiveDuelService.Available) OpenSearch(null);
        }

        // A lobby callout goes once its search has stopped refreshing (the phone that searched died).
        private void Update()
        {
            if (_bannerFromLobby && CallBanner.Showing && NowMs() > _lobbyUntil) HideLobbyBanner();
        }

        /// <summary>Opens the matchmaking overlay; the main screen recedes behind it.</summary>
        public void Show()
        {
            if (_open || _overlay == null) return;
            // A private room owns this action; it must never fall through to ghost search.
            if (_friendDuel != null && _friendDuel.HandleBattle()) return;
            if (SelectedGameMode.Current != GameMode.Pvp)
            {
                string scene = SelectedGameMode.Current == GameMode.Boss
                    ? FightConfig.PreparationSceneName : FightConfig.TrainingSceneName;
                if (!Application.CanStreamedLevelBeLoaded(scene))
                {
                    Debug.LogError($"[SearchOpponent] {scene} is missing from Build Settings.");
                    return;
                }
                if (SelectedGameMode.Current == GameMode.Boss) FightRequest.Boss();
                else FightRequest.Training(TrainingPlan.Load());
                OtaSceneLoader.LoadScene(scene);
                return;
            }
            OpenSearch(null);
        }

        /// <summary>The duel search itself — from the main CTA, or from another player's call
        /// (whatever mode the main screen has selected: the caller is waiting for a duel).</summary>
        private void OpenSearch(string caller)
        {
            if (_open || _overlay == null) return;
            _open = true;
            _caller = caller;
            _banner?.Hide();

            SearchScreenAppearance.Ensure(_overlay);
            if (_lobby == null && (_lobby = SearchLobbyView.Ensure(_overlay)) != null)
            {
                _lobby.BellTapped += OnBell;
                _lobby.OfferTapped += () => _offerTaken = true;
            }
            _lobby?.ResetView();
            _notify = PlayerPrefs.GetInt(KeyNotify, 0) == 1 && new PlayerPrefsSettingsStore().NotificationsEnabled
                      && NotificationAccess.Current() == NotificationAccess.State.Granted;
            _offer = null;
            _botLoad = null;
            _offerTaken = _unreachable = false;
            if (_notify) MarkStandby();
            RefreshBell();

            _overlay.SetActive(true);
            _ring?.SetVisible(true);
            _overlay.GetComponentInChildren<SearchOrbit>(true)?.Play();

            if (_juicy != null) _juicy.PlayIn();
            RecedeMain(true);

            ResetFoundCard();
            _searchRoutine = StartCoroutine(SearchRoutine());
        }

        /// <summary>Closes the overlay (quick scale-down + fade); the main screen returns.</summary>
        public void Hide()
        {
            if (!_open || _overlay == null) return;
            _open = false;

            if (_searchRoutine != null)
            {
                StopCoroutine(_searchRoutine);
                _searchRoutine = null;
                Analytics.SearchEnded("cancel", Elapsed(), _notify);
            }
            // Leaving the screen leaves the queue, or the room a stranger is waiting in. With
            // "notify me" on the player stays callable: the main screen shows a call above START.
            _liveSearch?.Cancel(standby: _notify, busy: false);
            _liveSearch = null;
            if (_notify) MarkStandby(); else PlayerPrefs.DeleteKey(KeyStandbyUntil);
            ResetFoundCard();

            if (_juicy != null) _juicy.PlayOut(() => { if (_overlay != null) _overlay.SetActive(false); });
            else                _overlay.SetActive(false);

            RecedeMain(false);
        }

        // ── Matchmaking: live queue → "OPPONENT FOUND: <имя>" → Fight scene ─────────────
        //
        // First a real person: someone pressing search at the same time. While nobody comes the
        // search simply goes on — the player can take the offered fight whenever they like: another
        // player's best set close to their level, else their own (a duel against your strongest
        // self, fought in real time). A player with no recording yet is sent to the level test
        // instead of being told "nobody found" — the search screen always ends in a set.
        private float _searchStarted;
        private float Elapsed() => Time.realtimeSinceStartup - _searchStarted;

        private IEnumerator SearchRoutine()
        {
            _searchStarted = Time.realtimeSinceStartup;
            var ghost = GhostStore.Load();
            if (!LiveDuelService.Available || !Application.CanStreamedLevelBeLoaded(FightConfig.PreparationSceneName))
            {
                // No live transport in this build: the ring spins a moment, then the shadow.
                yield return new WaitForSeconds(FightConfig.SearchDelaySec);
                yield return FightOffer(ghost);
                yield break;
            }

            int best = ghost?.reps ?? LocalProfile.BestReps;
            LiveQueueSearch search = null;
            float retryAt = 0f;
            while (true)
            {
                float now = Time.realtimeSinceStartup, elapsed = now - _searchStarted;
                if (search == null && now >= retryAt) search = _liveSearch = LiveDuelService.Search(best, () => _notify);
                if (search != null)
                {
                    search.Tick(now);
                    // The card on screen is the fight FIGHT takes: a later search's offer does not swap it.
                    if (search.Offer != null && search.Offer != _offer && (_lobby == null || !_lobby.OfferShown))
                    {
                        _offer = search.Offer;
                        // A bot's set comes with its motion — fetched now, so FIGHT does not wait.
                        _botLoad = _offer.IsBot ? PoolBotClient.Prefetch(_offer) : null;
                    }
                    if (search.State == LiveQueueSearch.Result.Live)
                    {
                        // Both phones are in the room: the preparation screen's READY opens the
                        // fight once both have pressed it.
                        ShowFoundCard(search.OpponentName);
                        PlayerPrefs.DeleteKey(KeyStandbyUntil);
                        Analytics.SearchEnded("live", elapsed, _notify);
                        yield return new WaitForSeconds(FightConfig.FoundPauseSec);
                        _liveSearch = null;
                        _searchRoutine = null;
                        FightRequest.Live(search.Link, ranked: true);
                        OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
                        yield break;
                    }
                    if (search.State == LiveQueueSearch.Result.Fallback)
                    {
                        // No queue to reach, or a stranger who paired but never came to the room:
                        // keep searching — after a pause when the network is out.
                        _unreachable = search.Unreachable;
                        search = _liveSearch = null;
                        retryAt = now + (_unreachable ? 5f : .5f);
                    }
                    else if (search.Answered) _unreachable = false;
                }
                UpdateStatus(elapsed, search);
                bool answered = search == null || search.Answered;
                if (_lobby != null && !_lobby.OfferShown &&
                    (_unreachable || elapsed >= OfferLatestSec || elapsed >= OfferDelaySec && answered))
                    ShowOffer(ghost);
                if (_offerTaken) break;
                yield return null;
            }

            // The player took the offered fight. With "notify me" on they stay callable: the queue
            // keeps their place (not during this fight) and the main screen listens for a call.
            _liveSearch?.Cancel(standby: _notify);
            _liveSearch = null;
            if (_notify) MarkStandby(); else PlayerPrefs.DeleteKey(KeyStandbyUntil);
            yield return FightOffer(ghost);
        }

        private void ShowOffer(GhostRecord ghost)
        {
            if (_offer != null && _offer.IsValid)
                // A recorded set, shown as the player it is — their name, best set and trophies.
                _lobby.ShowOffer("OPPONENT AT YOUR LEVEL", _offer.name,
                    Localization.Text($"Best set: {_offer.repTimes.Length}"), _offer.trophies, "FIGHT");
            else if (ghost != null)
                _lobby.ShowOffer("WHILE WE SEARCH", Localization.Text(FightConfig.GhostOpponentName),
                    Localization.Text($"Beat your record: {ghost.reps}"), null, "FIGHT");
            else
                _lobby.ShowOffer("NO RECORD YET", Localization.Text("Level test"),
                    Localization.Text("60 seconds, then a duel with your ghost"), null, "START");
        }

        // Status under the title: how long, and one reason to stay — a call made, the duel hour,
        // or how many played today (only when that number is not embarrassing).
        private void UpdateStatus(float elapsed, LiveQueueSearch search)
        {
            if (_lobby == null) return;
            int seconds = Mathf.FloorToInt(elapsed);
            string time = $"{seconds / 60}:{seconds % 60:00}";
            string extra = null;
            var now = DateTime.Now;
            int? today = OnlinePresence.Instance != null ? OnlinePresence.Instance.Today : null;
            if (_unreachable) extra = Localization.Text("No connection to the arena");
            else if (!string.IsNullOrEmpty(_caller) && elapsed < 15f) extra = Localization.Text($"{_caller} is waiting for you!");
            else if (search != null && search.CalledAt >= 0f && Time.realtimeSinceStartup - search.CalledAt < 40f)
                extra = Localization.Text("We called a player — waiting");
            else if (DuelHour.IsOn(now)) extra = Localization.Text($"Duel hour until {DuelHour.EndOf(now):HH:mm}");
            else if (today.HasValue && today.Value >= PlayersTodayMin)
                extra = Localization.Text($"{today.Value} players in the game today");
            _lobby.SetStatus(extra == null ? time : time + "  ·  " + extra);
        }

        private IEnumerator FightOffer(GhostRecord ghost)
        {
            if (!Application.CanStreamedLevelBeLoaded(FightConfig.FightSceneName) ||
                !Application.CanStreamedLevelBeLoaded(FightConfig.PreparationSceneName))
            {
                // Fight scene absent from this build (stale build settings) — stay in the
                // searching state instead of hard-failing; ВЫЙТИ still works.
                Debug.LogError("[SearchOpponent] Fight or FightPreparation is missing from Build Settings.");
                _searchRoutine = null;
                yield break;
            }
            float elapsed = Elapsed();

            if (_offer != null && _offer.IsValid && _offer.IsBot)
            {
                // Shown and fought as the player it plays. Met once: it leaves this player's search.
                ShowFoundCard($"{_offer.name}  ·  {_offer.repTimes.Length}");
                Analytics.SearchEnded("bot", elapsed, _notify);
                float waitUntil = Time.realtimeSinceStartup + BotLoadSec;
                yield return new WaitForSeconds(FightConfig.FoundPauseSec);
                while (_botLoad != null && !_botLoad.IsCompleted && Time.realtimeSinceStartup < waitUntil) yield return null;
                PoolBotClient.MarkFought(_offer.botId);
                if (_botLoad != null && _botLoad.IsCompleted && _botLoad.Result != null)
                {
                    _searchRoutine = null;
                    FightRequest.PoolBot(_botLoad.Result);
                    OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
                    yield break;
                }
                // No recording in time: the same set without its motion, still under the bot's name.
                var plain = GhostRecord.From(_offer.repTimes, 0, "stranger");
                plain.clapTimes = _offer.clapTimes ?? Array.Empty<float>();
                if (plain.IsValid)
                {
                    _searchRoutine = null;
                    FightRequest.StrangerGhost(plain, _offer.name);
                    OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
                    yield break;
                }
            }

            if (_offer != null && _offer.IsValid)
            {
                var record = GhostRecord.From(_offer.repTimes, 0, "stranger");
                record.clapTimes = _offer.clapTimes ?? Array.Empty<float>();
                if (record.IsValid)
                {
                    ShowFoundCard($"{_offer.name}  ·  {record.reps}");
                    Analytics.SearchEnded("stranger", elapsed, _notify);
                    yield return new WaitForSeconds(FightConfig.FoundPauseSec);
                    _searchRoutine = null;
                    FightRequest.StrangerGhost(record, _offer.name);
                    OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
                    yield break;
                }
            }

            if (ghost == null)
            {
                ShowFoundCard("TAKE AN ASSESSMENT FIRST");
                Analytics.SearchEnded("assessment", elapsed, _notify);
                yield return new WaitForSeconds(FightConfig.FoundPauseSec);
                _searchRoutine = null;
                FightRequest.LevelTest();
                OtaSceneLoader.LoadScene(FightConfig.FightSceneName);
                yield break;
            }

            ShowFoundCard($"{FightConfig.GhostOpponentName}  ·  {ghost.reps}");
            Analytics.SearchEnded("shadow", elapsed, _notify);
            yield return new WaitForSeconds(FightConfig.FoundPauseSec);

            _searchRoutine = null;
            FightRequest.Ghost();
            OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
        }

        // ── "Notify me when an opponent is found" ───────────────────────────────────────────

        private void OnBell()
        {
            if (_asking) return;
            if (_notify)
            {
                _notify = false;
                PlayerPrefs.SetInt(KeyNotify, 0);
                PlayerPrefs.DeleteKey(KeyStandbyUntil);
                RefreshBell();
                return;
            }
            if (NotificationAccess.Current() == NotificationAccess.State.Denied) { NotificationAccess.OpenSettings(); return; }
            StartCoroutine(EnableNotify());
        }

        private IEnumerator EnableNotify()
        {
            _asking = true;
            var ask = NotificationAccess.Ask();
            while (!ask.IsCompleted) yield return null;
            _asking = false;
            bool granted = ask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && ask.Result;
            Analytics.SearchNotify(granted);
            if (granted)
            {
                _notify = true;
                PlayerPrefs.SetInt(KeyNotify, 1);
                // Switching "notify me" on is an explicit yes to notifications.
                var settings = new PlayerPrefsSettingsStore();
                if (!settings.NotificationsEnabled) settings.NotificationsEnabled = true;
                _ = PushTokens.Enable();
                MarkStandby();
            }
            PlayerPrefs.Save();
            RefreshBell();
        }

        private void RefreshBell()
        {
            _lobby?.SetBell(_notify ? SearchLobbyView.Bell.On
                : NotificationAccess.Current() == NotificationAccess.State.Denied ? SearchLobbyView.Bell.Blocked
                : SearchLobbyView.Bell.Off);
        }

        private void MarkStandby()
        {
            PlayerPrefs.SetString(KeyStandbyUntil, (NowMs() + StandbyMs).ToString());
            PlayerPrefs.Save();
            WatchCalls();
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static bool StandingBy() =>
            long.TryParse(PlayerPrefs.GetString(KeyStandbyUntil, ""), out long until) && until > NowMs();

        // ── Calls from another player's search ─────────────────────────────────────────────

        /// <summary>The app came up (or back) — maybe on the push: a fresh call opens the search.</summary>
        private async void CheckCall()
        {
            if (!StandingBy()) return;
            QueueCall call;
            try { call = await LiveDuelClient.ReadCall(); }
            catch (Exception e) { Debug.LogWarning("[SearchOpponent] call: " + e.GetBaseException().Message); return; }
            if (this == null || !Unhandled(call)) return;
            Handle(call);
            Analytics.QueueCall("opened");
            OpenSearch(call.CallerName);
        }

        /// <summary>While the player stands by, a call arriving with the app open shows a banner.</summary>
        private void WatchCalls()
        {
            if (_callWatch != null || !StandingBy()) return;
            _callWatch = LiveDuelClient.WatchCall(OnCall);
        }

        private void OnCall(QueueCall call)
        {
            if (this == null || !Unhandled(call)) return;
            Handle(call);
            if (_open) return; // searching already: the pairing comes by itself
            Analytics.QueueCall("banner");
            ShowBanner(call.CallerName);
        }

        private void OnPush(string kind, IDictionary<string, string> data, bool opened)
        {
            if (kind != "queue" || this == null) return;
            long.TryParse(data.TryGetValue("at", out var at) ? at : "", out long atMs);
            var call = new QueueCall(atMs, data.TryGetValue("name", out var name) ? name : "");
            if (!Unhandled(call)) return;
            Handle(call);
            if (_open) return;
            if (opened) { Analytics.QueueCall("opened"); OpenSearch(call.CallerName); }
            else { Analytics.QueueCall("banner"); ShowBanner(call.CallerName); }
        }

        private static bool Unhandled(QueueCall call) =>
            call.Fresh(NowMs(), CallWindowMs) && long.TryParse(PlayerPrefs.GetString(KeyHandledCall, "0"), out long seen) && call.AtMs > seen;

        private static void Handle(QueueCall call)
        {
            PlayerPrefs.SetString(KeyHandledCall, call.AtMs.ToString());
            PlayerPrefs.Save();
        }

        // ── Someone is searching right now ────────────────────────────────────────────────

        /// <summary>Listens to the lobby once Firestore is up: anyone searching with nobody to pair
        /// with shows above START, so a player who is just in the game joins that fight — no "notify
        /// me" needed. The callout's FIGHT opens the search, which pairs with them at once.</summary>
        private IEnumerator WatchLobby()
        {
            while (_lobbyWatch == null)
            {
                _lobbyWatch = LiveDuelClient.WatchLobby(OnLobby);
                if (_lobbyWatch == null) yield return new WaitForSecondsRealtime(2f);
            }
        }

        private void OnLobby(LobbySearch search)
        {
            if (this == null) return;
            if (search == null || !search.Live(NowMs()) || search.Uid == LiveDuelClient.Uid)
            {
                if (_bannerFromLobby) HideLobbyBanner();
                return;
            }
            _lobbyUntil = search.UntilMs;
            // Once per search: its refreshes do not bring a dismissed callout back.
            if (_open || (search.Uid == _lobbyUid && search.SinceMs == _lobbySince)) return;
            _lobbyUid = search.Uid;
            _lobbySince = search.SinceMs;
            Analytics.QueueCall("lobby");
            ShowBanner(search.Name);
            _bannerFromLobby = true;
        }

        private void HideLobbyBanner()
        {
            _bannerFromLobby = false;
            _banner?.Hide();
        }

        private void ShowBanner(string caller)
        {
            _bannerFromLobby = false;
            if (_findButton == null) return;
            EnsureTitleRef();
            if (_banner == null)
                _banner = CallBanner.Create(_findButton.transform as RectTransform, _title != null ? _title.font : null, () => OpenSearch(null));
            string name = string.IsNullOrEmpty(caller) ? "PLAYER" : caller;
            _banner.Show(string.Format(Localization.Text("{0} is looking for a duel"), $"<color=#FFD21F>{name}</color>"));
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) return;
            // Back from the background: the push may have brought the player here.
            if (!_open) CheckCall();
            WatchCalls();
        }

        private void ShowFoundCard(string bossName)
        {
            EnsureTitleRef();
            if (_title == null) return;

            _title.text = "OPPONENT FOUND";
            _overlay.GetComponentInChildren<SearchOrbit>(true)?.Found();
            _lobby?.HideOffer();
            _lobby?.SetStatus("");

            // The boss name is a runtime clone of the title label (inherits the Rubik font and
            // styling), parked under the VS ring.
            if (_bossLabel == null)
            {
                var go = Instantiate(_title.gameObject, _title.transform.parent);
                go.name = "BossName";
                _bossLabel = go.GetComponent<TextMeshProUGUI>();
                var rt = _bossLabel.rectTransform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, -60f); // below the ring (ring centre sits at +70)
                rt.sizeDelta = new Vector2(340f, 40f);
                _bossLabel.fontSize = 26f;
                _bossLabel.color = AppColors.AccentYellow;
                _bossLabel.alignment = TextAlignmentOptions.Center;
            }
            _bossLabel.gameObject.SetActive(true);
            _bossLabel.text = bossName;
        }

        private void ResetFoundCard()
        {
            if (_title != null && !string.IsNullOrEmpty(_titleOriginalText))
                _title.text = _titleOriginalText;
            if (_bossLabel != null)
                _bossLabel.gameObject.SetActive(false);
        }

        private void EnsureTitleRef()
        {
            if (_title != null || _overlay == null) return;
            foreach (var tmp in _overlay.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp.gameObject.name != "Title") continue;
                _title = tmp;
                _titleOriginalText = tmp.text;
                break;
            }
            if (_title == null)
                Debug.LogWarning("[SearchOpponent] Title label not found in the overlay — the found-card will not render.");
        }

        // Pushes the main screen back (scale + fade) when opening, restores it when closing.
        private void RecedeMain(bool recede)
        {
            if (_mainContent == null && _mainGroup == null) return;
            if (_mainAnim != null) StopCoroutine(_mainAnim);
            _mainAnim = StartCoroutine(RecedeRoutine(recede));
        }

        private IEnumerator RecedeRoutine(bool recede)
        {
            float dur   = recede ? _mainRecedeTime : _mainReturnTime;
            float scale = recede ? _mainRecedeScale : 1f;
            float alpha = recede ? 0f : 1f;

            if (_mainContent != null)
                StartCoroutine(UITween.Scale(_mainContent, _mainContent.localScale,
                                             Vector3.one * scale, dur, 0f, UITween.EaseOutCubic));
            if (_mainGroup != null)
                yield return UITween.Fade(_mainGroup, _mainGroup.alpha, alpha, dur, 0f);
            else
                yield return null;

            _mainAnim = null;
        }

        private void OnDestroy()
        {
            _liveSearch?.Cancel(standby: _notify, busy: false);
            _callWatch?.Dispose();
            _lobbyWatch?.Dispose();
            PushTokens.Received -= OnPush;
            if (_findButton != null) _findButton.onClick.RemoveListener(Show);
            if (_exitButton != null) _exitButton.onClick.RemoveListener(Hide);
        }
    }
}
