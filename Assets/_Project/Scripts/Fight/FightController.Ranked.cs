using System;
using PushStars.Core;
using PushStars.Services;
using UnityEngine;

namespace PushStars.Fight
{
    public sealed partial class FightController
    {
        private RankedSession _ranked;
        private RankedReceipt _rankedReceipt;
        private bool _rankedStarting, _rankedStarted;
        private float _rankedRetryAt;

        private GhostRecord RankedOpponentRecord()
        {
            var record = GhostRecord.From(_ranked.opponentTimes, 0, "ranked");
            // Absent from sessions issued before the server kept claps.
            record.clapTimes = _ranked.opponentClapTimes ?? Array.Empty<float>();
            return record;
        }

        private void TakeRankedSession()
        {
            if (FightRequest.IsBotTest) { _ranked = null; return; }
            _ranked = LeagueClient.PreparedSession;
            LeagueClient.PreparedSession = null;
            if (_ranked != null && _ranked.uid != LeagueClient.Uid) _ranked = null;
        }
        private async void StartRankedClock()
        {
            if (_rankedStarting || Time.realtimeSinceStartup < _rankedRetryAt) return;
            _rankedStarting = true;
            _hud.ShowBanner("CONNECTING RANKED SET…", FightHud.BannerTone.Warn);
            try
            {
                await LeagueClient.Start(_ranked);
                if (this != null) { _rankedStarted = true; _hud.HideBanner(); }
            }
            catch (Exception e)
            {
                if (this != null)
                {
                    _rankedRetryAt = Time.realtimeSinceStartup + 5;
                    _hud.ShowBanner("RANKED CONNECTION FAILED · RETRYING…", FightHud.BannerTone.Warn);
                    Debug.LogWarning("[Ranked] " + e.Message);
                }
            }
            finally { if (this != null) _rankedStarting = false; }
        }
        private async void ReleaseUnstartedRanked()
        {
            if (_ranked == null || _rankedStarted) return;
            try { await LeagueClient.Cancel(_ranked); }
            catch (Exception e) { Debug.LogWarning("[Ranked] " + e.Message); }
        }
    }
}
