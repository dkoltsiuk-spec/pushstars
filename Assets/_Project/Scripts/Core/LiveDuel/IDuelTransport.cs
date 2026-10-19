using System;

namespace PushStars.Core
{
    public enum DuelLinkStatus { Idle, Connecting, InRoom, Reconnecting, Closed }

    /// <summary>
    /// A two-seat room and a pipe between its seats — everything a live duel needs from the
    /// network and nothing more. The Photon adapter (PushStars.Photon, behind PUSHSTARS_PHOTON)
    /// is one implementation; <see cref="LoopbackDuelTransport"/> is the other, so the room
    /// protocol in <see cref="LiveDuelLink"/> is tested without a server.
    ///
    /// <para>The transport reports state; it does not interpret messages. Presence, readiness,
    /// the shared start and the forfeit rule all live in the link, the same on every transport.</para>
    /// </summary>
    public interface IDuelTransport
    {
        DuelLinkStatus Status { get; }

        /// <summary>Why the transport closed, for the player. Empty while it is fine.</summary>
        string Error { get; }

        /// <summary>A millisecond clock both seats of a room read alike (Photon's server time).
        /// Wraps around; compare with unchecked subtraction only.</summary>
        int ServerTimeMs { get; }

        /// <summary>True while the other seat is connected to the room.</summary>
        bool PeerInRoom { get; }

        event Action<byte, byte[]> Received;

        /// <summary>Connect and enter the named room, creating it if the other seat has not yet.</summary>
        void Open(string roomName, string userId, string authToken);

        void Send(byte code, byte[] payload, bool reliable);

        /// <summary>Pumps the connection. Called every frame by the link.</summary>
        void Service();

        /// <summary>Leave the room for good and disconnect.</summary>
        void Close();
    }

    /// <summary>Where the app gets a real transport. The Photon assembly registers itself here at
    /// startup, so nothing that opens a room references Photon directly — and a build without the
    /// SDK simply reports live duels as unavailable.</summary>
    public static class DuelTransports
    {
        public static Func<IDuelTransport> Factory;
        public static bool Available => Factory != null;
        public static IDuelTransport Create() => Factory?.Invoke();
    }
}
