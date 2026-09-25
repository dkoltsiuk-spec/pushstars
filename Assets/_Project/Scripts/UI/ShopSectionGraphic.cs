using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ShopSectionGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            Shape(vh, r, Color.black);
            Shape(vh, new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), new Color32(5, 52, 245, 255));
        }

        private static void Shape(VertexHelper vh, Rect r, Color32 tint)
        {
            int start = vh.currentVertCount;
            vh.AddVert(r.center, tint, Vector2.zero);
            const int steps = 5;
            float radius = 6;
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = corner == 0 || corner == 3, top = corner >= 2;
                var center = new Vector2(right ? r.xMax - radius : r.xMin + radius, top ? r.yMax - radius : r.yMin + radius);
                for (int j = 0; j <= steps; j++)
                {
                    float angle = (-corner * 90 - j * 90f / steps) * Mathf.Deg2Rad;
                    var p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    p.x += Mathf.Lerp(-3, 3, (p.y - r.yMin) / r.height);
                    vh.AddVert(p, tint, Vector2.zero);
                }
            }
            int count = vh.currentVertCount - start - 1;
            for (int i = 0; i < count; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % count);
        }
    }
}
