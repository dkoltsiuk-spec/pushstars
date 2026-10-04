using UnityEngine;
using UnityEngine.UI;
namespace PushStars.UI
{
    [ExecuteAlways]
    public sealed class LeagueProgressGraphic : MonoBehaviour
    {
        [Range(0, 1)] public float Fill = .77f;
        public Image Track;
        public Image FillImage;
        public LeagueSurface StyledRail;
        public RectTransform ScoreMarker;
        public void Refresh()
        {
            if (ScoreMarker != null) ScoreMarker.anchoredPosition = new Vector2(Mathf.Lerp(0, 290, Mathf.Clamp01(Fill)), 0);
            if (StyledRail != null && !Mathf.Approximately(StyledRail.Amount, Mathf.Clamp01(Fill)))
            {
                StyledRail.Amount = Mathf.Clamp01(Fill);
                StyledRail.SetVerticesDirty();
            }
            if (Track == null || FillImage == null) return;
            float amount = Mathf.Clamp01(Fill);
            FillImage.enabled = amount > 0;
            // Keep the artwork registered to the track. Native UI filling clips the
            // visible portion without squeezing its slope, highlights or texture.
            var rect = FillImage.rectTransform;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Track.rectTransform.rect.width);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Track.rectTransform.rect.height);
            FillImage.type = Image.Type.Filled;
            FillImage.fillMethod = Image.FillMethod.Horizontal;
            FillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            FillImage.preserveAspect = false;
            FillImage.fillAmount = amount;
        }
        private void OnEnable() => Refresh();
        private void LateUpdate() => Refresh();
    }
}
