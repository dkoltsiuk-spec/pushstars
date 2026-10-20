using System;
using System.Threading.Tasks;
using PushStars.Core;
using PushStars.CV;
using PushStars.CV.AntiCheat;
using PushStars.Services;
using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>
    /// A live duel against another phone (<see cref="FightRequest.LiveDuel"/>) — a friend's by
    /// code, or a stranger's from the random queue (<see cref="FightRequest.IsRankedLive"/>).
    ///
    /// <para>The screen runs the ordinary duel with three differences. The start is shared: each
    /// phone reports its plank, the host names a moment on the server clock, and both count down
    /// to it — breaking the plank no longer cancels a start the other phone is counting to. The
    /// opponent is <see cref="LiveOpponent"/>, fed by the link. And the end can come early: a
    /// friend gone longer than the reconnect window forfeits, and this phone's set ends there.</para>
    ///
    /// <para>Nothing about the result is trusted to the other phone. Each phone sends its own rep
    /// timeline to finishLiveDuel; the server checks both like a ranked set, cross-checks each
    /// against the count the other phone saw live, and decides.</para>
    /// </summary>
    public sealed partial class FightController
    {
        private LiveDuelLink _liveLink;
        private LiveOpponent _live;
        private LiveDuelReceipt _liveReceipt;
        private bool _liveWalkover, _liveStartReported, _liveAborting, _liveRanked;
        private int _liveSentReps = -1, _liveSentClaps = -1, _liveSeenPeerReps = -1;
        private float _liveAbortAt;

        /// <summary>The set's clock checks against the server's: reps closer than 0.4 s or past 65
        /// would be refused there, so they are never counted here.</summary>
        private bool ServerTimed => _ranked != null || _liveLink != null;

        private bool TryStartLive()
        {
            var link = FightRequest.LiveDuel;
            if (link == null || link.Closed) return false;
            _liveLink = link;
            _liveRanked = FightRequest.IsRankedLive;
            _live = gameObject.AddComponent<LiveOpponent>();
            _live.Configure(link);
            _opponent = _live;
            foreach (var driver in FindObjectsByType<GhostAvatarDriver>(FindObjectsSortMode.None))
                if (driver.gameObject.scene == gameObject.scene) driver.Follow(_live);
            _live.OnClap += ShowOpponentClap;
            LiveDuelDiagnostics.Mark("fight:open");
            return true;
        }

        public bool TryGetStreamClock(out byte phase, out float time)
        {
            phase = GhostMotionClip.Setup; time = 0;
            if (_liveLink == null || _liveLink.Closed || _phase == Phase.Finished || _screenPreview) return false;
            if (_phase == Phase.Live) { phase = GhostMotionClip.Live; time = Mathf.Max(0, Time.time - _liveStartTime); }
            else time = Mathf.Max(0, Time.time - _sceneStartTime);
            return true;
        }

        public void StreamPose(GhostMotionClip.Frame frame) => _liveLink?.SendPose(frame);

        private string LivePeerName => _live != null ? _live.DisplayName : _liveRanked ? "OPPONENT" : "FRIEND";

        /// <summary>WaitPlank for a live duel. Returns true when it decided the frame.</summary>
        private bool TickLiveWaitPlank(PlankArmer armer)
        {
            if (TickLiveAbort()) return true;
            _liveLink.SetArmed(armer.IsArmed);
            if (_liveLink.HasStart)
            {
                LiveDuelDiagnostics.Mark("fight:countdown");
                _everArmed = true;
                ReportLiveStart();
                _phase = Phase.Countdown;
                _lastCountdownShown = int.MinValue;
                _hud.HideBanner();
                return true;
            }
            if (!armer.IsArmed) return false;   // the ordinary plank guidance
            _everArmed = true;
            _hud.ShowBanner(_liveLink.PeerPresent ? $"WAITING FOR {LivePeerName} TO GET INTO A PLANK"
                : $"{LivePeerName} IS RECONNECTING…", FightHud.BannerTone.Good);
            return true;
        }

        /// <summary>Counts down to the moment both phones were given. The plank still matters —
        /// reps only count from one — but losing it no longer stops the clock.</summary>
        private void TickLiveCountdown()
        {
            if (TickLiveAbort()) return;
            var armer = _session.Armer;
            _liveLink.SetArmed(armer != null && armer.IsArmed);
            float until = _liveLink.SecondsUntilStart;
            if (until > 0f)
            {
                int remain = Mathf.Min(3, Mathf.CeilToInt(until));
                if (remain != _lastCountdownShown) { _lastCountdownShown = remain; _hud.ShowCountdown(remain.ToString()); }
                if (armer != null && !armer.IsArmed) _hud.ShowBanner("GET INTO A PLANK — STARTING", FightHud.BannerTone.Warn);
                else _hud.HideBanner();
                return;
            }
            // Started late (a slow frame, a resent Start): join the clock where it already is.
            BeginLiveSet(Mathf.Min(-until, FightConfig.DuelDurationSec));
        }

        /// <summary>Live: reports the score when it moves, and ends the set early if the friend
        /// has gone for good. Returns true when the set ended.</summary>
        private bool TickLiveConnection()
        {
            if (_live.Reps != _liveSeenPeerReps)
            {
                _liveSeenPeerReps = _live.Reps;
                LiveDuelDiagnostics.Mark($"fight:set me {_repTimes.Count} peer {_liveSeenPeerReps} @{Time.time - _liveStartTime:0}s");
            }
            if (_repTimes.Count != _liveSentReps || _clapReps != _liveSentClaps)
            {
                _liveSentReps = _repTimes.Count; _liveSentClaps = _clapReps;
                LiveDuelDiagnostics.Mark($"fight:set me {_liveSentReps} peer {_live.Reps} @{Time.time - _liveStartTime:0}s");
                _liveLink.SendScore(_liveSentReps, _liveSentClaps, AverageForm(), Time.time - _liveStartTime);
            }
            _hud.SetOpponentForm(_live.FormPercent);
            _hud.SetOpponentTempo(_live.SecondsPerRep);
            bool peerFinished = _liveLink.Peer != null && _liveLink.Peer.Finished;
            if (_liveLink.PeerGone && !peerFinished)
            {
                _liveWalkover = true;
                Debug.Log("[LiveDuel] Opponent gone during the set — ending as a walkover.");
                Finish();
                return true;
            }
            if (!_liveLink.Online)
                _hud.ShowBanner("CONNECTION LOST · YOUR REPS STILL COUNT", FightHud.BannerTone.Warn);
            else if (!peerFinished && _liveLink.PeerAbsentSec > 0f)
                _hud.ShowBanner($"{LivePeerName} DISCONNECTED · {Mathf.CeilToInt(LiveDuelLink.PeerGraceSec - _liveLink.PeerAbsentSec)}s",
                    FightHud.BannerTone.Warn);
            return false;
        }

        /// <summary>Before the set: a friend who left (or an app too old to talk to) ends the
        /// duel here, with nothing to record.</summary>
        private bool TickLiveAbort()
        {
            if (_liveAborting)
            {
                if (Time.time >= _liveAbortAt) ExitToCaller();
                return true;
            }
            string reason = _liveLink.VersionMismatch
                ? (_liveRanked ? "YOUR OPPONENT HAS ANOTHER APP VERSION" : "YOUR FRIEND HAS ANOTHER APP VERSION — UPDATE BOTH")
                : _liveLink.PeerGone ? $"{LivePeerName} LEFT THE DUEL"
                : _liveLink.Status == DuelLinkStatus.Closed ? "CONNECTION CLOSED" : null;
            if (reason == null) return false;
            _liveAborting = true; _liveAbortAt = Time.time + 2.5f;
            _hud.HideCountdown();
            _hud.ShowBanner(reason, FightHud.BannerTone.Warn);
            return true;
        }

        private async void ReportLiveStart()
        {
            if (_liveStartReported) return;
            _liveStartReported = true;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { await LiveDuelClient.Start(_liveLink.MatchId); return; }
                catch (LeagueCallException e) when (e.Permanent) { Debug.LogWarning("[LiveDuel] start refused: " + e.Message); return; }
                catch (Exception e) { Debug.LogWarning("[LiveDuel] start: " + e.Message); }
                await Task.Delay(1500);
                if (this == null) return;
            }
        }

        /// <summary>Sends this phone's timeline; waits a little for the friend's to land so the
        /// result screen can show the server's verdict rather than a guess.</summary>
        private async Task FinishLiveOnServer(int myReps)
        {
            LiveDuelDiagnostics.Mark("fight:finish");
            _live?.StopPlayback();
            _liveLink.Finish(myReps);
            _hud.ShowBanner("SAVING DUEL RESULT…", FightHud.BannerTone.Warn);
            float duration = Mathf.Clamp(Time.time - _liveStartTime, 1, FightConfig.DuelDurationSec);
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    _liveReceipt = await LiveDuelClient.Finish(_liveLink.MatchId, _repTimes.ToArray(), _clapTimes.ToArray(),
                        duration, _opponent.Reps, _liveWalkover);
                    if (_liveReceipt != null && _liveReceipt.Settled) break;
                }
                catch (LeagueCallException e) when (e.Permanent) { Debug.LogWarning("[LiveDuel] result refused: " + e.Message); break; }
                catch (Exception e) { Debug.LogWarning("[LiveDuel] result: " + e.Message); }
                if (this == null) return;
                await Task.Delay(2500);
                if (this == null) return;
            }
            if (this != null) _hud.HideBanner();
        }

        private string LiveStatus()
        {
            string duel = _liveRanked ? "LIVE DUEL" : "FRIEND DUEL", who = _liveRanked ? "OPPONENT" : "FRIEND";
            return _liveReceipt == null ? $"{duel} · RESULT NOT SAVED"
                : !_liveReceipt.Settled ? (_liveWalkover ? $"{who} LEFT · RESULT PENDING" : $"{duel} · WAITING FOR {who}'S RESULT")
                : _liveReceipt.walkover ? (_liveReceipt.won ? $"{who} LEFT · WIN CONFIRMED" : "YOU LEFT · LOSS RECORDED")
                : $"{duel} · RESULT CONFIRMED";
        }

        private void ReleaseLive()
        {
            if (_live != null) _live.OnClap -= ShowOpponentClap;
            // Leaving tells the friend at once; after the set it arrives behind our FINISHED flag,
            // so it never reads as a forfeit.
            _liveLink?.Leave();
        }
    }
}
