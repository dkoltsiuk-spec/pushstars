using System;
using System.Threading.Tasks;
using Firebase.Auth;
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

    /// <summary>Another player's best set, handed back when the random queue found nobody.</summary>
    [Serializable] public sealed class LiveQueueGhost
    {
        public string uid, name;
        public float[] repTimes, clapTimes;
    }
    /// <summary>Where a search in the random queue stands (enqueueLiveDuel / pollLiveDuel /
    /// leaveLiveDuel): "waiting", "matched" (a ranked live duel with a stranger, room named) or
    /// "left" (with a stranger's ghost to fight instead, when the season has one).</summary>
    [Serializable] public sealed class LiveQueueStatus
    {
        public string status, matchId, roomName, mode, opponentUid, opponentName;
        public bool host;
        public int opponentHero, opponentGender;
        public long serverTimeMs;
        public LiveQueueGhost ghost;
        public bool Matched => status == "matched" && !string.IsNullOrEmpty(matchId) && !string.IsNullOrEmpty(roomName);
        // JsonUtility never leaves a nested field null: an absent ghost arrives empty.
        public bool HasGhost => ghost != null && ghost.repTimes != null && ghost.repTimes.Length > 0;
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
        [Serializable] private sealed class QueueRequest { public int hero, gender, best; public string country; }
        [Serializable] private sealed class Empty { }

        public static string Uid => LeagueClient.Uid;

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

        /// <summary>Starts a search in the random queue; pairs at once with anyone already waiting.</summary>
        public static async Task<LiveQueueStatus> Enqueue(int hero, int gender, string country, int best)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            return await LeagueClient.Call<QueueRequest, LiveQueueStatus>("enqueueLiveDuel",
                new QueueRequest { hero = hero, gender = gender, country = country ?? "", best = best }, Uid);
        }

        /// <summary>Keeps the place in the queue, and pairs or reports the pairing.</summary>
        public static Task<LiveQueueStatus> Poll(int hero, int gender, string country, int best) =>
            LeagueClient.Call<QueueRequest, LiveQueueStatus>("pollLiveDuel",
                new QueueRequest { hero = hero, gender = gender, country = country ?? "", best = best }, Uid);

        /// <summary>Stops searching. A pairing made in the last moment comes back as "matched".</summary>
        public static Task<LiveQueueStatus> LeaveQueue(int best) =>
            LeagueClient.Call<QueueRequest, LiveQueueStatus>("leaveLiveDuel", new QueueRequest { best = best }, Uid);

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
