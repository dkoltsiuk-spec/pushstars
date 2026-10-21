using System;
using System.Collections.Generic;

namespace PushStars.Core
{
    /// <summary>
    /// Two seats wired to each other in memory: the network as a test sees it. Messages land at
    /// the receiver's next <see cref="Service"/>; <see cref="SetConnected"/> drops a seat out of
    /// the room and back (what a lost signal looks like from both ends), and
    /// <see cref="DropUnreliable"/> loses the pose stream the way UDP may.
    /// </summary>
    public sealed class LoopbackDuelTransport : IDuelTransport
    {
        private sealed class Room { public int ClockMs; }
        private readonly Room _room;
        private LoopbackDuelTransport _peer;
        private readonly Queue<(byte code, byte[] payload)> _inbox = new Queue<(byte, byte[])>();
        private bool _opened, _connected = true;

        public DuelLinkStatus Status { get; private set; } = DuelLinkStatus.Idle;
        public string Error { get; private set; } = "";
        public int ServerTimeMs => _room.ClockMs;
        public bool PeerInRoom => _peer != null && _peer.Status == DuelLinkStatus.InRoom;
        public bool DropUnreliable;
        public int Sent { get; private set; }
        public string OpenedRoom { get; private set; }
        public event Action<byte, byte[]> Received;

        private LoopbackDuelTransport(Room room) { _room = room; }

        public static (LoopbackDuelTransport host, LoopbackDuelTransport guest) Pair()
        {
            var room = new Room();
            var a = new LoopbackDuelTransport(room);
            var b = new LoopbackDuelTransport(room);
            a._peer = b; b._peer = a;
            return (a, b);
        }

        public void AdvanceClock(int ms) => _room.ClockMs = unchecked(_room.ClockMs + ms);

        public void Connect(string userId, string authToken) => Connected = true;
        public bool Connected { get; private set; }

        public void Open(string roomName, string userId, string authToken)
        {
            OpenedRoom = roomName; _opened = true;
            Status = _connected ? DuelLinkStatus.InRoom : DuelLinkStatus.Connecting;
        }

        public void SetConnected(bool connected)
        {
            _connected = connected;
            if (!_opened || Status == DuelLinkStatus.Closed) return;
            Status = connected ? DuelLinkStatus.InRoom : DuelLinkStatus.Reconnecting;
            if (!connected) _inbox.Clear();
        }

        public void Send(byte code, byte[] payload, bool reliable)
        {
            if (Status != DuelLinkStatus.InRoom) throw new InvalidOperationException("Sent outside the room.");
            Sent++;
            if (!reliable && DropUnreliable) return;
            if (_peer.Status == DuelLinkStatus.InRoom) _peer._inbox.Enqueue((code, (byte[])payload.Clone()));
        }

        public void Service()
        {
            while (_inbox.Count > 0 && Status == DuelLinkStatus.InRoom)
            {
                var (code, payload) = _inbox.Dequeue();
                Received?.Invoke(code, payload);
            }
        }

        public void Close() { Status = DuelLinkStatus.Closed; _inbox.Clear(); }
    }
}
