using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using PushStars.Core;

namespace PushStars.Fight
{
    /// <summary>
    /// Full-screen result overlay. Two shapes, one layout:
    ///
    ///   • <see cref="ShowDuel"/> — ПОБЕДА / ПОРАЖЕНИЕ / НИЧЬЯ, the rep score, XP and trophies.
    ///   • <see cref="ShowLevelTest"/> — the onboarding measurement: the tier the player landed in,
    ///     what it means, and the fact that their set is now the opponent they will fight.
    ///
    /// Hidden until shown; the fight HUD stays underneath but this covers it.
    /// </summary>
    public sealed class FightResultScreen : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TextMeshProUGUI _title;
        [Tooltip("The headline number/word under the title: the tier, or the score line.")]
        [SerializeField] private TextMeshProUGUI _subtitle;
        [SerializeField] private TextMeshProUGUI _score;
        [SerializeField] private TextMeshProUGUI _rewards;
        [Tooltip("One quiet line at the bottom: what the result means or what was saved.")]
        [SerializeField] private TextMeshProUGUI _note;
        [SerializeField] private Button _continueButton;
        [SerializeField] private TextMeshProUGUI _continueLabel;
        [Tooltip("Second action, shown only when the result offers one (a failed level test).")]
        [SerializeField] private Button _secondaryButton;
        [SerializeField] private TextMeshProUGUI _secondaryLabel;

        private static readonly Color WinColor  = new Color32(107, 255,  74, 255); // AccentLime
        private static readonly Color LossColor = new Color32(255,  80,  80, 255);
        private static readonly Color DrawColor = new Color32(245, 200,  66, 255); // AccentYellow

        private void Awake()
        {
            if (_root != null) _root.SetActive(false);
            if (_continueButton != null) _continueButton.onClick.AddListener(Continue);
            if (_secondaryButton != null) _secondaryButton.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_continueButton != null) _continueButton.onClick.RemoveAllListeners();
            if (_secondaryButton != null) _secondaryButton.onClick.RemoveAllListeners();
        }

        // ── Duel ─────────────────────────────────────────────────────────────────────────────────

        public void ShowDuel(bool win, bool draw, int myReps, int oppReps, long xp, int trophies,
                             string opponentName, bool newRecord)
        {
            Open();

            SetText(_title, draw ? "НИЧЬЯ" : win ? "ПОБЕДА!" : "ПОРАЖЕНИЕ",
                    draw ? DrawColor : win ? WinColor : LossColor);
            SetText(_subtitle, $"{myReps} : {oppReps}", Color.white);
            SetText(_score, $"ТЫ — {opponentName}", new Color(1f, 1f, 1f, 0.7f));

            string rewards = xp > 0 ? $"+{xp} XP" : "";
            // Spelled out, not an emoji: the UI font is Rubik and a trophy glyph would render
            // as a box on device.
            if (trophies != 0)
                rewards += (rewards.Length > 0 ? "   " : "") + $"{trophies:+#;-#;0} КУБКОВ";
            SetText(_rewards, rewards, win ? WinColor : DrawColor);

            SetText(_note, newRecord ? "НОВЫЙ РЕКОРД — теперь тень сильнее" : "", DrawColor);

            SetPrimary("ДАЛЕЕ", Continue);
            HideSecondary();
        }

        // ── Level test ───────────────────────────────────────────────────────────────────────────

        /// <summary>The onboarding measurement. A zero-rep result is not a level: nothing was
        /// measured, so instead of stamping the player as a beginner it offers the test again, with
        /// a way past it for anyone whose camera simply will not cooperate.</summary>
        public void ShowLevelTest(int reps, FitnessTier tier, long xp, bool recorded)
        {
            Open();

            if (reps <= 0)
            {
                SetText(_title, "НЕ ЗАСЧИТАНО", LossColor);
                SetText(_subtitle, "0", Color.white);
                SetText(_score, "Ни одного повтора за 60 секунд", new Color(1f, 1f, 1f, 0.7f));
                SetText(_rewards, "", Color.white);
                SetText(_note, "Поставь телефон в 1.5–2 метрах так, чтобы в кадр попало всё тело.",
                        new Color(1f, 1f, 1f, 0.55f));

                SetPrimary("ПОПРОБОВАТЬ СНОВА", Retry);
                SetSecondary("ПРОПУСТИТЬ", SkipLevelTest);
                return;
            }

            SetText(_title, "ТВОЙ УРОВЕНЬ", new Color(1f, 1f, 1f, 0.7f));
            SetText(_subtitle, FitnessTest.DisplayName(tier), DrawColor);
            SetText(_score, $"{reps} отжиманий за 60 секунд", Color.white);
            SetText(_rewards, xp > 0 ? $"+{xp} XP" : "", WinColor);

            string note = FitnessTest.Blurb(tier);
            if (recorded) note += "\nЗапись сохранена — теперь тебе есть с кем драться.";
            SetText(_note, note, new Color(1f, 1f, 1f, 0.55f));

            SetPrimary("ПРОДОЛЖИТЬ", Continue);
            HideSecondary();
        }

        // ── Actions ──────────────────────────────────────────────────────────────────────────────

        private void Continue() => SceneManager.LoadScene(FightRequest.ReturnScene);

        private void Retry()
        {
            FightRequest.LevelTest(FightRequest.ReturnScene);
            SceneManager.LoadScene(FightConfig.FightSceneName);
        }

        /// <summary>Accepts a zero so the player is not stuck in the test forever. They keep no
        /// ghost and no tier; the main screen sends them back into a level test when they next
        /// look for an opponent.</summary>
        private void SkipLevelTest()
        {
            OnboardingState.CompleteLevelTest(0);
            Continue();
        }

        // ── Plumbing ─────────────────────────────────────────────────────────────────────────────

        private void Open()
        {
            if (_root != null) _root.SetActive(true);
        }

        private void SetPrimary(string label, UnityEngine.Events.UnityAction action)
        {
            if (_continueButton == null) return;
            _continueButton.gameObject.SetActive(true);
            _continueButton.onClick.RemoveAllListeners();
            _continueButton.onClick.AddListener(action);
            if (_continueLabel != null) _continueLabel.text = label;
        }

        private void SetSecondary(string label, UnityEngine.Events.UnityAction action)
        {
            if (_secondaryButton == null) return;
            _secondaryButton.gameObject.SetActive(true);
            _secondaryButton.onClick.RemoveAllListeners();
            _secondaryButton.onClick.AddListener(action);
            if (_secondaryLabel != null) _secondaryLabel.text = label;
        }

        private void HideSecondary()
        {
            if (_secondaryButton != null) _secondaryButton.gameObject.SetActive(false);
        }

        private static void SetText(TextMeshProUGUI label, string text, Color color)
        {
            if (label == null) return;
            label.text = text;
            label.color = color;
            label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }
    }
}
