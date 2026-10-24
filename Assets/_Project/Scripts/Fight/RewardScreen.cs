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
        private static readonly Color LevelCaption = new Color32(185, 201, 247, 255); // the profile's muted caption
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
            NickStyleView.Own(_summaryUi.PlayerName);
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
            if (!string.IsNullOrEmpty(result?.LeagueStatus)) Set(_summaryUi.Subtitle, result.LeagueStatus);
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            // Any Aura the result changed has its own screen next; a duel's case otherwise waits
            // behind its button, the assessment's opens here.
            Set(_summaryUi.ContinueLabel, data.Aura != 0 ? "CONTINUE" : assessment && hasAward ? "OPEN CASE" : "HOME");
            if (_summaryUi.ContinueIcon != null)
                _summaryUi.ContinueIcon.sprite = data.Aura != 0 ? theme?.IconAura
                    : assessment && hasAward ? Resources.Load<Sprite>("Rewards/CaseCommon") : theme?.IconHouse;
            if (_summaryUi.CaseAwardButton != null) _summaryUi.CaseAwardButton.gameObject.SetActive(hasAward);
            // Keep the baked image for Edit Mode/fallback; this scene owns a live idle stage.
            if (_summaryUi.Portrait != null)
            {
                var portrait = CharacterRoster.SavedGender == CharacterGender.Female ?
                    _summaryUi.FemalePortrait : _summaryUi.MalePortrait;
                if (portrait != null) _summaryUi.Portrait.texture = portrait;
                HeroAura.Attach(_summaryUi.Portrait);
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
            ShowEarlyEnd(duel ? result.EarlyEnd : null);
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

        private TextMeshProUGUI _earlyEnd;

        /// <summary>A live duel cut short says why at the end of the caption under the score —
        /// "92% FORM  VS OSKAT · OPPONENT LEFT" — so a loss at 4:4 or a win at 3:3 reads as what it
        /// is. Built at runtime from the opponent's word (same style), no new block on the screen.</summary>
        private void ShowEarlyEnd(string reason)
        {
            var name = _summaryUi.OpponentName;
            if (name == null) return;
            if (_earlyEnd == null)
            {
                if (string.IsNullOrEmpty(reason)) return;
                _earlyEnd = Instantiate(name, name.transform.parent, false);
                _earlyEnd.name = "EarlyEnd";
                _earlyEnd.transform.SetSiblingIndex(name.transform.GetSiblingIndex() + 1);
            }
            _earlyEnd.gameObject.SetActive(!string.IsNullOrEmpty(reason));
            if (string.IsNullOrEmpty(reason)) return;
            _earlyEnd.text = "· " + reason;
            _earlyEnd.color = _outcome == Outcome.Win ? Gold : Color.white;
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
            // An emote owns the body; celebrate again only after it.
            var emote = animator.GetComponent<EmotePlayer>();
            if (emote != null && emote.IsPlaying) { _celebrateAt = Time.unscaledTime + 1.4f; return; }
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

        /// <summary>The emote button, once the screen has settled. The opponent opens with a taunt
        /// when it won and a GG when it lost, so the button is discovered in context.</summary>
        private void AttachEmotes()
        {
            if (!EmoteCatalog.Enabled) return;
            var ui = _summaryUi;
            if (ui.Avatar == null || ui.Continue == null || _outcome == Outcome.Solo) return;
            Animator Body(FightAvatar a) => a != null && a.isActiveAndEnabled && a.Character != null
                ? a.Character.GetComponentInChildren<Animator>() : null;
            bool opponent = ui.Opponent != null && ui.Opponent.gameObject.activeInHierarchy;
            var bar = EmoteBar.Attach(ui.Continue.parent as RectTransform, () => Body(ui.Avatar),
                opponent ? () => Body(ui.Opponent) : (Func<Animator>)null, () => ui.Avatar.Character,
                EmoteLayout.ResultCorner, EmoteLayout.ResultOffset);
            if (bar != null && opponent && _outcome != Outcome.Draw && UnityEngine.Random.value < .6f)
                bar.OpponentOpens(UnityEngine.Random.Range(1.2f, 2.5f), _outcome == Outcome.Loss);
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
            AttachEmotes();
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
            // The profile's level block: "LVL" over the number on the tag, the count across the bar.
            Set(ui.Level, "<size=8>LVL</size>\n" + level);
            if (ui.LevelFill != null)
            {
                // Rounded bar: the capsule grows instead of being cut, never narrower than it is tall.
                float progress = Mathf.Clamp01(LevelCalculator.LevelProgress(xp));
                var area = ((RectTransform)ui.LevelFill.parent).rect;
                float least = area.width > 0 ? area.height / area.width : 0;
                var max = ui.LevelFill.anchorMax;
                max.x = Mathf.Max(least, progress);
                ui.LevelFill.anchorMax = max;
                var fill = ui.LevelFill.GetComponent<Graphic>();
                if (fill != null) fill.enabled = progress > 0;
            }
            Set(ui.LevelUp, levelledUp ? "LEVEL UP!" : "LEVEL PROGRESS");
            if (ui.LevelUp != null) ui.LevelUp.color = levelledUp ? Gold : LevelCaption;
            long floor = LevelCalculator.TotalXpForLevel(level);
            Set(ui.LevelProgress, level >= EconomyConfig.MaxLevel ? "MAX"
                : $"{xp - floor}/{LevelCalculator.XpForLevelUp(level)}");
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
                _gems = FightScreenNavigation.PreviewAssessment ? CaseRewards.AssessmentGems : 100 + (int)_rarity * 50;
                FightScreenNavigation.PreviewGems = _gems;
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
            bool hasBonus = prize != null && prize.Bonus != CaseBonus.None;
            if (_prizeUi.Note != null) _prizeUi.Note.gameObject.SetActive(hasCards || hasBonus);
            if (_prizeUi.AvatarCardIcon != null)
            {
                _prizeUi.AvatarCardIcon.gameObject.SetActive(hasCards);
                if (hasCards) _prizeUi.AvatarCardIcon.sprite = Resources.Load<Sprite>(avatar.HeadIcon);
            }
            Set(_prizeUi.Note, hasCards
                ? $"+{prize.AvatarCards} {avatar.Name} CARDS\nCollect cards to unlock your avatar!"
                : "Crystals will be added to your balance");
            // Gems first, on their own screen; the bonus gets the next one (see ShowBonusPage).
            _bonus = hasBonus ? prize : null;
            _bonusShown = false;
            if (hasBonus && _prizeUi.Note != null) _prizeUi.Note.gameObject.SetActive(false);
            Set(PrizeTitle, "GEMS");
            Set(_prizeUi.ClaimLabel, "CLAIM & HOME");
            // A daily case holds no gems: its bonus is the whole prize, so it opens straight on it.
            if (_gems <= 0 && hasBonus) { ApplyBonusPage(prize); _bonusShown = true; }
            // No preload here: streaming home stalls a frame mid pop-in. Home loads under the cover.
            _busy = true;
            if (_prizeUi.ClaimButton != null) _prizeUi.ClaimButton.interactable = false;
            StartCoroutine(EnterPrize());
        }
        private PendingCase _bonus;
        private bool _bonusShown;
        private TextMeshProUGUI _prizeTitle;
        private RectTransform _bonusArt;

        /// <summary>The prize screen's heading ("REWARDS" in the scene; "GEMS" or the bonus name here).</summary>
        private TextMeshProUGUI PrizeTitle
        {
            get
            {
                if (_prizeTitle != null) return _prizeTitle;
                foreach (var label in GetComponentsInChildren<TextMeshProUGUI>(true))
                    if (label.name == "Title" && label.transform.parent != null && label.transform.parent.name == "header")
                        return _prizeTitle = label;
                return null;
            }
        }

        /// <summary>Tap on the gems screen: the gems shrink away and the bonus pops in on its own
        /// screen, with its own colour. Nothing is claimed until the bonus screen is tapped too.</summary>
        private IEnumerator ShowBonusPage()
        {
            _busy = true;
            _bonusShown = true;
            var content = _prizeUi.Content;
            float from = content != null ? content.localScale.x : 1f;
            for (float t = 0; t < .16f; t += Time.unscaledDeltaTime)
            {
                Scale(content, Mathf.Lerp(from, 0f, t / .16f));
                yield return null;
            }
            Scale(content, 0);
            ApplyBonusPage(_bonus);
            GameAudio.Play(_bonus.IsJackpot ? SoundCue.CaseUpgradeComplete : SoundCue.CaseReveal, _bonus.IsJackpot ? 1.2f : 1f);
            if (_bonus.IsJackpot) StartCoroutine(JackpotConfetti());
            yield return PopIn(content, .55f);
            _busy = false;
        }

        private static readonly Color32 Gold32 = new Color32(255, 216, 74, 255);

        /// <summary>One screen per bonus, laid out like the gems one: title, picture, amount, and a
        /// single line only where the prize needs explaining.</summary>
        private void ApplyBonusPage(PendingCase prize)
        {
            string title, amount, note = null;
            Color top, bottom, glow;
            if (_bonusArt != null) Destroy(_bonusArt.gameObject);
            _bonusArt = ArenaUi.Rect(_prizeUi.Content, "BonusArt", Vector2.zero, Vector2.zero);
            ArenaUi.Stretch(_bonusArt);
            // Lifted a little: unlike the gem pile, these pictures fill their box to the bottom.
            _bonusArt.anchoredPosition = new Vector2(0, 14);
            foreach (var art in _prizeUi.Content.GetComponentsInChildren<Image>(true))
                if (!art.transform.IsChildOf(_bonusArt)) art.enabled = false;
            switch (prize.Bonus)
            {
                case CaseBonus.Aura:
                    title = "AURA"; amount = $"+{prize.BonusAmount:N0}";
                    top = new Color32(92, 20, 200, 255); bottom = new Color32(52, 8, 140, 255); glow = new Color32(205, 150, 255, 175);
                    BonusImage(Resources.Load<PushStarsTheme>("PushStarsTheme")?.IconAura, new Vector2(150, 150));
                    break;
                case CaseBonus.Boost:
                    title = "x2 BOOST";
                    amount = prize.BonusAmount > 1 ? $"NEXT {prize.BonusAmount} FIGHTS" : "NEXT FIGHT";
                    note = "Aura you earn counts double";
                    top = new Color32(247, 142, 12, 255); bottom = new Color32(214, 82, 0, 255); glow = new Color32(255, 232, 130, 180);
                    NickStyleView.Set(BonusText("x2", 120), "gold");
                    break;
                case CaseBonus.NickColor:
                    var color = NickColorCatalog.Find(prize.BonusId);
                    title = "NAME COLOR"; amount = color?.Name ?? "NEW COLOR";
                    note = (color != null ? NickColorCatalog.TierName(color.Tier) + " · " : "") + "pick it when you edit your name";
                    top = new Color32(26, 66, 216, 255); bottom = new Color32(12, 30, 142, 255); glow = new Color32(125, 195, 255, 170);
                    var name = BonusText(ProfileIdentityEditor.PlayerName(), 60);
                    name.enableAutoSizing = true; name.fontSizeMin = 28; name.fontSizeMax = 60;
                    name.rectTransform.sizeDelta = new Vector2(340, 90);
                    NickStyleView.Set(name, prize.BonusId);
                    break;
                case CaseBonus.Hero:
                    var hero = AvatarCatalog.Find(prize.BonusId);
                    title = "JACKPOT!"; amount = hero?.Name ?? "NEW HERO"; note = "Hero unlocked";
                    top = new Color32(222, 28, 96, 255); bottom = new Color32(128, 8, 72, 255); glow = new Color32(255, 196, 96, 185);
                    BonusHero(prize.BonusId, hero != null && !string.IsNullOrEmpty(hero.HeadIcon) ? Resources.Load<Sprite>(hero.HeadIcon) : null);
                    break;
                default:
                    var arena = ArenaCatalog.Get(prize.BonusId);
                    title = "JACKPOT!"; amount = arena?.Title.ToUpperInvariant() ?? "NEW ARENA"; note = "Arena unlocked";
                    top = new Color32(222, 28, 96, 255); bottom = new Color32(128, 8, 72, 255); glow = new Color32(255, 196, 96, 185);
                    BonusArena(arena?.Home, new Vector2(244, 140));
                    break;
            }
            Set(PrizeTitle, title);
            Set(_prizeUi.Amount, amount);
            if (_prizeUi.Note != null)
            {
                _prizeUi.Note.gameObject.SetActive(note != null);
                Set(_prizeUi.Note, note);
                if (_prizeUi.Amount != null)
                {
                    // Right under the amount, centred, instead of the old receipt row at the bottom.
                    var anchor = _prizeUi.Amount.rectTransform;
                    _prizeUi.Note.rectTransform.position = anchor.position;
                    _prizeUi.Note.rectTransform.anchoredPosition += new Vector2(0, -(anchor.rect.height * .5f + 24f));
                }
            }
            if (_prizeUi.AvatarCardIcon != null) _prizeUi.AvatarCardIcon.gameObject.SetActive(false);
            if (_prizeUi.Rarity != null) _prizeUi.Rarity.gameObject.SetActive(false);
            GetComponentInChildren<FightRewardBackdrop>(true)?.SetPalette(top, bottom, glow);
            if (_prizeUi.Glow != null) _prizeUi.Glow.color = new Color(glow.r, glow.g, glow.b, .45f);
            var rays = _prizeUi.Rays != null ? _prizeUi.Rays.GetComponent<Graphic>() : null;
            if (rays != null) rays.color = new Color(1f, 1f, 1f, rays.color.a);
        }

        private ConfettiBurst _confetti;

        /// <summary>The jackpot's party: a blast out of the prize, then two cannons from the bottom
        /// corners, then a last puff from the top while the first wave is still falling.</summary>
        private IEnumerator JackpotConfetti()
        {
            var canvas = GetComponentInChildren<Canvas>();
            if (canvas == null) yield break;
            if (_confetti == null) _confetti = ConfettiBurst.Create(canvas.transform);
            _confetti.transform.SetAsLastSibling();
            var area = _confetti.rectTransform.rect;
            Vector2 centre = _prizeUi.Content != null ? (Vector2)_confetti.rectTransform.InverseTransformPoint(_prizeUi.Content.position) : area.center;
            _confetti.Burst(centre, 190, 90f, 360f, 1050f);
            GameAudio.Play(SoundCue.RewardComplete, 1.1f);
            yield return new WaitForSecondsRealtime(.18f);
            _confetti.Burst(new Vector2(area.xMin + 10, area.yMin + 40), 95, 62f, 26f, 1750f);
            _confetti.Burst(new Vector2(area.xMax - 10, area.yMin + 40), 95, 118f, 26f, 1750f);
            yield return new WaitForSecondsRealtime(.45f);
            _confetti.Burst(new Vector2(area.center.x, area.yMax + 10), 90, 270f, 140f, 350f);
        }

        /// <summary>The jackpot hero in 3D, full body and idling, rendered like the hero cards.
        /// Falls back to the head icon when the body cannot be loaded.</summary>
        private void BonusHero(string heroId, Sprite fallback)
        {
            var prefab = HeroPrefabs.For(heroId);
            if (prefab == null) { BonusImage(fallback, new Vector2(165, 165)); return; }
            int layer = LayerMask.NameToLayer("AvatarCards");
            if (layer < 0) layer = 31;
            var raw = ArenaUi.Rect(_bonusArt, "Hero3D", new Vector2(210, 300), new Vector2(0, 40)).gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            // The home stage's rig (key, blue fill, cool rim), so the hero looks as it does at home.
            HeroLight(raw.transform, layer, "Key", 6.77f, Color.white, new Vector3(35, 25, 0));
            HeroLight(raw.transform, layer, "Fill", .45f, new Color(0, .341f, 1f), new Vector3(15, 325, 0));
            HeroLight(raw.transform, layer, "Rim", 2.9f, new Color(.575f, .664f, 1f), new Vector3(17.24f, 125.88f, 218.8f));
            var preview = raw.gameObject.AddComponent<AvatarCardPreview>();
            preview.Image = raw;
            preview.Slot = 48;
            preview.PreviewLayer = layer;
            preview.FullBody = true;
            preview.SetPrefab(prefab);
            // The standing body is taller than the icons: the amount steps down to clear the feet.
            if (_prizeUi.Amount != null) _prizeUi.Amount.rectTransform.anchoredPosition += new Vector2(0, -50);
        }

        private static void HeroLight(Transform parent, int layer, string name, float intensity, Color color, Vector3 rotation)
        {
            var light = new GameObject("Hero" + name + "Light").AddComponent<Light>();
            light.transform.SetParent(parent, false);
            light.type = LightType.Directional;
            light.cullingMask = 1 << layer;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.Euler(rotation);
        }

        private void BonusImage(Sprite sprite, Vector2 size)
        {
            if (sprite == null) return;
            var image = ArenaUi.Rect(_bonusArt, "Icon", size, Vector2.zero).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
        }

        /// <summary>Big lettering in the amount's font: the boost's "x2" or the player's name.</summary>
        private TextMeshProUGUI BonusText(string text, float size)
        {
            var label = ArenaUi.Label(_bonusArt, "Text", text, new Vector2(340, size * 1.3f), Vector2.zero, size);
            if (_prizeUi.Amount != null) { label.font = _prizeUi.Amount.font; label.fontSharedMaterial = _prizeUi.Amount.fontSharedMaterial; }
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>The jackpot arena as a rounded, landscape crop of its home art, like its card.</summary>
        /// <summary>The jackpot arena framed like a trophy: drop shadow, black keyline, a bevelled gold
        /// frame with a shine on its top edge, the art inside its own keyline and a red NEW ARENA ribbon.</summary>
        private void BonusArena(Sprite home, Vector2 size)
        {
            if (home == null) return;
            var ink = new Color32(18, 12, 34, 255);
            var frame = ArenaUi.Rect(_bonusArt, "ArenaFrame", size + new Vector2(30, 30), Vector2.zero);
            FrameLayer(frame, "Shadow", size + new Vector2(32, 32), new Vector2(0, -9), 24, new Color32(0, 0, 0, 95));
            FrameLayer(frame, "Keyline", size + new Vector2(30, 30), Vector2.zero, 24, ink);
            FrameLayer(frame, "GoldDark", size + new Vector2(23, 23), Vector2.zero, 20, new Color32(206, 116, 0, 255));
            FrameLayer(frame, "Gold", size + new Vector2(23, 18), new Vector2(0, 2.5f), 19, new Color32(255, 198, 36, 255));
            FrameLayer(frame, "GoldShine", new Vector2(size.x - 16, 4), new Vector2(0, size.y * .5f + 7.5f), 2, new Color32(255, 246, 190, 200));
            FrameLayer(frame, "InnerKeyline", size + new Vector2(7, 7), Vector2.zero, 15, ink);

            var mask = ArenaUi.Rect(frame, "Arena", size, Vector2.zero);
            var shape = mask.gameObject.AddComponent<ArenaRoundedGraphic>();
            shape.Radius = 13; shape.raycastTarget = false;
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var art = ArenaUi.Rect(mask, "Artwork", Vector2.zero, Vector2.zero).gameObject.AddComponent<RawImage>();
            ArenaUi.Stretch(art.rectTransform);
            art.raycastTarget = false;
            AvatarCollectionScreen.FrameArena(art, home, size);

            var ribbon = ArenaUi.Rect(frame, "NewRibbon", new Vector2(132, 30), new Vector2(0, size.y * .5f + 14));
            FrameLayer(ribbon, "Keyline", new Vector2(132, 30), Vector2.zero, 15, ink);
            FrameLayer(ribbon, "Fill", new Vector2(126, 24), Vector2.zero, 12, new Color32(238, 44, 64, 255));
            FrameLayer(ribbon, "Shine", new Vector2(116, 9), new Vector2(0, 5), 5, new Color32(255, 255, 255, 60));
            var label = ArenaUi.Label(ribbon, "Label", "NEW ARENA", new Vector2(126, 26), new Vector2(0, 1), 15);
            if (_prizeUi.Amount != null) { label.font = _prizeUi.Amount.font; label.fontSharedMaterial = _prizeUi.Amount.fontSharedMaterial; }
            label.color = Color.white; label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false;
            // The frame is taller than the art: the amount steps down to clear it.
            if (_prizeUi.Amount != null) _prizeUi.Amount.rectTransform.anchoredPosition += new Vector2(0, -34);
        }

        private static void FrameLayer(Transform parent, string name, Vector2 size, Vector2 position, float radius, Color32 color)
        {
            var layer = ArenaUi.Rect(parent, name, size, position).gameObject.AddComponent<ArenaRoundedGraphic>();
            layer.Radius = radius; layer.color = color; layer.raycastTarget = false;
        }

        private IEnumerator EnterPrize()
        {
            Scale(_prizeUi.Content, 0);
            yield return ScreenTransition.Settle();
            GameAudio.Play(SoundCue.CaseReveal, .9f + .05f * (int)_rarity);
            if (_bonusShown && _bonus != null && _bonus.IsJackpot) StartCoroutine(JackpotConfetti());
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
            if (_bonus != null && !_bonusShown) { StartCoroutine(ShowBonusPage()); return; }
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
                // Any Aura this fight changed (plus or minus) gets its own screen, which then
                // continues to this result's case or home. Rewards are already credited.
                if (_summary.Aura != 0)
                {
                    // Straight to black: the stamp's own riser and slam come out of the dark.
                    FightScreenNavigation.Navigate(FightScreen.AuraReward, .22f, ScreenTransition.Reveal.Instant);
                    return;
                }
                // Without Aura a duel goes home; its case waits behind the summary's case button.
                // The initial assessment keeps its guided case reveal.
                if (FightScreenNavigation.Result?.Mode != FightMode.LevelTest)
                {
                    LeaveHome();
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
