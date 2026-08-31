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
    /// </summary>
    public sealed class GhostOpponent : MonoBehaviour, IOpponentFeed
    {
        private GhostRecord _record;
        private int _nextRepIndex;

        public string DisplayName => FightConfig.GhostOpponentName;
        public int Reps { get; private set; }

        /// <summary>Reps in the recording — the target to beat, known before the duel starts.</summary>
        public int TargetReps => _record != null ? _record.reps : 0;

        public event Action<int> OnRep;

        /// <summary>Hands the feed its recording. Returns false when there is nothing to replay, so
        /// the caller can fall back instead of shipping a silent opponent that never scores.</summary>
        public bool Configure(GhostRecord record)
        {
            _record = record != null && record.IsValid ? record : null;
            return _record != null;
        }

        public void Begin()
        {
            Reps = 0;
            _nextRepIndex = 0;
        }

        public void Tick(float elapsedSec)
        {
            if (_record == null) return;
            var times = _record.repTimes;
            while (_nextRepIndex < times.Length && times[_nextRepIndex] <= elapsedSec)
            {
                _nextRepIndex++;
                Reps++;
                OnRep?.Invoke(Reps);
            }
        }
    }
}
