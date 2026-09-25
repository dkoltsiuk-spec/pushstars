using UnityEngine;
using UnityEngine.UI;
namespace PushStars.UI
{
    /// <summary>Sizes the whole scrollable league page above the shared navigation.</summary>
    [ExecuteAlways]
    public sealed class LeagueLayout : MonoBehaviour
    {
        public RectTransform Art;
        public RectTransform Background;
        public ScrollRect PageScroll;
        public RectTransform Rows;
        private readonly Vector3[] _corners = new Vector3[4];
        public void Fit()
        {
            if (Art == null) return;
            var rect = ((RectTransform)transform).rect;
            float scale = Mathf.Max(.1f, rect.width / 390f);
            Art.localScale = Vector3.one * scale;
            if (PageScroll != null && PageScroll.viewport != null)
            {
                var viewport = PageScroll.viewport;
                viewport.offsetMin = new Vector2(0, 86f);
                viewport.offsetMax = Vector2.zero;
                float bottom = 0;
                if (Rows != null)
                {
                    foreach (RectTransform row in Rows)
                        if (row.gameObject.activeSelf)
                            bottom = Mathf.Max(bottom, -row.anchoredPosition.y + row.rect.height);
                    Rows.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bottom);
                }
                float height = Mathf.Max(700f, Rows == null ? 0 : -Rows.anchoredPosition.y + bottom + 16f);
                Art.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                // Preserve the drag offset; only clamp when the page or device bounds change.
                float maxOffset = Mathf.Max(0, height * scale - viewport.rect.height);
                Art.anchoredPosition = new Vector2(0, Mathf.Clamp(Art.anchoredPosition.y, 0, maxOffset));
            }
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
