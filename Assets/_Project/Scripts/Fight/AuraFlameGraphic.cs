using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Resolution-independent violet flame with a luminous lilac core.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AuraFlameGraphic : MaskableGraphic
    {
        private static readonly Vector2[] Flame = BuildFlame();
        private static readonly int[] Triangles = Triangulate(Flame);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float s = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .91f;
            Vector2 c = rectTransform.rect.center;
            Draw(vh, c + Vector2.down * s * .035f, s * 1.05f, new Color(.12f, .035f, .28f), new Color(.22f, .08f, .40f));
            Draw(vh, c, s, new Color(.79f, .60f, 1), new Color(.98f, .82f, 1));
            Draw(vh, c, s * .91f, new Color(.45f, .15f, .88f), new Color(.75f, .25f, .96f));
            Draw(vh, c + new Vector2(.012f, -.015f) * s, s * .76f, new Color(.62f, .30f, 1), new Color(.95f, .50f, 1));
            Draw(vh, c + new Vector2(.006f, -.015f) * s, s * .64f, new Color(.48f, .18f, .84f), new Color(.66f, .21f, .95f));
            Draw(vh, c + new Vector2(.02f, -.20f) * s, s * .38f, new Color(.91f, .71f, 1), new Color(1, .88f, 1));
            Draw(vh, c + new Vector2(.018f, -.225f) * s, s * .23f, Color.white, new Color(1, .86f, 1));
        }
        private static void Draw(VertexHelper vh, Vector2 center, float size, Color bottom, Color top)
        {
            int n = vh.currentVertCount;
            foreach (var p in Flame)
                vh.AddVert(center + p * size, Color.Lerp(bottom, top, Mathf.Clamp01(p.y + .5f)), Vector2.zero);
            for (int i = 0; i < Triangles.Length; i += 3)
                vh.AddTriangle(n + Triangles[i], n + Triangles[i + 1], n + Triangles[i + 2]);
        }
        private static Vector2[] BuildFlame()
        {
            var p = new List<Vector2> { new Vector2(0, -.45f) };
            Curve(p, new Vector2(.35f, -.44f), new Vector2(.43f, -.17f), new Vector2(.32f, .09f));
            Curve(p, new Vector2(.30f, -.04f), new Vector2(.16f, -.02f), new Vector2(.20f, .13f));
            Curve(p, new Vector2(.32f, .31f), new Vector2(.10f, .30f), new Vector2(.16f, .50f));
            Curve(p, new Vector2(-.12f, .36f), new Vector2(-.14f, .19f), new Vector2(-.05f, .09f));
            Curve(p, new Vector2(-.15f, .04f), new Vector2(-.25f, .14f), new Vector2(-.23f, .23f));
            Curve(p, new Vector2(-.35f, .12f), new Vector2(-.23f, .01f), new Vector2(-.33f, -.06f));
            Curve(p, new Vector2(-.49f, -.24f), new Vector2(-.28f, -.46f), new Vector2(0, -.45f));
            p.RemoveAt(p.Count - 1);
            return p.ToArray();
        }
        private static void Curve(List<Vector2> p, Vector2 a, Vector2 b, Vector2 end)
        {
            Vector2 start = p[p.Count - 1];
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f, u = 1 - t;
                p.Add(u * u * u * start + 3 * u * u * t * a + 3 * u * t * t * b + t * t * t * end);
            }
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static int[] Triangulate(Vector2[] points)
        {
            var remaining = new List<int>(); var output = new List<int>();
            float area = 0;
            for (int i = 0; i < points.Length; i++) area += Cross(points[i], points[(i + 1) % points.Length]);
            for (int i = 0; i < points.Length; i++) remaining.Add(area > 0 ? i : points.Length - 1 - i);
            int guard = points.Length * points.Length;
            while (remaining.Count > 2 && guard-- > 0)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (Cross(points[b] - points[a], points[c] - points[b]) <= 0) continue;
                    bool inside = false;
                    foreach (int j in remaining)
                    {
                        if (j == a || j == b || j == c) continue;
                        if (Cross(points[b] - points[a], points[j] - points[a]) >= 0 &&
                            Cross(points[c] - points[b], points[j] - points[b]) >= 0 &&
                            Cross(points[a] - points[c], points[j] - points[c]) >= 0) { inside = true; break; }
                    }
                    if (inside) continue;
                    output.Add(a); output.Add(b); output.Add(c); remaining.RemoveAt(i); found = true; break;
                }
                if (!found) break;
            }
            return output.ToArray();
        }
    }
}
