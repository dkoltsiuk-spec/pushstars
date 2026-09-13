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
    public sealed class RewardScreen : MonoBehaviour
    {
        [Serializable] public sealed class SummaryElements
        {
            public TextMeshProUGUI PlayerName, TotalReps, Technique, Trophies, EnergyXp, Aura;
            public RectTransform TrophyContent, XpContent, AuraContent;
            public GameObject AuraGroup;
            public RawImage Portrait;
            public Texture2D MalePortrait, FemalePortrait;
            public Button CaseAwardButton;
        }
        [Serializable] public sealed class CaseElements
        {
            public TextMeshProUGUI Rarity, Source, Status, Hint, ActionLabel;
            public Image Artwork, Glow;
            public RectTransform Content, StarsContent;
            public Button TapButton, ActionButton;
            public RewardStarGraphic[] Stars = new RewardStarGraphic[0];
            public GameObject[] RarityStarGroups = new GameObject[0];
            public Image[] TapPips = new Image[0];
            public Sprite[] RarityArtwork = new Sprite[4];
        }
        [Serializable] public sealed class PrizeElements
        {
            public TextMeshProUGUI Amount, Rarity, Note, ClaimLabel;
            public RectTransform Content;
            public Image Glow;
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
        [Header("Sample data — direct scene launch never grants rewards")]
        [SerializeField] private FightRewardFlow.Summary _sampleSummary = new FightRewardFlow.Summary
        {
            PlayerName = "BEASTCORE_DEV", TotalReps = 57, Technique = 0.92f,
            Trophies = 21, EnergyXp = 570, Aura = 32, HasCase = true
        };

        private struct Pose { public Vector3 Scale; public Quaternion Rotation; }
        private readonly Dictionary<RectTransform, Pose> _poses = new Dictionary<RectTransform, Pose>();
        private static readonly string[] CaseNames = { "ОБЫЧНЫЙ", "РЕДКИЙ", "ЭПИЧЕСКИЙ", "ЛЕГЕНДАРНЫЙ" };
        private static readonly Color Gold = new Color32(255, 214, 17, 255);
        private string _caseId;
        private CaseRarity _rarity;
        private int _tapsUsed, _gems;
        private bool _opened, _available, _busy;
        private Color _glowColor;

        public FightScreen Screen => _screen;
        public SummaryElements SummaryUi => _summaryUi;
        public CaseElements CaseUi => _caseUi;
        public PrizeElements PrizeUi => _prizeUi;
        public void Configure(FightScreen screen) => _screen = screen;

        private void Start()
        {
            Capture(_summaryUi.TrophyContent); Capture(_summaryUi.XpContent); Capture(_summaryUi.AuraContent);
            Capture(_caseUi.Content); Capture(_caseUi.StarsContent); Capture(_prizeUi.Content);
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
            foreach (var item in _poses)
                if (item.Key != null)
                {
                    item.Key.localScale = item.Value.Scale;
                    item.Key.localRotation = item.Value.Rotation;
                }
        }

        private void Update()
        {
            if (ScreenLayoutRoot.IsAnyEditing) return;
            float pulse = 1f + 0.045f * Mathf.Sin(Time.unscaledTime * 1.8f);
            if (_caseUi.Glow != null) Scale(_caseUi.Glow.rectTransform, pulse);
            if (_prizeUi.Glow != null) Scale(_prizeUi.Glow.rectTransform, pulse);
        }

        private void FillSummary()
        {
            var data = FightScreenNavigation.IsPreview ? _sampleSummary : FightScreenNavigation.RewardSummary;
            Set(_summaryUi.PlayerName, string.IsNullOrWhiteSpace(data.PlayerName) ? "ТВОЙ РЕЗУЛЬТАТ" : data.PlayerName);
            Set(_summaryUi.TotalReps, Mathf.Max(0, data.TotalReps).ToString());
            Set(_summaryUi.Technique, $"{Mathf.Clamp01(data.Technique) * 100f:0}%");
            if (_summaryUi.AuraGroup != null) _summaryUi.AuraGroup.SetActive(data.Aura > 0);
            if (_summaryUi.Trophies != null && data.Trophies < 0)
                _summaryUi.Trophies.color = new Color32(255, 107, 95, 255);
            bool hasAward = FightScreenNavigation.IsPreview || !string.IsNullOrEmpty(FightScreenNavigation.AwardedCaseId);
            if (_summaryUi.CaseAwardButton != null) _summaryUi.CaseAwardButton.gameObject.SetActive(hasAward);
            // The authored standing portrait survives the preceding battle scene unloading.
            if (_summaryUi.Portrait != null)
            {
                var portrait = CharacterRoster.SavedGender == CharacterGender.Female ?
                    _summaryUi.FemalePortrait : _summaryUi.MalePortrait;
                if (portrait != null) _summaryUi.Portrait.texture = portrait;
            }
            if (_summaryUi.Portrait != null && data.AvatarSource != null && data.AvatarSource.texture != null)
            {
                _summaryUi.Portrait.texture = data.AvatarSource.texture;
                _summaryUi.Portrait.uvRect = data.AvatarSource.uvRect;
            }
            StartCoroutine(CountSummary(data));
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
                _gems = Mathf.Max(1, FightScreenNavigation.PreviewGems);
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
                Set(_caseUi.Status, "Кейс недоступен"); Set(_caseUi.Hint, "Открой другой кейс из инвентаря");
                SetCaseInteractable(false); return;
            }
            UpdateCaseLabels();
            Set(_caseUi.Status, _screen == FightScreen.CaseAward ? "КЕЙС СОХРАНЁН В ИНВЕНТАРЕ" : "");
            if (_screen == FightScreen.CaseAward)
            {
                Set(_caseUi.Hint, "Открой сейчас или вернись к нему позже");
                Set(_caseUi.ActionLabel, "ОТКРЫТЬ СЕЙЧАС");
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
            Set(_caseUi.ActionLabel, canOpen ? "ОТКРЫТЬ" : $"УЛУЧШИТЬ  {_tapsUsed + 1}/3");
            Set(_caseUi.Hint, canOpen ? "Нажми на кейс, чтобы получить награду" :
                "Нажимай на кейс — каждый тап\nдаёт шанс повысить редкость");
        }

        public void TapCase()
        {
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
            _busy = true; SetCaseInteractable(false); UpdateCaseLabels();
            bool upgraded = _rarity != before;
            Set(_caseUi.Status, upgraded ? "РЕДКОСТЬ ПОВЫШЕНА!" : _tapsUsed >= 3 ? "КЕЙС ГОТОВ К ОТКРЫТИЮ" : "ЕЩЁ ЕСТЬ ШАНС!");
            if (_caseUi.Status != null) _caseUi.Status.color = upgraded ? RarityColor(_rarity) : Color.white;
            StartCoroutine(AnimateTap(upgraded));
        }
        private IEnumerator AnimateTap(bool upgraded)
        {
            GameAudio.Play(SoundCue.CaseUpgrade, 1f + .06f * _tapsUsed);
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
            _busy = true; SetCaseInteractable(false); Set(_caseUi.Status, "ОТКРЫВАЕМ…");
            StartCoroutine(RevealCase());
        }
        private IEnumerator RevealCase()
        {
            GameAudio.Play(SoundCue.CaseCharge);
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
                Set(_prizeUi.Note, "Сначала открой кейс из инвентаря");
                if (_prizeUi.ClaimButton != null) _prizeUi.ClaimButton.interactable = false;
                return;
            }
            GameAudio.Play(SoundCue.CaseReveal, .9f + .05f * (int)_rarity);
            Set(_prizeUi.Rarity, CaseNames[Mathf.Clamp((int)_rarity, 0, 3)] + " КЕЙС");
            Set(_prizeUi.Amount, "×" + _gems); Set(_prizeUi.Note, "Кристаллы пополнят твой баланс");
            Set(_prizeUi.ClaimLabel, "ЗАБРАТЬ И ДОМОЙ"); StartCoroutine(PopIn(_prizeUi.Content, 0.65f));
        }
        public void ClaimPrize()
        {
            if (_busy || !_available || !_opened || ScreenLayoutRoot.IsAnyEditing) return;
            if (!FightScreenNavigation.IsPreview)
            {
                try
                {
                    if (!CaseRewards.TryClaim(_caseId, out _)) { Set(_prizeUi.Note, "Не удалось сохранить. Попробуй ещё раз."); return; }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this); Set(_prizeUi.Note, "Не удалось сохранить. Попробуй ещё раз."); return;
                }
            }
            _busy = true;
            if (_prizeUi.ClaimButton != null) _prizeUi.ClaimButton.interactable = false;
            Set(_prizeUi.ClaimLabel, "ПОЛУЧЕНО");
            Set(_prizeUi.Note, FightScreenNavigation.IsPreview ? "Награда в режиме просмотра" : "Кристаллы зачислены!");
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
            if (!ScreenLayoutRoot.IsAnyEditing) FightScreenNavigation.Navigate(_homeDestination);
        }
        public void InspectCaseAward()
        {
            if (ScreenLayoutRoot.IsAnyEditing) return;
            if (!FightScreenNavigation.IsPreview && string.IsNullOrEmpty(FightScreenNavigation.AwardedCaseId)) return;
            FightScreenNavigation.Navigate(_awardDestination);
        }
        private void SaveFailed() => Set(_caseUi.Status, "Не удалось сохранить. Нажми ещё раз.");
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
