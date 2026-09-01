using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using PushStars.Core;

namespace PushStars.Fight
{
    /// <summary>
    /// Full-screen result overlay, in two layouts because the two results answer different
    /// questions.
    ///
    ///   • <see cref="ShowDuel"/> — a trading card for each fighter: portrait, name, reps, FORM and
    ///     tempo, with the verdict banner across the seam. The winner's count goes green and the
    ///     loser's red, so who won is legible before a single word is read — and FORM/TEMPO get the
    ///     same treatment stat-by-stat, so "who actually moved better" doesn't hide behind who
    ///     happened to finish more reps.
    ///   • <see cref="ShowLevelTest"/> — the onboarding measurement: the tier the player landed in,
    ///     what it means, and the fact that their set is now the opponent they will fight.
    ///
    /// Hidden until shown; the fight HUD stays underneath but this covers it.
    /// </summary>
    public sealed class FightResultScreen : MonoBehaviour
    {
        [SerializeField] private GameObject _root;

        [Header("Duel layout")]
        [SerializeField] private GameObject _duelLayout;
        [SerializeField] private TextMeshProUGUI _banner;
        [SerializeField] private TextMeshProUGUI _opponentName;
        [SerializeField] private TextMeshProUGUI _opponentReps;
        [SerializeField] private TextMeshProUGUI _opponentForm;
        [SerializeField] private TextMeshProUGUI _opponentTempo;
        [Tooltip("This screen's own crop of the opponent's body. MirrorTexture points it at the " +
                 "render the duel HUD already shows full-size — no camera of its own.")]
        [SerializeField] private RawImage _opponentAvatarImage;
        [SerializeField] private RawImage _opponentAvatarSource;
        [SerializeField] private TextMeshProUGUI _playerName;
        [SerializeField] private TextMeshProUGUI _playerReps;
        [SerializeField] private TextMeshProUGUI _playerForm;
        [SerializeField] private TextMeshProUGUI _playerTempo;
        [SerializeField] private RawImage _playerAvatarImage;
        [SerializeField] private RawImage _playerAvatarSource;
        [SerializeField] private TextMeshProUGUI _duelRewards;
        [SerializeField] private TextMeshProUGUI _duelNote;

        [Header("Level-test layout")]
        [SerializeField] private GameObject _levelTestLayout;
        [SerializeField] private TextMeshProUGUI _testTitle;
        [SerializeField] private TextMeshProUGUI _testTier;
        [SerializeField] private TextMeshProUGUI _testScore;
        [SerializeField] private TextMeshProUGUI _testRewards;
        [SerializeField] private TextMeshProUGUI _testNote;

        [Header("Actions")]
        [SerializeField] private Button _continueButton;
        [SerializeField] private TextMeshProUGUI _continueLabel;
        [Tooltip("Second action, shown only when the result offers one (a failed level test).")]
        [SerializeField] private Button _secondaryButton;
        [SerializeField] private TextMeshProUGUI _secondaryLabel;

        private static readonly Color WinColor = new Color32(107, 255, 74, 255); // AccentLime
        private static readonly Color LossColor = new Color32(255, 80, 80, 255);
        private static readonly Color DrawColor = new Color32(245, 200, 66, 255); // AccentYellow
        private static readonly Color NeutralColor = Color.white;

        private void Awake()
        {
            if (_root != null) _root.SetActive(false);
            if (_secondaryButton != null) _secondaryButton.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_continueButton != null) _continueButton.onClick.RemoveAllListeners();
            if (_secondaryButton != null) _secondaryButton.onClick.RemoveAllListeners();
        }

        // ── Duel ─────────────────────────────────────────────────────────────────────────────────

        public void ShowDuel(bool win, bool draw, int myReps, int oppReps, float myForm, float oppForm,
                             float myRepsPerMinute, float oppSecondsPerRep, long xp, int trophies,
                             string opponentName, string playerName, bool newRecord)
        {
            Open(duel: true);

            SetText(_banner, draw ? "НИЧЬЯ" : win ? "ПОБЕДА" : "ПОРАЖЕНИЕ",
                    draw ? DrawColor : win ? WinColor : LossColor);

            Color mine = draw ? DrawColor : win ? WinColor : LossColor;
            Color theirs = draw ? DrawColor : win ? LossColor : WinColor;

            // FORM and TEMPO are judged against each other too, independently of who won on reps —
            // a loser who moved better than the winner should see that, not just red across the
            // board. Tempo is compared on ONE clock (seconds per rep, converted from whichever unit
            // the caller handed us) so "faster" always means the same thing on both sides.
            float mySecondsPerRep = myRepsPerMinute > 0.01f ? 60f / myRepsPerMinute : float.PositiveInfinity;
            float oppSecPerRep = oppSecondsPerRep > 0.01f ? oppSecondsPerRep : float.PositiveInfinity;
            BetterWorse(myForm, oppForm, higherIsBetter: true, out Color myFormColor, out Color oppFormColor);
            BetterWorse(mySecondsPerRep, oppSecPerRep, higherIsBetter: false, out Color myTempoColor, out Color oppTempoColor);

            SetText(_opponentName, opponentName, NeutralColor);
            SetText(_opponentReps, oppReps.ToString(), theirs);
            SetText(_opponentForm, $"{oppForm:0}", oppFormColor);
            SetText(_opponentTempo, oppSecPerRep < float.PositiveInfinity ? $"{oppSecPerRep:0.0}с" : "—", oppTempoColor);

            SetText(_playerName, playerName, NeutralColor);
            SetText(_playerReps, myReps.ToString(), mine);
            SetText(_playerForm, $"{myForm:0}", myFormColor);
            SetText(_playerTempo, mySecondsPerRep < float.PositiveInfinity ? $"{mySecondsPerRep:0.0}с" : "—", myTempoColor);

            // Same trick as the ready card: both stages already have the real bodies rendering by
            // the time anything's Start() reaches here (Unity runs every Awake before any Start),
            // so pointing at their textures needs no camera of this screen's own.
            MirrorTexture(_opponentAvatarImage, _opponentAvatarSource);
            MirrorTexture(_playerAvatarImage, _playerAvatarSource);

            string rewards = xp > 0 ? $"+{xp} XP" : "";
            // Spelled out, not an emoji: the UI font is Rubik and a trophy glyph would render
            // as a box on device.
            if (trophies != 0)
                rewards += (rewards.Length > 0 ? "   " : "") + $"{trophies:+#;-#;0} КУБКОВ";
            SetText(_duelRewards, rewards, win ? WinColor : DrawColor);

            SetText(_duelNote, newRecord ? "НОВЫЙ РЕКОРД — теперь тень сильнее" : "", DrawColor);

            SetPrimary("ДАЛЕЕ", Continue);
            HideSecondary();
        }

        /// <summary>Colours two comparable numbers by which one actually is better, not by who won
        /// the match — a tie (including "neither side has a number") stays neutral rather than
        /// picking a winner that doesn't exist.</summary>
        private static void BetterWorse(float mine, float theirs, bool higherIsBetter,
                                        out Color mineColor, out Color theirsColor)
        {
            bool bothMissing = float.IsInfinity(mine) && float.IsInfinity(theirs);
            bool tied = !bothMissing && Mathf.Approximately(mine, theirs);
            if (bothMissing || tied) { mineColor = NeutralColor; theirsColor = NeutralColor; return; }

            bool iAmBetter = higherIsBetter ? mine > theirs : mine < theirs;
            mineColor = iAmBetter ? WinColor : LossColor;
            theirsColor = iAmBetter ? LossColor : WinColor;
        }

        /// <summary>Points this screen's crop at the same texture the duel HUD already renders to —
        /// a reference copy, not a render of its own.</summary>
        private static void MirrorTexture(RawImage target, RawImage source)
        {
            if (target == null || source == null) return;
            target.texture = source.texture;
        }

        // ── Level test ───────────────────────────────────────────────────────────────────────────

        /// <summary>The onboarding measurement. A zero-rep result is not a level: nothing was
        /// measured, so instead of stamping the player as a beginner it offers the test again, with
        /// a way past it for anyone whose camera simply will not cooperate.</summary>
        public void ShowLevelTest(int reps, FitnessTier tier, long xp, bool recorded)
        {
            Open(duel: false);

            if (reps <= 0)
            {
                SetText(_testTitle, "НЕ ЗАСЧИТАНО", LossColor);
                SetText(_testTier, "0", NeutralColor);
                SetText(_testScore, "Ни одного повтора за 60 секунд", new Color(1f, 1f, 1f, 0.7f));
                SetText(_testRewards, "", NeutralColor);
                SetText(_testNote, "Поставь телефон в 1.5–2 метрах так, чтобы в кадр попало всё тело.",
                        new Color(1f, 1f, 1f, 0.55f));

                SetPrimary("ПОПРОБОВАТЬ СНОВА", Retry);
                SetSecondary("ПРОПУСТИТЬ", SkipLevelTest);
                return;
            }

            SetText(_testTitle, "ТВОЙ УРОВЕНЬ", new Color(1f, 1f, 1f, 0.7f));
            SetText(_testTier, FitnessTest.DisplayName(tier), DrawColor);
            SetText(_testScore, $"{reps} отжиманий за 60 секунд", NeutralColor);
            SetText(_testRewards, xp > 0 ? $"+{xp} XP" : "", WinColor);

            string note = FitnessTest.Blurb(tier);
            if (recorded) note += "\nЗапись сохранена — теперь тебе есть с кем драться.";
            SetText(_testNote, note, new Color(1f, 1f, 1f, 0.55f));

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

        private void Open(bool duel)
        {
            if (_root != null) _root.SetActive(true);
            if (_duelLayout != null) _duelLayout.SetActive(duel);
            if (_levelTestLayout != null) _levelTestLayout.SetActive(!duel);
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
