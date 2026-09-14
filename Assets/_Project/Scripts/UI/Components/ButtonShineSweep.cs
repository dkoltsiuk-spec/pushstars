using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>One short sweep every three seconds, clipped by the parent viewport.</summary>
    public sealed class ButtonShineSweep : MonoBehaviour
    {
        public Image Stripes;
        [Min(.1f)] public float Interval = 3f;
        [Min(.05f)] public float Duration = .6f;
        private float _elapsed;

        private void OnEnable()
        {
            _elapsed = 0;
            if (Stripes != null) Stripes.enabled = false;
        }

        private void Update()
        {
            if (Stripes == null) return;
            _elapsed += Time.unscaledDeltaTime;
            float phase = Mathf.Repeat(_elapsed, Mathf.Max(.1f, Interval));
            float duration = Mathf.Min(Duration, Interval);
            Stripes.enabled = phase < duration;
            if (!Stripes.enabled) return;
            var viewport = (RectTransform)transform;
            float edge = (viewport.rect.width + Stripes.rectTransform.rect.width) * .5f;
            Stripes.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-edge, edge, phase / duration), 0);
        }

        private void OnDisable()
        {
            if (Stripes != null) Stripes.enabled = false;
        }
    }
}
