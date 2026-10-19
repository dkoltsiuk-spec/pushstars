using System;
using UnityEngine;
using PushStars.Core;

namespace PushStars.Fight
{
    /// <summary>
    /// A friend on another phone as a duel opponent: the score their phone reports and the body
    /// their camera sees, both arriving over <see cref="LiveDuelLink"/>. To the fight screen it is
    /// one more <see cref="IOpponentFeed"/> next to <see cref="GhostOpponent"/> and
    /// <see cref="BossOpponent"/>; to the avatar it is one more <see cref="IOpponentPose"/>.
    ///
    /// <para>The count is never inferred here — no rep is invented between reports and none is
    /// taken back. What this phone shows is what the other phone counted, a few hundred
    /// milliseconds late; the server compares the two full timelines once both are in.</para>
    /// </summary>
    public sealed class LiveOpponent : MonoBehaviour, IOpponentFeed, IOpponentPose
    {
        private LiveDuelLink _link;
        private bool _stopped;
        private float _lastElapsed;

        public string DisplayName => string.IsNullOrEmpty(_link?.Peer?.Name) ? "FRIEND" : _link.Peer.Name;
        public int Reps { get; private set; }
        public int Claps { get; private set; }
        public int ExpectedReps => 0;   // nobody knows a live opponent's score in advance
        public float FormPercent => _link != null ? _link.PeerForm : 0f;
        public float SecondsPerRep => Reps > 0 ? Mathf.Max(_lastElapsed, _link?.PeerElapsed ?? 0) / Reps : 0f;

        public event Action<int> OnRep;
        public event Action<int> OnClap;

        public void Configure(LiveDuelLink link) { _link = link; _stopped = false; }

        public void Begin() { Reps = Claps = 0; _lastElapsed = 0; }

        public void Tick(float elapsedSec)
        {
            if (_link == null) return;
            _lastElapsed = elapsedSec;
            while (Reps < _link.PeerReps) OnRep?.Invoke(++Reps);
            while (Claps < _link.PeerClaps) OnClap?.Invoke(++Claps);
        }

        public void StopPlayback() => _stopped = true;

        // ── IOpponentPose ─────────────────────────────────────────────────────────────────────
        public GhostMotionClip Motion => _link?.PeerPose.Clip;
        public byte MotionPhase => _link != null ? _link.PeerPose.Phase : GhostMotionClip.Setup;
        public float Elapsed => _link != null ? _link.PeerPose.PlaybackTime : 0f;
        public bool PlaybackActive => !_stopped && _link != null;
        public bool HasPose => PlaybackActive && _link.PeerPose.HasPose;
        // Until the first pose lands the body stands in the idle, not mid push-up.
        public bool IsWorking => false;
        public float Depth01 => 0f;
    }
}
