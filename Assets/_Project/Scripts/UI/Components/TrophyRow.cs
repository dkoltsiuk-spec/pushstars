using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Single row in a league leaderboard or reward screen.
    /// Displays rank, player name, and trophy count.
    /// Highlight the local player's row via isLocalPlayer = true.
    /// </summary>
    public class TrophyRow : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _rankText;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _trophyCountText;
        [SerializeField] private Image           _background;
        [SerializeField] private Image           _trophyIcon;    // optional decorative icon

        public void SetRow(int rank, string playerName, int trophies, bool isLocalPlayer = false)
        {
            if (_rankText        != null) _rankText.text        = $"#{rank}";
            if (_nameText        != null) _nameText.text        = playerName;
            if (_trophyCountText != null) _trophyCountText.text = trophies.ToString("N0");

            if (_background != null)
            {
                _background.color = isLocalPlayer
                    ? new Color(AppColors.AccentYellow.r, AppColors.AccentYellow.g,
                                AppColors.AccentYellow.b, 0.18f)
                    : AppColors.BtnSecondaryBg;
            }
        }

        public void SetTrophyDelta(int delta)
        {
            if (_trophyCountText == null) return;
            string sign = delta >= 0 ? "+" : "";
            _trophyCountText.text  = $"{sign}{delta}";
            _trophyCountText.color = delta >= 0 ? AppColors.AccentLime : AppColors.DangerRed;
        }
    }
}
