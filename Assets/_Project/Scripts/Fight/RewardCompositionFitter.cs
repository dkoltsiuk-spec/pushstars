using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>Fits the authored 390x844 composition to the device safe area.</summary>
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class RewardCompositionFitter : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)]
        private float _widthBias;

        /// <summary>
        /// Blends the height-limited fit toward a width-limited fit. A value of zero keeps the
        /// original contain behavior; higher values make wide displays use more of their width
        /// without changing the authored 390x844 composition.
        /// </summary>
        public float WidthBias
        {
            get => _widthBias;
            set => _widthBias = Mathf.Clamp01(value);
        }

        private void OnEnable() => Fit();
        private void LateUpdate() => Fit();
        private void Fit()
        {
            var rect = (RectTransform)transform;
            if (!(rect.parent is RectTransform parent) || parent.rect.width <= 0 || parent.rect.height <= 0) return;
            float heightFit = parent.rect.height / 844f;
            float widthFit = parent.rect.width / 390f;
            float scale = Mathf.Min(widthFit, heightFit);
            if (widthFit > heightFit)
                scale = Mathf.Lerp(scale, widthFit, _widthBias);
            var wanted = new Vector3(scale, scale, 1);
            if (rect.localScale != wanted) rect.localScale = wanted;
        }
    }
}
