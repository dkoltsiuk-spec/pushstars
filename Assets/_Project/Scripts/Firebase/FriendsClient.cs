using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using PushStars.Core;

namespace PushStars.Services
{
    /// <summary>A player this one has fought in a friend-code duel (functions/src/friends.js).</summary>
    [Serializable] public sealed class FriendEntry
    {
        public string uid, name;
        public int hero, gender, trophies;
        public bool online;
        /// <summary>Their last heartbeat; 0 when never seen (or long gone).</summary>
        public long lastSeenMs, lastDuelMs;
    }
    [Serializable] public sealed class FriendList
    {
        public FriendEntry[] friends = Array.Empty<FriendEntry>();
        public long serverTimeMs;
    }

    /// <summary>A friend calls this player into their open duel code (friendInvites/{uid}).</summary>
    public sealed class FriendInvite
    {
        public readonly string FromUid, FromName, Code, MatchId;
        public readonly int FromHero, FromGender;
        public readonly long AtMs, ExpiresAtMs;
        public FriendInvite(string fromUid, string fromName, int fromHero, int fromGender, string code, string matchId, long atMs, long expiresAtMs)
        {
            FromUid = fromUid ?? ""; FromName = fromName ?? ""; FromHero = fromHero; FromGender = fromGender;
            Code = code ?? ""; MatchId = matchId ?? ""; AtMs = atMs; ExpiresAtMs = expiresAtMs;
        }
        public bool Live(long nowMs) => Code.Length == 6 && ExpiresAtMs > nowMs;
    }

    /// <summary>
    /// The friends list: everyone this player fought in a friend-code duel lands on it by itself
    /// (the server adds both sides when the guest joins). From it the host of a new code calls a
    /// friend in with one tap; the friend's phone hears the call through <see cref="WatchInvite"/>
    /// (and a push when the app is closed).
    /// </summary>
    public static class FriendsClient
    {
        [Serializable] private sealed class Empty { }
        [Serializable] private sealed class FriendRequest { public string friendUid; }
        [Serializable] private sealed class InviteRequest { public string friendUid, code; }
        [Serializable] private sealed class DismissRequest { public string matchId; }
        [Serializable] public sealed class InviteReceipt { public string status; public bool pushed; public long expiresAtMs; }

        /// <summary>Next to Firestore, like the queue.</summary>
        private const string Region = "europe-west1";

        public static string Uid => LeagueClient.Uid;

        public static async Task<FriendList> Load()
        {
            await FirebaseAuthService.EnsureReadyAsync();
            var list = await LeagueClient.Call<Empty, FriendList>("getFriends", new Empty(), Uid, Region);
            if (list != null && list.friends == null) list.friends = Array.Empty<FriendEntry>();
            return list;
        }

        public static async Task<InviteReceipt> Invite(string friendUid, string code)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            return await LeagueClient.Call<InviteRequest, InviteReceipt>("inviteFriend",
                new InviteRequest { friendUid = friendUid, code = code }, Uid, Region);
        }

        public static Task Dismiss(string matchId) =>
            LeagueClient.Call<DismissRequest, Empty>("dismissFriendInvite", new DismissRequest { matchId = matchId ?? "" }, Uid, Region);

        public static Task Remove(string friendUid) =>
            LeagueClient.Call<FriendRequest, Empty>("removeFriend", new FriendRequest { friendUid = friendUid }, Uid, Region);

        /// <summary>Listens to this player's invitation while the main screen is up; null when the
        /// call is gone. Only server answers count — the cache may hold a long-spent call. Null
        /// when Firestore is not up yet. Dispose to stop.</summary>
        public static IDisposable WatchInvite(Action<FriendInvite> changed)
        {
            string uid = Uid;
            if (string.IsNullOrEmpty(uid) || !ServiceLocator.TryGet<FirestoreService>(out var fs) || fs.Db == null) return null;
            try
            {
                var registration = fs.Db.Collection("friendInvites").Document(uid).Listen(snapshot =>
                {
                    if (snapshot == null || snapshot.Metadata.IsFromCache) return;
                    if (!snapshot.Exists) { changed?.Invoke(null); return; }
                    var d = snapshot.ToDictionary();
                    changed?.Invoke(new FriendInvite(Text(d, "fromUid"), Text(d, "fromName"), (int)Number(d, "fromHero"),
                        (int)Number(d, "fromGender"), Text(d, "code"), Text(d, "matchId"), Number(d, "atMs"), Number(d, "expiresAtMs")));
                });
                return new Watch(registration);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[Friends] watch invite: " + e.Message); return null; }
        }

        /// <summary>Plain words for the friends screen.</summary>
        public static string Explain(Exception e)
        {
            if (e is LeagueCallException call)
            {
                string m = call.Message ?? "";
                if (m.Contains("Already called")) return "Already called. Wait a moment.";
                if (m.Contains("Not your friend")) return "This player is no longer on your list.";
                if (m.Contains("expired")) return "The invitation expired. Try again.";
                return m;
            }
            return "No connection. Check the internet and try again.";
        }

        private sealed class Watch : IDisposable
        {
            private ListenerRegistration _registration;
            public Watch(ListenerRegistration registration) { _registration = registration; }
            public void Dispose() { _registration?.Stop(); _registration = null; }
        }

        private static string Text(IDictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) && v is string text ? text : "";

        private static long Number(IDictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return 0;
            try { return Convert.ToInt64(v); } catch (Exception) { return 0; }
        }
    }
}
