using System;
using System.Threading;
using System.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Opens the live room behind a friend-duel code — or a pairing in the random queue
    /// (<see cref="Search"/>): the server binds both players to one match (createLiveDuel /
    /// joinLiveDuel / enqueueLiveDuel), then both phones enter the Photon room named by it. The
    /// room sheet (<see cref="FriendDuelController"/>) only reads the resulting
    /// <see cref="LiveDuelLink"/>; the fight screen takes the same link over.
    /// </summary>
    public static class LiveDuelService
    {
        /// <summary>True when this build carries a real transport (the Photon SDK and its config).</summary>
        public static bool Available => DuelTransports.Available;

        public static LiveDuelSeat LocalSeat() => new LiveDuelSeat
        {
            Uid = LiveDuelClient.Uid ?? "", Name = ProfileIdentityEditor.PlayerName(),
            Hero = CharacterRoster.SavedHomeAvatar, Gender = (int)CharacterRoster.SavedGender,
            Country = ProfileCountry.Code ?? ""
        };

        public static async Task<(LiveDuelTicket ticket, LiveDuelLink link)> Host()
        {
            var seat = LocalSeat();
            var ticket = await LiveDuelClient.Create(seat.Hero, seat.Gender, seat.Country);
            return (ticket, await Enter(ticket, true, seat));
        }

        public static async Task<(LiveDuelTicket ticket, LiveDuelLink link)> Join(string code)
        {
            var seat = LocalSeat();
            var ticket = await LiveDuelClient.Join(code, seat.Hero, seat.Gender, seat.Country);
            return (ticket, await Enter(ticket, false, seat));
        }

        private static Task<LiveDuelLink> Enter(LiveDuelTicket ticket, bool host, LiveDuelSeat seat) =>
            Enter(ticket.matchId, ticket.roomName, host, seat);

        /// <param name="connected">A transport already connected ahead of the room (the queue's
        /// warm-up); a fresh one when null.</param>
        private static async Task<LiveDuelLink> Enter(string matchId, string roomName, bool host, LiveDuelSeat seat,
            IDuelTransport connected = null)
        {
            string token = await LiveDuelClient.IdToken();
            seat.Uid = LiveDuelClient.Uid ?? seat.Uid;
            var link = new LiveDuelLink(connected ?? DuelTransports.Create(), matchId, roomName, host, seat);
            link.Open(token, Time.realtimeSinceStartup);
            LiveDuelLink.SetActive(link);
            return link;
        }

        /// <summary>A search in the random live queue (the main screen's "Search opponent"), open
        /// until the player picks another fight or leaves. <paramref name="best"/> is the player's
        /// best set: the stranger's set offered meanwhile is picked near it. <paramref name="notify"/>
        /// is read on every call: "notify me" may be switched on while the search runs.</summary>
        public static LiveQueueSearch Search(int best, Func<bool> notify) =>
            new LiveQueueSearch(new ServerQueue(notify), LocalSeat(), best, float.PositiveInfinity);

        /// <summary>
        /// The server's queue for one search. While the search waits it connects to Photon ahead
        /// of the room (name server, sign-in, master — the slow part of entering), and listens to
        /// its own place so a pairing the other phone makes arrives at once instead of on the
        /// next poll. The warm connection is dropped after <see cref="WarmSec"/>: an open-ended
        /// search would hold a Photon CCU for minutes; a later pairing simply connects then.
        /// </summary>
        private sealed class ServerQueue : ILiveQueue
        {
            private const float WarmSec = 20f;
            private readonly Func<bool> _notify;
            private float _warmSince = -1f;
            private Task<IDuelTransport> _warm;
            private IDuelTransport _warmed;      // connected ahead, not yet handed to a room
            private IDisposable _watch;
            private LiveQueueStatus _pushed;
            private bool _ended;

            public ServerQueue(Func<bool> notify) { _notify = notify ?? (() => false); }

            public async Task<LiveQueueStatus> Enqueue(LiveDuelSeat seat, int best)
            {
                if (_warm == null) { _warm = Warm(); _warmSince = Time.realtimeSinceStartup; }
                var status = await LiveDuelClient.Enqueue(seat.Hero, seat.Gender, seat.Country, best, _notify());
                if (!_ended && _watch == null && status != null && status.status == "waiting")
                    _watch = LiveDuelClient.WatchQueue(status.serverTimeMs, match => Volatile.Write(ref _pushed, match));
                return status;
            }

            public Task<LiveQueueStatus> Poll(LiveDuelSeat seat, int best) => LiveDuelClient.Poll(seat.Hero, seat.Gender, seat.Country, best, _notify());
            public Task<LiveQueueStatus> Leave(int best, bool standby, bool busy) => LiveDuelClient.LeaveQueue(best, standby, busy);

            public async Task<LiveDuelLink> Enter(LiveQueueStatus match, LiveDuelSeat seat)
            {
                var connected = _warm != null ? await _warm : null;
                _warmed = null;                  // the room's link pumps it from here
                if (connected != null && connected.Status == DuelLinkStatus.Closed) connected = null;
                try { return await LiveDuelService.Enter(match.matchId, match.roomName, match.host, seat, connected); }
                catch (Exception) { connected?.Close(); throw; }
            }

            public LiveQueueStatus Tick()
            {
                _warmed?.Service();
                if (_warmed != null && _warm.IsCompleted && Time.realtimeSinceStartup - _warmSince > WarmSec)
                {
                    // Nobody came while it was warm: let the CCU go; Enter connects afresh.
                    _warmed.Close();
                    _warmed = null;
                    _warm = null;
                }
                return Interlocked.Exchange(ref _pushed, null);
            }

            public void End()
            {
                if (_ended) return;
                _ended = true;
                _watch?.Dispose();
                _watch = null;
                var unused = _warmed;
                _warmed = null;
                unused?.Close();
            }

            private async Task<IDuelTransport> Warm()
            {
                try
                {
                    string token = await LiveDuelClient.IdToken();
                    if (_ended) return null;
                    var transport = DuelTransports.Create();
                    transport?.Connect(LiveDuelClient.Uid ?? "", token);
                    return _warmed = transport;
                }
                catch (Exception e) { Debug.LogWarning("[LiveQueue] warm-up: " + e.Message); return null; }
            }

            public async void Cancel(string matchId)
            {
                try { await LiveDuelClient.Cancel(matchId); }
                catch (System.Exception e) { Debug.LogWarning("[LiveQueue] cancel: " + e.Message); }
            }
        }

        /// <summary>Leaves the room; a host who leaves before the duel also frees the code.</summary>
        public static async void Leave(LiveDuelLink link, bool releaseCode)
        {
            if (link == null) return;
            link.Leave();
            if (!releaseCode) return;
            try { await LiveDuelClient.Cancel(link.MatchId); }
            catch (System.Exception e) { Debug.LogWarning("[LiveDuel] cancel: " + e.Message); }
        }
    }
}
