using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Auth;
using Firebase.Firestore;
using PushStars.Core;

namespace PushStars.Services
{
    [Serializable] public sealed class LiveDuelTicket
    {
        public string matchId, code, roomName, hostUid, hostName, guestUid;
        public int hostHero, hostGender;
        public long expiresAtMs, serverTimeMs;
    }
    [Serializable] public sealed class LiveDuelReceipt
    {
        /// <summary>"settled" once both timelines (or a walkover) are in; "pending" while the
        /// friend's phone has not reported yet.</summary>
        public string state, reason;
        public bool won, draw, walkover;
        public int reps, opponentReps, trophyDelta;
        public bool Settled => state == "settled";
    }

    /// <summary>Another player's best set: offered with the first answer of a search (close to
    /// the player's level), or handed back when an old-style search found nobody.</summary>
    [Serializable] public sealed class LiveQueueGhost
    {
        public string uid, name;
        public int trophies;
        public float[] repTimes, clapTimes;
        public bool IsValid => repTimes != null && repTimes.Length > 0;
    }
    /// <summary>Where a search in the random queue stands (enqueueLiveDuel / pollLiveDuel /
    /// leaveLiveDuel): "waiting", "matched" (a ranked live duel with a stranger, room named) or
    /// "left" (with a stranger's ghost to fight instead, when the season has one).</summary>
    [Serializable] public sealed class LiveQueueStatus
    {
        public string status, matchId, roomName, mode, opponentUid, opponentName;
        public bool host;
        public int opponentHero, opponentGender;
        /// <summary>Players who asked to be notified and were just called for this search.</summary>
        public int called;
        public long serverTimeMs;
        public LiveQueueGhost ghost;
        public bool Matched => status == "matched" && !string.IsNullOrEmpty(matchId) && !string.IsNullOrEmpty(roomName);
        // JsonUtility never leaves a nested field null: an absent ghost arrives empty.
        public bool HasGhost => ghost != null && ghost.IsValid;
    }

    /// <summary>Another player's search called this one (functions/src/liveDuel.js callStandby).</summary>
    public readonly struct QueueCall
    {
        public readonly long AtMs;
        public readonly string CallerName;
        public QueueCall(long atMs, string callerName) { AtMs = atMs; CallerName = callerName ?? ""; }
        public bool Fresh(long nowMs, long windowMs) => AtMs > 0 && nowMs - AtMs < windowMs;
    }

    /// <summary>Who is searching for a live duel right now with nobody to pair with (the server's
    /// lobby, liveDuelLobby/now): main screens show it above START.</summary>
    public sealed class LobbySearch
    {
        public readonly string Uid, Name;
        public readonly long SinceMs, UntilMs;
        public LobbySearch(string uid, string name, long sinceMs, long untilMs)
        { Uid = uid ?? ""; Name = name ?? ""; SinceMs = sinceMs; UntilMs = untilMs; }
        public bool Live(long nowMs) => !string.IsNullOrEmpty(Uid) && UntilMs > nowMs;
    }

    /// <summary>
    /// Server side of a live duel (functions/src/liveDuel.js): the six-digit code, the match both
    /// phones are bound to, and the result. The Photon room only carries the duel; whether it
    /// counts is decided here, from both rep timelines, never from what one phone says about the
    /// other.
    /// </summary>
    public static class LiveDuelClient
    {
        [Serializable] private sealed class Look { public int hero, gender; public string country; }
        [Serializable] private sealed class CodeRequest { public string code; public int hero, gender; public string country; }
        [Serializable] private sealed class MatchRequest { public string matchId; }
        [Serializable] private sealed class FinishRequest
        {
            public string matchId; public float[] repTimes, clapTimes; public float durationSec;
            public int opponentReps; public bool opponentLeft;
        }
        [Serializable] private sealed class QueueRequest { public int hero, gender, best; public string country; public bool notify; }
        [Serializable] private sealed class LeaveRequest { public int best; public bool standby, busy, ghost; }
        [Serializable] private sealed class Empty { }

        public static string Uid => LeagueClient.Uid;

        /// <summary>Where the queue's functions run: next to Firestore (eur3), so the several
        /// calls a search makes do not each cross an ocean.</summary>
        private const string QueueRegion = "europe-west1";

        public static async Task<LiveDuelTicket> Create(int hero, int gender, string country)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            return await LeagueClient.Call<Look, LiveDuelTicket>("createLiveDuel",
                new Look { hero = hero, gender = gender, country = country ?? "" }, Uid);
        }

        /// <summary>Who invited — shown before the guest commits to the room.</summary>
        public static async Task<LiveDuelTicket> Peek(string code)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            return await LeagueClient.Call<CodeRequest, LiveDuelTicket>("peekLiveDuel", new CodeRequest { code = code }, Uid);
        }

        public static async Task<LiveDuelTicket> Join(string code, int hero, int gender, string country)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            return await LeagueClient.Call<CodeRequest, LiveDuelTicket>("joinLiveDuel",
                new CodeRequest { code = code, hero = hero, gender = gender, country = country ?? "" }, Uid);
        }

        /// <summary>Starts a search in the random queue; pairs at once with anyone already waiting.
        /// <paramref name="notify"/>: the player allowed a push when someone else searches later.
        /// While waiting, the answer offers a stranger's set at the player's level (<c>ghost</c>).</summary>
        public static async Task<LiveQueueStatus> Enqueue(int hero, int gender, string country, int best, bool notify)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            return await LeagueClient.Call<QueueRequest, LiveQueueStatus>("enqueueLiveDuel",
                new QueueRequest { hero = hero, gender = gender, country = country ?? "", best = best, notify = notify }, Uid, QueueRegion);
        }

        /// <summary>Keeps the place in the queue, and pairs or reports the pairing.</summary>
        public static Task<LiveQueueStatus> Poll(int hero, int gender, string country, int best, bool notify) =>
            LeagueClient.Call<QueueRequest, LiveQueueStatus>("pollLiveDuel",
                new QueueRequest { hero = hero, gender = gender, country = country ?? "", best = best, notify = notify }, Uid, QueueRegion);

        /// <summary>Stops searching. A pairing made in the last moment comes back as "matched".
        /// <paramref name="standby"/>: left with "notify me" on — the place stays callable;
        /// <paramref name="busy"/>: not until the recorded fight the player went to is over.</summary>
        public static Task<LiveQueueStatus> LeaveQueue(int best, bool standby, bool busy) =>
            LeagueClient.Call<LeaveRequest, LiveQueueStatus>("leaveLiveDuel",
                new LeaveRequest { best = best, standby = standby, busy = busy, ghost = false }, Uid, QueueRegion);

        /// <summary>Whether another player's search called this one lately (the push may have
        /// opened the app, or been missed): read from the server, never the offline cache.</summary>
        public static async Task<QueueCall> ReadCall()
        {
            await FirebaseAuthService.EnsureReadyAsync();
            string uid = Uid;
            if (string.IsNullOrEmpty(uid) || !ServiceLocator.TryGet<FirestoreService>(out var fs) || fs.Db == null) return default;
            var snapshot = await fs.Db.Collection("liveDuelQueue").Document(uid).GetSnapshotAsync(Source.Server);
            return CallOf(snapshot);
        }

        /// <summary>Listens to this player's queue place for calls while the app is open on the
        /// main screen; null when Firestore is not up. Dispose to stop.</summary>
        public static IDisposable WatchCall(Action<QueueCall> called)
        {
            string uid = Uid;
            if (string.IsNullOrEmpty(uid) || !ServiceLocator.TryGet<FirestoreService>(out var fs) || fs.Db == null) return null;
            try
            {
                var registration = fs.Db.Collection("liveDuelQueue").Document(uid).Listen(snapshot =>
                {
                    if (snapshot == null || snapshot.Metadata.IsFromCache) return;
                    var call = CallOf(snapshot);
                    if (call.AtMs > 0) called?.Invoke(call);
                });
                return new Watch(registration);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[LiveQueue] watch call: " + e.Message); return null; }
        }

        /// <summary>Listens to the lobby — whoever is searching right now — for the main screen;
        /// null (no search) once it is cleared. Null when Firestore is not up yet. Dispose to stop.</summary>
        public static IDisposable WatchLobby(Action<LobbySearch> changed)
        {
            if (string.IsNullOrEmpty(Uid) || !ServiceLocator.TryGet<FirestoreService>(out var fs) || fs.Db == null) return null;
            try
            {
                var registration = fs.Db.Collection("liveDuelLobby").Document("now").Listen(snapshot =>
                {
                    if (snapshot == null || snapshot.Metadata.IsFromCache) return;
                    if (!snapshot.Exists) { changed?.Invoke(null); return; }
                    var lobby = snapshot.ToDictionary();
                    changed?.Invoke(new LobbySearch(Text(lobby, "uid"), Text(lobby, "name"),
                        Number(lobby, "sinceMs"), Number(lobby, "untilMs")));
                });
                return new Watch(registration);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[LiveQueue] watch lobby: " + e.Message); return null; }
        }

        private static QueueCall CallOf(DocumentSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.Exists) return default;
            var place = snapshot.ToDictionary();
            if (Text(place, "state") != "waiting") return default;
            return new QueueCall(Number(place, "calledAtMs"), Text(place, "callerName"));
        }

        /// <summary>
        /// Listens to this player's own place in the queue (liveDuelQueue/{uid}, firestore.rules)
        /// and reports a pairing the moment another phone's call writes it, so the waiting phone
        /// need not learn of it on its next poll. Only answers from the server count — the offline
        /// cache may still hold a pairing from an earlier search. A place that no longer holds a
        /// pairing reports null: a phone back from the background must not take a pairing that was
        /// cancelled while it was away. Null when Firestore is not up (the polls still find the
        /// pairing); dispose to stop.
        /// </summary>
        public static IDisposable WatchQueue(long sinceServerMs, Action<LiveQueueStatus> matched)
        {
            string uid = Uid;
            if (string.IsNullOrEmpty(uid) || !ServiceLocator.TryGet<FirestoreService>(out var fs) || fs.Db == null) return null;
            try
            {
                var registration = fs.Db.Collection("liveDuelQueue").Document(uid).Listen(snapshot =>
                {
                    if (snapshot == null || snapshot.Metadata.IsFromCache) return;
                    matched?.Invoke(snapshot.Exists ? FromPlace(snapshot.ToDictionary(), sinceServerMs) : null);
                });
                return new Watch(registration);
            }
            catch (Exception e) { UnityEngine.Debug.LogWarning("[LiveQueue] watch: " + e.Message); return null; }
        }

        private sealed class Watch : IDisposable
        {
            private ListenerRegistration _registration;
            public Watch(ListenerRegistration registration) { _registration = registration; }
            public void Dispose() { _registration?.Stop(); _registration = null; }
        }

        /// <summary>A matched place as the poll would have answered it (functions/src/liveDuel.js
        /// queueTicket). A pairing older than the search (minus clock slack) is not this search's.</summary>
        private static LiveQueueStatus FromPlace(IDictionary<string, object> place, long sinceServerMs)
        {
            if (Text(place, "state") != "matched" || Number(place, "matchedAtMs") < sinceServerMs - 2000) return null;
            var opponent = place.TryGetValue("opponent", out var o) ? o as IDictionary<string, object> : null;
            var status = new LiveQueueStatus
            {
                status = "matched", matchId = Text(place, "matchId"), roomName = Text(place, "roomName"), mode = "pvp",
                host = Text(place, "role") == "host", opponentUid = Text(opponent, "uid"), opponentName = Text(opponent, "name"),
                opponentHero = (int)Number(opponent, "hero"), opponentGender = (int)Number(opponent, "gender"),
                serverTimeMs = Number(place, "matchedAtMs"), ghost = new LiveQueueGhost()
            };
            return status.Matched ? status : null;
        }

        private static string Text(IDictionary<string, object> d, string key) =>
            d != null && d.TryGetValue(key, out var v) && v is string text ? text : "";

        private static long Number(IDictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || v == null) return 0;
            try { return Convert.ToInt64(v); } catch (Exception) { return 0; }
        }

        public static Task Cancel(string matchId) =>
            LeagueClient.Call<MatchRequest, Empty>("cancelLiveDuel", new MatchRequest { matchId = matchId }, Uid);

        /// <summary>Called when the shared start is scheduled; the server times the set from here.</summary>
        public static Task Start(string matchId) =>
            LeagueClient.Call<MatchRequest, Empty>("startLiveDuel", new MatchRequest { matchId = matchId }, Uid);

        public static Task<LiveDuelReceipt> Finish(string matchId, float[] repTimes, float[] clapTimes, float durationSec,
            int opponentReps, bool opponentLeft) =>
            LeagueClient.Call<FinishRequest, LiveDuelReceipt>("finishLiveDuel", new FinishRequest
            {
                matchId = matchId, repTimes = repTimes ?? Array.Empty<float>(), clapTimes = clapTimes ?? Array.Empty<float>(),
                durationSec = durationSec, opponentReps = opponentReps, opponentLeft = opponentLeft
            }, Uid);

        /// <summary>The Firebase ID token Photon's custom authentication checks (photonAuth).</summary>
        public static async Task<string> IdToken()
        {
            await FirebaseAuthService.EnsureReadyAsync();
            var user = FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null) throw new InvalidOperationException("Sign in required.");
            var task = user.TokenAsync(false);
            if (await Task.WhenAny(task, Task.Delay(12000)) != task) throw new TimeoutException("Sign-in timed out.");
            return await task;
        }

        /// <summary>Plain words for the room sheet.</summary>
        public static string Explain(Exception e)
        {
            if (e is LeagueCallException call)
            {
                string m = call.Message ?? "";
                if (m.Contains("own")) return "This is your own invitation code.";
                if (m.Contains("full")) return "This room is already full.";
                if (m.Contains("expired")) return "Code expired. Ask your friend for a new one.";
                if (call.Status == "NOT_FOUND") return "Room not found. Check the code.";
                return m;
            }
            return "No connection. Check the internet and try again.";
        }
    }
}
