using System;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// One live duel between two phones, from the lobby to the last rep — the room protocol on
    /// top of any <see cref="IDuelTransport"/>. Survives the scene change from the home screen's
    /// room sheet to the fight (<see cref="Active"/>, pumped by <see cref="LiveDuelRunner"/>).
    ///
    /// <para><b>Presence.</b> Each seat re-sends its <see cref="LiveDuelSeat"/> every second. The
    /// peer counts as away when the transport says it left the room <i>or</i> it has been silent
    /// for <see cref="SilenceSec"/> — a phone that froze never says goodbye. Away for
    /// <see cref="PeerGraceSec"/> (constants.md: 10 s reconnect window) means gone: the duel
    /// ends and the server settles it as a walkover. While this phone is itself offline the peer
    /// is not judged at all — we cannot know. The mirror of that is <see cref="CutOff"/>: this
    /// phone dropped out first and stayed out as long as the peer waits, so it is the one that
    /// lost — both phones reach the same verdict from their own side.</para>
    ///
    /// <para><b>Shared start.</b> Once both seats hold a plank the host names a moment on the
    /// transport's server clock <see cref="StartLeadMs"/> ahead; both count down to that same
    /// moment. Nothing after that waits on the network: the timer, the counting and the result
    /// are local, and the server compares the two timelines afterwards.</para>
    /// </summary>
    public sealed class LiveDuelLink
    {
        public const int MaxReps = 65;            // functions/src/constants.js MAX_REPS_PER_MATCH
        public const int StartLeadMs = 4000;      // 3-2-1 plus a second for the message to land
        public const float PeerGraceSec = 10f, SilenceSec = 3f, HeartbeatSec = 1f;
        public const float PoseIntervalSec = 1f / 12f;

        public static LiveDuelLink Active { get; private set; }

        public readonly string MatchId, RoomName;
        public readonly bool IsHost;
        public readonly LiveDuelSeat Local;
        public readonly LivePoseBuffer PeerPose = new LivePoseBuffer();
        public readonly IDuelTransport Transport;
        public LiveDuelSeat Peer { get; private set; }
        public bool PeerQuit { get; private set; }
        public bool VersionMismatch { get; private set; }
        public bool HasStart { get; private set; }
        public int StartAtMs { get; private set; }
        public int PeerReps { get; private set; }
        public int PeerClaps { get; private set; }
        public float PeerForm { get; private set; }
        public float PeerElapsed { get; private set; }
        public bool Closed { get; private set; }

        public DuelLinkStatus Status => Transport.Status;
        public bool Online => Transport.Status == DuelLinkStatus.InRoom;
        public bool PeerPresent => Peer != null && Online && Transport.PeerInRoom && _now - _peerHeardAt < SilenceSec;
        public float PeerAbsentSec => Peer == null || PeerPresent || _peerAwaySince < 0 ? 0f : _now - _peerAwaySince;
        /// <summary>The peer said goodbye, or has been away for the whole grace. Never while this
        /// phone is itself the likelier one to have dropped (<see cref="CutOff"/>).</summary>
        public bool PeerGone => !CutOff && (PeerQuit || !Suspect && PeerAbsentSec >= PeerGraceSec);
        /// <summary>When the peer was last heard from (the clock passed to <see cref="Tick"/>).</summary>
        public float PeerHeardAt => _peerHeardAt;
        /// <summary>This phone dropped out — lost the room, or the app went to the background
        /// (<see cref="MarkAway"/>) — before the peer had gone quiet, and has heard nothing for as
        /// long as the peer waits before ending the set: silence plus grace, counted from the last
        /// message, as the peer counts from the last one it heard. On the other phone the duel is
        /// already over by walkover. Stays true once reached: an empty room afterwards must not
        /// read as the peer having left.</summary>
        public bool CutOff { get; private set; }
        /// <summary>Seconds left before <see cref="CutOff"/> while this phone is out; 0 when it is not.</summary>
        public float CutOffInSec => Suspect && !CutOff ? Mathf.Max(0f, _peerHeardAt + SilenceSec + PeerGraceSec - _now) : 0f;
        // Out since before the peer went quiet: until we hear the peer again, the drop is ours.
        private bool Suspect => _offlineSince >= 0 && _offlineSince <= _peerHeardAt + SilenceSec;
        public float SecondsUntilStart => HasStart ? unchecked(StartAtMs - Transport.ServerTimeMs) / 1000f : float.PositiveInfinity;

        private float _now, _peerHeardAt = float.NegativeInfinity, _peerAwaySince = -1f, _nextHeartbeat, _offlineSince = -1f;
        private DuelLinkStatus _lastStatus = DuelLinkStatus.Idle;

        public LiveDuelLink(IDuelTransport transport, string matchId, string roomName, bool isHost, LiveDuelSeat local)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            MatchId = matchId; RoomName = roomName; IsHost = isHost; Local = local ?? new LiveDuelSeat();
            Transport.Received += OnReceived;
        }

        public void Open(string authToken, float now)
        {
            _now = now;
            Transport.Open(RoomName, Local.Uid, authToken);
        }

        public static void SetActive(LiveDuelLink link)
        {
            if (Active != null && Active != link) Active.Leave();
            Active = link;
            if (link != null) LiveDuelRunner.Ensure();
        }

        public void Tick(float now)
        {
            if (Closed) return;
            _now = now;
            Transport.Service();
            var status = Transport.Status;
            if (status != _lastStatus)
            {
                _lastStatus = status;
                // Back in the room: say who we are, and (host) when the duel starts, at once —
                // the peer may have missed both while we were away.
                if (status == DuelLinkStatus.InRoom) { SendState(); if (IsHost && HasStart) SendStart(); }
            }
            if (status == DuelLinkStatus.InRoom && now >= _nextHeartbeat) SendState();
            TrackPresence();
            TrackCutOff();
            PeerPose.Advance(now);

            if (IsHost && !HasStart && Local.Armed && Peer != null && Peer.Armed && PeerPresent && !PeerQuit)
            {
                StartAtMs = unchecked(Transport.ServerTimeMs + StartLeadMs);
                HasStart = true;
                SendStart();
            }
        }

        private void TrackPresence()
        {
            if (Peer == null || !Online) return;      // offline here: nothing to judge the peer by
            if (PeerPresent) _peerAwaySince = -1f;
            else if (_peerAwaySince < 0) _peerAwaySince = _now;
        }

        private void TrackCutOff()
        {
            if (Peer == null || CutOff) return;
            if (!Online) MarkAway(_now);
            if (_offlineSince >= 0 && _peerHeardAt > _offlineSince) _offlineSince = -1f;   // heard again: back in time
            if (Suspect && _now - _peerHeardAt >= SilenceSec + PeerGraceSec) CutOff = true;
        }

        /// <summary>This phone stopped taking part at <paramref name="since"/> (the app went to the
        /// background) even if the socket has not noticed yet: the peer hears nothing from then on.</summary>
        public void MarkAway(float since)
        {
            if (Peer != null && _offlineSince < 0) _offlineSince = since;
        }

        public void SetReady(bool ready) { if (Local.Ready == ready) return; Local.Ready = ready; SendState(); }
        public void SetArmed(bool armed) { if (Local.Armed == armed) return; Local.Armed = armed; SendState(); }

        public void SendScore(int reps, int claps, float form, float elapsed)
        {
            if (!Online) return;   // the next heartbeat-free score after reconnecting carries the total
            Transport.Send(LiveDuelProtocol.Score, LiveDuelProtocol.EncodeScore(new LiveDuelProtocol.ScoreUpdate
            { Reps = Mathf.Clamp(reps, 0, MaxReps), Claps = Mathf.Clamp(claps, 0, MaxReps), Form = form, Elapsed = Mathf.Max(0, elapsed) }), true);
        }

        public void SendPose(GhostMotionClip.Frame frame)
        {
            if (!Online || !Transport.PeerInRoom) return;
            Transport.Send(LiveDuelProtocol.Pose, LivePoseCodec.Encode(frame), false);
        }

        public void Finish(int reps)
        {
            Local.Finished = true; Local.FinalReps = Mathf.Clamp(reps, 0, MaxReps);
            SendState();
        }

        /// <summary>Leaving on purpose: the peer hears it at once rather than after the grace.</summary>
        public void Leave()
        {
            if (Closed) return;
            if (Online) Transport.Send(LiveDuelProtocol.Bye, Array.Empty<byte>(), true);
            Closed = true;
            Transport.Received -= OnReceived;
            Transport.Close();
            if (Active == this) Active = null;
        }

        private void SendState()
        {
            _nextHeartbeat = _now + HeartbeatSec;
            if (Online) Transport.Send(LiveDuelProtocol.State, LiveDuelProtocol.EncodeState(Local), true);
        }

        private void SendStart() { if (Online) Transport.Send(LiveDuelProtocol.Start, LiveDuelProtocol.EncodeStart(StartAtMs), true); }

        private void OnReceived(byte code, byte[] payload)
        {
            if (Closed) return;
            switch (code)
            {
                case LiveDuelProtocol.State:
                    if (LiveDuelProtocol.TryDecodeState(payload, out var seat, out bool mismatch))
                    {
                        Peer = seat; _peerHeardAt = _now; PeerQuit = false;
                        if (seat.Finished) PeerReps = Math.Max(PeerReps, seat.FinalReps);
                    }
                    else if (mismatch) VersionMismatch = true;
                    break;
                case LiveDuelProtocol.Start:
                    // Only the host schedules; the first time named stands, so a resent Start
                    // after a reconnect cannot move a countdown already running.
                    if (!IsHost && !HasStart && LiveDuelProtocol.TryDecodeStart(payload, out int at)) { StartAtMs = at; HasStart = true; }
                    _peerHeardAt = _now;
                    break;
                case LiveDuelProtocol.Score:
                    if (LiveDuelProtocol.TryDecodeScore(payload, out var score))
                    {
                        // Reliable and ordered, but a resend after a reconnect may repeat an old
                        // total: the count only ever goes up.
                        PeerReps = Math.Max(PeerReps, score.Reps); PeerClaps = Math.Max(PeerClaps, score.Claps);
                        PeerForm = score.Form; PeerElapsed = Math.Max(PeerElapsed, score.Elapsed);
                        _peerHeardAt = _now;
                    }
                    break;
                case LiveDuelProtocol.Pose:
                    if (LivePoseCodec.TryDecode(payload, out var frame)) { PeerPose.Add(frame, _now); _peerHeardAt = _now; }
                    break;
                case LiveDuelProtocol.Bye:
                    PeerQuit = true;
                    break;
            }
        }
    }

    /// <summary>Keeps the <see cref="LiveDuelLink.Active"/> link pumped while scenes change under it.</summary>
    public sealed class LiveDuelRunner : MonoBehaviour
    {
        private static LiveDuelRunner _instance;
        public static void Ensure()
        {
            if (_instance != null || !Application.isPlaying) return;
            var go = new GameObject("LiveDuelRunner");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<LiveDuelRunner>();
        }
        private void Update()
        {
            var link = LiveDuelLink.Active;
            if (link == null) { LiveDuelDiagnostics.Stop(); return; }
            LiveDuelDiagnostics.Beat();
            link.Tick(Time.realtimeSinceStartup);
        }
        private void OnApplicationQuit() => LiveDuelLink.Active?.Leave();
        // A paused app is not a frozen one: the watch restarts on the first frame back.
        private void OnApplicationPause(bool paused) { if (paused) LiveDuelDiagnostics.Stop(); }
    }
}
