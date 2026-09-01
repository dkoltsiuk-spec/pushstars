using System;
using UnityEngine;
using PushStars.Core;

namespace PushStars.Fight
{
    /// <summary>
    /// Plays back a <see cref="BossProfile"/> rep timeline (phase 08.9). Purely deterministic:
    /// reps fire when the controller's clock passes their timestamps.
    /// </summary>
    public sealed class BossOpponent : MonoBehaviour, IOpponentFeed
    {
        private BossProfile _profile;
        private int _nextRepIndex;

        public string DisplayName => _profile != null ? _profile.DisplayName : "БОСС";
        public int Reps { get; private set; }

        public int ExpectedReps => _profile != null ? _profile.RepTimes.Count : 0;

        /// <summary>A scripted boss has no technique to report, only a timeline.</summary>
        public float FormPercent => 0f;

        public float SecondsPerRep =>
            _profile != null && _profile.RepTimes.Count > 0
                ? FightConfig.DuelDurationSec / (float)_profile.RepTimes.Count
                : 0f;

        public event Action<int> OnRep;

        /// <summary>Called by the controller on scene start with the ladder's current boss.</summary>
        public void Configure(BossProfile profile) => _profile = profile;

        public void Begin()
        {
            Reps = 0;
            _nextRepIndex = 0;
        }

        public void Tick(float elapsedSec)
        {
            if (_profile == null) return;
            var times = _profile.RepTimes;
            while (_nextRepIndex < times.Count && times[_nextRepIndex] <= elapsedSec)
            {
                _nextRepIndex++;
                Reps++;
                OnRep?.Invoke(Reps);
            }
        }
    }
}
