using PushStars.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Authored aura rig shared by the final case charge and its prize scene.</summary>
    public sealed class AuraRewardPresentation : MonoBehaviour
    {
        public const float RevealSeconds = 1.6f;
        public AuraEnergyGraphic Energy;
        public RewardScreen Screen;
        public TextMeshProUGUI Title, ClaimHint;
        public Image PrizeIcon;
        public AuraFlameGraphic Flame;
        public CanvasGroup AmountGroup, TitleGroup;
        private Vector3 _prizeScale, _amountScale;
        private bool _captured, _idle, _active;
        private float _idleTime;

        public void ConfigureCase()
        {
            _active = true;
            Energy.gameObject.SetActive(true);
            SampleCharge(0);
        }
        public void ConfigurePrize()
        {
            _active = true;
            var ui = Screen.PrizeUi;
            if (!_captured) { _prizeScale = ui.Content.localScale; _amountScale = ui.Amount.rectTransform.localScale; _captured = true; }
            Energy.gameObject.SetActive(true);
            Title.text = "AURA";
            Title.gameObject.SetActive(true);
            Title.fontSize = Title.fontSizeMax = 40;
            Title.enableAutoSizing = false;
            Title.overflowMode = TextOverflowModes.Overflow;
            Title.textWrappingMode = TextWrappingModes.NoWrap;
            Title.color = new Color(.87f, .70f, 1);
            PrizeIcon.gameObject.SetActive(false);
            Flame.gameObject.SetActive(true);
            ui.Amount.color = new Color(.91f, .8f, 1);
            ui.Amount.fontSize = ui.Amount.fontSizeMax = 52;
            ui.Amount.enableAutoSizing = false;
            var amountBox = (RectTransform)ui.Amount.transform.parent;
            amountBox.anchoredPosition = new Vector2(195, -574);
            amountBox.sizeDelta = new Vector2(270, 78);
            ui.Glow.color = new Color(.61f, .21f, 1, .5f);
            if (ui.Rays != null) ui.Rays.gameObject.SetActive(false);
            var backdrop = Energy.transform.parent.GetComponentInChildren<FightRewardBackdrop>();
            if (backdrop != null) { backdrop.Appearance = FightRewardBackdrop.Style.Aura; backdrop.SetVerticesDirty(); }
            var field = Energy.transform.parent.GetComponentInChildren<LightningField>();
            if (field != null) field.gameObject.SetActive(false);
            ClaimHint.gameObject.SetActive(true);
            ClaimHint.text = "";
            SampleReveal(0);
        }
        public void SampleCharge(float progress)
        {
            Energy.Sample(progress * 1.5f, .25f + Mathf.SmoothStep(0, 1, progress) * .75f, 0);
        }
        public void SampleReveal(float seconds)
        {
            float t = Mathf.Max(0, seconds);
            Energy.Sample(1.5f + t, Mathf.Lerp(1, .75f, Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - .5f) / 1.1f))), Mathf.Clamp01(t / 1.05f));
            float u = Mathf.Clamp01((t - .24f) / .7f) - 1;
            float pop = 1 + 2.7f * u * u * u + 1.7f * u * u;
            Screen.PrizeUi.Content.localScale = _prizeScale * Mathf.LerpUnclamped(.12f, 1, pop);
            float count = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - .52f) / .74f));
            Screen.PrizeUi.Amount.text = "+" + Mathf.RoundToInt(Core.CaseRewards.AssessmentAura * count);
            AmountGroup.alpha = Mathf.Clamp01((t - .5f) / .2f);
            TitleGroup.alpha = Mathf.Clamp01(t / .22f);
            Screen.PrizeUi.Amount.rectTransform.localScale = _amountScale * (1 + .13f * Mathf.Sin(count * Mathf.PI));
        }
        public void Settle()
        {
            SampleReveal(RevealSeconds);
            _idle = true; _idleTime = RevealSeconds;
            ClaimHint.text = "TAP TO COLLECT";
        }
        private void Update()
        {
            if (!_idle || !_active || PushStars.UI.Layout.ScreenLayoutRoot.IsAnyEditing) return;
            _idleTime += Time.unscaledDeltaTime;
            Energy.Sample(1.5f + _idleTime, .75f, 1);
            Screen.PrizeUi.Content.localScale = _prizeScale * (1 + Mathf.Sin((_idleTime - RevealSeconds) * 2) * .022f);
        }
        public void ResetPresentation()
        {
            _idle = _active = false;
            if (Energy != null) Energy.Sample(0, 0, 0);
            if (_captured)
            {
                Screen.PrizeUi.Content.localScale = _prizeScale;
                Screen.PrizeUi.Amount.rectTransform.localScale = _amountScale;
                AmountGroup.alpha = TitleGroup.alpha = 1;
            }
        }
        private void OnDisable() => ResetPresentation();
    }
}
