using UnityEngine;
namespace PushStars.UI
{
    /// <summary>Fits the composition above the shared navigation, inside the safe area.</summary>
    [ExecuteAlways]
    public sealed class LeagueLayout : MonoBehaviour
    {
        public RectTransform Art;
        public RectTransform Background;
        private readonly Vector3[] _corners = new Vector3[4];
        public void Fit()
        {
            if (Art == null) return;
            var rect = ((RectTransform)transform).rect;
            float scale = Mathf.Max(.1f, Mathf.Min(rect.width / 390f, (rect.height - 86f) / 700f));
            Art.localScale = Vector3.one * scale;
            Art.anchoredPosition = new Vector2(0, -Mathf.Max(0, (rect.height - 86f - 700f * scale) * .3f));
            if (Background != null && GetComponentInParent<Canvas>() is Canvas canvas)
            {
                canvas.rootCanvas.GetComponent<RectTransform>().GetWorldCorners(_corners);
                Vector3 min = transform.InverseTransformPoint(_corners[0]);
                Vector3 max = transform.InverseTransformPoint(_corners[2]);
                Background.sizeDelta = max - min;
                Background.localPosition = (min + max) * .5f;
            }
        }
        private void OnEnable() => Fit();
        private void LateUpdate() => Fit();
    }
}
