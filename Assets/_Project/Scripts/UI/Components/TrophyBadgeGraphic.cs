using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Trophy tag contours traced in the supplied 919 x 577 reference coordinates.
    /// Curves are tessellated directly into UI geometry; no capsule/ellipse sprites.
    /// The reference artwork occupies (27,90)–(783,368).
    /// </summary>
    [AddComponentMenu("Push Stars/UI/Trophy Badge Graphic"), RequireComponent(typeof(CanvasRenderer))]
    public sealed class TrophyBadgeGraphic : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] private float _progress = 0.79f;
        private readonly List<Vector2> _path = new List<Vector2>(160);

        public float Progress
        {
            get => _progress;
            set { _progress = Mathf.Clamp01(value); SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            // Flat upper/lower edges and a slanted right shoulder.
            Begin(254, 137); Line(696, 137);
            Curve(715, 137, 724, 151, 719, 171);
            Line(679, 344); Curve(676, 360, 667, 368, 651, 368);
            Line(242, 368); Curve(220, 368, 204, 351, 204, 330);
            Line(204, 176); Curve(204, 151, 225, 137, 254, 137);
            Paint(vh, Color.black);

            Bar(1f); Paint(vh, new Color32(31, 36, 43, 255));
            if (_progress > 0f)
            {
                Bar(_progress); Paint(vh, new Color32(239, 139, 0, 255));
            }

            // Asymmetric red info tab, including the outlined, leaning gold glyph.
            Begin(648, 103); Line(753, 90);
            Curve(772, 87, 784, 100, 783, 119); Line(780, 185);
            Curve(780, 201, 769, 210, 753, 212); Line(660, 224);
            Curve(641, 226, 630, 218, 628, 200); Line(621, 135);
            Curve(619, 117, 630, 106, 648, 103); Paint(vh, Color.red);

            Begin(704, 118); Line(714, 117); Curve(724, 116, 727, 122, 725, 128);
            Line(723, 138); Line(720, 143); Line(714, 183);
            Curve(713, 191, 708, 192, 699, 193); Line(693, 193);
            Curve(686, 193, 686, 188, 687, 183); Line(692, 149);
            Curve(692, 144, 694, 141, 696, 139); Line(695, 132);
            Curve(693, 123, 695, 120, 704, 118); Paint(vh, Color.black);

            Begin(703, 125); Line(718, 123); Line(716, 137); Line(701, 139);
            Paint(vh, new Color32(255, 215, 0, 255), new Color32(255, 153, 0, 255));
            Begin(700, 145); Line(716, 143); Line(709, 184);
            Curve(705, 187, 699, 188, 693, 187); Line(699, 150);
            Paint(vh, new Color32(255, 224, 0, 255), new Color32(255, 157, 0, 255));
        }

        private void Bar(float amount)
        {
            // Interpolate the end of the shape, preserving the reference's rounded slanted cap.
            float capAmount = Mathf.Max(0.25f, amount);
            float end = Mathf.Lerp(273f, 591f, capAmount);
            float top = Mathf.Lerp(267f, 237f, capAmount);
            Begin(297, 259); Line(end - 31, top);
            Curve(end - 10, top - 2, end + 5, top + 13, end, top + 31);
            Line(end - 9, 291); Curve(end - 12, 305, end - 20, 310, end - 34, 310);
            Line(299, 315); Curve(280, 316, 272, 306, 273, 289);
            Curve(274, 274, 281, 261, 297, 259);
            // Near zero the entire short cap contracts; progress never has a minimum fill.
            if (amount < capAmount)
                for (int i = 0; i < _path.Count; i++)
                    _path[i] = new Vector2(273f + (_path[i].x - 273f) * amount / capAmount, _path[i].y);
        }

        private void Begin(float x, float y) { _path.Clear(); Line(x, y); }
        private void Line(float x, float y) => _path.Add(new Vector2(x, y));
        private void Curve(float x1, float y1, float x2, float y2, float x3, float y3)
        {
            Vector2 a = _path[_path.Count - 1], b = new Vector2(x1, y1);
            Vector2 c = new Vector2(x2, y2), d = new Vector2(x3, y3);
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f, u = 1f - t;
                _path.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d);
            }
        }

        private void Paint(VertexHelper vh, Color left, Color? right = null)
        {
            if ((_path[0] - _path[_path.Count - 1]).sqrMagnitude < 0.001f)
                _path.RemoveAt(_path.Count - 1);
            Rect rect = rectTransform.rect;
            Vector2 center = Vector2.zero;
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (Vector2 p in _path)
            { center += p; minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); }
            center /= _path.Count;
            int start = vh.currentVertCount;
            Add(center, 1f);
            foreach (Vector2 p in _path) Add(p, 1f);
            // A one-screen-pixel transparent fringe smooths the contour at phone resolution.
            float screenScale = canvas != null ? canvas.scaleFactor *
                Mathf.Abs(rectTransform.lossyScale.x / canvas.transform.lossyScale.x) : 1f;
            float feather = 756f / Mathf.Max(1f, rect.width * screenScale);
            for (int i = 0; i < _path.Count; i++)
            {
                Vector2 prev = _path[(i + _path.Count - 1) % _path.Count];
                Vector2 next = _path[(i + 1) % _path.Count];
                Vector2 tangent = (next - prev).normalized;
                Add(_path[i] + new Vector2(tangent.y, -tangent.x) * feather, 0f);
            }
            for (int i = 0; i < _path.Count; i++)
            {
                int a = start + 1 + i, b = start + 1 + (i + 1) % _path.Count;
                vh.AddTriangle(start, a, b);
                vh.AddTriangle(a, a + _path.Count, b + _path.Count);
                vh.AddTriangle(a, b + _path.Count, b);
            }

            void Add(Vector2 p, float alpha)
            {
                Color tint = Color.Lerp(left, right ?? left, Mathf.InverseLerp(minX, maxX, p.x)) * color;
                tint.a *= alpha;
                vh.AddVert(new Vector3(rect.xMin + (p.x - 27f) / 756f * rect.width,
                    rect.yMax - (p.y - 90f) / 278f * rect.height, 0f), tint, Vector2.zero);
            }
        }
    }
}
