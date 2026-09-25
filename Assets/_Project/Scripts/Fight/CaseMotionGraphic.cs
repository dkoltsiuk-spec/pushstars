using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Two ordered vector layers: grounded shadow/glow behind the case,
    /// tapered orbital ribbons and glints in front. No particle system or random state.</summary>
    public sealed class CaseMotionGraphic : MaskableGraphic
    {
        public bool Foreground;
        [System.NonSerialized] public float Clock, Burst, Orbit, Lift, Charge, Flash, Idle;
        [System.NonSerialized] public Color Tint = Color.white;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float size = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height);
            Vector2 center = rectTransform.rect.center;
            if (!Foreground)
            {
                Disc(vh, center + new Vector2(0, -.30f * size), new Vector2(.31f - Lift * .0012f, .045f) * size,
                    new Color(.035f, .015f, .09f, .26f - Lift * .002f), true);
                if (Charge > 0)
                    Disc(vh, center + Vector2.up * Lift, Vector2.one * size * (.34f + Charge * .12f),
                        new Color(Tint.r, Tint.g, Tint.b, Charge * .32f), true);
                if (Burst > 0)
                {
                    Disc(vh, center + Vector2.up * Lift, Vector2.one * size * (.35f + Burst * .23f),
                        new Color(Tint.r, Tint.g, Tint.b, Mathf.Sin(Burst * Mathf.PI) * .34f), true);
                    Arc(vh, center, new Vector2(.36f, .13f) * size * (1 + Burst), 0, Mathf.PI * 2,
                        size * .014f * (1 - Burst), new Color(1, 1, 1, (1 - Burst) * .6f), false);
                }
            }
            if (Orbit > 0)
            {
                for (int i = 0; i < 3; i++)
                {
                    float a = Clock * 12 + i * 2.0944f;
                    Arc(vh, center + Vector2.up * Lift, new Vector2(.52f, .22f) * size,
                        a, a + 1.8f, size * .038f * Orbit, new Color(1, .98f, .91f, Orbit), true);
                }
            }
            if (!Foreground) return;
            for (int i = 0; i < 12; i++)
            {
                float phase = Mathf.Repeat(Clock * (.45f + i * .014f) + i * .618f, 1);
                float angle = i * 2.39996f;
                float radius = size * (.40f + .15f * Mathf.Sin(i * 3.3f) + Burst * .24f);
                Vector2 p = center + new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * .83f + Lift * .3f);
                float alpha = Mathf.Sin(phase * Mathf.PI) * (.34f * Idle + .75f * Orbit + Charge * .7f);
                Color c = Color.Lerp(Tint, Color.white, .8f); c.a = Mathf.Clamp01(alpha);
                float s = size * (.009f + (i % 3) * .008f) * Mathf.Sin(phase * Mathf.PI);
                if (i % 3 == 0) Disc(vh, p, new Vector2(s * .4f, s), c, false);
                else Star(vh, p, s, c);
            }
            if (Flash > 0)
                Disc(vh, center + Vector2.up * Lift, Vector2.one * size * .9f, new Color(1, .96f, .78f, Flash), true);
        }

        private void Arc(VertexHelper vh, Vector2 center, Vector2 radius, float from, float to, float width, Color tint, bool split)
        {
            const int segments = 48;
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)segments, u = (i + 1f) / segments;
                float a = Mathf.Lerp(from, to, t), b = Mathf.Lerp(from, to, u);
                // The same orbital ellipse is occluded by the actual case, never painted over it twice.
                if (split && (Mathf.Sin((a + b) * .5f) < 0) != Foreground) continue;
                Vector2 p = center + Vector2.Scale(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), radius);
                Vector2 q = center + Vector2.Scale(new Vector2(Mathf.Cos(b), Mathf.Sin(b)), radius);
                Vector2 n1 = new Vector2(Mathf.Cos(a) / radius.x, Mathf.Sin(a) / radius.y).normalized;
                Vector2 n2 = new Vector2(Mathf.Cos(b) / radius.x, Mathf.Sin(b) / radius.y).normalized;
                float w1 = width * Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.PI)), .7f);
                float w2 = width * Mathf.Pow(Mathf.Max(0, Mathf.Sin(u * Mathf.PI)), .7f);
                Quad(vh, p - n1 * w1, q - n2 * w2, q + n2 * w2, p + n1 * w1, tint, tint);
            }
        }
        private static void Star(VertexHelper vh, Vector2 p, float radius, Color c)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4, b = (i + 1) * Mathf.PI / 4;
                int n = vh.currentVertCount;
                vh.AddVert(p, c, Vector2.zero);
                vh.AddVert(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * (i % 2 == 0 ? 1 : .28f), c, Vector2.zero);
                vh.AddVert(p + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius * (i % 2 == 0 ? .28f : 1), c, Vector2.zero);
                vh.AddTriangle(n, n + 1, n + 2);
            }
        }
        private static void Disc(VertexHelper vh, Vector2 p, Vector2 radius, Color c, bool fade)
        {
            int n = vh.currentVertCount;
            vh.AddVert(p, c, Vector2.zero);
            Color edge = c; if (fade) edge.a = 0;
            for (int i = 0; i <= 48; i++)
            {
                float a = i * Mathf.PI * 2 / 48;
                vh.AddVert(p + Vector2.Scale(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), radius), edge, Vector2.zero);
                if (i > 0) vh.AddTriangle(n, n + i, n + i + 1);
            }
        }
        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color bottom, Color top)
        {
            int n = vh.currentVertCount;
            vh.AddVert(a, bottom, Vector2.zero); vh.AddVert(b, bottom, Vector2.zero);
            vh.AddVert(c, top, Vector2.zero); vh.AddVert(d, top, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
    }
}

