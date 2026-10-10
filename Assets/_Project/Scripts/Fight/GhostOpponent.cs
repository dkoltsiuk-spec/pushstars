using System;
using UnityEngine;
using PushStars.Core;

namespace PushStars.Fight
{
    /// <summary>
    /// Replays a <see cref="GhostRecord"/> as a duel opponent — the player's own best set, fought
    /// again in real time. Mechanically identical to <see cref="BossOpponent"/> (timestamps fire as
    /// the controller's clock passes them); the difference is only where the timeline came from.
    ///
    /// <para>This is the seam phase 12.5 widens: swap the record for one downloaded from another
    /// player's pool and the fight screen doesn't change a line.</para>
    ///
    /// <para>New records replay final Humanoid poses through GhostPosePlayback. Depth01 is only
    /// a compatibility approximation for old timestamp-only recordings. Those files cannot
    /// recover the original descent/ascent or resting posture.</para>
    /// </summary>
    public sealed class GhostOpponent : MonoBehaviour, IOpponentFeed
    {
        private GhostRecord _record;
        private int _nextRepIndex, _nextClapIndex;
        private string _displayName;
        public GhostMotionClip Motion { get; private set; }
        public float Elapsed { get; private set; }
        public byte MotionPhase { get; private set; } = GhostMotionClip.Setup;
        public bool PlaybackActive { get; private set; } = true;
        public bool HasPose => PlaybackActive && Motion != null && Motion.HasPhase(MotionPhase);
        public void SetSetupClock(float elapsed) { MotionPhase = GhostMotionClip.Setup; Elapsed = elapsed; }
        public void StopPlayback() { PlaybackActive = false; IsWorking = false; }

        public string DisplayName => _displayName ?? FightConfig.GhostOpponentName;
        public int Reps { get; private set; }

        /// <summary>Reps in the recording — the target to beat, known before the duel starts.</summary>
        public int ExpectedReps => _record != null ? _record.reps : 0;

        /// <summary>Mean FORM of the recorded set.</summary>
        public float FormPercent => _record != null ? _record.avgForm : 0f;

        /// <summary>Mean seconds per rep across the whole recording, 0 when there is none.</summary>
        public float SecondsPerRep =>
            _record != null && _record.reps > 0 ? _record.durationSec / _record.reps : 0f;

        /// <summary>Where in a push-up the recorded body is right now: 0 at the top, 1 at the
        /// bottom. Drives the opponent's avatar exactly as the CV depth drives the player's.</summary>
        public float Depth01 { get; private set; }

        /// <summary>True while there are still reps to come — the body is working rather than
        /// resting at the top.</summary>
        public bool IsWorking { get; private set; }

        public event Action<int> OnRep;
        /// <summary>Raised with the running count each time the recording lands a clap push-up.</summary>
        public event Action<int> OnClap;

        /// <summary>Hands the feed its recording. Returns false when there is nothing to replay, so
        /// the caller can fall back instead of shipping a silent opponent that never scores.</summary>
        public bool Configure(GhostRecord record, string displayName = null, bool allowEmpty = false)
        {
            _displayName = displayName;
            _record = record != null && (record.IsValid || (allowEmpty && record.reps == 0 && record.repTimes != null && record.repTimes.Length == 0)) ? record : null;
            Motion = null;
            if (_record != null)
                try { Motion = GhostMotionClip.Decode(_record.motionBase64); }
                catch (Exception e) { Debug.LogWarning("[Ghost] Animation could not be loaded: " + e.Message); }
            PlaybackActive = true;
            return _record != null;
        }

        public void Begin()
        {
            Reps = 0;
            Elapsed = 0; MotionPhase = GhostMotionClip.Live; PlaybackActive = true;
            _nextRepIndex = _nextClapIndex = 0;
            Depth01 = 0f;
            IsWorking = false;
        }

        public void Tick(float elapsedSec)
        {
            if (_record == null) return;
            Elapsed = Mathf.Max(0, elapsedSec); MotionPhase = GhostMotionClip.Live;

            var times = _record.repTimes;
            while (_nextRepIndex < times.Length && times[_nextRepIndex] <= elapsedSec)
            {
                _nextRepIndex++;
                Reps++;
                OnRep?.Invoke(Reps);
            }
            var claps = _record.clapTimes;
            while (claps != null && _nextClapIndex < claps.Length && claps[_nextClapIndex] <= elapsedSec)
                OnClap?.Invoke(++_nextClapIndex);

            UpdateDepth(times, elapsedSec);
        }

        /// <summary>One arc per interval between credited reps. A rep is credited at the TOP, so
        /// the body leaves the top after one rep and must be back at the top for the next: a half
        /// sine over the interval is the simplest curve that honours both ends and the single
        /// bottom between them.</summary>
        private void UpdateDepth(float[] times, float elapsedSec)
        {
            if (_nextRepIndex >= times.Length)
            {
                // Set finished — the body rests at the top for whatever is left of the duel.
                IsWorking = false;
                Depth01 = Mathf.MoveTowards(Depth01, 0f, Time.deltaTime * 2f);
                return;
            }

            // Legacy saves have no poses. Keep their score timing but don't stretch a rep
            // over a long rest. New saves replay the actual Humanoid poses.
            float from = Mathf.Max(_nextRepIndex > 0 ? times[_nextRepIndex - 1] : 0f, times[_nextRepIndex] - 1.5f);
            float to = times[_nextRepIndex];
            if (elapsedSec < from) { Depth01 = 0; IsWorking = true; return; }
            float span = to - from;
            if (span <= 0.01f) { Depth01 = 0f; IsWorking = true; return; }

            float u = Mathf.Clamp01((elapsedSec - from) / span);
            Depth01 = Mathf.Sin(u * Mathf.PI);
            IsWorking = true;
        }
    }
}
