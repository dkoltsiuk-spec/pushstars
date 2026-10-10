using UnityEngine;
using PushStars.Core;
using PushStars.CV;

namespace PushStars.Fight
{
    public sealed partial class FightController
    {
        private ClapImpactEffect _clapImpact;
        private PushupAvatarDriver _clapDriver;
        private bool _clapAirborne;

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
            _hud.PunchPlayerReps();
        }
    }
}
