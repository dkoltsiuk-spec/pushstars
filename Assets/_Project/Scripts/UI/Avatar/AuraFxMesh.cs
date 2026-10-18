using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// A UI mesh rebuilt every frame from simple marks: tapered strokes (swooshes, streaks,
    /// lightning) and four-point sparkles. Each stroke is a strip five vertices wide — feathered
    /// edge, colour, hot core, colour, feathered edge — so marks read as crisp cartoon energy with a
    /// white centre. Drawn with <c>Resources/AuraFxGlow.shader</c>, which adds light.
    /// Coordinates are in this graphic's local space.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AuraFxMesh : MaskableGraphic
    {
        public enum Profile { Crescent, Taper, Even, Blade }

        private static readonly float[] Rows = { -1f, -.72f, 0f, .72f, 1f };

        private readonly List<UIVertex> _vertices = new List<UIVertex>(1024);
        private readonly List<int> _indices = new List<int>(2048);
        private readonly List<Vector2> _normals = new List<Vector2>(64);

        public void Clear()
        {
            _vertices.Clear();
            _indices.Clear();
        }

        public void Commit() => SetVerticesDirty();

        /// <summary>Width along a stroke at <paramref name="t"/> (0 = tail, 1 = head).</summary>
        public static float Width(Profile profile, float t)
        {
            switch (profile)
            {
                // Thin tail, fat body just behind the head, rounded tip: a swoosh.
                case Profile.Crescent: return Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(t, 1.6f))), .7f);
                case Profile.Taper: return t;
                // Straight edges to a point at both ends, widest a quarter in: a cut, not a brush.
                case Profile.Blade: return t < .25f ? t / .25f : (1f - t) / .75f;
                default: return 1f;
            }
        }

        /// <summary>A stroke through <paramref name="points"/> (tail first). <paramref name="from"/>
        /// and <paramref name="to"/> are where this run sits on the whole stroke, so a stroke split
        /// between the back and front layers keeps one width profile.</summary>
        public void Stroke(IReadOnlyList<Vector2> points, int start, int count, float from, float to,
            float width, Profile profile, Color core, Color edge, float alpha)
        {
            if (count < 2 || alpha <= .001f) return;
            _normals.Clear();
            for (int i = 0; i < count; i++)
            {
                Vector2 prev = points[start + Mathf.Max(0, i - 1)], next = points[start + Mathf.Min(count - 1, i + 1)];
                Vector2 tangent = next - prev;
                _normals.Add(tangent.sqrMagnitude > 1e-8f ? new Vector2(-tangent.y, tangent.x).normalized : Vector2.up);
            }

            int baseIndex = _vertices.Count;
            Color edgeClear = edge; edgeClear.a = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = Mathf.Lerp(from, to, count == 1 ? 0f : (float)i / (count - 1));
                float w = width * Width(profile, t);
                for (int r = 0; r < Rows.Length; r++)
                {
                    Color c = r == 0 || r == Rows.Length - 1 ? edgeClear : r == 2 ? core : edge;
                    c.a *= alpha;
                    AddVertex(points[start + i] + _normals[i] * (Rows[r] * w), c);
                }
            }
            for (int i = 0; i < count - 1; i++)
            for (int r = 0; r < Rows.Length - 1; r++)
            {
                int a = baseIndex + i * Rows.Length + r, b = a + Rows.Length;
                _indices.Add(a); _indices.Add(b); _indices.Add(a + 1);
                _indices.Add(a + 1); _indices.Add(b); _indices.Add(b + 1);
            }
        }

        /// <summary>A four-point star: two thin diamonds crossed, hot centre, clear tips.</summary>
        public void Sparkle(Vector2 centre, float size, float angle, Color core, Color edge, float alpha)
        {
            if (size <= .01f || alpha <= .001f) return;
            Color tip = edge; tip.a = 0f;
            Color mid = core; mid.a *= alpha;
            for (int arm = 0; arm < 2; arm++)
            {
                float a = angle + arm * Mathf.PI * .5f;
                Vector2 along = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size;
                Vector2 across = new Vector2(-along.y, along.x) * .18f;
                int i = _vertices.Count;
                AddVertex(centre, mid);
                AddVertex(centre + along, tip);
                AddVertex(centre + across, tip);
                AddVertex(centre - along, tip);
                AddVertex(centre - across, tip);
                _indices.Add(i); _indices.Add(i + 1); _indices.Add(i + 2);
                _indices.Add(i); _indices.Add(i + 2); _indices.Add(i + 3);
                _indices.Add(i); _indices.Add(i + 3); _indices.Add(i + 4);
                _indices.Add(i); _indices.Add(i + 4); _indices.Add(i + 1);
            }
        }

        private void AddVertex(Vector2 position, Color colour)
        {
            var v = UIVertex.simpleVert;
            v.position = position;
            v.color = colour;
            _vertices.Add(v);
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_vertices.Count == 0) return;
            vh.AddUIVertexStream(_vertices, _indices);
        }
    }
}
