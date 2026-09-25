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
            public GameObject TrophyGroup, AssessmentBonus;
            public Image ContinueIcon;
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
        [SerializeField] private AuraRewardPresentation _auraPresentation;
        [Header("Sample data — direct scene launch never grants rewards")]
        [SerializeField] private FightRewardFlow.Summary _sampleSummary = new FightRewardFlow.Summary
        {
            PlayerName = "BEASTCORE_DEV", TotalReps = 57, Technique = 0.92f,
            Trophies = 21, EnergyXp = 570, Aura = 32, HasCase = true
        };

        private struct Pose { public Vector3 Scale; public Quaternion Rotation; }
        private readonly Dictionary<RectTransform, Pose> _poses = new Dictionary<RectTransform, Pose>();
        private static readonly string[] CaseNames = { "COMMON", "RARE", "EPIC", "LEGENDARY" };
        private static readonly Color Gold = new Color32(255, 214, 17, 255);
        private string _caseId;
        private CaseRarity _rarity;
        private int _tapsUsed, _gems, _aura;
        private bool _opened, _available, _busy;
        private Color _glowColor;
        private bool _summaryPortraitFramed;
        private Vector2 _summaryPortraitSize;
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
            if (_auraPresentation != null) _auraPresentation.ResetPresentation();
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
            var data = FightScreenNavigation.IsPreview && FightScreenNavigation.PreviewAura == 0 ? _sampleSummary : FightScreenNavigation.RewardSummary;
            HomeRewardFlight.QueueSummary(data);
            Set(_summaryUi.PlayerName, string.IsNullOrWhiteSpace(data.PlayerName) ? "YOUR RESULT" : data.PlayerName);
            Set(_summaryUi.TotalReps, Mathf.Max(0, data.TotalReps).ToString());
            Set(_summaryUi.Technique, $"{Mathf.Clamp01(data.Technique) * 100f:0}%");
            if (_summaryUi.AuraGroup != null) _summaryUi.AuraGroup.SetActive(data.Aura > 0);
            if (_summaryUi.Trophies != null && data.Trophies < 0)
                _summaryUi.Trophies.color = new Color32(255, 107, 95, 255);
            bool hasAward = FightScreenNavigation.IsPreview || !string.IsNullOrEmpty(FightScreenNavigation.AwardedCaseId);
            bool assessment = FightScreenNavigation.Result?.Mode == FightMode.LevelTest;
            bool auraCase = FightScreenNavigation.IsPreview ? FightScreenNavigation.PreviewAura > 0
                : CaseRewards.Find(FightScreenNavigation.AwardedCaseId)?.Aura > 0;
            if (assessment)
            {
                Set(_summaryUi.Title, "ASSESSMENT COMPLETE!");
                Set(_summaryUi.Subtitle, "Great result. Strong start!");
                if (_summaryUi.TrophyGroup != null) _summaryUi.TrophyGroup.SetActive(data.Trophies != 0);
            }
            if (_summaryUi.AssessmentBonus != null) _summaryUi.AssessmentBonus.SetActive(auraCase);
            Set(_summaryUi.ContinueLabel, hasAward ? "OPEN CASE" : "HOME");
            if (_summaryUi.ContinueIcon != null)
                _summaryUi.ContinueIcon.sprite = hasAward ? Resources.Load<Sprite>("Rewards/CaseLegendary")
                    : Resources.Load<PushStarsTheme>("PushStarsTheme")?.IconHouse;
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
            StartCoroutine(CountSummary(data));
        }

        private void LateUpdate()
        {
            if (_screen != FightScreen.RewardSummary) return;
            var avatar = _summaryUi.Avatar;
            var portrait = _summaryUi.Portrait;
            if (avatar == null || portrait == null || avatar.StageCamera == null) return;
            var texture = avatar.StageCamera.targetTexture;
            if (texture == null) return;
            portrait.texture = texture;
            var size = portrait.rectTransform.rect.size;
            // Once framed, let the idle move inside a fixed crop instead of chasing each breath.
            if (_summaryPortraitFramed && size == _summaryPortraitSize) return;
            if (!avatar.IsPreparationFramed || !avatar.TryGetBodyViewport(out var body)) return;
            float textureAspect = (float)texture.width / texture.height;
            float boxAspect = size.x / Mathf.Max(1, size.y);
            float height = Mathf.Min(1, body.height / .94f);
            float width = Mathf.Min(1, height * boxAspect / textureAspect);
            height = Mathf.Min(height, width * textureAspect / boxAspect);
            portrait.uvRect = new Rect(
                Mathf.Clamp(body.center.x - width * .5f, 0, 1 - width),
                Mathf.Clamp(body.yMin - (height - body.height) * .1f, 0, 1 - height), width, height);
            _summaryPortraitFramed = true;
            _summaryPortraitSize = size;
        }

        private IEnumerator CountSummary(FightRewardFlow.Summary data)
        {
            float elapsed = 0, nextTick = 0;
            bool hasReward = data.Trophies > 0 || data.EnergyXp > 0 || data.Aura > 0;
            while (elapsed < 1.05f)
            {
                elapsed += Time.unscaledDeltaTime;
                if (hasReward && elapsed >= nextTick)
                {
                    GameAudio.Play(SoundCue.RewardTick, Mathf.Lerp(.9f, 1.3f, elapsed / 1.05f));
                    nextTick = elapsed + .075f;
                }
                float t = Mathf.Clamp01(elapsed / 1.05f), eased = 1f - Mathf.Pow(1f - t, 3f);
                Set(_summaryUi.Trophies, Signed((long)Math.Round(data.Trophies * eased)));
                Set(_summaryUi.EnergyXp, Signed((long)Math.Round(data.EnergyXp * eased)));
                Set(_summaryUi.Aura, Signed((long)Math.Round(data.Aura * eased)));
                float punch = 1f + Mathf.Sin(t * Mathf.PI) * 0.07f;
                Scale(_summaryUi.TrophyContent, punch); Scale(_summaryUi.XpContent, punch); Scale(_summaryUi.AuraContent, punch);
                yield return null;
            }
            if (hasReward) GameAudio.Play(SoundCue.RewardComplete);
            Set(_summaryUi.Trophies, Signed(data.Trophies));
            Set(_summaryUi.EnergyXp, Signed(data.EnergyXp)); Set(_summaryUi.Aura, Signed(data.Aura));
            Scale(_summaryUi.TrophyContent, 1); Scale(_summaryUi.XpContent, 1); Scale(_summaryUi.AuraContent, 1);
        }

        private bool ReadCase()
        {
            if (FightScreenNavigation.IsPreview)
            {
                _rarity = FightScreenNavigation.PreviewRarity;
                _aura = FightScreenNavigation.PreviewAura;
                _gems = _aura > 0 ? 0 : Mathf.Max(1, FightScreenNavigation.PreviewGems);
                _tapsUsed = _aura > 0 ? CaseRewards.UpgradeTapCount : 0;
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
                _aura = saved.Aura;
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
            if (_aura > 0 && _auraPresentation != null) _auraPresentation.ConfigureCase();
            Set(_caseUi.Status, _screen == FightScreen.CaseAward ? "CASE SAVED TO INVENTORY" : "");
            if (_aura > 0) Set(_caseUi.Status, "200 AURA INSIDE");
            if (_screen == FightScreen.CaseAward)
            {
                Set(_caseUi.Hint, "Open it now or come back later");
                Set(_caseUi.ActionLabel, "OPEN NOW");
            }
            _busy = true; SetCaseInteractable(false); StartCoroutine(EnterCase());
        }
        private IEnumerator EnterCase()
        {
            GameAudio.Play(_screen == FightScreen.CaseAward ? SoundCue.RewardComplete : SoundCue.CaseUnlock);
            yield return PopIn(_caseUi.Content, 0.5f);
            _busy = false; SetCaseInteractable(_available);
        }
        private void UpdateCaseLabels()
        {
            int rarity = Mathf.Clamp((int)_rarity, 0, 3);
            GetComponent<CaseRarityPresentation>()?.Apply(rarity);
            Set(_caseUi.Rarity, CaseNames[rarity]);
            if (_aura > 0) Set(_caseUi.Source, "ASSESSMENT REWARD · 200 AURA");
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
                _gems = _aura > 0 ? 0 : 100 + (int)_rarity * 50; FightScreenNavigation.PreviewGems = _gems;
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
                    if (_aura > 0 && _auraPresentation != null) _auraPresentation.SampleCharge(time / CaseOpeningMotion.RevealDuration);
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
                if (_aura > 0 && _auraPresentation != null) _auraPresentation.SampleCharge(t);
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
            GameAudio.Play(SoundCue.CaseReveal, .9f + .05f * (int)_rarity);
            Set(_prizeUi.Rarity, CaseNames[Mathf.Clamp((int)_rarity, 0, 3)] + " CASE");
            Set(_prizeUi.Amount, "×" + _gems);
            if (_aura > 0)
            {
                Set(_prizeUi.Amount, "+" + _aura);
                Set(_prizeUi.Rarity, "ASSESSMENT REWARD");
                if (_auraPresentation != null) _auraPresentation.ConfigurePrize();
            }
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
                : _aura > 0 ? "" : "Crystals will be added to your balance");
            Set(_prizeUi.ClaimLabel, "CLAIM & HOME");
            _busy = true;
            if (_prizeUi.ClaimButton != null) _prizeUi.ClaimButton.interactable = false;
            StartCoroutine(EnterPrize());
        }
        private IEnumerator EnterPrize()
        {
            if (_aura > 0 && _auraPresentation != null)
            {
                float time = 0;
                while (time < AuraRewardPresentation.RevealSeconds)
                {
                    if (ScreenLayoutRoot.IsAnyEditing) { yield return null; continue; }
                    time += Time.unscaledDeltaTime;
                    _auraPresentation.SampleReveal(time);
                    yield return null;
                }
                _auraPresentation.Settle();
            }
            else
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
                    HomeRewardFlight.QueueAura(_aura);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this); ShowPrizeError("Couldn't save. Tap to retry."); return;
                }
            }
            if (FightScreenNavigation.IsPreview) HomeRewardFlight.QueueGems(_gems);
            if (FightScreenNavigation.IsPreview) HomeRewardFlight.QueueAura(_aura);
            _busy = true;
            if (_prizeUi.ClaimButton != null) _prizeUi.ClaimButton.interactable = false;
            Set(_prizeUi.ClaimLabel, "CLAIMED");
            Set(_prizeUi.Note, FightScreenNavigation.IsPreview ? "Reward preview" : "Rewards added to your collection!");
            GameAudio.Play(SoundCue.RewardComplete);
            StartCoroutine(FinishClaim());
        }
        private IEnumerator FinishClaim()
        {
            float elapsed = 0;
            while (elapsed < 0.45f)
            {
                elapsed += Time.unscaledDeltaTime;
                Scale(_prizeUi.Content, 1 + 0.09f * Mathf.Sin(Mathf.Clamp01(elapsed / 0.45f) * Mathf.PI));
                yield return null;
            }
            FightScreenNavigation.Navigate(_homeDestination);
        }
        public void Home()
        {
            if (ScreenLayoutRoot.IsAnyEditing) return;
            if (_screen == FightScreen.RewardSummary)
            {
                bool hasAward = FightScreenNavigation.IsPreview ? _sampleSummary.HasCase :
                    !string.IsNullOrEmpty(FightScreenNavigation.AwardedCaseId);
                if (hasAward)
                {
                    // Only this result's award, never an unrelated pending inventory case.
                    // Navigation validates the receipt and resumes an already opened prize.
                    FightScreenNavigation.CaseId = FightScreenNavigation.AwardedCaseId;
                    FightScreenNavigation.Navigate(_openDestination);
                    return;
                }
            }
            FightScreenNavigation.Navigate(_homeDestination);
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
