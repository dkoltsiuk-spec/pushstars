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
        public void Refresh()
        {
            if (Track == null || FillImage == null) return;
            float amount = Mathf.Clamp01(Fill);
            FillImage.enabled = amount > 0;
            // Resize the complete exported fill so its rounded, slanted end stays intact.
            // A Filled Image would cut the artwork off with a vertical edge.
            FillImage.rectTransform.sizeDelta = new Vector2(Track.rectTransform.rect.width * amount, Track.rectTransform.rect.height);
        }
        private void OnEnable() => Refresh();
        private void LateUpdate() => Refresh();
    }
}
