using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Clean game button with a fixed-width edge and shadow at every aspect ratio.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DuelActionPlate : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            var face = new Rect(r.x + 4, r.y + 5, r.width - 8, r.height - 6);
            var shadow = face; shadow.position += new Vector2(1, -4);
            var ink = new Color(0, 0, 0, color.a);
            Rounded(vh, shadow, 11, ink);
            Rounded(vh, face, 11, ink);
            Rounded(vh, new Rect(face.x + 1.5f, face.y + 1.5f, face.width - 3, face.height - 3), 9.5f, color);
        }

        private static void Rounded(VertexHelper vh, Rect r, float radius, Color tint)
        {
            int first = vh.currentVertCount;
            vh.AddVert(r.center, tint, Vector2.zero);
            const int steps = 16;
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = corner == 0 || corner == 3, top = corner >= 2;
                var center = new Vector2(right ? r.xMax - radius : r.xMin + radius,
                    top ? r.yMax - radius : r.yMin + radius);
                for (int j = 0; j <= steps; j++)
                {
                    float angle = (-corner * 90 - j * 90f / steps) * Mathf.Deg2Rad;
                    var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    point.x += Mathf.Lerp(-2, 2, (point.y - r.yMin) / r.height);
                    vh.AddVert(point, tint, Vector2.zero);
                }
            }
            int count = vh.currentVertCount - first - 1;
            for (int i = 0; i < count; i++)
                vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % count);
            // A subpixel fringe keeps the tilted edges smooth without blurring the fill.
            int fringe = vh.currentVertCount;
            var clear = tint; clear.a = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = corner == 0 || corner == 3, top = corner >= 2;
                var center = new Vector2(right ? r.xMax - radius : r.xMin + radius,
                    top ? r.yMax - radius : r.yMin + radius);
                for (int j = 0; j <= steps; j++)
                {
                    float angle = (-corner * 90 - j * 90f / steps) * Mathf.Deg2Rad;
                    var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius + .6f);
                    point.x += Mathf.LerpUnclamped(-2, 2, (point.y - r.yMin) / r.height);
                    vh.AddVert(point, clear, Vector2.zero);
                }
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                vh.AddTriangle(first + 1 + i, fringe + i, fringe + next);
                vh.AddTriangle(first + 1 + i, fringe + next, first + 1 + next);
            }
        }
    }
}
