using System.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Opens the live room behind a friend-duel code: the server binds both players to one match
    /// (createLiveDuel / joinLiveDuel), then both phones enter the Photon room named by it. The
    /// room sheet (<see cref="FriendDuelController"/>) only reads the resulting
    /// <see cref="LiveDuelLink"/>; the fight screen takes the same link over.
    /// </summary>
    public static class LiveDuelService
    {
        /// <summary>True when this build carries a real transport (the Photon SDK and its config).</summary>
        public static bool Available => DuelTransports.Available;

        public static LiveDuelSeat LocalSeat() => new LiveDuelSeat
        {
            Uid = LiveDuelClient.Uid ?? "", Name = ProfileIdentityEditor.ResolveName("YOU"),
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

        private static async Task<LiveDuelLink> Enter(LiveDuelTicket ticket, bool host, LiveDuelSeat seat)
        {
            string token = await LiveDuelClient.IdToken();
            seat.Uid = LiveDuelClient.Uid ?? seat.Uid;
            var link = new LiveDuelLink(DuelTransports.Create(), ticket.matchId, ticket.roomName, host, seat);
            link.Open(token, Time.realtimeSinceStartup);
            LiveDuelLink.SetActive(link);
            return link;
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
