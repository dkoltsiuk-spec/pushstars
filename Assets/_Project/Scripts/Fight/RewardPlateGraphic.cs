using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Chunky Brawl-style plate: hard drop shadow, dark keyline, vertical gradient face
    /// and a thin top gloss. Resolution independent, so one component covers panels, cards,
    /// badges and progress bars without extra sprites.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RewardPlateGraphic : MaskableGraphic
    {
        public Color Top = new Color32(46, 58, 170, 255);
        public Color Bottom = new Color32(27, 33, 112, 255);
        public Color Border = new Color32(8, 9, 24, 255);
        public Color Shadow = new Color32(5, 6, 18, 200);
        public float Radius = 14;
        public float BorderWidth = 3;
        public float ShadowOffset = 5;
        public float Slant;
        [Range(0, 1)] public float Gloss = .06f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (r.width <= 0 || r.height <= 0) return;
            float alpha = color.a;
            if (ShadowOffset > 0 && Shadow.a > 0)
                Rounded(vh, new Rect(r.x, r.y - ShadowOffset, r.width, r.height), Radius, Tint(Shadow, alpha), Tint(Shadow, alpha));
            if (BorderWidth > 0 && Border.a > 0) Rounded(vh, r, Radius, Tint(Border, alpha), Tint(Border, alpha));
            var face = Inset(r, BorderWidth);
            float faceRadius = Mathf.Max(0, Radius - BorderWidth);
            Rounded(vh, face, faceRadius, Tint(Top, alpha), Tint(Bottom, alpha));
            if (Gloss > 0)
            {
                // A soft band across the upper face, the "plastic" highlight of mobile UI plates.
                var band = Inset(face, Mathf.Min(face.height * .12f, 3));
                band.yMin = band.yMax - band.height * .42f;
                var shine = new Color(1, 1, 1, Gloss * alpha);
                Rounded(vh, band, Mathf.Max(0, faceRadius - 2), shine, new Color(1, 1, 1, 0));
            }
        }

        private Color Tint(Color c, float alpha) { c.a *= alpha; return c; }

        private static Rect Inset(Rect r, float amount)
        {
            amount = Mathf.Min(amount, r.width * .5f, r.height * .5f);
            return new Rect(r.x + amount, r.y + amount, r.width - amount * 2, r.height - amount * 2);
        }

        /// <summary>A rounded rectangle as a centre fan; colour runs top → bottom.</summary>
        private void Rounded(VertexHelper vh, Rect r, float radius, Color top, Color bottom)
        {
            if (r.width <= 0 || r.height <= 0) return;
            radius = Mathf.Clamp(radius, 0, Mathf.Min(r.width, r.height) * .5f);
            const int segments = 8;
            int start = vh.currentVertCount;
            vh.AddVert(r.center, Color.Lerp(bottom, top, .5f), Vector2.zero);
            Vector2[] corners =
            {
                new Vector2(r.xMax - radius, r.yMax - radius), new Vector2(r.xMin + radius, r.yMax - radius),
                new Vector2(r.xMin + radius, r.yMin + radius), new Vector2(r.xMax - radius, r.yMin + radius)
            };
            int count = 0;
            for (int c = 0; c < 4; c++)
                for (int s = 0; s <= segments; s++)
                {
                    float angle = (c + s / (float)segments) * Mathf.PI * .5f;
                    var p = corners[c] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    p.x += Slant * (Mathf.InverseLerp(r.yMin, r.yMax, p.y) - .5f);
                    vh.AddVert(p, Color.Lerp(bottom, top, Mathf.InverseLerp(r.yMin, r.yMax, p.y)), Vector2.zero);
                    count++;
                }
            for (int i = 0; i < count; i++)
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % count);
        }
    }
}
