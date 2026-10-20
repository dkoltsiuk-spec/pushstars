using System;
using System.Collections.Generic;
using System.Threading;
using ExitGames.Client.Photon;
using Photon.Realtime;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Photon
{
    /// <summary>
    /// <see cref="IDuelTransport"/> over Photon Realtime (the LoadBalancingClient that ships inside
    /// PUN 2). No PhotonView, no PhotonNetwork singleton: the duel sends its own binary messages
    /// with RaiseEvent, and the room is just two seats named after the server's match.
    ///
    /// <para><b>Room.</b> Both phones JoinOrCreate the same name, so it does not matter who gets
    /// there first. Two seats, invisible to matchmaking, PlayerTtl = the 10 s reconnect window
    /// (constants.md), EmptyRoomTtl 0 so the room closes — and the webhook fires — as soon as both
    /// have gone.</para>
    ///
    /// <para><b>Reconnect.</b> A dropped connection is retried every couple of seconds with
    /// ReconnectAndRejoin (same actor, inside the TTL), falling back to a fresh connect and a
    /// rejoin by name. Past <see cref="GiveUpSec"/> the transport closes; the duel itself decides
    /// what that means.</para>
    ///
    /// <para><b>Loading screens.</b> A background timer sends acknowledgements when the main
    /// thread stalls (scene loads), so the server does not time this phone out mid-load. It never
    /// touches the peer while the main thread does: every main-thread use holds <c>_peerLock</c>,
    /// and the timer only tries it.</para>
    /// </summary>
    public sealed class PhotonDuelTransport : IDuelTransport, IConnectionCallbacks, IMatchmakingCallbacks, IInRoomCallbacks, IOnEventCallback
    {
        private const float RetryEverySec = 2f, GiveUpSec = 25f;
        private readonly LoadBalancingClient _client;
        private readonly LiveDuelConfig _config;
        private string _room, _userId, _token;
        private bool _wantRoom, _closing, _everJoined, _rejoinByName, _freshJoinTried;
        private float _lostAt = -1f, _nextRetry;
        private Timer _keepAlive;
        private int _lastServiceTick;
        private readonly object _peerLock = new object();

        public DuelLinkStatus Status { get; private set; } = DuelLinkStatus.Idle;
        public string Error { get; private set; } = "";
        public int ServerTimeMs => _client.LoadBalancingPeer.ServerTimeInMilliSeconds;
        public event Action<byte, byte[]> Received;

        public bool PeerInRoom
        {
            get
            {
                var room = _client.CurrentRoom;
                if (room == null || !_client.InRoom) return false;
                foreach (var player in room.Players.Values)
                    if (!player.IsLocal && !player.IsInactive) return true;
                return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            var config = LiveDuelConfig.Load();
            if (config == null || !config.IsValid)
            {
                Debug.LogWarning("[LiveDuel] Resources/LiveDuelConfig.json is missing or has no Photon AppId — live duels are off.");
                return;
            }
            DuelTransports.Factory = () => new PhotonDuelTransport(config);
        }

        public PhotonDuelTransport(LiveDuelConfig config)
        {
            _config = config;
            _client = new LoadBalancingClient(ConnectionProtocol.Udp);
            _client.AddCallbackTarget(this);
        }

        public void Open(string roomName, string userId, string authToken)
        {
            if (Status != DuelLinkStatus.Idle) return;
            _room = roomName; _userId = userId; _token = authToken; _wantRoom = true;
            Status = DuelLinkStatus.Connecting;
            _lastServiceTick = Environment.TickCount;
            _keepAlive = new Timer(KeepAlive, null, 100, 100);
            lock (_peerLock) Connect();
        }

        private void Connect()
        {
            var auth = new AuthenticationValues(_userId);
            if (_config.customAuth)
            {
                auth.AuthType = CustomAuthenticationType.Custom;
                auth.AddAuthParameter("token", _token);
            }
            _client.AuthValues = auth;
            var settings = new AppSettings
            {
                AppIdRealtime = _config.appId, AppVersion = _config.appVersion,
                FixedRegion = _config.region, UseNameServer = true, Protocol = ConnectionProtocol.Udp
            };
            if (!_client.ConnectUsingSettings(settings)) Lost();
        }

        public void Send(byte code, byte[] payload, bool reliable)
        {
            lock (_peerLock)
            {
                if (!_client.InRoom) return;
                _client.OpRaiseEvent(code, payload, RaiseEventOptions.Default,
                    reliable ? SendOptions.SendReliable : SendOptions.SendUnreliable);
            }
        }

        public void Service()
        {
            _lastServiceTick = Environment.TickCount;
            if (Status == DuelLinkStatus.Closed) return;
            lock (_peerLock)
            {
                _client.Service();
                if (Status != DuelLinkStatus.Reconnecting && !(Status == DuelLinkStatus.Connecting && _lostAt >= 0)) return;
                float now = Time.realtimeSinceStartup;
                if (now - _lostAt > GiveUpSec) { Fail("Connection lost. Room closed."); return; }
                if (now < _nextRetry || _client.IsConnected) return;
                _nextRetry = now + RetryEverySec;
                if (_everJoined && !_rejoinByName && _client.ReconnectAndRejoin()) return;
                _rejoinByName = _everJoined;
                Connect();
            }
        }

        public void Close()
        {
            if (Status == DuelLinkStatus.Closed) return;
            _closing = true; _wantRoom = false;
            Status = DuelLinkStatus.Closed;
            _keepAlive?.Dispose(); _keepAlive = null;
            lock (_peerLock)
            try
            {
                if (_client.InRoom) _client.OpLeaveRoom(false);
                _client.LoadBalancingPeer.SendOutgoingCommands();   // the goodbye goes before the socket closes
                _client.Disconnect();
            }
            finally { _client.RemoveCallbackTarget(this); }
        }

        private void Fail(string error)
        {
            Error = error;
            Debug.LogWarning("[LiveDuel] " + error);
            Close();
        }

        private void Lost()
        {
            if (_lostAt < 0) { _lostAt = Time.realtimeSinceStartup; _nextRetry = _lostAt + .5f; }
            if (_everJoined) Status = DuelLinkStatus.Reconnecting;
        }

        /// <summary>Keeps the connection alive while the main thread is busy (scene loads).</summary>
        private void KeepAlive(object _)
        {
            try
            {
                if (_closing || Environment.TickCount - Volatile.Read(ref _lastServiceTick) <= 200) return;
                if (!Monitor.TryEnter(_peerLock)) return;   // the main thread is in the peer right now
                try { if (!_closing && _client.IsConnected) _client.LoadBalancingPeer.SendAcksOnly(); }
                finally { Monitor.Exit(_peerLock); }
            }
            catch (Exception) { /* the main thread owns errors */ }
        }

        // ── Photon callbacks ──────────────────────────────────────────────────────────────────

        public void OnConnectedToMaster()
        {
            if (!_wantRoom) return;
            if (_rejoinByName) { _client.OpRejoinRoom(_room); return; }
            _client.OpJoinOrCreateRoom(new EnterRoomParams
            {
                RoomName = _room,
                RoomOptions = new RoomOptions { MaxPlayers = 2, PlayerTtl = 10000, EmptyRoomTtl = 0, IsVisible = false, PublishUserId = true }
            });
        }

        public void OnJoinedRoom()
        {
            _everJoined = true; _rejoinByName = _freshJoinTried = false; _lostAt = -1f; Error = "";
            Status = DuelLinkStatus.InRoom;
        }

        public void OnJoinRoomFailed(short returnCode, string message)
        {
            if (_everJoined && returnCode != ErrorCode.GameFull && !_freshJoinTried)
            {
                // Our seat timed out while we were away; take it again as a new arrival.
                _rejoinByName = false; _freshJoinTried = true;
                OnConnectedToMaster();
                return;
            }
            Fail(returnCode == ErrorCode.GameFull ? "This room is already full." : "Could not enter the room: " + message);
        }

        public void OnCreateRoomFailed(short returnCode, string message) => Fail("Could not open the room: " + message);

        public void OnDisconnected(DisconnectCause cause)
        {
            if (_closing) return;
            switch (cause)
            {
                case DisconnectCause.CustomAuthenticationFailed:
                case DisconnectCause.InvalidAuthentication:
                    Fail("Photon refused the sign-in. Check the custom authentication setup."); return;
                case DisconnectCause.InvalidRegion:
                    Fail("Photon region is not available for this app."); return;
                case DisconnectCause.MaxCcuReached:
                    Fail("The duel server is full right now. Try again in a minute."); return;
                default: Lost(); return;
            }
        }

        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code >= 200 || !(photonEvent.CustomData is byte[] payload)) return;
            Received?.Invoke(photonEvent.Code, payload);
        }

        public void OnLeftRoom() { }
        public void OnConnected() { }
        public void OnRegionListReceived(RegionHandler regionHandler) { }
        public void OnCustomAuthenticationResponse(Dictionary<string, object> data) { }
        public void OnCustomAuthenticationFailed(string debugMessage) => Fail("Photon refused the sign-in: " + debugMessage);
        public void OnFriendListUpdate(List<FriendInfo> friendList) { }
        public void OnCreatedRoom() { }
        public void OnJoinRandomFailed(short returnCode, string message) { }
        public void OnPlayerEnteredRoom(Player newPlayer) { }
        public void OnPlayerLeftRoom(Player otherPlayer) { }
        public void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged) { }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps) { }
        public void OnMasterClientSwitched(Player newMasterClient) { }
    }
}
