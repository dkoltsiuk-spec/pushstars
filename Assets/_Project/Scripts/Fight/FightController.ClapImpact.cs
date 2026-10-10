using UnityEngine;
using PushStars.Core;
using PushStars.CV;

namespace PushStars.Fight
{
    public sealed partial class FightController
    {
        private ClapImpactEffect _clapImpact;
        private PushupAvatarDriver _clapDriver;
        private bool _clapAirborne, _opponentClapsBound;

        /// <summary>Seconds between two entries of a rep timeline. The ranked server and the bot
        /// recording check refuse anything closer than 0.4 s; the margin keeps a float32 round
        /// trip from landing a hair under it and voiding the whole result.</summary>
        private const float ClapRepSpacingSec = .42f;
        /// <summary>The session's rep number of the last rep this fight accepted.</summary>
        private int _lastRepSession = -1;
        // A clap's second rep, waiting for its place on the timeline: live-clock time, < 0 none.
        private float _clapRepAt = -1f, _clapRepForm;

        /// <summary>A duel answers a clap push-up on screen and in sound. The boss battle has its
        /// own double strike; a solo set has nobody to hit and no Aura to show.</summary>
        private void BeginClapImpact()
        {
            _clapAirborne = false;
            if (_mode != FightMode.Ghost || _hud == null) return;
            if (_clapImpact == null)
            {
                _clapImpact = _hud.GetComponent<ClapImpactEffect>();
                if (_clapImpact == null) _clapImpact = _hud.gameObject.AddComponent<ClapImpactEffect>();
                _clapImpact.Bind(_hud);
            }
            _clapDriver = null;
            foreach (var driver in FindObjectsByType<PushupAvatarDriver>(FindObjectsSortMode.None))
                if (driver.Session == _session) { _clapDriver = driver; break; }
            if (_ghost != null && !_opponentClapsBound) { _ghost.OnClap += ShowOpponentClap; _opponentClapsBound = true; }
        }

        /// <summary>The recording the player is fighting just landed a clap of its own.</summary>
        private void ShowOpponentClap(int clapNumber)
        {
            if (_phase != Phase.Live || _clapImpact == null) return;
            _clapImpact.OpponentLand();
        }

        private void ReleaseClapImpact()
        {
            if (_ghost != null && _opponentClapsBound) _ghost.OnClap -= ShowOpponentClap;
            _opponentClapsBound = false;
        }

        /// <summary>A clap push-up is worth two reps. The second goes on the timeline as a rep of
        /// its own — so the score, the XP and the recording all carry it, and a ghost of this set
        /// scores the same two when it is replayed — a legal rep's distance after the first.
        /// That is usually already past when the clap confirms (0.33–0.43 s after the rep).</summary>
        private void QueueClapRep(float form)
        {
            float limit = _mode == FightMode.Training ? TrainingPlan.SetSeconds : FightConfig.DuelDurationSec;
            float last = _repTimes[_repTimes.Count - 1];
            float at = Mathf.Max(Time.time - _liveStartTime, last + ClapRepSpacingSec);
            // Clapped before the buzzer: it counts on the buzzer if a rep still fits there.
            if (at > limit) { at = limit; if (at - last < ClapRepSpacingSec - .01f) return; }
            _clapRepForm = form;
            _clapRepAt = at;
        }

        private void AddClapRep()
        {
            float at = _clapRepAt;
            _clapRepAt = -1f;
            if (_ranked != null && _repTimes.Count >= 65) return;
            _repForms.Add(_clapRepForm);
            _repTimes.Add(at);
            _hud.SetPlayerReps(_repTimes.Count);
            _hud.PunchPlayerReps();
            if (_mode == FightMode.Training && !_paused && !_layoutPaused) ShowRepMilestone(_repTimes.Count);
        }

        /// <summary>The next rep arrived while a clap's second rep was still waiting (only a rep
        /// timeline can do that, not a person): it takes its place first, or is lost.</summary>
        private void SettleClapRep(float repTime)
        {
            if (_clapRepAt < 0f) return;
            if (_clapRepAt <= repTime) AddClapRep();
            _clapRepAt = -1f;
        }

        /// <summary>The first beat: the character has left the floor. The driver has already
        /// thrown out the "flights" that are the player getting up.</summary>
        private void TickClapTakeoff()
        {
            if (_clapImpact == null || _clapDriver == null) return;
            bool airborne = _clapDriver.InClapFlight;
            if (airborne && !_clapAirborne) _clapImpact.Takeoff();
            _clapAirborne = airborne;
        }

        /// <summary>The second beat: the clap just confirmed on landing. Shows what it is worth
        /// now — the same Aura the result screen will count, capped the same way.</summary>
        private void ShowClapImpact()
        {
            if (_clapImpact == null) return;
            long aura = _clapReps <= EconomyConfig.AuraMaxClaps
                ? (long)System.Math.Round(EconomyConfig.AuraPerClap * (double)AuraCalculator.Stakes(LocalProfile.League.Id), System.MidpointRounding.AwayFromZero)
                : 0;
            _clapImpact.Land(_clapReps, EconomyConfig.AuraMaxClaps, aura, _clapDriver != null ? _clapDriver.ClapFlightRemainingSec : 0f);
        }
    }
}
