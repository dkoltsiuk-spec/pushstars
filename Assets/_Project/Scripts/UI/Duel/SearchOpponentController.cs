using System.Collections;
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
    /// The spinner is not pure cosmetics — it is the search ladder: with a live transport in the
    /// build (<see cref="LiveDuelService.Available"/>) the player waits in the random live queue
    /// for up to <see cref="LiveQueueSearch.SearchSec"/>; two players searching at the same moment
    /// meet in a ranked live duel. When nobody comes, the opponent is another player's best set
    /// (handed back by the queue), else the player's own best recording (<see cref="GhostStore"/>).
    /// Either way the card flips to OPPONENT FOUND and the fight loads. The found-state UI is built
    /// at runtime from the overlay's own Title label, so the serialized Main scene needs no
    /// regeneration.
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

        private bool      _open;
        private Coroutine _mainAnim;

        // ── Boss matchmaking state (phase 08.9) ──
        private Coroutine _searchRoutine;
        private LiveQueueSearch _liveSearch; // the live queue part of the search, while it runs
        private TextMeshProUGUI _title;      // the overlay's "ПОИСК СОПЕРНИКА" label (found by name)
        private string _titleOriginalText;
        private TextMeshProUGUI _bossLabel;  // runtime clone of the title showing the boss name

        // Wire in Start (not Awake) so every Button/ExitButton has finished its own Awake,
        // mirroring DuelModeController's ordering safeguard.
        private void Start()
        {
            if (_findButton != null) _findButton.onClick.AddListener(Show);
            if (_exitButton != null) _exitButton.onClick.AddListener(Hide);

            // Default closed — the main screen is visible first.
            if (_overlay != null) _overlay.SetActive(false);
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
            _open = true;

            SearchScreenAppearance.Ensure(_overlay);
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

            if (_searchRoutine != null) { StopCoroutine(_searchRoutine); _searchRoutine = null; }
            // Leaving the screen leaves the queue, or the room a stranger is waiting in.
            _liveSearch?.Cancel();
            _liveSearch = null;
            ResetFoundCard();

            if (_juicy != null) _juicy.PlayOut(() => { if (_overlay != null) _overlay.SetActive(false); });
            else                _overlay.SetActive(false);

            RecedeMain(false);
        }

        // ── Matchmaking: live queue → "OPPONENT FOUND: <имя>" → Fight scene ─────────────
        //
        // First a real person: someone pressing search at the same moment. Nobody → another
        // player's best set, else the player's own (a duel against your strongest self, fought in
        // real time). A player with no recording yet is sent to the level test instead of being
        // told "nobody found" — the search screen always ends in a set.
        private IEnumerator SearchRoutine()
        {
            float started = Time.realtimeSinceStartup;
            LiveQueueGhost stranger = null;
            if (LiveDuelService.Available && Application.CanStreamedLevelBeLoaded(FightConfig.PreparationSceneName))
            {
                var search = _liveSearch = LiveDuelService.Search(GhostStore.Load()?.reps ?? LocalProfile.BestReps);
                while (search.State == LiveQueueSearch.Result.Searching)
                {
                    search.Tick(Time.realtimeSinceStartup);
                    yield return null;
                }
                if (search.State == LiveQueueSearch.Result.Live)
                {
                    // Both phones are in the room: the preparation screen's READY opens the fight
                    // once both have pressed it.
                    ShowFoundCard(search.OpponentName);
                    yield return new WaitForSeconds(FightConfig.FoundPauseSec);
                    _liveSearch = null;
                    _searchRoutine = null;
                    FightRequest.Live(search.Link, ranked: true);
                    OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
                    yield break;
                }
                _liveSearch = null;
                stranger = search.Ghost;
            }
            // The ring always spins a moment, even when the queue answered at once.
            float rest = FightConfig.SearchDelaySec - (Time.realtimeSinceStartup - started);
            if (rest > 0f) yield return new WaitForSeconds(rest);

            if (!Application.CanStreamedLevelBeLoaded(FightConfig.FightSceneName) ||
                !Application.CanStreamedLevelBeLoaded(FightConfig.PreparationSceneName))
            {
                // Fight scene absent from this build (stale build settings) — stay in the
                // searching state instead of hard-failing; ВЫЙТИ still works.
                Debug.LogError("[SearchOpponent] Fight or FightPreparation is missing from Build Settings.");
                _searchRoutine = null;
                yield break;
            }

            if (stranger != null)
            {
                var record = GhostRecord.From(stranger.repTimes, 0, "stranger");
                record.clapTimes = stranger.clapTimes ?? System.Array.Empty<float>();
                if (record.IsValid)
                {
                    ShowFoundCard($"{stranger.name}  ·  {record.reps}");
                    yield return new WaitForSeconds(FightConfig.FoundPauseSec);
                    _searchRoutine = null;
                    FightRequest.StrangerGhost(record, stranger.name);
                    OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
                    yield break;
                }
            }

            var ghost = GhostStore.Load();
            if (ghost == null)
            {
                ShowFoundCard("TAKE AN ASSESSMENT FIRST");
                yield return new WaitForSeconds(FightConfig.FoundPauseSec);
                _searchRoutine = null;
                FightRequest.LevelTest();
                OtaSceneLoader.LoadScene(FightConfig.FightSceneName);
                yield break;
            }

            ShowFoundCard($"{FightConfig.GhostOpponentName}  ·  {ghost.reps}");
            yield return new WaitForSeconds(FightConfig.FoundPauseSec);

            _searchRoutine = null;
            FightRequest.Ghost();
            OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
        }

        private void ShowFoundCard(string bossName)
        {
            EnsureTitleRef();
            if (_title == null) return;

            _title.text = "OPPONENT FOUND";
            _overlay.GetComponentInChildren<SearchOrbit>(true)?.Found();

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
            _liveSearch?.Cancel();
            if (_findButton != null) _findButton.onClick.RemoveListener(Show);
            if (_exitButton != null) _exitButton.onClick.RemoveListener(Hide);
        }
    }
}
