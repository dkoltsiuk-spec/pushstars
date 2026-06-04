using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Compact player identity pill: country flag emoji + username + optional trophy count.
    /// Used in the pre-match VS screen, leaderboard rows, and the reward screen.
    /// </summary>
    public class PlayerPill : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _flagText;    // emoji flag (e.g. 🇺🇸)
        [SerializeField] private TextMeshProUGUI _nameText;    // username
        [SerializeField] private TextMeshProUGUI _trophyText;  // "🏆 1250" or hidden
        [SerializeField] private Image           _background;

        /// <summary>
        /// Populates the pill with player data. Pass trophies = -1 to hide the trophy field.
        /// </summary>
        public void SetPlayer(string countryFlag, string playerName, int trophies = -1)
        {
            if (_flagText   != null) _flagText.text  = countryFlag;
            if (_nameText   != null) _nameText.text  = playerName;

            if (_trophyText != null)
            {
                bool show = trophies >= 0;
                _trophyText.gameObject.SetActive(show);
                if (show) _trophyText.text = $"T {trophies}";
            }
        }

        /// <summary>Highlights the pill background (e.g. to mark the local player).</summary>
        public void SetLocalPlayerHighlight(bool highlight)
        {
            if (_background == null) return;
            _background.color = highlight
                ? new Color(AppColors.AccentYellow.r, AppColors.AccentYellow.g,
                            AppColors.AccentYellow.b, 0.20f)
                : AppColors.BtnSecondaryBg;
        }
    }
}
