using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using PushStars.Core;
using PushStars.CV;
using PushStars.CV.AntiCheat;

namespace PushStars.Fight
{
    /// <summary>
    /// State machine of a 60-second set: WaitPlank → Countdown → Live → Finished.
    ///
    ///   • WaitPlank — the CV session runs, the HUD shows the "встань в планку" guidance
    ///     (same reason mapping the debug HUD proved on device). Nothing starts until
    ///     <see cref="PlankArmer"/> confirms the plank.
    ///   • Countdown — 3-2-1 once armed; breaking the plank drops back to WaitPlank.
    ///   • Live — the timer runs, the opponent feed ticks, reps count relative to the
    ///     baseline captured at start (pre-duel warm-up reps never leak in).
    ///   • Finished — the result, by mode.
    ///
    /// <para><b>Three modes, one screen</b> (<see cref="FightRequest"/>):
    /// <see cref="FightMode.LevelTest"/> is the onboarding measurement — no opponent, and the score
    /// becomes the player's first <see cref="GhostRecord"/> and their place on the ladder.
    /// <see cref="FightMode.Ghost"/> replays that record as the opponent. <see cref="FightMode.Boss"/>
    /// keeps the scripted PvE ladder. The opponent is any <see cref="IOpponentFeed"/>, so phase 12.5
    /// slots another player's recording in without touching this file.</para>
    ///
    /// <para><b>Every live set is recorded.</b> The rep timestamps the player produces are the ghost
    /// recording — no separate capture pass, and no way for the two to disagree about what
    /// happened. A set that beats the stored one replaces it, so the shadow you fight is always
    /// your best self.</para>
    /// </summary>
    public sealed class FightController : MonoBehaviour
    {
        private enum Phase { WaitPlank, Countdown, Live, Finished }

        [SerializeField] private PushupSession _session;
        [SerializeField] private BossOpponent _boss;
        [SerializeField] private GhostOpponent _ghost;
        [SerializeField] private FightHud _hud;
        [SerializeField] private FightResultScreen _result;
        [SerializeField] private UnityEngine.UI.Button _exitButton;
        [Tooltip("Label on the exit button. Relabelled to ПРОПУСТИТЬ when a level test cannot start.")]
        [SerializeField] private TMPro.TextMeshProUGUI _exitLabel;

        [Header("Debug")]
        [Tooltip("IMGUI diagnostics toggled together by the corner button: the tuning HUD and the " +
                 "camera preview with the detected skeleton drawn on it. Off until asked for.")]
        [SerializeField] private Behaviour[] _debugPanels;
        [SerializeField] private UnityEngine.UI.Button _debugButton;

        private Phase _phase = Phase.WaitPlank;
        private FightMode _mode;
        private IOpponentFeed _opponent;   // null in the level test
        private float _countdownEndTime;
        private int _lastCountdownShown = int.MinValue;
        private float _liveStartTime;
        private int _baselineReps;
        private readonly List<float> _repForms = new List<float>();
        private readonly List<float> _repTimes = new List<float>();

        /// <summary>How long a level test may fail to start before the screen offers a way past
        /// it. A player whose camera cannot see them never reaches the result screen — the plank
        /// never arms, so the 60 seconds never begin — and every later launch would route them
        /// straight back here. Only offered when the plank has never armed at all.</summary>
        private const float LevelTestStuckSec = 45f;
        private float _sceneStartTime;
        private bool _everArmed;
        private bool _skipOffered;

        private const int DebugTapsToReset = 5;
        private const float DebugTapWindowSec = 1.2f;
        private int _debugTaps;
        private float _debugTapWindowEnd;
        private bool _debugVisible;

        private void Start()
        {
            _sceneStartTime = Time.time;
            _mode = ResolveMode();
            ConfigureHudForMode();

            _hud.SetPlayerReps(0);
            _hud.SetTimer(FightConfig.DuelDurationSec);

            if (_session != null) _session.OnRep += HandleRep;
            if (_exitButton != null) _exitButton.onClick.AddListener(ExitToCaller);
            if (_debugButton != null) _debugButton.onClick.AddListener(ToggleDebugHud);
            SetDebugPanels(false);
        }

        private void OnDestroy()
        {
            if (_session != null) _session.OnRep -= HandleRep;
            if (_exitButton != null) _exitButton.onClick.RemoveListener(ExitToCaller);
            if (_debugButton != null) _debugButton.onClick.RemoveListener(ToggleDebugHud);
        }

        // ── Mode setup ───────────────────────────────────────────────────────────────────────────

        /// <summary>Picks the opponent for the requested mode, degrading rather than failing: a
        /// ghost duel with no recording (a build opened straight into the fight scene, a wiped
        /// profile) falls back to the boss ladder instead of putting a permanently-zero opponent
        /// on the scoreboard.</summary>
        private FightMode ResolveMode()
        {
            var mode = FightRequest.Mode;

            if (mode == FightMode.LevelTest)
            {
                _opponent = null;
                return mode;
            }

            if (mode == FightMode.Ghost)
            {
                if (_ghost != null && _ghost.Configure(GhostStore.Load()))
                {
                    _opponent = _ghost;
                    return FightMode.Ghost;
                }
                Debug.LogWarning("[Fight] Ghost duel requested with no stored recording — falling back to the boss.");
                mode = FightMode.Boss;
            }

            _boss.Configure(BossCatalog.Current);
            _opponent = _boss;
            return FightMode.Boss;
        }

        private void ConfigureHudForMode()
        {
            if (_mode == FightMode.LevelTest)
            {
                _hud.ConfigureSolo("ЗАМЕР УРОВНЯ");
                return;
            }

            _hud.ConfigureDuel(_opponent.DisplayName);
            _hud.SetBossReps(0);
        }

        /// <summary>One tap shows the diagnostics — the tuning HUD and the camera preview with the
        /// detected skeleton on it, which is the only way to see what the phone is actually
        /// pointed at now that the fight screen draws the character instead of the feed. Five
        /// quick taps wipe the first-run state and
        /// restart from Boot — without it, re-testing onboarding on a TestFlight build means
        /// deleting and reinstalling the app for every run.</summary>
        private void ToggleDebugHud()
        {
            if (Time.unscaledTime > _debugTapWindowEnd) _debugTaps = 0;
            _debugTapWindowEnd = Time.unscaledTime + DebugTapWindowSec;

            if (++_debugTaps >= DebugTapsToReset)
            {
                _debugTaps = 0;
                OnboardingState.Reset();
                LocalProfile.Reset();
                Debug.Log("[Fight] First-run state wiped — restarting from Boot.");
                SceneManager.LoadScene(FightConfig.BootSceneName);
                return;
            }

            SetDebugPanels(!_debugVisible);
        }

        private void SetDebugPanels(bool visible)
        {
            _debugVisible = visible;
            if (_debugPanels == null) return;
            foreach (var panel in _debugPanels)
                if (panel != null) panel.enabled = visible;
        }

        // ── Live counting ────────────────────────────────────────────────────────────────────────

        private void HandleRep(int totalReps)
        {
            if (_phase != Phase.Live) return;
            _repForms.Add(_session.Form);
            _repTimes.Add(Time.time - _liveStartTime);
            _hud.SetPlayerReps(totalReps - _baselineReps);
        }

        private void Update()
        {
            if (_session == null) return;
            switch (_phase)
            {
                case Phase.WaitPlank:  TickWaitPlank();  break;
                case Phase.Countdown:  TickCountdown();  break;
                case Phase.Live:       TickLive();       break;
            }
        }

        // ── WaitPlank ────────────────────────────────────────────────────────────────────────────
        private void TickWaitPlank()
        {
            var armer = _session.Armer;
            if (armer == null)
            {
                _hud.ShowBanner("ИНИЦИАЛИЗАЦИЯ…", FightHud.BannerTone.Warn);
                return;
            }

            if (armer.IsArmed)
            {
                _everArmed = true;
                _phase = Phase.Countdown;
                _countdownEndTime = Time.time + FightConfig.CountdownSec;
                _lastCountdownShown = int.MinValue;
                _hud.HideBanner();
                return;
            }

            OfferSkipIfStuck();

            if (armer.State == PlankArmerState.Arming)
                _hud.ShowBanner($"ДЕРЖИ ПЛАНКУ…  {armer.ArmingProgress01 * 100f:0}%", FightHud.BannerTone.Good);
            else
                _hud.ShowBanner(_skipOffered
                    ? HintFor(armer.LastRejectReason) + "\nили нажми ПРОПУСТИТЬ"
                    : HintFor(armer.LastRejectReason), FightHud.BannerTone.Warn);
        }

        private void OfferSkipIfStuck()
        {
            if (_skipOffered || _everArmed || _mode != FightMode.LevelTest) return;
            if (Time.time - _sceneStartTime < LevelTestStuckSec) return;

            _skipOffered = true;
            if (_exitLabel != null) _exitLabel.text = "ПРОПУСТИТЬ";
            Debug.LogWarning("[Fight] Level test never armed — offering a skip.");
        }

        // ── Countdown ────────────────────────────────────────────────────────────────────────────
        private void TickCountdown()
        {
            var armer = _session.Armer;
            if (armer == null || !armer.IsArmed)
            {
                // Plank broke during the count — back to guidance.
                _phase = Phase.WaitPlank;
                _hud.HideCountdown();
                return;
            }

            int remain = Mathf.CeilToInt(_countdownEndTime - Time.time);
            if (remain > 0)
            {
                if (remain != _lastCountdownShown)
                {
                    _lastCountdownShown = remain;
                    _hud.ShowCountdown(remain.ToString());
                }
                return;
            }

            // Go live. Baseline excludes any reps done while getting set.
            _phase = Phase.Live;
            _liveStartTime = Time.time;
            _baselineReps = _session.Reps;
            _repForms.Clear();
            _repTimes.Clear();
            _opponent?.Begin();
            _hud.FlashGo();
        }

        // ── Live ─────────────────────────────────────────────────────────────────────────────────
        private void TickLive()
        {
            float elapsed = Time.time - _liveStartTime;

            if (_opponent != null)
            {
                _opponent.Tick(elapsed);
                _hud.SetBossReps(_opponent.Reps);
            }
            _hud.SetForm(_session.Form);

            float remain = FightConfig.DuelDurationSec - elapsed;
            _hud.SetTimer(Mathf.Max(0, Mathf.CeilToInt(remain)));

            // Live guidance: the timer never pauses (a duel is a duel), but the HUD says loudly
            // why counting stopped so the player can fix it mid-fight.
            var armer = _session.Armer;
            if (armer != null && !armer.IsArmed)
                _hud.ShowBanner(armer.State == PlankArmerState.Arming
                    ? $"ДЕРЖИ ПЛАНКУ…  {armer.ArmingProgress01 * 100f:0}%"
                    : "ВЕРНИСЬ В ПЛАНКУ — СЧЁТ НА ПАУЗЕ", FightHud.BannerTone.Warn);
            else if (_session.WristAnchor.LastVerdict == AnchorVerdict.Airborne)
                _hud.ShowBanner("СЧЁТ НА ПАУЗЕ — ладони на пол", FightHud.BannerTone.Warn);
            else
                _hud.HideBanner();

            if (remain <= 0f) Finish();
        }

        // ── Finished ─────────────────────────────────────────────────────────────────────────────
        private void Finish()
        {
            _phase = Phase.Finished;
            _hud.HideBanner();
            _hud.HideCountdown();

            int myReps = _session.Reps - _baselineReps;

            // XP by economy rules: per-rep form-weighted XP. Daily-cap carryover and streak
            // multipliers need server state — they arrive with phase 11.5's sync.
            long xp = XpCalculator.XpForReps(_repForms);

            if (_mode == FightMode.LevelTest) FinishLevelTest(myReps, xp);
            else                              FinishDuel(myReps, xp);
        }

        private void FinishLevelTest(int myReps, long xp)
        {
            if (myReps >= EconomyConfig.MinSessionReps) xp += EconomyConfig.SessionCompleteXp;

            bool recorded = GhostStore.SaveIfBest(NewRecord("calibration"));

            // A zero is not a measurement — the camera never saw a rep. Leaving the flag unset
            // sends the player back through the test rather than stamping them as a beginner;
            // the result screen's ПРОПУСТИТЬ is the way out for a device that simply cannot see them.
            if (myReps > 0)
            {
                OnboardingState.CompleteLevelTest(myReps);
                LocalProfile.SeedFromLevelTest(myReps);
                OfflineXpBank.Add(xp);
            }

            _result.ShowLevelTest(myReps, FitnessTest.TierFor(myReps), xp, recorded);
        }

        private void FinishDuel(int myReps, long xp)
        {
            int oppReps = _opponent.Reps;
            bool win = myReps > oppReps;
            bool draw = myReps == oppReps;
            bool ghost = _mode == FightMode.Ghost;

            if (win) xp += FightConfig.BossWinXpBonus;
            OfflineXpBank.Add(xp);
            LocalProfile.RecordSet(myReps);
            int trophies = LocalProfile.ApplyDuelResult(win, draw, ghost);

            // Beating your shadow makes a new shadow: the record always tracks the best set, so the
            // next duel is against the version of you that just won.
            bool newRecord = GhostStore.SaveIfBest(NewRecord("duel"));

            if (!ghost) BossCatalog.ReportResult(win);

            _result.ShowDuel(win, draw, myReps, oppReps, xp, trophies,
                             _opponent.DisplayName, newRecord);
        }

        private GhostRecord NewRecord(string source)
        {
            float avgForm = 0f;
            if (_repForms.Count > 0)
            {
                foreach (float f in _repForms) avgForm += f;
                avgForm /= _repForms.Count;
            }
            return GhostRecord.From(_repTimes.ToArray(), avgForm, source);
        }

        private void ExitToCaller()
        {
            // Leaving a level test that was offered as skippable accepts the zero, so the router
            // stops sending the player back into a measurement their device cannot take. Leaving
            // one that simply hasn't started yet changes nothing — they get it again next launch.
            if (_skipOffered && _mode == FightMode.LevelTest)
                OnboardingState.CompleteLevelTest(0);

            SceneManager.LoadScene(FightRequest.ReturnScene);
        }

        /// <summary>Same wording the on-device debug HUD converged on (phase 08.1).</summary>
        private static string HintFor(PlankRejectReason reason) => reason switch
        {
            PlankRejectReason.TrackingLost        => "НЕ ВИЖУ ТЕБЯ — отойди на 1.5–2 метра",
            PlankRejectReason.TooCloseOrFar       => "ВСТАНЬ В 1.5–2 МЕТРАХ ОТ ТЕЛЕФОНА",
            PlankRejectReason.BadFraming          => "ПОМЕСТИСЬ В КАДР — голова и обе ладони видны",
            PlankRejectReason.PhoneTilted         => "ПОСТАВЬ ТЕЛЕФОН РОВНЕЕ",
            PlankRejectReason.HipNotVisible       => "НЕ ВИДНО КОРПУС — поправь кадр",
            PlankRejectReason.BodyIncline         => "ПРИМИ УПОР ЛЁЖА",
            PlankRejectReason.LowerBodyNotVisible => "ОТОЙДИ — НЕ ВИДНО НОГ",
            PlankRejectReason.BodySagging         => "ВЫПРЯМИ ТЕЛО",
            PlankRejectReason.KneesBent           => "ВЫТЯНИ ТЕЛО — колени не под собой",
            PlankRejectReason.NotAtTop            => "ВЫПРЯМИ РУКИ",
            PlankRejectReason.WristsAirborne      => "ПОСТАВЬ ЛАДОНИ НА ПОЛ",
            _                                     => "ВСТАНЬ В ПЛАНКУ ПЕРЕД КАМЕРОЙ",
        };
    }
}
