using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Badge showing a numeric or text stat with a descriptive label underneath.
    /// Examples: "142 / MAX PUSHUP", "87% / WIN RATE", "1250 / TROPHIES".
    /// Call SetHighlight to tint the value green (win) or red (loss).
    /// </summary>
    public class StatBadge : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _valueText;
        [SerializeField] private TextMeshProUGUI _labelText;

        // Phase 17: localisation system will look up this key instead of the runtime label arg.
#pragma warning disable CS0414
        [SerializeField] private string _localizationKey = "";
#pragma warning restore CS0414

        public void SetStat(string value, string label)
        {
            if (_valueText != null) _valueText.text = value;
            if (_labelText != null) _labelText.text = label;
        }

        /// <summary>Colours the value text to reflect win (lime) or loss (red).</summary>
        public void SetHighlight(bool win)
        {
            if (_valueText == null) return;
            _valueText.color = win ? AppColors.AccentLime : AppColors.DangerRed;
        }

        public void ResetHighlight()
        {
            if (_valueText != null) _valueText.color = AppColors.TextPrimary;
        }
    }
}
