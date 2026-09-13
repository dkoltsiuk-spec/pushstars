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
        private string TrainingCaption => $"ПОДХОД {_training.CurrentSet}/{_training.Plan.Sets}";

        private void FinishTrainingSet(int reps, long xp)
        {
            if (reps >= EconomyConfig.MinSessionReps) xp += EconomyConfig.SessionCompleteXp;
            _trainingReps += reps;
            foreach (float form in _repForms) { _trainingFormSum += form; _trainingFormCount++; }
            _trainingXp += xp;
            _trainingRecord |= GhostStore.SaveIfBest(NewRecord("training"));
            LocalProfile.RecordSet(reps);
            OfflineXpBank.Add(xp);
            _training.CompleteSet();
            if (!_training.IsComplete)
            {
                _phase = Phase.Rest;
                _hud.SetScoresVisible(true);
                _hud.ShowTrainingRest(_training.CurrentSet, _training.Plan.Sets, _training.RestRemaining);
                return;
            }
            if (_trainingScreen != null)
            {
                _trainingScreen.ShowResults(_trainingReps, _training.CompletedSets,
                    _trainingFormCount == 0 ? 0 : _trainingFormSum / _trainingFormCount,
                    _trainingXp, _training.Plan.Sets < TrainingPlan.MaxSets);
                return;
            }
            PresentResults(new FightResultData {
                Mode = FightMode.Training, MyReps = _trainingReps,
                MyForm = _trainingFormCount == 0 ? 0 : _trainingFormSum / _trainingFormCount,
                Xp = _trainingXp, PlayerName = PlayerLabel, NewRecord = _trainingRecord,
                TrainingSets = _training.CompletedSets
            });
        }

        private void TickTrainingRest()
        {
            if (_training.TickRest(Time.deltaTime)) { BeginNextTrainingSet(); return; }
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
                avatar.SetPreparationPresentation(false);
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
                _phase == Phase.Live ? Mathf.Max(0, _session.Reps - _baselineReps) : 0,
                _session.Form, remaining, hint, _phase == Phase.Live && !_paused, _phase == Phase.Live);
        }
    }
}
