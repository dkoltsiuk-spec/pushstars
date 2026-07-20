using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using PushStars.Core;
using PushStars.CV;
using PushStars.CV.AntiCheat;

namespace PushStars.Fight
{
    /// <summary>
    /// State machine of the boss duel (phase 08.9): WaitPlank → Countdown → Live → Finished.
    ///
    ///   • WaitPlank — the CV session runs, the HUD shows the "встань в планку" guidance
    ///     (same reason mapping the debug HUD proved on device). The duel does not start
    ///     until <see cref="PlankArmer"/> confirms the plank.
    ///   • Countdown — 3-2-1 once armed; breaking the plank drops back to WaitPlank.
    ///   • Live — the timer runs, the opponent feed ticks, reps count relative to the
    ///     baseline captured at start (pre-duel warm-up reps never leak in).
    ///   • Finished — winner by rep count, XP by economy rules, boss ladder advances.
    ///
    /// The opponent is any <see cref="IOpponentFeed"/>; today that's <see cref="BossOpponent"/>,
    /// in phase 12.5 a ghost recording or pacer bot slots in with zero changes here.
    /// </summary>
    public sealed class FightController : MonoBehaviour
    {
        private enum Phase { WaitPlank, Countdown, Live, Finished }

        [SerializeField] private PushupSession _session;
        [SerializeField] private BossOpponent _opponent;
        [SerializeField] private FightHud _hud;
        [SerializeField] private FightResultScreen _result;
        [SerializeField] private UnityEngine.UI.Button _exitButton;

        private Phase _phase = Phase.WaitPlank;
        private float _countdownEndTime;
        private int _lastCountdownShown = int.MinValue;
        private float _liveStartTime;
        private int _baselineReps;
        private readonly List<float> _repForms = new List<float>();

        private void Start()
        {
            _opponent.Configure(BossCatalog.Current);
            _hud.SetBossName(_opponent.DisplayName);
            _hud.SetPlayerReps(0);
            _hud.SetBossReps(0);
            _hud.SetTimer(FightConfig.DuelDurationSec);

            if (_session != null) _session.OnRep += HandleRep;
            if (_exitButton != null) _exitButton.onClick.AddListener(ExitToMain);
        }

        private void OnDestroy()
        {
            if (_session != null) _session.OnRep -= HandleRep;
            if (_exitButton != null) _exitButton.onClick.RemoveListener(ExitToMain);
        }

        private void HandleRep(int totalReps)
        {
            if (_phase != Phase.Live) return;
            _repForms.Add(_session.Form);
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
                _phase = Phase.Countdown;
                _countdownEndTime = Time.time + FightConfig.CountdownSec;
                _lastCountdownShown = int.MinValue;
                _hud.HideBanner();
                return;
            }

            if (armer.State == PlankArmerState.Arming)
                _hud.ShowBanner($"ДЕРЖИ ПЛАНКУ…  {armer.ArmingProgress01 * 100f:0}%", FightHud.BannerTone.Good);
            else
                _hud.ShowBanner(HintFor(armer.LastRejectReason), FightHud.BannerTone.Warn);
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
            _opponent.Begin();
            _hud.FlashGo();
        }

        // ── Live ─────────────────────────────────────────────────────────────────────────────────
        private void TickLive()
        {
            float elapsed = Time.time - _liveStartTime;

            _opponent.Tick(elapsed);
            _hud.SetBossReps(_opponent.Reps);
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

            int myReps = _session.Reps - _baselineReps;
            int bossReps = _opponent.Reps;
            bool win = myReps > bossReps;
            bool draw = myReps == bossReps;

            // XP by economy rules: per-rep form-weighted XP + the win bonus. Daily-cap carryover
            // and streak multipliers need server state — they arrive with phase 11.5's sync.
            long xp = XpCalculator.XpForReps(_repForms);
            if (win) xp += FightConfig.BossWinXpBonus;
            OfflineXpBank.Add(xp);
            BossCatalog.ReportResult(win);

            _result.Show(win, draw, myReps, bossReps, xp, _opponent.DisplayName);
        }

        private void ExitToMain() => SceneManager.LoadScene(FightConfig.MainSceneName);

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
