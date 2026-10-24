using PushStars.Core;
using UnityEngine;

namespace PushStars.Fight
{
    public sealed partial class FightController
    {
        private TrainingProgress _training;
        private int _trainingReps, _trainingFormCount;
        private float _trainingFormSum;
        private long _trainingXp;
        private bool _trainingRecord;
        private TrainingScreen _trainingScreen;
        private float _trainingElapsed;
        /// <summary>The player has been out of the plank since the rest began: a plank now is a
        /// new one, not the end of the set still being held.</summary>
        private bool _restLeftPlank;
        private string TrainingCaption => $"SET {_training.CurrentSet}/{_training.Plan.Sets}";

        private void FinishTrainingSet(int reps, long xp)
        {
            if (reps >= EconomyConfig.MinSessionReps) xp += EconomyConfig.SessionCompleteXp;
            LocalProfile.RecordWorkout(_rewardSessionId + ":set:" + _training.CurrentSet, "training", reps, xp,
                durationSec: Mathf.Clamp(Mathf.RoundToInt(Time.time - _liveStartTime), 0, TrainingPlan.SetSeconds));
            _trainingReps += reps;
            foreach (float form in _repForms) { _trainingFormSum += form; _trainingFormCount++; }
            _trainingXp += xp;
            _trainingRecord |= GhostStore.SaveIfBest(NewRecord("training"));
            _training.CompleteSet();
            if (!_training.IsComplete)
            {
                _phase = Phase.Rest;
                // The camera keeps watching through the rest: a plank starts the next set.
                _session.enabled = true;
                _restLeftPlank = false;
                foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
                    avatar.SetRestPresentation(true);
                _hud.SetScoresVisible(true);
                _hud.ShowTrainingRest(_training.CurrentSet, _training.Plan.Sets, _training.RestRemaining);
                return;
            }
            // The finished workout gets the duel's one result screen; its rewards then fly home.
            float averageForm = _trainingFormCount == 0 ? 0 : _trainingFormSum / _trainingFormCount;
            _auraMoments = AuraCalculator.ForWorkout(_trainingReps, averageForm);
            PresentResults(new FightResultData {
                Mode = FightMode.Training, MyReps = _trainingReps, MyForm = averageForm,
                Xp = _trainingXp, PlayerName = PlayerLabel, NewRecord = _trainingRecord,
                TrainingSets = _training.CompletedSets,
                LeagueStatus = _training.CompletedSets == 1 ? "1 SET" : _training.CompletedSets + " SETS"
            });
        }

        private void TickTrainingRest()
        {
            // Hands-free: getting back into a plank starts the next set — nobody has to walk to the
            // phone for START SET. A plank still held from the set that just ended does not count.
            var armer = _session.Armer;
            bool armed = armer != null && armer.IsArmed;
            if (!armed) _restLeftPlank = true;
            else if (_restLeftPlank && _training.Continue()) { BeginNextTrainingSet(); return; }
            if (_training.TickRest(Time.deltaTime))
            {
                // Rest is over: a chime from across the room says "get into a plank".
                GameAudio.Play(SoundCue.Confirm);
                BeginNextTrainingSet();
                return;
            }
            _hud.ShowTrainingRest(_training.CurrentSet, _training.Plan.Sets, _training.RestRemaining);
        }

        private void BeginNextTrainingSet()
        {
            _session.ResetSession();
            _session.enabled = true;
            _phase = Phase.WaitPlank;
            _hud.ConfigureSolo(TrainingCaption);
            _hud.SetScoresVisible(true);
            _hud.SetTimer(TrainingPlan.SetSeconds);
            foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
            {
                avatar.SetRestPresentation(false);
                avatar.SetPreparationPresentation(false);
            }
        }

        private void InitializeTrainingScreen()
        {
            _trainingScreen = _hud.GetComponent<TrainingScreen>();
            if (_trainingScreen == null) return;
            _hud.SetLayoutAvailable(false);
            _trainingScreen.Bind(ExitToCaller, ToggleTrainingPause, FinishEarly,
                () => { if (!_paused) _training.AddRest(15); },
                () => { if (!_paused && _training.Continue()) BeginNextTrainingSet(); },
                () => { if (_training.AddSet()) BeginNextTrainingSet(); });
        }
        private void ToggleTrainingPause()
        {
            if (_phase == Phase.Finished) return;
            if (_phase == Phase.Rest)
            {
                _paused = !_paused;
            }
            else if (_paused) ResumeSet();
            else PauseSet();
            _trainingScreen.SetPaused(_paused);
        }
        private void UpdateTrainingScreen()
        {
            if (_trainingScreen == null || _training == null || _phase == Phase.Finished) return;
            if (!_paused) _trainingElapsed += Time.deltaTime;
            if (_phase == Phase.Rest)
            {
                _trainingScreen.ShowRest(_training.CompletedSets, _training.Plan.Sets, _training.RestRemaining,
                    _trainingElapsed, _training.Plan.IsManualRest);
                return;
            }
            float remaining = _phase == Phase.Live ? TrainingPlan.SetSeconds - ((_paused ? _pausedAt : Time.time) - _liveStartTime) : TrainingPlan.SetSeconds;
            string hint = _phase == Phase.WaitPlank ? (_session.Armer == null ? "LOADING CAMERA…" : "HOLD A PLANK TO START") :
                _phase == Phase.Countdown ? Mathf.Max(1, Mathf.CeilToInt(_countdownEndTime - Time.time)).ToString() : "";
            _trainingScreen.ShowExercise(_training.CurrentSet, _training.Plan.Sets,
                _phase == Phase.Live ? _repTimes.Count : 0,
                _session.Form, remaining, hint, _phase == Phase.Live && !_paused, _phase == Phase.Live);
        }
    }
}
