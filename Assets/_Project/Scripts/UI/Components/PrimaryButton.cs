using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Large full-width call-to-action button (e.g. "FIND OPPONENT").
    /// Visual colours are baked into serialised fields at prefab-creation time.
    /// Phase 17: replace _fallbackText with a proper localisation lookup via _localizationKey.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class PrimaryButton : MonoBehaviour
    {
        [SerializeField] private Image            _background;
        [SerializeField] private TextMeshProUGUI  _label;

        // Phase 17: localisation system will look up this key instead of _fallbackText.
#pragma warning disable CS0414
        [SerializeField] private string _localizationKey = "btn.find_opponent";
#pragma warning restore CS0414
        [SerializeField] private string _fallbackText    = "FIND OPPONENT";

        public Button Button { get; private set; }

        private void Awake()
        {
            Button = GetComponent<Button>();
        }

        /// <summary>Sets the button label at runtime (before Phase 17 localisation).</summary>
        public void SetLabel(string text)
        {
            _fallbackText = text;
            if (_label != null) _label.text = text;
        }

        public void SetInteractable(bool interactable)
        {
            if (Button != null) Button.interactable = interactable;
            if (_background != null)
                _background.color = interactable
                    ? AppColors.BtnPrimaryBg
                    : Color.Lerp(AppColors.BtnPrimaryBg, AppColors.BgDark, 0.5f);
        }
    }
}
