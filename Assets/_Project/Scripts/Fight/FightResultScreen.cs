using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using PushStars.Core;
using PushStars.OTA;
using PushStars.UI;
using PushStars.UI.Layout;

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
        [SerializeField, HideInInspector] private bool _sceneAuthored;
        [SerializeField] private Texture2D _standingPortrait;
        public GameObject Root => _root;
        public bool IsSceneAuthored => _sceneAuthored;

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
        [SerializeField, HideInInspector] private ScreenLayoutRoot _editableLayout;
        private FightRewardFlow.Summary _summary;
        private bool _hasSummary;
        [SerializeField, HideInInspector] private bool _layoutDefaultsApplied;
        private bool _sourcesHidden;
        private bool _playerSourceWasEnabled, _opponentSourceWasEnabled;
        private FightAvatar _playerStage, _opponentStage;
        public bool IsShowing => _root != null && _root.activeSelf;
        public RawImage PlayerAvatarSource => _playerAvatarSource;
        public RawImage OpponentAvatarSource => _opponentAvatarSource;
        public System.Action PreviewContinuation { get; set; }
        public System.Action ContinueRequested { get; set; }

        public void MarkSceneAuthored(Texture2D standingPortrait)
        {
            _sceneAuthored = true;
            _layoutDefaultsApplied = true;
            _standingPortrait = standingPortrait;
            if (_editableLayout != null) _editableLayout.MarkSceneAuthored();
            CropPortrait(_playerAvatarImage, _playerAvatarSource, null);
            CropPortrait(_opponentAvatarImage, _opponentAvatarSource, null);
        }

        public void SetRewardSummary(int reps, float form, long xp, int trophies, string playerName)
        {
            _hasSummary = true;
            _summary = new FightRewardFlow.Summary
            {
                PlayerName = playerName, TotalReps = reps, Technique = form / 100f,
                EnergyXp = xp, Trophies = trophies, Aura = 0,
                HasCase = CaseRewards.Pending != null, AvatarSource = _playerAvatarSource
            };
        }

        public void Hide()
        {
            RestoreSourcePortraits();
            if (_root != null) _root.SetActive(false);
        }

        private void Awake()
        {
            if (!_sceneAuthored && _root != null) _root.SetActive(false);
            if (_secondaryButton != null) _secondaryButton.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            RestoreSourcePortraits();
            if (_continueButton != null) _continueButton.onClick.RemoveAllListeners();
            if (_secondaryButton != null) _secondaryButton.onClick.RemoveAllListeners();
        }

        private void OnDisable() => RestoreSourcePortraits();

        private void LateUpdate()
        {
            if (!IsShowing || _duelLayout == null || !_duelLayout.activeSelf) return;
            // Only the UV crop follows the settling stage cameras; saved portrait boxes stay put.
            CropPortrait(_opponentAvatarImage, _opponentAvatarSource, _opponentStage);
            CropPortrait(_playerAvatarImage, _playerAvatarSource, _playerStage);
        }

        // ── Duel ─────────────────────────────────────────────────────────────────────────────────

        public void ShowDuel(bool win, bool draw, int myReps, int oppReps, float myForm, float oppForm,
                             float myRepsPerMinute, float oppSecondsPerRep, long xp, int trophies,
                             string opponentName, string playerName, bool newRecord)
        {
            Open(duel: true);
            GameAudio.Play(win && !draw ? SoundCue.Victory : draw ? SoundCue.Confirm : SoundCue.Back);

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

            _opponentStage = AvatarBehind(_opponentAvatarSource);
            _playerStage = AvatarBehind(_playerAvatarSource);
            CropPortrait(_opponentAvatarImage, _opponentAvatarSource, _opponentStage);
            CropPortrait(_playerAvatarImage, _playerAvatarSource, _playerStage);

            // The next screen presents the credited XP/trophies with their own animation and room.
            // Keeping the old repeated line here obscured the lower portrait and action button.
            SetText(_duelRewards, "", NeutralColor);

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

        private static FightAvatar AvatarBehind(RawImage source)
        {
            if (source == null || source.texture == null) return null;
            foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (avatar.StageCamera != null && avatar.StageCamera.targetTexture == source.texture) return avatar;
            return null;
        }

        private void CropPortrait(RawImage target, RawImage source, FightAvatar stage)
        {
            if (target == null) return;
            if (_sceneAuthored && (!Application.isPlaying || source == null || source.texture == null))
            {
                target.texture = _standingPortrait;
                float fallbackWidth = _standingPortrait != null
                    ? target.rectTransform.rect.width / Mathf.Max(1f, target.rectTransform.rect.height) * _standingPortrait.height / _standingPortrait.width : 1f;
                target.uvRect = new Rect((1f - fallbackWidth) * 0.5f, 0f, fallbackWidth, 1f);
                target.enabled = _standingPortrait != null;
                return;
            }
            target.enabled = source != null && source.texture != null;
            if (!target.enabled) return;
            target.texture = source.texture;
            target.raycastTarget = false;
            target.uvRect = source.uvRect;
            if (stage == null || !stage.TryGetBodyViewport(out var body)) return;
            float textureAspect = (float)source.texture.width / source.texture.height;
            float boxAspect = target.rectTransform.rect.width / Mathf.Max(1f, target.rectTransform.rect.height);
            float height = Mathf.Min(1f, body.height / 0.94f);
            float width = Mathf.Min(1f, height * boxAspect / textureAspect);
            height = Mathf.Min(height, width * textureAspect / boxAspect);
            target.uvRect = new Rect(
                Mathf.Clamp(body.center.x - width * 0.5f, 0f, 1f - width),
                Mathf.Clamp(body.yMin - (height - body.height) * 0.1f, 0f, 1f - height), width, height);
        }

        private void HideSourcePortraits()
        {
            if (_sourcesHidden) return;
            _sourcesHidden = true;
            if (_playerAvatarSource != null)
            {
                _playerSourceWasEnabled = _playerAvatarSource.enabled;
                _playerAvatarSource.enabled = false;
            }
            if (_opponentAvatarSource != null)
            {
                _opponentSourceWasEnabled = _opponentAvatarSource.enabled;
                _opponentAvatarSource.enabled = false;
            }
        }

        private void RestoreSourcePortraits()
        {
            if (!_sourcesHidden) return;
            _sourcesHidden = false;
            if (_playerAvatarSource != null) _playerAvatarSource.enabled = _playerSourceWasEnabled;
            if (_opponentAvatarSource != null) _opponentAvatarSource.enabled = _opponentSourceWasEnabled;
        }

        // ── Level test ───────────────────────────────────────────────────────────────────────────

        /// <summary>The onboarding measurement. A zero-rep result is not a level: nothing was
        /// measured, so instead of stamping the player as a beginner it offers the test again, with
        /// a way past it for anyone whose camera simply will not cooperate.</summary>
        public void ShowLevelTest(int reps, FitnessTier tier, long xp, bool recorded)
        {
            Open(duel: false);
            GameAudio.Play(reps > 0 ? SoundCue.RewardComplete : SoundCue.Back);

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

        public void ShowTraining(int reps, int sets, long xp, bool recorded)
        {
            Open(duel: false);
            GameAudio.Play(reps > 0 ? SoundCue.Victory : SoundCue.Back);
            SetText(_testTitle, "ТРЕНИРОВКА ЗАВЕРШЕНА", NeutralColor);
            SetText(_testTier, $"{reps} ПОВТОРОВ", DrawColor);
            SetText(_testScore, $"Подходов: {sets}", NeutralColor);
            SetText(_testRewards, xp > 0 ? $"+{xp} XP" : "", WinColor);
            SetText(_testNote, recorded ? "Новый лучший подход сохранён." : "Тренировка завершена. Хорошего отдыха!", NeutralColor);
            SetPrimary("ПРОДОЛЖИТЬ", Continue);
            HideSecondary();
        }

        // ── Actions ──────────────────────────────────────────────────────────────────────────────

        private void Continue()
        {
            if (ContinueRequested != null) { ContinueRequested(); return; }
            if (PreviewContinuation != null) { PreviewContinuation(); return; }
            if (_sceneAuthored) { ReturnHome(); return; }
            if (!_hasSummary) { ReturnHome(); return; }
            _hasSummary = false;
            Hide();
            foreach (var avatar in FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
                avatar.SetPreparationPresentation(true);
            var flow = GetComponent<FightRewardFlow>();
            if (flow == null) flow = gameObject.AddComponent<FightRewardFlow>();
            flow.Show(_summary, ReturnHome);
        }

        private static void ReturnHome() => FightScreenNavigation.ReturnTo(FightScreenNavigation.ReturnScene);

        private void Retry()
        {
            if (FightScreenNavigation.IsPreview)
            {
                FightScreenNavigation.Preview(FightScreen.Battle);
                return;
            }
            FightRequest.LevelTest(FightScreenNavigation.ReturnScene);
            FightScreenNavigation.Navigate(FightScreen.Battle);
        }

        /// <summary>Accepts a zero so the player is not stuck in the test forever. They keep no
        /// ghost and no tier; the main screen sends them back into a level test when they next
        /// look for an opponent.</summary>
        private void SkipLevelTest()
        {
            if (!FightScreenNavigation.IsPreview) OnboardingState.CompleteLevelTest(0);
            ReturnHome();
        }

        // ── Plumbing ─────────────────────────────────────────────────────────────────────────────

        private void Open(bool duel)
        {
            if (_root != null) _root.SetActive(true);
            if (_duelLayout != null) _duelLayout.SetActive(duel);
            if (_levelTestLayout != null) _levelTestLayout.SetActive(!duel);
            HideSourcePortraits();
            if (_sceneAuthored) return;
            ApplyLayoutDefaults();
            if (_editableLayout == null)
            {
                _editableLayout = _root.AddComponent<ScreenLayoutRoot>();
                _editableLayout.Configure("results", false);
                RegisterResultChildren(_duelLayout, "duel");
                RegisterResultChildren(_levelTestLayout, "solo");
                if (_continueButton != null) _editableLayout.Register("continue", (RectTransform)_continueButton.transform);
                if (_secondaryButton != null) _editableLayout.Register("secondary", (RectTransform)_secondaryButton.transform);
            }
            _editableLayout.ApplySavedLayout();
        }

        private void ApplyLayoutDefaults()
        {
            if (_layoutDefaultsApplied || _sceneAuthored) return;
            _layoutDefaultsApplied = true;

            // Apply before the edit root captures its originals. User/authored layouts win afterward.
            Place(_opponentAvatarImage != null ? _opponentAvatarImage.rectTransform : null,
                new Vector2(1f, 1f), new Vector2(-18f, -83f), new Vector2(186f, 268f));
            Place(_playerAvatarImage != null ? _playerAvatarImage.rectTransform : null,
                new Vector2(0f, 0f), new Vector2(24f, 112f), new Vector2(178f, 240f));

            var playerNamePlate = _playerName != null ? _playerName.transform.parent as RectTransform : null;
            if (playerNamePlate != null && playerNamePlate.GetComponent<Image>() != null)
                playerNamePlate.anchoredPosition = new Vector2(-18f, 315f);
            Place(_playerReps != null ? _playerReps.rectTransform : null,
                new Vector2(1f, 0f), new Vector2(-18f, 221f), new Vector2(170f, 82f));
            PlacePlayerStat(_playerForm, 194f, 156f);
            PlacePlayerStat(_playerTempo, 132f, 96f);

            if (_duelNote != null)
            {
                var rect = _duelNote.rectTransform;
                rect.anchorMin = new Vector2(0f, 0f); rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 5f);
                rect.sizeDelta = new Vector2(-24f, 18f);
                _duelNote.fontSize = 11f;
                _duelNote.enableAutoSizing = true;
                _duelNote.fontSizeMin = 9f; _duelNote.fontSizeMax = 11f;
            }

            if (_continueButton != null)
            {
                Place((RectTransform)_continueButton.transform, new Vector2(0.5f, 0f),
                    new Vector2(0f, 45f), new Vector2(230f, 58f));
                var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
                var plate = _continueButton.GetComponent<Image>();
                if (plate == null) plate = _continueButton.gameObject.AddComponent<Image>();
                plate.sprite = theme != null ? theme.BtnShape : null;
                plate.type = Image.Type.Simple;
                plate.color = plate.sprite != null ? Color.white : DrawColor;
                _continueButton.targetGraphic = plate;
            }
            if (_continueLabel != null)
            {
                _continueLabel.color = Color.white;
                FightTypography.Apply(_continueLabel, FightTypography.Role.Button);
                _continueLabel.fontSize = 20f;
                _continueLabel.enableAutoSizing = true;
                _continueLabel.fontSizeMin = 13f; _continueLabel.fontSizeMax = 20f;
                _continueLabel.alignment = TextAlignmentOptions.Center;
                _continueLabel.raycastTarget = false;
                var rect = _continueLabel.rectTransform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(17f, 4f); rect.offsetMax = new Vector2(-17f, 0f);
            }
        }

        private static void PlacePlayerStat(TextMeshProUGUI value, float captionY, float valueY)
        {
            if (value == null) return;
            var caption = value.transform.parent.Find(value.name + "Caption") as RectTransform;
            Place(caption, new Vector2(1f, 0f), new Vector2(-18f, captionY), new Vector2(170f, 14f));
            Place(value.rectTransform, new Vector2(1f, 0f), new Vector2(-18f, valueY), new Vector2(170f, 34f));
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
        {
            if (rect == null) return;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private void RegisterResultChildren(GameObject parent, string prefix)
        {
            if (parent == null) return;
            foreach (Transform child in parent.transform)
            {
                if (!(child is RectTransform rect)) continue;
                if (child.GetComponent<Graphic>() != null || child.GetComponent<Button>() != null)
                    _editableLayout.Register(prefix + "/" + child.name, rect);
                else RegisterResultChildren(child.gameObject, prefix + "/" + child.name);
            }
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
