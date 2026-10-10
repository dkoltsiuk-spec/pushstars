using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Firebase.Auth;
using PushStars.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace PushStars.Services
{
    [Serializable] public sealed class LeagueRow
    {
        public string uid, displayName, league, standing;
        public int trophies, rank;
    }
    [Serializable] public sealed class LeagueSeason
    {
        public string id, name;
        public long startAtMs, endAtMs;
    }
    [Serializable] public sealed class LeaguePage
    {
        public LeagueSeason season;
        public string league, nextCursor;
        public int total;
        public bool seeded;
        public long serverTimeMs;
        public LeagueRow own;
        public LeagueRow[] rows;
    }
    [Serializable] public sealed class RankedSession
    {
        public string id, uid, mode, opponentUid, opponentName;
        public float[] opponentTimes, opponentClapTimes;
        public long expiresAtMs;
    }
    [Serializable] public sealed class RankedReceipt
    {
        public string id, uid, seasonId, league;
        public int trophies, trophyDelta, streakBonus, streakDays, reps, opponentReps;
        public bool won, draw, expired;
    }
    public sealed class LeagueCallException : Exception
    {
        public readonly string Status;
        public bool Permanent => Status == "INVALID_ARGUMENT" || Status == "FAILED_PRECONDITION" || Status == "NOT_FOUND";
        public LeagueCallException(string status, string message) : base(message) { Status = status; }
    }

    /// <summary>Cloud rankings are separate from the device workout ledger. Never uploads local trophy totals.</summary>
    public static class LeagueClient
    {
        [Serializable] private sealed class Envelope<T> { public T data; }
        [Serializable] private sealed class Response<T> { public T result; public Error error; }
        [Serializable] private sealed class Error { public string status, message; }
        [Serializable] private sealed class PageRequest { public string cursor, seasonId, league; }
        [Serializable] private sealed class BeginRequest { public string requestId, mode; }
        [Serializable] private sealed class IdRequest { public string id; }
        [Serializable] private sealed class NameRequest { public string displayName; }
        [Serializable] public sealed class PendingResult { public string id, uid; public float[] repTimes, clapTimes; public float durationSec; }
        [Serializable] private sealed class Outbox { public List<PendingResult> items = new List<PendingResult>(); }
        [Serializable] private sealed class Empty { }
        public static RankedSession PreparedSession;
        public static string LastSyncError { get; private set; }
        private static Task _flush;
        private static LeaguePage _cached;
        private static string _cacheUid;
        private static float _cachedAt;
        public static string Uid => ServiceLocator.TryGet<FirebaseAuthService>(out var auth) ? auth.Uid : null;
        private static string OutboxPath => Path.Combine(Application.persistentDataPath, "ranked-results-v1.json");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { PreparedSession = null; _cached = null; _cacheUid = null; _flush = null; LastSyncError = null; }

        public static async Task<LeaguePage> GetPage(string cursor = "", string seasonId = "", string league = "", bool force = false)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            string uid = Uid;
            if (!force && cursor == "" && seasonId == "" && league == "" && _cached != null && _cacheUid == uid &&
                Time.realtimeSinceStartup - _cachedAt < 60 && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _cached.season.endAtMs)
            {
                var cached = JsonUtility.FromJson<LeaguePage>(JsonUtility.ToJson(_cached));
                cached.serverTimeMs += (long)((Time.realtimeSinceStartup - _cachedAt) * 1000);
                return cached;
            }
            var page = await Call<PageRequest, LeaguePage>("getLeagueStandings",
                new PageRequest { cursor = cursor, seasonId = seasonId, league = league }, uid);
            if (cursor == "" && seasonId == "" && league == "") { _cached = page; _cacheUid = uid; _cachedAt = Time.realtimeSinceStartup; }
            return page;
        }

        public static async Task<RankedSession> Begin(bool calibration)
        {
            await FlushPending();
            var request = new BeginRequest { requestId = Guid.NewGuid().ToString("N"), mode = calibration ? "assessment" : "ghost" };
            string uid = Uid;
            // Reuse the request ID if the server committed but its response was lost.
            try { return await Call<BeginRequest, RankedSession>("beginRankedMatch", request, uid); }
            catch (LeagueCallException) { throw; }
            catch { return await Call<BeginRequest, RankedSession>("beginRankedMatch", request, uid); }
        }
        public static Task Start(RankedSession session) => Call<IdRequest, Empty>("startRankedMatch", new IdRequest { id = session.id }, session.uid);
        public static Task Cancel(RankedSession session) => Call<IdRequest, Empty>("cancelRankedMatch", new IdRequest { id = session.id }, session.uid);
        public static async Task SyncDisplayName(string name)
        {
            await Call<NameRequest, Empty>("updateDisplayName", new NameRequest { displayName = name }, Uid);
            _cached = null;
        }

        public static async Task<RankedReceipt> Finish(RankedSession session, float[] times, float[] clapTimes, float duration)
        {
            var pending = new PendingResult { id = session.id, uid = session.uid, repTimes = times, clapTimes = clapTimes, durationSec = duration };
            var outbox = ReadOutbox();
            if (!outbox.items.Exists(p => p.id == pending.id && p.uid == pending.uid)) { outbox.items.Add(pending); SaveOutbox(outbox); }
            try { return await SendPending(pending); }
            catch (Exception e) { LastSyncError = e.Message; return null; }
        }
        public static Task FlushPending()
        {
            if (_flush == null || _flush.IsCompleted) _flush = Flush();
            return _flush;
        }
        public static bool HasPending(RankedSession session) => ReadOutbox().items.Exists(p => p.uid == session.uid && p.id == session.id);
        public static bool HasPendingResults => ReadOutbox().items.Exists(p => p.uid == Uid);
        private static async Task Flush()
        {
            if (string.IsNullOrEmpty(Uid)) return;
            foreach (var pending in ReadOutbox().items)
            {
                if (pending.uid != Uid) continue;
                try { await SendPending(pending); }
                catch (Exception e) { LastSyncError = e.Message; break; }
            }
        }
        private static async Task<RankedReceipt> SendPending(PendingResult pending)
        {
            try
            {
                var receipt = await Call<PendingResult, RankedReceipt>("finishRankedMatch", pending, pending.uid);
                RemovePending(pending);
                _cached = null;
                LastSyncError = null;
                return receipt;
            }
            catch (LeagueCallException e) when (e.Permanent)
            {
                // Keep an audit copy; permanently rejected payloads must not block later results.
                File.AppendAllText(Path.Combine(Application.persistentDataPath, "ranked-rejected-v1.jsonl"), JsonUtility.ToJson(pending) + "\n");
                RemovePending(pending);
                throw;
            }
        }
        private static void RemovePending(PendingResult p)
        {
            var box = ReadOutbox();
            box.items.RemoveAll(x => x.id == p.id && x.uid == p.uid);
            SaveOutbox(box);
        }
        private static Outbox ReadOutbox() => File.Exists(OutboxPath)
            ? JsonUtility.FromJson<Outbox>(File.ReadAllText(OutboxPath)) ?? new Outbox() : new Outbox();
        private static void SaveOutbox(Outbox box)
        {
            string temp = OutboxPath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(box));
            if (File.Exists(OutboxPath)) File.Replace(temp, OutboxPath, null); else File.Move(temp, OutboxPath);
        }
        public static async Task<TResponse> Call<TRequest, TResponse>(string method, TRequest data, string uid) where TResponse : class
        {
            if (string.IsNullOrEmpty(uid) || Uid != uid) throw new InvalidOperationException("Sign in to the same account to continue.");
            if (!ServiceLocator.TryGet<FirebaseService>(out var firebase) || !firebase.IsReady)
                throw new InvalidOperationException("Connection unavailable.");
            var user = FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null || user.UserId != uid) throw new InvalidOperationException("Account changed.");
            var tokenTask = user.TokenAsync(false);
            if (await Task.WhenAny(tokenTask, Task.Delay(12000)) != tokenTask) throw new TimeoutException("Sign-in timed out.");
            var token = await tokenTask;
            if (Uid != uid) throw new InvalidOperationException("Account changed.");
            using (var request = new UnityWebRequest($"https://us-central1-{firebase.App.Options.ProjectId}.cloudfunctions.net/{method}", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Envelope<TRequest> { data = data })));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + token);
                request.timeout = 15;
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                if (Uid != uid) throw new InvalidOperationException("Account changed.");
                Response<TResponse> response = null;
                try { response = JsonUtility.FromJson<Response<TResponse>>(request.downloadHandler.text); } catch (ArgumentException) { }
                // JsonUtility never leaves a nested [Serializable] field null: a successful reply
                // parses with an empty Error object. Only a filled one is an error.
                if (response?.error != null && !(string.IsNullOrEmpty(response.error.status) && string.IsNullOrEmpty(response.error.message)))
                    throw new LeagueCallException(response.error.status, response.error.message ?? response.error.status);
                if (request.result != UnityWebRequest.Result.Success || response?.result == null)
                    throw new InvalidOperationException("League connection unavailable. Please retry.");
                return response.result;
            }
        }
    }
}
