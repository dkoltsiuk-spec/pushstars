using System;
using System.Collections;
using System.Collections.Generic;
using PushStars.Core;
using PushStars.UI;
using PushStars.UI.Layout;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Fills and animates one authored scene. UI and button routes are serialized;
    /// runtime code never creates the hierarchy or restores authored group positions.</summary>
    [DefaultExecutionOrder(350)]
    public sealed class RewardScreen : MonoBehaviour
    {
        [Serializable] public sealed class SummaryElements
        {
            public TextMeshProUGUI PlayerName, TotalReps, Technique, Trophies, EnergyXp, Aura;
            public RectTransform TrophyContent, XpContent, AuraContent;
            public GameObject AuraGroup;
            public RawImage Portrait;
            public FightAvatar Avatar;
            public Texture2D MalePortrait, FemalePortrait;
            public Button CaseAwardButton;
            public TextMeshProUGUI Title, Subtitle, ContinueLabel;
            public GameObject TrophyGroup;
            public Image ContinueIcon;
            [Header("Staged entrance: header → hero → reps → cards → level → button")]
            public RectTransform Header, Stage, RepsPanel, Cards, LevelGroup, Continue;
            public RectTransform Rays, RecordBadge, LevelBadge;
            public RewardStarGraphic[] TechniqueStars = new RewardStarGraphic[0];
            public RectTransform LevelFill;
            public TextMeshProUGUI Level, LevelProgress, LevelUp;
            [Header("One screen after a fight: outcome tone, opponent, score, streak")]
            public RewardToneBackdrop Backdrop;
            public FightAvatar Opponent;
            public RawImage OpponentPortrait;
            public GameObject OpponentShadow;
            public TextMeshProUGUI OpponentReps, ScoreSeparator, OpponentName;
            public GameObject StreakBonusGroup;
            public TextMeshProUGUI StreakDays, StreakBonusTrophies;
        }
        [Serializable] public sealed class CaseElements
        {
            public TextMeshProUGUI Rarity, Source, Status, Hint, ActionLabel;
            public Image Artwork, Glow;
            public RectTransform Content, StarsContent;
            public RectTransform Rays;
            public CanvasGroup TapHint;
            public Button TapButton, ActionButton;
            public RewardStarGraphic[] Stars = new RewardStarGraphic[0];
            public GameObject[] RarityStarGroups = new GameObject[0];
            public Image[] TapPips = new Image[0];
            public Sprite[] RarityArtwork = new Sprite[4];
        }
        [Serializable] public sealed class PrizeElements
        {
            public TextMeshProUGUI Amount, Rarity, Note, ClaimLabel;
            public RectTransform Content, Rays;
            public Image Glow;
            public Image AvatarCardIcon;
            public Button ClaimButton;
        }

        [Header("Scene and destinations")]
        [SerializeField] private FightScreen _screen = FightScreen.RewardSummary;
        [SerializeField] private FightScreen _homeDestination = FightScreen.Home;
        [SerializeField] private FightScreen _awardDestination = FightScreen.CaseAward;
        [SerializeField] private FightScreen _openDestination = FightScreen.CaseOpening;
        [SerializeField] private FightScreen _prizeDestination = FightScreen.CaseReward;
        [Header("Serialized scene elements")]
        [SerializeField] private SummaryElements _summaryUi = new SummaryElements();
        [SerializeField] private CaseElements _caseUi = new CaseElements();
        [SerializeField] private PrizeElements _prizeUi = new PrizeElements();
        [SerializeField] private CaseOpeningMotion _caseMotion;
        [Header("Sample data — direct scene launch never grants rewards")]
        [SerializeField] private FightRewardFlow.Summary _sampleSummary = new FightRewardFlow.Summary
        {
            PlayerName = "BEASTCORE_DEV", TotalReps = 57, Technique = 0.92f,
            Trophies = 21, EnergyXp = 570, Aura = FightScreenNavigation.SampleAura, HasCase = true, NewRecord = true,
            StreakDays = 2, StreakBonusTrophies = 1,
            AuraMoments = FightScreenNavigation.SampleAuraMoments
        };

        private struct Pose { public Vector3 Scale; public Quaternion Rotation; }
        private readonly Dictionary<RectTransform, Pose> _poses = new Dictionary<RectTransform, Pose>();
        private static readonly string[] CaseNames = { "COMMON", "RARE", "EPIC", "LEGENDARY" };
        private static readonly Color Gold = new Color32(255, 214, 17, 255);
        private string _caseId;
        private CaseRarity _rarity;
        private int _tapsUsed, _gems;
        private FightRewardFlow.Summary _summary;
        private bool _opened, _available, _busy;
        private Color _glowColor;
        private bool _summaryPortraitFramed;
        private Vector2 _summaryPortraitSize;
        private bool _summaryEntering, _summarySkip;
        private float _summaryRaysAngle;
        private static readonly Color StarOff = new Color(1, 1, 1, .16f);
        private float _caseInactiveSeconds, _caseIdleSeconds, _caseRaysAngle, _caseIdleSince;
        private const float CaseHintDelay = 5f;

        public FightScreen Screen => _screen;
        public SummaryElements SummaryUi => _summaryUi;
        public CaseElements CaseUi => _caseUi;
        public PrizeElements PrizeUi => _prizeUi;
        public void Configure(FightScreen screen) => _screen = screen;

        private void Start()
        {
            if (_caseMotion != null) _caseMotion.Initialize();
            Capture(_summaryUi.TrophyContent); Capture(_summaryUi.XpContent); Capture(_summaryUi.AuraContent);
            foreach (var step in SummarySteps) Capture(step);
            Capture(_summaryUi.Rays); Capture(_summaryUi.RecordBadge); Capture(_summaryUi.LevelBadge);
            foreach (var star in _summaryUi.TechniqueStars) if (star != null) Capture(star.rectTransform);
            Capture(_caseUi.Content); Capture(_caseUi.StarsContent); Capture(_prizeUi.Content);
            Capture(_caseUi.Rays);
            Capture(_prizeUi.Rays);
            if (_caseUi.TapHint != null)
            {
                Capture((RectTransform)_caseUi.TapHint.transform);
                _caseUi.TapHint.alpha = 0;
            }
            if (_caseUi.Glow != null) Capture(_caseUi.Glow.rectTransform);
            if (_prizeUi.Glow != null) Capture(_prizeUi.Glow.rectTransform);
            switch (_screen)
            {
                case FightScreen.RewardSummary: FillSummary(); break;
                case FightScreen.CaseAward:
                case FightScreen.CaseOpening: FillCase(); break;
                case FightScreen.CaseReward: FillPrize(); break;
            }
        }

        private void OnDisable()
        {
            StopAllCoroutines(); _busy = false;
            if (_caseMotion != null) _caseMotion.ResetPose();
            ResetCaseAttention();
            foreach (var item in _poses)
                if (item.Key != null)
                {
                    item.Key.localScale = item.Value.Scale;
                    item.Key.localRotation = item.Value.Rotation;
                }
        }

        private void Update()
        {
            if (ScreenLayoutRoot.IsAnyEditing)
            {
                if (_screen == FightScreen.CaseOpening) ResetCaseAttention();
                if (_caseMotion != null) _caseMotion.ResetPose();
                return;
            }
            float pulse = 1f + 0.045f * Mathf.Sin(Time.unscaledTime * 1.8f);
            if (_caseUi.Glow != null) Scale(_caseUi.Glow.rectTransform, pulse);
            if (_prizeUi.Glow != null) Scale(_prizeUi.Glow.rectTransform, pulse);
            if (_screen == FightScreen.CaseOpening) UpdateCaseAttention(Time.unscaledDeltaTime);
            if (_screen == FightScreen.RewardSummary)
            {
                _summaryRaysAngle = Mathf.Repeat(_summaryRaysAngle + Time.unscaledDeltaTime * 6f, 360f);
                Rotate(_summaryUi.Rays, _summaryRaysAngle);
                KeepCelebrating();
                // A tap during the entrance skips straight to the settled screen.
                if (_summaryEntering && (Input.GetMouseButtonDown(0) ||
                    (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)))
                    _summarySkip = true;
            }
            if (_screen == FightScreen.CaseReward)
            {
                _caseRaysAngle = Mathf.Repeat(_caseRaysAngle + Time.unscaledDeltaTime * 3, 360);
                Rotate(_prizeUi.Rays, _caseRaysAngle);
            }
        }

        private void UpdateCaseAttention(float deltaTime)
        {
            _caseRaysAngle = Mathf.Repeat(_caseRaysAngle + deltaTime * 3f, 360f);
            Rotate(_caseUi.Rays, _caseRaysAngle);
            Scale(_caseUi.Rays, 1f + .025f * Mathf.Sin(Time.unscaledTime * .7f));

            // Also dismiss the help cue when the player touches outside the chest.
            bool interacting = Input.GetMouseButton(0) || Input.touchCount > 0 || Input.anyKeyDown;
            if (_busy || !_available || interacting)
            {
                ResetCaseAttention();
                return;
            }
            // Use an absolute timestamp so a long frame cannot count time from before a tap.
            _caseInactiveSeconds = _caseIdleSeconds = Time.realtimeSinceStartup - _caseIdleSince;

            // A short, restrained rattle followed by a pause. Only the artwork moves;
            // the hit target and rarity row stay still. Tap/reveal coroutines own it while busy.
            float phase = Mathf.Repeat(_caseIdleSeconds, 3f);
            float envelope = phase < .7f ? Mathf.Sin(phase / .7f * Mathf.PI) : 0f;
            if (_caseMotion != null) _caseMotion.Idle(_caseIdleSeconds);
            else
            {
                Rotate(_caseUi.Content, Mathf.Sin(phase / .7f * Mathf.PI * 6f) * envelope * 1.8f);
                Scale(_caseUi.Content, 1f + .012f * envelope);
            }
            if (_caseUi.TapHint != null)
            {
                float target = _caseInactiveSeconds >= CaseHintDelay ? 1f : 0f;
                _caseUi.TapHint.alpha = Mathf.MoveTowards(_caseUi.TapHint.alpha, target, deltaTime * 5f);
                Scale((RectTransform)_caseUi.TapHint.transform,
                    1f - .045f * (.5f + .5f * Mathf.Sin((_caseInactiveSeconds - CaseHintDelay) * 5f)));
            }
        }

        private void ResetCaseAttention()
        {
            _caseInactiveSeconds = _caseIdleSeconds = 0;
            _caseIdleSince = Time.realtimeSinceStartup;
            if (_caseUi.TapHint != null)
            {
                _caseUi.TapHint.alpha = 0;
                Scale((RectTransform)_caseUi.TapHint.transform, 1);
            }
            if (!_busy)
            {
                if (_caseMotion != null) _caseMotion.ResetPose();
                Scale(_caseUi.Content, 1);
                Rotate(_caseUi.Content, 0);
            }
        }

        private void FillSummary()
        {
            var data = FightScreenNavigation.IsPreview && !FightScreenNavigation.PreviewAssessment ? _sampleSummary : FightScreenNavigation.RewardSummary;
            _summary = data;
            HomeRewardFlight.QueueSummary(data);
            Set(_summaryUi.PlayerName, string.IsNullOrWhiteSpace(data.PlayerName) ? "YOUR RESULT" : data.PlayerName);
            Set(_summaryUi.TotalReps, Mathf.Max(0, data.TotalReps).ToString());
            Set(_summaryUi.Technique, $"{Mathf.Clamp01(data.Technique) * 100f:0}%");
            // Aura is never counted here: it gets its own screen right after this one.
            if (_summaryUi.AuraGroup != null) _summaryUi.AuraGroup.SetActive(false);
            if (_summaryUi.Trophies != null && data.Trophies < 0)
                _summaryUi.Trophies.color = new Color32(255, 107, 95, 255);
            bool hasAward = FightScreenNavigation.IsPreview || !string.IsNullOrEmpty(FightScreenNavigation.AwardedCaseId);
            bool assessment = FightScreenNavigation.Result?.Mode == FightMode.LevelTest;
            var result = FightScreenNavigation.Result;
            if (result != null && !assessment)
                Set(_summaryUi.Title, result.Mode == FightMode.Training ? "WORKOUT DONE!"
                    : result.Draw ? "DRAW!" : result.Win ? "YOU WIN!" : "YOU LOSE!");
            if (assessment)
            {
                Set(_summaryUi.Title, "ASSESSMENT COMPLETE!");
                Set(_summaryUi.Subtitle, "Great result. Strong start!");
            }
            // A card for nothing is noise: training and the assessment usually move no trophies.
            if (_summaryUi.TrophyGroup != null) _summaryUi.TrophyGroup.SetActive(data.Trophies != 0);
            if (_summaryUi.StreakBonusGroup != null) _summaryUi.StreakBonusGroup.SetActive(data.StreakBonusTrophies > 0);
            Set(_summaryUi.StreakDays, data.StreakDays.ToString());
            Set(_summaryUi.StreakBonusTrophies, Signed(data.StreakBonusTrophies));
            FillOutcome(result, data);
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            Set(_summaryUi.ContinueLabel, assessment ? data.Aura != 0 ? "CONTINUE" : hasAward ? "OPEN CASE" : "HOME" : "HOME");
            if (_summaryUi.ContinueIcon != null)
                _summaryUi.ContinueIcon.sprite = !assessment ? theme?.IconHouse : data.Aura != 0 ? theme?.IconAura
                    : hasAward ? Resources.Load<Sprite>("Rewards/CaseCommon") : theme?.IconHouse;
            if (_summaryUi.CaseAwardButton != null) _summaryUi.CaseAwardButton.gameObject.SetActive(hasAward);
            // Keep the baked image for Edit Mode/fallback; this scene owns a live idle stage.
            if (_summaryUi.Portrait != null)
            {
                var portrait = CharacterRoster.SavedGender == CharacterGender.Female ?
                    _summaryUi.FemalePortrait : _summaryUi.MalePortrait;
                if (portrait != null) _summaryUi.Portrait.texture = portrait;
            }
            if (_summaryUi.Avatar != null)
            {
                _summaryUi.Avatar.SetPreparationPresentation(true);
                var animator = _summaryUi.Avatar.Character != null ?
                    _summaryUi.Avatar.Character.GetComponentInChildren<Animator>() : null;
                if (animator != null) animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                if (_summaryUi.Avatar.StageCamera != null) _summaryUi.Avatar.StageCamera.enabled = true;
                _summaryPortraitFramed = false;
            }
            StartCoroutine(EnterSummary(data, data.NewRecord && !assessment));
        }

        private enum Outcome { Solo, Win, Loss, Draw }
        private Outcome _outcome;
        private float _celebrateAt;

        /// <summary>This screen replaces the old duel card too: the tone says who won (our blue,
        /// their red), the score is the big number, and the opponent stands behind the hero as a
        /// muted figure, like Brawl Stars' teammates.</summary>
        private void FillOutcome(FightResultData result, FightRewardFlow.Summary data)
        {
            var ui = _summaryUi;
            bool duel = result != null && (result.Mode == FightMode.Ghost || result.Mode == FightMode.Boss);
            _outcome = !duel ? Outcome.Solo : result.Draw ? Outcome.Draw : result.Win ? Outcome.Win : Outcome.Loss;
            if (duel)
            {
                Set(ui.TotalReps, Mathf.Max(0, result.MyReps).ToString());
                Set(ui.OpponentReps, Mathf.Max(0, result.OppReps).ToString());
                Set(ui.OpponentName, "VS " + (string.IsNullOrWhiteSpace(result.OpponentName) ? "GHOST" : result.OpponentName));
            }
            else Set(ui.OpponentName, "REPS");
            if (ui.OpponentReps != null) ui.OpponentReps.gameObject.SetActive(duel);
            if (ui.ScoreSeparator != null) ui.ScoreSeparator.gameObject.SetActive(duel);
            if (ui.OpponentPortrait != null) ui.OpponentPortrait.gameObject.SetActive(duel);
            if (ui.OpponentShadow != null) ui.OpponentShadow.SetActive(duel);
            if (ui.Opponent != null)
            {
                ui.Opponent.gameObject.SetActive(duel);
                if (duel && ui.Opponent.StageCamera != null) ui.Opponent.StageCamera.enabled = true;
            }
            if (ui.Backdrop != null) Tint(ui.Backdrop, _outcome);
        }

        private static void Tint(RewardToneBackdrop backdrop, Outcome outcome)
        {
            if (outcome == Outcome.Loss)
            {
                backdrop.Top = new Color32(112, 24, 58, 255); backdrop.Middle = new Color32(196, 52, 82, 255);
                backdrop.Bottom = new Color32(104, 22, 56, 255); backdrop.Light = new Color32(255, 186, 196, 120);
                backdrop.Vignette = new Color32(52, 6, 26, 140);
            }
            else if (outcome == Outcome.Draw)
            {
                backdrop.Top = new Color32(52, 38, 150, 255); backdrop.Middle = new Color32(104, 84, 232, 255);
                backdrop.Bottom = new Color32(48, 34, 142, 255); backdrop.Light = new Color32(210, 196, 255, 120);
                backdrop.Vignette = new Color32(20, 10, 70, 140);
            }
            backdrop.SetVerticesDirty();
        }

        /// <summary>Brawl Stars keeps the win pose going for as long as the screen is open: after
        /// each celebration and a short breath in StandIdle the hero celebrates again.</summary>
        private void KeepCelebrating()
        {
            if (_outcome == Outcome.Loss || _outcome == Outcome.Draw || _summaryUi.Avatar == null) return;
            var body = _summaryUi.Avatar.Character;
            var animator = body != null ? body.GetComponentInChildren<Animator>() : null;
            if (animator == null || _celebrateAt <= 0 || animator.IsInTransition(0)) return;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            int victory = Animator.StringToHash("Victory"), idle = Animator.StringToHash("StandIdle");
            if (state.IsName("Victory"))
            {
                // A replayed celebration has no one to send it home: breathe in StandIdle after it.
                if (state.normalizedTime >= .98f && animator.HasState(0, idle)) animator.CrossFadeInFixedTime(idle, .25f, 0);
                _celebrateAt = Time.unscaledTime + 1.4f;
                return;
            }
            if (!state.IsName("StandIdle") || Time.unscaledTime < _celebrateAt) return;
            if (animator.HasState(0, victory)) animator.CrossFadeInFixedTime(victory, .2f, 0, 0f);
            _celebrateAt = Time.unscaledTime + 1.4f;
        }

        private void PresentAvatars()
        {
            var ui = _summaryUi;
            if (ui.Avatar != null)
            {
                if (_outcome == Outcome.Loss) ui.Avatar.SetResultPresentation(true);
                else if (_outcome != Outcome.Draw) { ui.Avatar.SetResultPresentation(false, true); _celebrateAt = Time.unscaledTime + 1.4f; }
            }
            if (ui.Opponent != null && ui.Opponent.gameObject.activeInHierarchy)
            {
                ui.Opponent.SetPreparationPresentation(true);
                var animator = ui.Opponent.Character != null ? ui.Opponent.Character.GetComponentInChildren<Animator>() : null;
                if (animator != null) animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                if (_outcome == Outcome.Win) ui.Opponent.SetResultPresentation(true);
                else if (_outcome == Outcome.Loss) ui.Opponent.SetResultPresentation(false, true);
            }
        }

        private RectTransform[] SummarySteps => new[]
        {
            _summaryUi.Header, _summaryUi.Stage, _summaryUi.RepsPanel,
            _summaryUi.Cards, _summaryUi.LevelGroup, _summaryUi.Continue
        };

        /// <summary>One beat at a time, like a Brawl Stars result: the title lands, the hero
        /// celebrates in front of the burst, the set counts up, rewards pop and count, the level
        /// bar fills, then the button arrives. A tap skips to the settled screen.</summary>
        private IEnumerator EnterSummary(FightRewardFlow.Summary data, bool record)
        {
            var ui = _summaryUi;
            foreach (var step in SummarySteps) Show(step, 0);
            if (ui.RecordBadge != null) ui.RecordBadge.gameObject.SetActive(false);
            Set(ui.TotalReps, "0"); Set(ui.Technique, "0%");
            Set(ui.Trophies, Signed(0)); Set(ui.EnergyXp, Signed(0) + " XP");
            Set(ui.StreakBonusTrophies, Signed(0));
            foreach (var star in ui.TechniqueStars) if (star != null) star.color = StarOff;
            Set(ui.OpponentReps, "0");
            LevelSpan(data, out long before, out long after);
            ShowLevel(before, false);
            _summaryEntering = true; _summarySkip = false;
            yield return ScreenTransition.Settle();

            StartCoroutine(Reveal(ui.Header, .4f, .7f));
            yield return Beat(.12f);
            GameAudio.Play(_outcome == Outcome.Loss ? SoundCue.Back : SoundCue.RewardBurst);
            StartCoroutine(Reveal(ui.Stage, .45f, .88f));
            PresentAvatars();
            yield return Beat(.28f);
            StartCoroutine(Reveal(ui.RepsPanel, .38f, .7f));
            yield return Beat(.12f);
            yield return CountSet(data);
            if (record && ui.RecordBadge != null)
            {
                ui.RecordBadge.gameObject.SetActive(true);
                GameAudio.Play(SoundCue.CaseUpgradeComplete);
                yield return Pop(ui.RecordBadge, .3f, 1.9f);
            }
            // Trophies (under the title) and XP (under the score) arrive and count together.
            StartCoroutine(Reveal(ui.Cards, .38f, .7f));
            StartCoroutine(Reveal(ui.LevelGroup, .38f, .85f));
            yield return Beat(.1f);
            yield return CountSummary(data);
            StartCoroutine(Reveal(ui.Continue, .4f, .7f, .2f));
            yield return FillLevel(before, after);
            _summaryEntering = false;
        }

        /// <summary>Reps count up with ticks; technique follows and earns its stars one by one.</summary>
        private IEnumerator CountSet(FightRewardFlow.Summary data)
        {
            var ui = _summaryUi;
            var result = FightScreenNavigation.Result;
            bool duel = _outcome != Outcome.Solo && result != null;
            int reps = Mathf.Max(0, duel ? result.MyReps : data.TotalReps);
            int theirs = duel ? Mathf.Max(0, result.OppReps) : 0;
            float form = Mathf.Clamp01(data.Technique);
            int earned = form >= .9f ? 3 : form >= .75f ? 2 : form >= .5f ? 1 : 0, lit = 0;
            const float duration = .75f;
            float elapsed = 0, nextTick = 0;
            while (elapsed < duration)
            {
                elapsed += Step();
                float t = Mathf.Clamp01(elapsed / duration), eased = 1f - Mathf.Pow(1f - t, 3f);
                if (reps > 0 && elapsed >= nextTick && elapsed < duration)
                {
                    GameAudio.Play(SoundCue.RewardTick, Mathf.Lerp(.85f, 1.25f, t));
                    nextTick = elapsed + .07f;
                }
                Set(ui.TotalReps, Mathf.RoundToInt(reps * eased).ToString());
                Set(ui.OpponentReps, Mathf.RoundToInt(theirs * eased).ToString());
                Set(ui.Technique, $"{form * 100f * eased:0}%");
                while (lit < Mathf.Min(earned, Mathf.FloorToInt(t * 3.2f))) LightStar(lit++);
                yield return null;
            }
            while (lit < earned) LightStar(lit++);
            Set(ui.TotalReps, reps.ToString());
            Set(ui.OpponentReps, theirs.ToString());
            Set(ui.Technique, $"{form * 100f:0}%");
        }

        private void LightStar(int index)
        {
            var stars = _summaryUi.TechniqueStars;
            if (index >= stars.Length || stars[index] == null) return;
            stars[index].color = Gold;
            GameAudio.Play(SoundCue.CaseUpgrade, 1.1f + index * .12f);
            StartCoroutine(Pop(stars[index].rectTransform, .28f, 1.6f));
        }

        /// <summary>Total XP before and after this set, for the level bar.</summary>
        private static void LevelSpan(FightRewardFlow.Summary data, out long before, out long after)
        {
            long gained = Math.Max(0, data.EnergyXp);
            if (FightScreenNavigation.IsPreview)
            {
                // Sample: part way into level 4, so the preview shows the bar crossing a level.
                before = LevelCalculator.TotalXpForLevel(4) + (long)(LevelCalculator.XpForLevelUp(4) * .7f);
                after = before + gained;
                return;
            }
            // The ledger has already credited this set by the time the summary opens.
            after = Math.Max(0, LocalProfile.Xp);
            before = Math.Max(0, after - gained);
        }

        private void ShowLevel(long xp, bool levelledUp)
        {
            var ui = _summaryUi;
            int level = LevelCalculator.LevelFromXp(xp);
            Set(ui.Level, level.ToString());
            if (ui.LevelFill != null)
            {
                var max = ui.LevelFill.anchorMax;
                max.x = Mathf.Clamp01(LevelCalculator.LevelProgress(xp));
                ui.LevelFill.anchorMax = max;
            }
            if (ui.LevelProgress != null) ui.LevelProgress.color = Color.white;
            Set(ui.LevelUp, levelledUp ? "LEVEL UP!" : "");
            long floor = LevelCalculator.TotalXpForLevel(level);
            Set(ui.LevelProgress, level >= EconomyConfig.MaxLevel ? "MAX LEVEL"
                : $"{xp - floor} / {LevelCalculator.XpForLevelUp(level)}");
        }

        private IEnumerator FillLevel(long before, long after)
        {
            int startLevel = LevelCalculator.LevelFromXp(before), shown = startLevel;
            const float duration = .9f;
            for (float elapsed = 0; elapsed < duration && after > before;)
            {
                elapsed += Step();
                float t = Mathf.Clamp01(elapsed / duration), eased = 1f - Mathf.Pow(1f - t, 2.4f);
                long xp = before + (long)Math.Round((after - before) * (double)eased);
                ShowLevel(xp, false);
                int level = LevelCalculator.LevelFromXp(xp);
                if (level != shown)
                {
                    shown = level;
                    GameAudio.Play(SoundCue.RewardComplete, 1.15f);
                    StartCoroutine(Pop(_summaryUi.LevelBadge, .35f, 1.5f));
                }
                yield return null;
            }
            ShowLevel(after, LevelCalculator.LevelFromXp(after) > startLevel);
        }

        private float Step() => _summarySkip ? 100f : Time.unscaledDeltaTime;
        private IEnumerator Beat(float seconds)
        {
            for (float elapsed = 0; elapsed < seconds; elapsed += Step()) yield return null;
        }

        /// <summary>Fade in with a soft overshoot (half a back-ease; big elastic pops read jerky).</summary>
        private IEnumerator Reveal(RectTransform step, float duration, float from, float delay = 0)
        {
            if (step == null) yield break;
            if (delay > 0) yield return Beat(delay);
            for (float elapsed = 0; elapsed < duration;)
            {
                elapsed += Step();
                float t = Mathf.Clamp01(elapsed / duration);
                Show(step, Mathf.Clamp01(t * 2.2f));
                Scale(step, Mathf.LerpUnclamped(from, 1, UITween.EaseOutBackSoft(t)));
                yield return null;
            }
            Show(step, 1); Scale(step, 1);
        }

        /// <summary>A stamp-like punch: arrives oversized and settles.</summary>
        private IEnumerator Pop(RectTransform target, float duration, float from)
        {
            if (target == null) yield break;
            for (float elapsed = 0; elapsed < duration;)
            {
                elapsed += Step();
                float t = Mathf.Clamp01(elapsed / duration);
                Scale(target, Mathf.LerpUnclamped(from, 1, UITween.EaseOutBackSoft(t)));
                yield return null;
            }
            Scale(target, 1);
        }

        private static void Show(RectTransform step, float alpha)
        {
            if (step == null) return;
            var group = step.GetComponent<CanvasGroup>();
            if (group == null) return;
            group.alpha = alpha;
            group.interactable = group.blocksRaycasts = alpha >= 1;
        }

        private void LateUpdate()
        {
            if (_screen != FightScreen.RewardSummary) return;
            Frame(_summaryUi.Avatar, _summaryUi.Portrait, ref _summaryPortraitFramed, ref _summaryPortraitSize);
            if (_summaryUi.OpponentPortrait != null && _summaryUi.OpponentPortrait.gameObject.activeInHierarchy)
            {
                Frame(_summaryUi.Opponent, _summaryUi.OpponentPortrait, ref _opponentPortraitFramed, ref _opponentPortraitSize);
                _summaryUi.OpponentPortrait.color = _outcome == Outcome.Loss ? new Color(.62f, .36f, .5f, .9f) : OpponentTint;
            }
        }

        private bool _opponentPortraitFramed;
        private Vector2 _opponentPortraitSize;
        /// <summary>The opponent stands behind the hero dimmed into the tone, like Brawl Stars'
        /// teammates. Re-applied every frame: CharacterStage resets its image to white.</summary>
        public static readonly Color OpponentTint = new Color(.3f, .38f, .7f, .9f);

        private static void Frame(FightAvatar avatar, RawImage portrait, ref bool framed, ref Vector2 framedSize)
        {
            if (avatar == null || portrait == null || avatar.StageCamera == null) return;
            AvatarWideImage.Configure(portrait, avatar.StageCamera);
            var texture = avatar.StageCamera.targetTexture;
            if (texture == null) return;
            portrait.texture = texture;
            var size = portrait.rectTransform.rect.size;
            // Once framed, let the idle move inside a fixed crop instead of chasing each breath.
            if (framed && size == framedSize) return;
            if (!avatar.IsPreparationFramed || !avatar.TryGetBodyViewport(out var body)) return;
            float textureAspect = AvatarWideCamera.TextureAspect(avatar.StageCamera, texture);
            float boxAspect = size.x / Mathf.Max(1, size.y);
            portrait.uvRect = AvatarFraming.FitPortrait(body, boxAspect, textureAspect,
                body.height / .94f, .1f);
            framed = true;
            framedSize = size;
        }

        private IEnumerator CountSummary(FightRewardFlow.Summary data)
        {
            float elapsed = 0, nextTick = 0;
            bool hasReward = data.Trophies > 0 || data.EnergyXp > 0;
            const float duration = .85f;
            while (elapsed < duration)
            {
                elapsed += Step();
                if (hasReward && elapsed >= nextTick && elapsed < duration)
                {
                    GameAudio.Play(SoundCue.RewardTick, Mathf.Lerp(.9f, 1.3f, elapsed / duration));
                    nextTick = elapsed + .075f;
                }
                float t = Mathf.Clamp01(elapsed / duration), eased = 1f - Mathf.Pow(1f - t, 3f);
                Set(_summaryUi.Trophies, Signed((long)Math.Round((data.Trophies - data.StreakBonusTrophies) * eased)));
                Set(_summaryUi.StreakBonusTrophies, Signed((long)Math.Round(data.StreakBonusTrophies * eased)));
                Set(_summaryUi.EnergyXp, Signed((long)Math.Round(data.EnergyXp * eased)) + " XP");
                float punch = 1f + Mathf.Sin(t * Mathf.PI) * 0.07f;
                Scale(_summaryUi.TrophyContent, punch); Scale(_summaryUi.XpContent, punch);
                yield return null;
            }
            if (hasReward) GameAudio.Play(SoundCue.RewardComplete);
            Set(_summaryUi.Trophies, Signed(data.Trophies - data.StreakBonusTrophies));
            Set(_summaryUi.StreakBonusTrophies, Signed(data.StreakBonusTrophies));
            Set(_summaryUi.EnergyXp, Signed(data.EnergyXp) + " XP");
            Scale(_summaryUi.TrophyContent, 1); Scale(_summaryUi.XpContent, 1);
        }

        private bool ReadCase()
        {
            if (FightScreenNavigation.IsPreview)
            {
                _rarity = FightScreenNavigation.PreviewRarity;
                _gems = Mathf.Max(1, FightScreenNavigation.PreviewGems);
                _tapsUsed = 0;
                _opened = _screen == FightScreen.CaseReward;
                return _available = true;
            }
            _caseId = _screen == FightScreen.CaseAward ? FightScreenNavigation.AwardedCaseId : FightScreenNavigation.CaseId;
            try
            {
                var saved = CaseRewards.Find(_caseId);
                if (saved == null) return _available = false;
                _rarity = saved.Rarity; _tapsUsed = saved.UpgradeTapsUsed;
                _opened = saved.Opened; _gems = saved.Gems;
                return _available = true;
            }
            catch (Exception exception) { Debug.LogException(exception, this); return _available = false; }
        }

        private void FillCase()
        {
            if (!ReadCase())
            {
                Set(_caseUi.Status, "Case unavailable"); Set(_caseUi.Hint, "Open another case from your inventory");
                SetCaseInteractable(false); return;
            }
            UpdateCaseLabels();
            string caseId = FightScreenNavigation.CaseId ?? "";
            if (caseId.StartsWith("boss-map:", System.StringComparison.Ordinal))
                Set(_caseUi.Source, (BossCatalog.FindChapter(caseId.Split(':')[1])?.Title ?? "ISLAND") + " CASE");
            Set(_caseUi.Status, _screen == FightScreen.CaseAward ? "CASE SAVED TO INVENTORY" : "");
            if (_screen == FightScreen.CaseAward)
            {
                Set(_caseUi.Hint, "Open it now or come back later");
                Set(_caseUi.ActionLabel, "OPEN NOW");
            }
            _busy = true; SetCaseInteractable(false); StartCoroutine(EnterCase());
        }
        private IEnumerator EnterCase()
        {
            // Hold the case back until the scene's first heavy frames are over, then pop it in
            // with its sound, so a load stall never freezes the entrance halfway.
            Scale(_caseUi.Content, 0);
            yield return ScreenTransition.Settle();
            GameAudio.Play(_screen == FightScreen.CaseAward ? SoundCue.RewardComplete : SoundCue.CaseUnlock);
            yield return PopIn(_caseUi.Content, 0.5f);
            _busy = false; SetCaseInteractable(_available);
        }
        private void UpdateCaseLabels()
        {
            int rarity = Mathf.Clamp((int)_rarity, 0, 3);
            GetComponent<CaseRarityPresentation>()?.Apply(rarity);
            Set(_caseUi.Rarity, CaseNames[rarity]);
            if (_caseUi.Rarity != null) _caseUi.Rarity.color = RarityColor(_rarity);
            if (_caseUi.Artwork != null && rarity < _caseUi.RarityArtwork.Length)
                _caseUi.Artwork.sprite = _caseUi.RarityArtwork[rarity];
            if (_caseUi.Glow != null)
            {
                _glowColor = RarityColor(_rarity); _glowColor.a = 0.34f; _caseUi.Glow.color = _glowColor;
            }
            // Each rarity has a centered row authored in the scene. Switching rows does not
            // reposition stars or overwrite edits to the containing group.
            for (int i = 0; i < _caseUi.RarityStarGroups.Length; i++)
                if (_caseUi.RarityStarGroups[i] != null) _caseUi.RarityStarGroups[i].SetActive(i == rarity);
            for (int i = 0; i < _caseUi.TapPips.Length; i++)
                if (_caseUi.TapPips[i] != null) _caseUi.TapPips[i].color = i < _tapsUsed ? Gold : new Color(1, 1, 1, 0.2f);
            bool canOpen = _opened || _tapsUsed >= CaseRewards.UpgradeTapCount;
            Set(_caseUi.ActionLabel, canOpen ? "OPEN" : $"UPGRADE  {_tapsUsed + 1}/3");
            Set(_caseUi.Hint, _screen == FightScreen.CaseOpening ? (canOpen ? "Tap to open case" : $"Tap to upgrade · {CaseRewards.UpgradeTapCount - _tapsUsed} chances left") :
                canOpen ? "Tap the case to reveal your reward" :
                "Tap the case — each tap gives you\na chance to upgrade its rarity");
        }

        public void TapCase()
        {
            ResetCaseAttention();
            if (_busy || !_available || ScreenLayoutRoot.IsAnyEditing) return;
            if (_screen == FightScreen.CaseAward) { OpenNow(); return; }
            if (_opened || _tapsUsed >= CaseRewards.UpgradeTapCount) { OpenCase(); return; }
            var before = _rarity;
            if (FightScreenNavigation.IsPreview)
            {
                _tapsUsed++; _rarity = (CaseRarity)Mathf.Min(3, (int)_rarity + 1);
                FightScreenNavigation.PreviewRarity = _rarity;
            }
            else
            {
                try
                {
                    if (!CaseRewards.TryUpgrade(_caseId, _tapsUsed, out var changed)) { SaveFailed(); return; }
                    _rarity = changed.Rarity; _tapsUsed = changed.UpgradeTapsUsed;
                }
                catch (Exception exception) { Debug.LogException(exception, this); SaveFailed(); return; }
            }
            _busy = true; SetCaseInteractable(false);
            if (_caseMotion == null) UpdateCaseLabels();
            bool upgraded = _rarity != before;
            if (_caseMotion != null) Set(_caseUi.Status, "");
            else SetTapStatus(upgraded);
            StartCoroutine(AnimateTap(upgraded));
        }
        private void SetTapStatus(bool upgraded)
        {
            Set(_caseUi.Status, upgraded ? "RARITY UPGRADED!" : _tapsUsed >= 3 ? "CASE READY TO OPEN" : "ONE MORE CHANCE!");
            if (_caseUi.Status != null) _caseUi.Status.color = upgraded ? RarityColor(_rarity) : Color.white;
        }
        private IEnumerator AnimateTap(bool upgraded)
        {
            GameAudio.Play(SoundCue.CaseUpgrade, 1f + .06f * _tapsUsed);
            if (_caseMotion != null)
            {
                float time = 0;
                bool committed = false;
                while (time < CaseOpeningMotion.TapDuration)
                {
                    if (ScreenLayoutRoot.IsAnyEditing) { yield return null; continue; }
                    time += Time.unscaledDeltaTime;
                    if (!committed && time >= CaseOpeningMotion.UpgradeMoment)
                    {
                        committed = true; UpdateCaseLabels(); SetTapStatus(upgraded);
                        if (upgraded) GameAudio.Play(SoundCue.CaseUpgradeComplete, .9f + .05f * (int)_rarity);
                    }
                    _caseMotion.SampleTap(time, upgraded, _tapsUsed);
                    yield return null;
                }
                _caseMotion.ResetPose();
                _busy = false; ResetCaseAttention(); SetCaseInteractable(true);
                yield break;
            }
            if (upgraded) GameAudio.Play(SoundCue.CaseUpgradeComplete, .9f + .05f * (int)_rarity);
            float elapsed = 0, duration = upgraded ? 0.65f : 0.4f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration), wave = Mathf.Sin(t * Mathf.PI);
                Scale(_caseUi.Content, 1 + wave * (upgraded ? 0.16f : 0.07f));
                Rotate(_caseUi.Content, Mathf.Sin(t * Mathf.PI * 6) * (1 - t) * 6);
                Scale(_caseUi.StarsContent, 1 + wave * (upgraded ? 0.35f : 0));
                yield return null;
            }
            Scale(_caseUi.Content, 1); Rotate(_caseUi.Content, 0); Scale(_caseUi.StarsContent, 1);
            _busy = false; SetCaseInteractable(true);
        }
        public void OpenNow()
        {
            if (_busy || !_available || ScreenLayoutRoot.IsAnyEditing) return;
            FightScreenNavigation.CaseId = _caseId;
            FightScreenNavigation.Navigate(_opened ? _prizeDestination : _openDestination);
        }
        private void OpenCase()
        {
            if (FightScreenNavigation.IsPreview)
            {
                _gems = 100 + (int)_rarity * 50; FightScreenNavigation.PreviewGems = _gems;
            }
            else
            {
                try
                {
                    if (!CaseRewards.TryOpen(_caseId, out var opened)) { SaveFailed(); return; }
                    _rarity = opened.Rarity; _gems = opened.Gems;
                }
                catch (Exception exception) { Debug.LogException(exception, this); SaveFailed(); return; }
            }
            _busy = true; SetCaseInteractable(false); Set(_caseUi.Status, "OPENING…");
            StartCoroutine(RevealCase());
        }
        private IEnumerator RevealCase()
        {
            GameAudio.Play(SoundCue.CaseCharge);
            if (_caseMotion != null)
            {
                float time = 0;
                bool unlocked = false;
                while (time < CaseOpeningMotion.RevealDuration)
                {
                    if (ScreenLayoutRoot.IsAnyEditing) { yield return null; continue; }
                    time += Time.unscaledDeltaTime;
                    if (!unlocked && time >= .38f) { unlocked = true; GameAudio.Play(SoundCue.CaseUnlock); }
                    _caseMotion.SampleReveal(time);
                    yield return null;
                }
                FightScreenNavigation.CaseId = _caseId; FightScreenNavigation.Navigate(_prizeDestination);
                yield break;
            }
            float elapsed = 0;
            while (elapsed < 0.7f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.7f);
                Scale(_caseUi.Content, 1 + t * t * 0.32f); Rotate(_caseUi.Content, Mathf.Sin(t * 42) * t * 7);
                if (_caseUi.Glow != null)
                {
                    var glow = _glowColor; glow.a = 0.34f + 0.6f * t; _caseUi.Glow.color = glow;
                }
                yield return null;
            }
            FightScreenNavigation.CaseId = _caseId; FightScreenNavigation.Navigate(_prizeDestination);
        }
        private void FillPrize()
        {
            if (!ReadCase() || !_opened)
            {
                ShowPrizeError("Open a case from your inventory first\nTap anywhere to return home");
                return;
            }
            Set(_prizeUi.Rarity, CaseNames[Mathf.Clamp((int)_rarity, 0, 3)] + " CASE");
            Set(_prizeUi.Amount, "×" + _gems);
            var prize = FightScreenNavigation.IsPreview ? null : CaseRewards.Find(_caseId);
            var avatar = prize != null ? AvatarCatalog.Find(prize.AvatarId) : null;
            bool hasCards = avatar != null && prize.AvatarCards > 0;
            if (_prizeUi.Note != null) _prizeUi.Note.gameObject.SetActive(hasCards);
            if (_prizeUi.AvatarCardIcon != null)
            {
                _prizeUi.AvatarCardIcon.gameObject.SetActive(hasCards);
                if (hasCards) _prizeUi.AvatarCardIcon.sprite = Resources.Load<Sprite>(avatar.HeadIcon);
            }
            Set(_prizeUi.Note, hasCards
                ? $"+{prize.AvatarCards} {avatar.Name} CARDS\nCollect cards to unlock your avatar!"
                : "Crystals will be added to your balance");
            Set(_prizeUi.ClaimLabel, "CLAIM & HOME");
            // No preload here: streaming home stalls a frame mid pop-in. Home loads under the cover.
            _busy = true;
            if (_prizeUi.ClaimButton != null) _prizeUi.ClaimButton.interactable = false;
            StartCoroutine(EnterPrize());
        }
        private IEnumerator EnterPrize()
        {
            Scale(_prizeUi.Content, 0);
            yield return ScreenTransition.Settle();
            GameAudio.Play(SoundCue.CaseReveal, .9f + .05f * (int)_rarity);
            yield return PopIn(_prizeUi.Content, .65f);
            _busy = false;
            if (_prizeUi.ClaimButton != null) _prizeUi.ClaimButton.interactable = true;
        }
        private void ShowPrizeError(string message)
        {
            if (_prizeUi.Note != null) _prizeUi.Note.gameObject.SetActive(true);
            Set(_prizeUi.Note, message);
        }
        public void ClaimPrize()
        {
            if (_busy || ScreenLayoutRoot.IsAnyEditing) return;
            if (!_available || !_opened) { Home(); return; }
            if (!FightScreenNavigation.IsPreview)
            {
                try
                {
                    if (!CaseRewards.TryClaim(_caseId, out var claimedGems)) { ShowPrizeError("Couldn't save. Tap to retry."); return; }
                    HomeRewardFlight.QueueGems(claimedGems);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this); ShowPrizeError("Couldn't save. Tap to retry."); return;
                }
            }
            if (FightScreenNavigation.IsPreview) HomeRewardFlight.QueueGems(_gems);
            _busy = true;
            if (_prizeUi.ClaimButton != null) _prizeUi.ClaimButton.interactable = false;
            Set(_prizeUi.ClaimLabel, "CLAIMED");
            Set(_prizeUi.Note, FightScreenNavigation.IsPreview ? "Reward preview" : "Rewards added to your collection!");
            GameAudio.Play(SoundCue.RewardComplete);
            StartCoroutine(FinishClaim());
        }
        private IEnumerator FinishClaim()
        {
            // The prize swells and lifts toward the HUD while the cover closes over it; home then
            // opens from the centre, where the crystals burst out and fly into their counter.
            LeaveHome(coverDelay: .2f);
            for (float elapsed = 0; ; elapsed += Time.unscaledDeltaTime)
            {
                float pop = Mathf.Sin(Mathf.Clamp01(elapsed / .3f) * Mathf.PI) * .09f;
                float lift = Mathf.Clamp01((elapsed - .2f) / .35f);
                Scale(_prizeUi.Content, 1 + pop + lift * lift * .25f);
                yield return null;
            }
        }
        /// <summary>Every exit from the reward chain to home shares one seam and one sound.</summary>
        private void LeaveHome(float coverDelay = 0) =>
            FightScreenNavigation.Navigate(_homeDestination, TransitionCover, ScreenTransition.Reveal.Iris, SoundCue.UiTransition, coverDelay);
        private const float TransitionCover = .3f;
        public void Home()
        {
            if (ScreenLayoutRoot.IsAnyEditing) return;
            if (_summaryEntering) { _summarySkip = true; return; }
            if (_screen == FightScreen.RewardSummary)
            {
                // Fight rewards are already credited; HOME returns to the hub. The initial
                // assessment retains its guided Aura/case reveal sequence.
                if (FightScreenNavigation.Result?.Mode != FightMode.LevelTest)
                {
                    LeaveHome();
                    return;
                }
                // Any Aura this fight changed (plus or minus) gets its own screen, which then continues to the case.
                if (_summary.Aura != 0)
                {
                    // Straight to black: the stamp's own riser and slam come out of the dark.
                    FightScreenNavigation.Navigate(FightScreen.AuraReward, .22f, ScreenTransition.Reveal.Instant);
                    return;
                }
                bool hasAward = FightScreenNavigation.IsPreview ? _sampleSummary.HasCase :
                    !string.IsNullOrEmpty(FightScreenNavigation.AwardedCaseId);
                if (hasAward)
                {
                    // Only this result's award, never an unrelated pending inventory case.
                    // Navigation validates the receipt and resumes an already opened prize.
                    FightScreenNavigation.CaseId = FightScreenNavigation.AwardedCaseId;
                    FightScreenNavigation.Navigate(_openDestination, TransitionCover, ScreenTransition.Reveal.Iris, SoundCue.UiTransition);
                    return;
                }
            }
            LeaveHome();
        }
        public void InspectCaseAward()
        {
            if (ScreenLayoutRoot.IsAnyEditing) return;
            if (!FightScreenNavigation.IsPreview && string.IsNullOrEmpty(FightScreenNavigation.AwardedCaseId)) return;
            FightScreenNavigation.Navigate(_awardDestination);
        }
        private void SaveFailed() => Set(_caseUi.Status, "Couldn't save. Tap to retry.");
        private void SetCaseInteractable(bool value)
        {
            if (_caseUi.TapButton != null) _caseUi.TapButton.interactable = value;
            if (_caseUi.ActionButton != null) _caseUi.ActionButton.interactable = value;
        }
        private IEnumerator PopIn(RectTransform content, float duration)
        {
            float elapsed = 0;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(elapsed / duration) - 1;
                Scale(content, Mathf.LerpUnclamped(0.3f, 1, 1 + 2.5f * u * u * u + 1.5f * u * u));
                yield return null;
            }
            Scale(content, 1);
        }
        private void Capture(RectTransform target)
        {
            if (target != null && !_poses.ContainsKey(target))
                _poses.Add(target, new Pose { Scale = target.localScale, Rotation = target.localRotation });
        }
        private void Scale(RectTransform target, float factor)
        {
            if (target != null && _poses.TryGetValue(target, out var pose)) target.localScale = pose.Scale * factor;
        }
        private void Rotate(RectTransform target, float degrees)
        {
            if (target != null && _poses.TryGetValue(target, out var pose))
                target.localRotation = pose.Rotation * Quaternion.Euler(0, 0, degrees);
        }
        private static void Set(TMP_Text label, string value) { if (label != null) label.text = value; }
        private static string Signed(long value) => value > 0 ? "+" + value : value.ToString();
        public static Color RarityColor(CaseRarity rarity)
        {
            switch (rarity)
            {
                case CaseRarity.Rare: return new Color32(100, 179, 255, 255);
                case CaseRarity.Epic: return new Color32(255, 209, 87, 255);
                case CaseRarity.Legendary: return new Color32(210, 136, 255, 255);
                default: return new Color32(218, 225, 241, 255);
            }
        }
    }
}
