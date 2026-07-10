using System;

namespace PushStars.CV
{
    public enum WorkoutSetState
    {
        /// <summary>Session started, user has never armed yet.</summary>
        Idle = 0,
        /// <summary>Armed and counting — the user is in a working set.</summary>
        Active = 1,
        /// <summary>Was active, then disarmed (wrists lifted / left the plank) — resting. The rep
        /// counter is already gated off by the armer; this state exists for UX ("ОТДЫХ").</summary>
        Resting = 2,
        /// <summary>Rest exceeded <see cref="CVConstants.RestToSetCompleteSec"/> — the set is over.
        /// Re-arming starts the next set.</summary>
        SetComplete = 3,
    }

    /// <summary>
    /// Session-level set/rest semantics on top of the PlankArmer (owner's request: "both wrists
    /// off the floor → counting stops and it's rest mode / set finished"). The counting stop
    /// itself is enforced upstream (armer gate + the immediate Airborne suspension in
    /// PushupSession); this tracker only CLASSIFIES the pause so the UI can tell
    /// "resting between reps" from "the set is done".
    ///
    /// Pure C#: Tick with (isArmed, totalReps, now); events for the HUD / future duel logic.
    /// </summary>
    public sealed class WorkoutSetTracker
    {
        public WorkoutSetState State { get; private set; } = WorkoutSetState.Idle;

        /// <summary>1-based set number; 0 until the first arm.</summary>
        public int SetIndex { get; private set; }

        /// <summary>Reps credited within the current (or just-completed) set.</summary>
        public int RepsInSet { get; private set; }

        /// <summary>Seconds spent in the current rest; 0 outside Resting.</summary>
        public float RestingForSec { get; private set; }

        public event Action OnRestStarted;
        /// <summary>Rest ended before the set-complete timeout — same set continues.</summary>
        public event Action OnSetResumed;
        /// <summary>(setIndex, repsInSet) — rest outlasted the timeout, the set is closed.</summary>
        public event Action<int, int> OnSetCompleted;

        private int _repsAtSetStart;
        private float _restStartedAt = -1f;

        public void Reset()
        {
            State = WorkoutSetState.Idle;
            SetIndex = 0;
            RepsInSet = 0;
            RestingForSec = 0f;
            _repsAtSetStart = 0;
            _restStartedAt = -1f;
        }

        public void Tick(bool isArmed, int totalReps, float nowSec)
        {
            switch (State)
            {
                case WorkoutSetState.Idle:
                    if (isArmed) StartSet(totalReps);
                    break;

                case WorkoutSetState.Active:
                    RepsInSet = totalReps - _repsAtSetStart;
                    if (!isArmed)
                    {
                        State = WorkoutSetState.Resting;
                        _restStartedAt = nowSec;
                        RestingForSec = 0f;
                        OnRestStarted?.Invoke();
                    }
                    break;

                case WorkoutSetState.Resting:
                    RestingForSec = nowSec - _restStartedAt;
                    if (isArmed)
                    {
                        State = WorkoutSetState.Active;
                        RestingForSec = 0f;
                        OnSetResumed?.Invoke();
                    }
                    else if (RestingForSec >= CVConstants.RestToSetCompleteSec)
                    {
                        State = WorkoutSetState.SetComplete;
                        OnSetCompleted?.Invoke(SetIndex, RepsInSet);
                    }
                    break;

                case WorkoutSetState.SetComplete:
                    RestingForSec = 0f;
                    if (isArmed) StartSet(totalReps); // next set
                    break;
            }
        }

        private void StartSet(int totalReps)
        {
            SetIndex++;
            _repsAtSetStart = totalReps;
            RepsInSet = 0;
            RestingForSec = 0f;
            State = WorkoutSetState.Active;
        }
    }
}
