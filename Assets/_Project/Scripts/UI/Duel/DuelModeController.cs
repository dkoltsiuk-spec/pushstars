using System;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Drives the three mode chips on the main (Duel) screen:
    ///   • Mode chip      — toggles ДУЭЛЬ ↔ ТРЕНИР. (also re-labels the primary CTA).
    ///   • Exercise chip  — push-ups only for the MVP; a tap shows a "coming soon" hint
    ///                      because squats (приседания) land in a later phase.
    ///   • Duration chip  — toggles 60 СЕК ↔ МАКС (max-reps mode).
    ///
    /// Phase 03: pure UI state. Phase 14 reads <see cref="CurrentMode"/> /
    /// <see cref="CurrentDuration"/> when it assembles the match request — nothing here
    /// talks to matchmaking yet.
    /// </summary>
    public class DuelModeController : MonoBehaviour
    {
        public enum Mode { Duel, Training }
        public enum Duration { Sixty, Max }

        [Header("Chips")]
        [SerializeField] private SecondaryChip _modeChip;
        [SerializeField] private SecondaryChip _exerciseChip;
        [SerializeField] private SecondaryChip _durationChip;

        [Header("Primary CTA (re-labelled per mode)")]
        [SerializeField] private PrimaryButton _findButton;

        [Header("Transient hint")]
        [SerializeField] private Toast _toast;

        public Mode     CurrentMode     { get; private set; } = Mode.Duel;
        public Duration CurrentDuration { get; private set; } = Duration.Sixty;

        public event Action<Mode>     OnModeChanged;
        public event Action<Duration> OnDurationChanged;

        // Wiring happens in Start (not Awake) so every SecondaryChip.Awake has already
        // initialised its Button property regardless of script execution order.
        private void Start()
        {
            if (_modeChip != null     && _modeChip.Button != null)     _modeChip.Button.onClick.AddListener(ToggleMode);
            if (_exerciseChip != null && _exerciseChip.Button != null) _exerciseChip.Button.onClick.AddListener(ShowExerciseHint);
            if (_durationChip != null && _durationChip.Button != null) _durationChip.Button.onClick.AddListener(ToggleDuration);

            ApplyMode();
            ApplyDuration();
        }

        public void ToggleMode()
        {
            CurrentMode = CurrentMode == Mode.Duel ? Mode.Training : Mode.Duel;
            ApplyMode();
            OnModeChanged?.Invoke(CurrentMode);
        }

        public void ToggleDuration()
        {
            CurrentDuration = CurrentDuration == Duration.Sixty ? Duration.Max : Duration.Sixty;
            ApplyDuration();
            OnDurationChanged?.Invoke(CurrentDuration);
        }

        /// <summary>Push-ups is the only MVP exercise; squats are flagged as upcoming.</summary>
        public void ShowExerciseHint()
        {
            if (_toast != null)
                _toast.Show("Приседания скоро — пока только отжимания");
        }

        private void ApplyMode()
        {
            if (_modeChip != null)
            {
                _modeChip.SetLabel(CurrentMode == Mode.Duel ? "ДУЭЛЬ" : "ТРЕНИР.");
                _modeChip.SetSelected(true); // the mode chip always reads as the active selection
            }

            if (_findButton != null)
                _findButton.SetLabel(CurrentMode == Mode.Duel ? "НАЙТИ СОПЕРНИКА" : "НАЧАТЬ ТРЕНИРОВКУ");
        }

        private void ApplyDuration()
        {
            if (_durationChip != null)
                _durationChip.SetLabel(CurrentDuration == Duration.Sixty ? "60 СЕК" : "МАКС");
        }
    }
}
