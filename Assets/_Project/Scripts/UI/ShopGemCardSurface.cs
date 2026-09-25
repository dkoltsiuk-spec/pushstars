using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>One aligned card, border and price band without independently skewed sprites.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ShopGemCardSurface : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float unit = r.width / 106f;
            Rounded(vh, r, 8 * unit, false, new Color32(4, 14, 8, 255), new Color32(4, 14, 8, 255));
            var inner = new Rect(r.x + 2 * unit, r.y + 5 * unit, r.width - 4 * unit, r.height - 7 * unit);
            Rounded(vh, inner, 6 * unit, false, new Color32(183, 255, 0, 255), new Color32(76, 192, 29, 255));
            inner.height = 24 * unit;
            Rounded(vh, inner, 6 * unit, true, new Color32(45, 115, 28, 255), new Color32(45, 115, 28, 255));
        }

        private static void Rounded(VertexHelper vh, Rect r, float radius, bool squareTop, Color32 centerTint, Color32 edgeTint)
        {
            int first = vh.currentVertCount;
            vh.AddVert(r.center, centerTint, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = corner == 0 || corner == 3, top = corner >= 2;
                float cr = squareTop && top ? 0 : radius;
                var center = new Vector2(right ? r.xMax - cr : r.xMin + cr, top ? r.yMax - cr : r.yMin + cr);
                for (int j = 0; j <= 12; j++)
                {
                    float angle = (-corner * 90 - j * 90f / 12) * Mathf.Deg2Rad;
                    vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cr, edgeTint, Vector2.zero);
                }
            }
            int count = vh.currentVertCount - first - 1;
            for (int i = 0; i < count; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % count);
        }
    }
}
