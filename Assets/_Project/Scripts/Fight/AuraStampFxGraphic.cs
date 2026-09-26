using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Procedural impact layer behind the "+N AURA" stamp: glow, shockwaves, sparks and
    /// rising embers, plus the black-and-white glitch bars of the skull reveal.
    /// Deterministic — never touches UnityEngine.Random.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AuraStampFxGraphic : MaskableGraphic
    {
        private const int SparkCount = 32, EmberCount = 26, BarCount = 9;
        private static readonly Color Core = new Color(.96f, .9f, 1f), Violet = new Color(.62f, .3f, 1f);
        private float _sinceImpact = -1, _idle, _mono, _glitch;

        /// <param name="sinceImpact">Seconds since the stamp landed; negative before it.</param>
        /// <param name="idle">Free-running clock for the looping glow, embers and glitch steps.</param>
        /// <param name="mono">0 = violet, 1 = fully black and white.</param>
        /// <param name="glitch">Strength of the horizontal glitch bars.</param>
        public void Sample(float sinceImpact, float idle, float mono = 0, float glitch = 0)
        {
            _sinceImpact = sinceImpact; _idle = idle; _mono = mono; _glitch = glitch;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            float d = _sinceImpact;
            if (d < 0) return;
            Vector2 c = rectTransform.rect.center;

            // Settles low so the keyline keeps its contrast against the background.
            float glow = Mathf.Lerp(.24f + .05f * Mathf.Sin(_idle * 2.1f), .95f, Mathf.Exp(-d * 3.5f));
            Fan(vh, c, 250 + 40 * Mathf.Exp(-d * 5), Tint(Violet, glow * .55f), Tint(Violet, 0), 48);
            Fan(vh, c, 120, Tint(new Color(.85f, .6f, 1), glow * .35f), Tint(Violet, 0), 32);

            for (int ring = 0; ring < 2; ring++)
            {
                float r = Mathf.Clamp01((d - ring * .08f) / .5f);
                if (r <= 0 || r >= 1) continue;
                float ease = 1 - Mathf.Pow(1 - r, 3);
                Ring(vh, c, Mathf.Lerp(70, 540, ease), Mathf.Lerp(ring == 0 ? 30 : 16, 2, r),
                    Tint(ring == 0 ? Core : Violet, Mathf.Pow(1 - r, 1.5f) * (ring == 0 ? .9f : .7f)));
            }

            for (int i = 0; i < SparkCount; i++)
            {
                float life = .35f + Hash(i, 1) * .45f;
                if (d >= life) continue;
                float angle = Hash(i, 2) * Mathf.PI * 2, speed = 700 + Hash(i, 3) * 900, drag = 6;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float distance = 50 + speed * (1 - Mathf.Exp(-drag * d)) / drag;
                float velocity = speed * Mathf.Exp(-drag * d);
                var head = c + dir * distance;
                Line(vh, head - dir * (10 + velocity * .035f), head, 3.2f,
                    Tint(Hash(i, 4) > .45f ? Core : Violet, 1 - d / life));
            }

            float embers = Mathf.Clamp01((d - .15f) / .6f);
            for (int i = 0; i < EmberCount && embers > 0; i++)
            {
                float period = 3.5f + Hash(i, 5) * 2.5f;
                float phase = Mathf.Repeat(_idle / period + Hash(i, 6), 1);
                var p = c + new Vector2((Hash(i, 7) - .5f) * 440 + Mathf.Sin(_idle * 1.3f + i) * 14, -320 + phase * 720);
                float size = 2.5f + Hash(i, 8) * 3.5f;
                float alpha = Mathf.Sin(phase * Mathf.PI) * .85f * embers;
                Fan(vh, p, size * 2.6f, Tint(Violet, alpha * .35f), Tint(Violet, 0), 8);
                Fan(vh, p, size, Tint(Core, alpha), Tint(Core, alpha * .2f), 8);
            }

            // Glitch: hard black and white bars that jump every ~40 ms.
            if (_glitch > 0)
            {
                int step = Mathf.FloorToInt(_idle * 25);
                for (int i = 0; i < BarCount; i++)
                {
                    if (Hash(step * 31 + i, 40) > .75f) continue;
                    float y = (Hash(step * 31 + i, 41) - .5f) * 900, h = 3 + Hash(step * 31 + i, 42) * 24;
                    float x = (Hash(step * 31 + i, 43) - .5f) * 500, w = 150 + Hash(step * 31 + i, 44) * 850;
                    var bar = Hash(step * 31 + i, 45) > .5f ? Color.white : Color.black;
                    Rect(vh, new Vector2(c.x + x - w * .5f, c.y + y), new Vector2(w, h), Tint(bar, _glitch * .85f));
                }
            }
        }

        private Color Tint(Color color, float alpha)
        {
            float grey = color.r * .3f + color.g * .59f + color.b * .11f;
            color = Color.Lerp(color, new Color(grey, grey, grey), _mono);
            color.a = Mathf.Clamp01(alpha) * this.color.a;
            return color;
        }

        private static void Rect(VertexHelper vh, Vector2 min, Vector2 size, Color color)
        {
            int i = vh.currentVertCount;
            vh.AddVert(min, color, Vector2.zero);
            vh.AddVert(min + new Vector2(size.x, 0), color, Vector2.zero);
            vh.AddVert(min + size, color, Vector2.zero);
            vh.AddVert(min + new Vector2(0, size.y), color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            var dir = b - a;
            if (dir.sqrMagnitude < .01f) return;
            var n = new Vector2(-dir.y, dir.x).normalized * width * .5f;
            var clear = color; clear.a = 0;
            int i = vh.currentVertCount;
            vh.AddVert(a + n * 2, clear, Vector2.zero); vh.AddVert(a + n * .5f, color, Vector2.zero);
            vh.AddVert(a - n * .5f, color, Vector2.zero); vh.AddVert(a - n * 2, clear, Vector2.zero);
            vh.AddVert(b + n * 2, clear, Vector2.zero); vh.AddVert(b + n * .5f, color, Vector2.zero);
            vh.AddVert(b - n * .5f, color, Vector2.zero); vh.AddVert(b - n * 2, clear, Vector2.zero);
            for (int k = 0; k < 3; k++)
            {
                vh.AddTriangle(i + k, i + k + 1, i + k + 5);
                vh.AddTriangle(i + k, i + k + 5, i + k + 4);
            }
        }

        private static void Ring(VertexHelper vh, Vector2 c, float radius, float thickness, Color color)
        {
            const int steps = 72;
            var clear = color; clear.a = 0;
            int start = vh.currentVertCount;
            for (int s = 0; s <= steps; s++)
            {
                float angle = s * Mathf.PI * 2 / steps;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(c + dir * (radius - thickness), clear, Vector2.zero);
                vh.AddVert(c + dir * radius, color, Vector2.zero);
                vh.AddVert(c + dir * (radius + thickness * .5f), clear, Vector2.zero);
                if (s == 0) continue;
                int p = start + (s - 1) * 3, q = start + s * 3;
                vh.AddTriangle(p, p + 1, q + 1); vh.AddTriangle(p, q + 1, q);
                vh.AddTriangle(p + 1, p + 2, q + 2); vh.AddTriangle(p + 1, q + 2, q + 1);
            }
        }

        private static void Fan(VertexHelper vh, Vector2 c, float radius, Color inner, Color outer, int steps)
        {
            int start = vh.currentVertCount;
            vh.AddVert(c, inner, Vector2.zero);
            for (int s = 0; s <= steps; s++)
            {
                float angle = s * Mathf.PI * 2 / steps;
                vh.AddVert(c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, outer, Vector2.zero);
                if (s > 0) vh.AddTriangle(start, start + s, start + s + 1);
            }
        }

        private static float Hash(int index, int salt)
        {
            uint h = (uint)(index * 374761393 + salt * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
        }
    }
}
