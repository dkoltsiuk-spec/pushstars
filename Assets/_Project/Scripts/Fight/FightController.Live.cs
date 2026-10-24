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
    /// opponent is <see cref="LiveOpponent"/>, fed by the link. And the end can come early.</para>
    ///
    /// <para><b>Leaving.</b> Both phones judge it the same way from their own side, and the server
    /// from Photon's record (functions/src/liveDuelRules.js):
    /// <list type="bullet">
    /// <item>Before the set — waiting for planks, or in the 3-2-1 — leaving costs nothing. The one
    /// left behind is told and, from the queue, goes straight back to searching.</item>
    /// <item>In the set, the exit button asks first; leaving is a defeat (trophies, RAGE QUIT
    /// Aura) and the opponent wins at once.</item>
    /// <item>The app in the background does not stop the minute: back within the reconnect window,
    /// the set carries on with the time lost; later, the duel is lost.</item>
    /// <item>A dropped connection: reps keep counting; out as long as the opponent waits (the
    /// link's <see cref="LiveDuelLink.CutOff"/>) and the duel is lost, as the opponent sees it won.</item>
    /// </list>
    /// A duel open when the app died is settled as a defeat on the next launch
    /// (<see cref="LiveDuelGuard"/>), so killing the app is no way out of a loss.</para>
    ///
    /// <para>Nothing about the result is trusted to the other phone. Each phone sends its own rep
    /// timeline to finishLiveDuel; the server checks both like a ranked set, cross-checks each
    /// against the count the other phone saw live, and decides.</para>
    /// </summary>
    public sealed partial class FightController
    {
        /// <summary>How a live set ended before the bell, if it did.</summary>
        private enum LiveEnd { None, OpponentLeft, Forfeit }
        private enum LiveForfeit { Exit, Away, ConnectionLost }

        private const float LiveAbortHoldSec = 2.5f;
        /// <summary>A peer finished this long before our bell ended its set early (clock skew
        /// between the phones is well under it).</summary>
        private const float LiveEarlyFinishSec = 5f;

        private LiveDuelLink _liveLink;
        private LiveOpponent _live;
        private bool _liveStartReported, _liveAborting, _liveRanked, _liveSearchAgain;
        private LiveEnd _liveEnd;
        private LiveForfeit _liveForfeit;
        private int _liveSentReps = -1, _liveSentClaps = -1, _liveSeenPeerReps = -1;
        private float _liveAbortAt, _liveSetBeganAt = float.PositiveInfinity;
        private DateTime? _liveAwayFrom;
        private LiveExitConfirm _liveExitConfirm;

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

        /// <summary>The set has begun — <paramref name="lateBy"/> seconds ago on the shared clock.
        /// From here on, leaving is a defeat, even by killing the app.</summary>
        private void OnLiveSetBegan(float lateBy)
        {
            _liveSetBeganAt = Time.realtimeSinceStartup - Mathf.Max(0f, lateBy);
            LiveDuelGuard.Open(_liveLink.MatchId, _rewardSessionId, _liveRanked, OpponentLabel);
            LiveDuelDiagnostics.Mark("fight:set-begin");
        }

        /// <summary>Live: reports the score when it moves, and ends the set early when one side has
        /// gone for good. Returns true when the set ended (or is being called off).</summary>
        private bool TickLiveConnection()
        {
            if (_liveAborting) { TickLiveAbort(); return true; }
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
            if (_liveLink.CutOff)
            {
                Debug.Log("[LiveDuel] This phone was out of the duel as long as the opponent waits — a defeat.");
                ForfeitLive(LiveForfeit.ConnectionLost);
                return true;
            }
            bool peerFinished = _liveLink.Peer != null && _liveLink.Peer.Finished;
            // Both sets run on one clock and none can be cut short by hand, so a peer done well
            // before the bell ended it on a walkover — against this phone, back too late to see it.
            if (peerFinished && FightConfig.DuelDurationSec - (Time.time - _liveStartTime) > LiveEarlyFinishSec)
            {
                Debug.Log("[LiveDuel] The opponent's set ended early: they won by walkover while this phone was away.");
                ForfeitLive(LiveForfeit.ConnectionLost);
                return true;
            }
            if (_liveLink.PeerGone && !peerFinished)
            {
                // Not heard from since before the bell rang for the set: they never took part.
                if (_liveLink.PeerHeardAt < _liveSetBeganAt)
                {
                    BeginLiveAbort($"{LivePeerName} LEFT BEFORE THE START", true);
                    return true;
                }
                _liveEnd = LiveEnd.OpponentLeft;
                Debug.Log("[LiveDuel] Opponent gone during the set — ending as a walkover.");
                Finish();
                return true;
            }
            float cutOffIn = _liveLink.CutOffInSec;
            if (cutOffIn > 0f)
                _hud.ShowBanner($"CONNECTION LOST · RECONNECTING {Mathf.CeilToInt(cutOffIn)}s", FightHud.BannerTone.Warn);
            else if (!_liveLink.Online)
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
            bool peerLeft = _liveLink.PeerGone;
            string reason = _liveLink.VersionMismatch
                ? (_liveRanked ? "YOUR OPPONENT HAS ANOTHER APP VERSION" : "YOUR FRIEND HAS ANOTHER APP VERSION — UPDATE BOTH")
                : peerLeft ? $"{LivePeerName} LEFT THE DUEL"
                : _liveLink.CutOff || _liveLink.Status == DuelLinkStatus.Closed ? "CONNECTION LOST — DUEL CALLED OFF" : null;
            if (reason == null) return false;
            BeginLiveAbort(reason, peerLeft || _liveLink.VersionMismatch);
            return true;
        }

        /// <summary>Calls the duel off: nobody wins, nothing is recorded. The banner stays up for a
        /// moment, then the screen leaves — back to the search when a stranger walked away, since
        /// the player came to fight someone.</summary>
        private void BeginLiveAbort(string reason, bool searchAgain)
        {
            if (_liveAborting) return;
            _liveAborting = true; _liveAbortAt = Time.time + LiveAbortHoldSec;
            _liveSearchAgain = searchAgain && _liveRanked;
            _liveExitConfirm?.Hide();
            LiveDuelGuard.Close();
            _hud.HideCountdown();
            _hud.ShowBanner(_liveSearchAgain ? reason + "\nFINDING A NEW OPPONENT…" : reason, FightHud.BannerTone.Warn);
            if (_session != null) _session.enabled = false;
            Debug.Log("[LiveDuel] Called off: " + reason);
            // The server already has the start: say the duel is off, or it waits for a result.
            if (_liveStartReported) SendLiveAborted(_liveLink.MatchId);
        }

        /// <summary>The exit button in the middle of a live set: leaving is a defeat, so it asks.
        /// The duel does not wait while it does.</summary>
        private bool ConfirmLiveExit()
        {
            if (_liveLink == null || _phase != Phase.Live || _liveAborting || _liveEnd != LiveEnd.None) return false;
            if (_liveExitConfirm == null) _liveExitConfirm = LiveExitConfirm.Attach(_hud.transform);
            string cost = _liveRanked && LocalProfile.Games >= EconomyConfig.AuraRookieFights
                ? $"−{EconomyConfig.TrophyLoss} trophies and Aura."
                : _liveRanked ? $"−{EconomyConfig.TrophyLoss} trophies." : "Your friend wins.";
            _liveExitConfirm.Show("LEAVE THE DUEL?", "Leaving now is a defeat:\n" + cost, () => ForfeitLive(LiveForfeit.Exit));
            return true;
        }

        /// <summary>This phone walks out of the set (or is judged to have): the duel is lost here at
        /// once, and the opponent's phone hears it and wins.</summary>
        private void ForfeitLive(LiveForfeit why)
        {
            if (_phase != Phase.Live || _liveAborting || _liveEnd != LiveEnd.None) return;
            _liveEnd = LiveEnd.Forfeit;
            _liveForfeit = why;
            _liveExitConfirm?.Hide();
            Debug.Log($"[LiveDuel] Forfeit ({why}).");
            Finish();
        }

        /// <summary>The app went to the background mid-duel and came back. The minute ran on
        /// meanwhile: the set's clock catches up instead of handing the player a rest the opponent
        /// never had. Away as long as the reconnect window and the duel is gone.</summary>
        private void OnLiveApplicationPause(bool paused)
        {
            if (paused)
            {
                if (_phase != Phase.Countdown && _phase != Phase.Live) return;
                _liveAwayFrom = DateTime.UtcNow;
                _liveLink.MarkAway(Time.realtimeSinceStartup);
                return;
            }
            if (_liveAwayFrom == null) return;
            float away = Mathf.Max(0f, (float)(DateTime.UtcNow - _liveAwayFrom.Value).TotalSeconds);
            _liveAwayFrom = null;
            if (_liveAborting || _liveEnd != LiveEnd.None) return;
            if (_phase == Phase.Countdown)
            {
                // The countdown runs on the server's clock, so it has caught up by itself. Away
                // for the window, the opponent has called the duel off by now.
                if (away >= LiveDuelLink.PeerGraceSec) BeginLiveAbort("YOU LEFT THE APP — DUEL CALLED OFF", false);
                return;
            }
            if (_phase != Phase.Live) return;
            _liveStartTime -= away;
            Debug.Log($"[LiveDuel] Back after {away:0.0}s in the background.");
            if (away >= LiveDuelLink.PeerGraceSec) ForfeitLive(LiveForfeit.Away);
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

        /// <summary>Tells the other phone this set is over and hands the timeline to the server in
        /// the background. The result screen opens at once on this phone's own count (what both
        /// phones watched live); the server checks both timelines whenever they land.</summary>
        private void FinishLiveInBackground(int myReps)
        {
            LiveDuelDiagnostics.Mark("fight:finish");
            _live?.StopPlayback();
            LiveDuelGuard.Close();
            bool forfeit = _liveEnd == LiveEnd.Forfeit;
            // A forfeit leaves without the FINISHED flag, so the other phone reads it as the
            // walkover it is, at once.
            if (forfeit) _liveLink.Leave();
            else _liveLink.Finish(myReps);
            float duration = Mathf.Clamp(Time.time - _liveStartTime, 1, FightConfig.DuelDurationSec);
            SendLiveResult(_liveLink.MatchId, _repTimes.ToArray(), _clapTimes.ToArray(), duration, _opponent.Reps,
                _liveEnd == LiveEnd.OpponentLeft, forfeit);
        }

        /// <summary>Static on purpose: it outlives the fight screen, which has moved on by then.</summary>
        internal static async void SendLiveResult(string matchId, float[] repTimes, float[] clapTimes, float duration,
            int opponentReps, bool opponentLeft, bool forfeit = false, bool aborted = false)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    var receipt = await LiveDuelClient.Finish(matchId, repTimes, clapTimes, duration, opponentReps, opponentLeft, forfeit, aborted);
                    Debug.Log($"[LiveDuel] result sent: {receipt?.state} won={receipt?.won} reps={receipt?.reps}:{receipt?.opponentReps} ({receipt?.reason})");
                    return;
                }
                catch (LeagueCallException e) when (e.Permanent) { Debug.LogWarning("[LiveDuel] result refused: " + e.Message); return; }
                catch (Exception e) { Debug.LogWarning("[LiveDuel] result: " + e.Message); }
                await Task.Delay(3000 * (attempt + 1));
            }
        }

        private static void SendLiveAborted(string matchId) =>
            SendLiveResult(matchId, Array.Empty<float>(), Array.Empty<float>(), 1f, 0, false, aborted: true);

        private string LiveStatus()
        {
            switch (_liveEnd)
            {
                case LiveEnd.OpponentLeft: return _liveRanked ? "OPPONENT LEFT THE DUEL" : "FRIEND LEFT THE DUEL";
                case LiveEnd.Forfeit:
                    return _liveForfeit == LiveForfeit.Exit ? "YOU LEFT THE DUEL"
                        : _liveForfeit == LiveForfeit.Away ? "YOU LEFT THE APP FOR TOO LONG"
                        : "CONNECTION LOST";
                default: return _liveRanked ? "LIVE DUEL" : "FRIEND DUEL";
            }
        }

        /// <summary>The same, said in two or three words for the line under the result's score.</summary>
        private string LiveEarlyEnd()
        {
            switch (_liveEnd)
            {
                case LiveEnd.OpponentLeft: return _liveRanked ? "OPPONENT LEFT" : "FRIEND LEFT";
                case LiveEnd.Forfeit:
                    return _liveForfeit == LiveForfeit.Exit ? "YOU LEFT"
                        : _liveForfeit == LiveForfeit.Away ? "YOU LEFT THE APP"
                        : "CONNECTION LOST";
                default: return null;
            }
        }

        private void ReleaseLive()
        {
            if (_live != null) _live.OnClap -= ShowOpponentClap;
            // Torn down mid-set without passing through a forfeit (the scene went some other
            // way): it is one all the same.
            if (_liveLink != null && _phase == Phase.Live && _liveEnd == LiveEnd.None && !_liveAborting) LiveDuelGuard.SettleOpen();
            // Leaving tells the friend at once; after the set it arrives behind our FINISHED flag,
            // so it never reads as a forfeit.
            _liveLink?.Leave();
        }
    }
}
