using System;
using System.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>The random queue as the search screen needs it. The app's is the server
    /// (<see cref="LiveDuelService.Search"/>); the Editor regression pairs two searches in memory.</summary>
    public interface ILiveQueue
    {
        Task<LiveQueueStatus> Enqueue(LiveDuelSeat seat, int best);
        Task<LiveQueueStatus> Poll(LiveDuelSeat seat, int best);
        /// <param name="standby">Left for a recorded fight with "notify me" on: stay callable.</param>
        Task<LiveQueueStatus> Leave(int best, bool standby);
        /// <summary>Opens the match's room (Photon in the app).</summary>
        Task<LiveDuelLink> Enter(LiveQueueStatus match, LiveDuelSeat seat);
        /// <summary>Releases a match that never started.</summary>
        void Cancel(string matchId);
        /// <summary>Every frame of the search: pumps what the queue keeps open meanwhile (the room
        /// connection it warms up for <see cref="Enter"/>) and hands over a pairing the server
        /// pushed before the next poll would have found it — null when there is none.</summary>
        LiveQueueStatus Tick();
        /// <summary>The search is over: stop listening, and drop a warmed-up connection no room used.</summary>
        void End();
    }

    /// <summary>
    /// One press of "Search opponent" against the live queue: enqueue, poll (every second, slower
    /// after a while) for up to the search length — <see cref="SearchSec"/>, or open-ended while
    /// few people play: the player then picks <see cref="Offer"/> or their shadow when they like
    /// (a pairing another phone makes is usually pushed sooner, through
    /// <see cref="ILiveQueue.Tick"/>), and on a pairing open the match's room and wait — at most
    /// <see cref="RoomSec"/> — for the stranger to be there. Ends in <see cref="Result.Live"/>
    /// (a link both phones are in; the preparation screen's READY check comes next) or <see cref="Result.Fallback"/> (nobody came: the queue place is freed, and
    /// <see cref="Ghost"/> is a stranger's best set when the season has one).
    ///
    /// <para>Ticked by its owner every frame; never throws. Every network failure degrades to the
    /// fallback, so the search screen always ends in a fight.</para>
    /// </summary>
    public sealed class LiveQueueSearch
    {
        public enum Result { Searching, Live, Fallback, Cancelled }

        public const float SearchSec = 9f, PollSec = 1f, RoomSec = 10f;
        /// <summary>A long search polls less often: the server keeps the place for 5 s per poll,
        /// and a pairing reaches the phone through its listener anyway.</summary>
        public static float PollInterval(float elapsed) => elapsed < 30f ? PollSec : elapsed < 120f ? 2f : 4f;

        public Result State { get; private set; } = Result.Searching;
        public LiveDuelLink Link { get; private set; }
        public LiveQueueGhost Ghost { get; private set; }
        public string OpponentName { get; private set; } = "";
        public string MatchId => _match?.matchId;
        /// <summary>True once the queue answered the first time (with or without an offer).</summary>
        public bool Answered { get; private set; }
        /// <summary>The queue could not be reached at all (no network, server down).</summary>
        public bool Unreachable { get; private set; }
        /// <summary>A stranger's set at the player's level, offered by the queue's first answer.</summary>
        public LiveQueueGhost Offer { get; private set; }
        /// <summary>When the server last called players who asked to be notified (-1: never).</summary>
        public float CalledAt { get; private set; } = -1f;
        public float StartedAt => _startedAt;

        private enum Step { Enqueue, Wait, Poll, Leave, Enter, Room }
        private readonly ILiveQueue _queue;
        private readonly LiveDuelSeat _seat;
        private readonly int _best;
        private Step _step;
        private Task<LiveQueueStatus> _call;
        private Task<LiveDuelLink> _enter;
        private LiveQueueStatus _match;
        private float _startedAt = -1f, _nextPoll, _roomUntil;
        private readonly float _searchSec;

        /// <param name="searchSec">How long to wait before leaving with a fallback;
        /// <see cref="float.PositiveInfinity"/> searches until <see cref="Cancel"/>.</param>
        public LiveQueueSearch(ILiveQueue queue, LiveDuelSeat seat, int best, float searchSec = SearchSec)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _seat = seat ?? new LiveDuelSeat();
            _best = Mathf.Clamp(best, 0, LiveDuelLink.MaxReps);
            _searchSec = searchSec;
        }

        public void Tick(float now)
        {
            if (State != Result.Searching) return;
            if (_startedAt < 0f)
            {
                _startedAt = now;
                _step = Step.Enqueue;
                _call = Call(() => _queue.Enqueue(_seat, _best));
            }
            LiveQueueStatus pushed = null;
            try { pushed = _queue.Tick(); }
            catch (Exception e) { Warn("tick", e); }
            if ((_step == Step.Wait || _step == Step.Poll) && pushed != null && pushed.Matched)
            {
                // Paired by the other phone's call: no need to wait for our own poll (its answer,
                // still on the way, says the same and is dropped).
                TakeMatch(pushed);
                return;
            }
            switch (_step)
            {
                case Step.Enqueue: case Step.Poll: case Step.Leave:
                    if (_call.IsCompleted) OnStatus(now);
                    return;
                case Step.Wait:
                    if (now - _startedAt >= _searchSec) { _step = Step.Leave; _call = Call(() => _queue.Leave(_best, false)); }
                    else if (now >= _nextPoll) { _step = Step.Poll; _call = Call(() => _queue.Poll(_seat, _best)); }
                    return;
                case Step.Enter:
                    if (!_enter.IsCompleted) return;
                    if (_enter.Status != TaskStatus.RanToCompletion || _enter.Result == null)
                    { Warn("room", _enter.Exception); Abandon(); return; }
                    Link = _enter.Result;
                    LiveDuelDiagnostics.Mark("search:room");
                    _roomUntil = now + RoomSec;
                    _step = Step.Room;
                    return;
                case Step.Room:
                    TickRoom(now);
                    return;
            }
        }

        private void OnStatus(float now)
        {
            var step = _step;
            var status = _call.Status == TaskStatus.RanToCompletion ? _call.Result : null;
            if (status == null)
            {
                Warn(step.ToString().ToLowerInvariant(), _call.Exception);
                // A lost poll keeps the place (the server keeps it a few seconds); anything else
                // means there is no queue to search right now.
                if (step == Step.Poll) { _step = Step.Wait; _nextPoll = now + PollSec; }
                else if (step == Step.Enqueue) { Answered = Unreachable = true; Fallback(null); }
                else Fallback(null);
                return;
            }
            if (status.Matched) { TakeMatch(status); return; }
            if (step == Step.Leave) { Fallback(status.HasGhost ? status.ghost : null); return; }
            if (step == Step.Enqueue)
            {
                Answered = true;
                if (status.HasGhost) Offer = status.ghost;
            }
            if (status.called > 0) CalledAt = now;
            _step = Step.Wait;
            _nextPoll = now + PollInterval(now - _startedAt);
        }

        private void TakeMatch(LiveQueueStatus status)
        {
            _match = status;
            OpponentName = status.opponentName ?? "";
            _step = Step.Enter;
            _enter = Call(() => _queue.Enter(status, _seat));
        }

        /// <summary>Both phones are in the match's room and have introduced themselves.
        /// A stranger who never arrives, leaves, or runs another app version ends the search.</summary>
        private void TickRoom(float now)
        {
            var link = Link;
            if (link.VersionMismatch || link.Closed || link.Status == DuelLinkStatus.Closed || link.PeerQuit || now >= _roomUntil)
            {
                Abandon();
                return;
            }
            var peer = link.Peer;
            if (peer == null || !link.PeerPresent) return;
            if (!string.IsNullOrEmpty(peer.Name)) OpponentName = peer.Name;
            State = Result.Live;
            End();
        }

        /// <summary>The player gave up (ВЫЙТИ), took a recorded fight instead, or left the found card
        /// before the fight opened. <paramref name="standby"/>: "notify me" is on and the player
        /// went to a recorded fight — the queue keeps them callable.</summary>
        public void Cancel(bool standby = false)
        {
            if (State == Result.Cancelled || State == Result.Fallback) return;
            bool queued = _startedAt >= 0f && _match == null;
            State = Result.Cancelled;
            if (queued) Forget(Call(() => _queue.Leave(_best, standby)));
            else if (_step == Step.Enter && Link == null) DiscardWhenOpen(_enter);
            Release();
            End();
        }

        private void Abandon()
        {
            Release();
            Fallback(null);
        }

        private void Release()
        {
            Link?.Leave();
            Link = null;
            if (_match != null) _queue.Cancel(_match.matchId);
        }

        private void Fallback(LiveQueueGhost ghost)
        {
            Ghost = ghost;
            State = Result.Fallback;
            End();
        }

        private void End()
        {
            try { _queue.End(); }
            catch (Exception e) { Warn("end", e); }
        }

        private static Task<T> Call<T>(Func<Task<T>> call)
        {
            try { return call() ?? Task.FromResult<T>(default); }
            catch (Exception e) { return Task.FromException<T>(e); }
        }

        private static async void Forget(Task task)
        {
            try { await task; } catch (Exception e) { Debug.LogWarning("[LiveQueue] leave: " + e.Message); }
        }

        private static async void DiscardWhenOpen(Task<LiveDuelLink> entering)
        {
            try { (await entering)?.Leave(); } catch (Exception) { /* never opened */ }
        }

        private static void Warn(string what, Exception e) =>
            Debug.LogWarning($"[LiveQueue] {what}: {e?.GetBaseException().Message ?? "no answer"}");
    }
}
