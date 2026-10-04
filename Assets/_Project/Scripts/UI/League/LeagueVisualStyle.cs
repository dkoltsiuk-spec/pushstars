using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Current-league milestones and artwork matching the large trophy illustrations.</summary>
    public sealed class LeagueVisualStyle : MonoBehaviour
    {
        public Image BackdropImage;
        public Image[] TierIcons;
        public Image ScoreCup;
        public TextMeshProUGUI ScoreLabel;
        public Sprite[] TrophySprites;
        public GameObject[] DivisionTicks;
        public TextMeshProUGUI Next, Remaining, Minimum, Maximum, Heading;
        public TextMeshProUGUI[] TierLabels;
        public Sprite RankedPlate, SecondaryPlate, HistoryIcon;
        public static readonly Color32[] Colors = { new Color32(255, 177, 45, 255), new Color32(208, 223, 255, 255),
            new Color32(255, 224, 44, 255), new Color32(71, 241, 255, 255) };
        static readonly Color32[] Backgrounds = { new Color32(228, 108, 24, 255), new Color32(73, 116, 225, 255),
            new Color32(232, 165, 24, 255), new Color32(82, 85, 230, 255) };

        public void Refresh(string leagueId, long trophies, bool hasScore = true)
        {
            int index = Mathf.Max(0, System.Array.FindIndex(Leagues.All, l => l.Id == leagueId));
            var league = Leagues.All[index];
            if (BackdropImage != null) BackdropImage.color = Backgrounds[index];
            if (Heading != null) Heading.color = Colors[index];
            if (Next != null) Next.gameObject.SetActive(false);
            if (Remaining != null) Remaining.gameObject.SetActive(false);
            Sprite cup = TrophySprites != null && index < TrophySprites.Length ? TrophySprites[index] : null;
            if (ScoreCup != null && cup != null) ScoreCup.sprite = cup;
            if (ScoreCup != null && ScoreLabel != null)
            {
                // Center the visible cup + number, including single-digit scores.
                float width = Mathf.Clamp(ScoreLabel.GetPreferredValues(hasScore ? trophies.ToString() : "—").x + 6, 36, 184);
                float left = (280 - 88 - width) / 2;
                ScoreCup.rectTransform.anchoredPosition = new Vector2(left, 0);
                ScoreLabel.rectTransform.anchoredPosition = new Vector2(left + 88, 0);
                ScoreLabel.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }
            if (Minimum != null) Minimum.text = league.MinTrophies.ToString();
            if (Maximum != null) Maximum.text = league.IsTop ? "MAX" : (league.MaxTrophies + 1).ToString();
            for (int i = 0; TierIcons != null && i < TierIcons.Length; i++)
            {
                long threshold = league.IsTop ? league.MinTrophies : league.MinTrophies + (league.MaxTrophies + 1 - league.MinTrophies) * i / (TierIcons.Length - 1);

                bool nextTier = !league.IsTop && i == TierIcons.Length - 1;
                TierIcons[i].gameObject.SetActive(i == 0 || nextTier);
                Sprite markerCup = nextTier && TrophySprites != null && index + 1 < TrophySprites.Length ? TrophySprites[index + 1] : cup;
                if (markerCup != null) TierIcons[i].sprite = markerCup;
                TierIcons[i].color = Color.white;
                TierIcons[i].rectTransform.localScale = Vector3.one;
                if (TierLabels != null && i < TierLabels.Length)
                {
                    var label = TierLabels[i];
                    label.gameObject.SetActive(!league.IsTop || i == 0 || i == TierIcons.Length - 1);
                    label.text = league.IsTop && i == TierIcons.Length - 1 ? "MAX" : threshold.ToString();
                    label.color = i == 0 ? Colors[index] : Color.white;
                }
            }
            if (DivisionTicks != null) foreach (var tick in DivisionTicks) if (tick != null) tick.SetActive(!league.IsTop);
        }
    }
}
