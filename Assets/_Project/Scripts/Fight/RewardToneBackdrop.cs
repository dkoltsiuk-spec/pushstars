using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>One-tone reward background with depth, drawn as a mesh: a saturated vertical
    /// gradient, a spotlight behind the hero, a lit floor under their feet, optional faint
    /// diagonal bands, and a vignette that darkens the corners. Positions are normalised
    /// (0 = left/bottom, 1 = right/top) so it holds on every aspect.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RewardToneBackdrop : MaskableGraphic
    {
        [Header("Gradient")]
        public Color Top = new Color32(20, 48, 160, 255);
        public Color Middle = new Color32(42, 104, 240, 255);
        public Color Bottom = new Color32(16, 38, 138, 255);
        [Range(0, 1)] public float MiddleAt = .52f;
        [Header("Spotlight behind the hero")]
        public Color Light = new Color32(130, 196, 255, 170);
        public Vector2 LightCenter = new Vector2(.5f, .5f);
        public float LightRadius = .85f;
        [Header("Floor glow under the feet")]
        public Color Floor = new Color32(110, 175, 255, 110);
        public Vector2 FloorCenter = new Vector2(.5f, .19f);
        public Vector2 FloorSize = new Vector2(1.1f, .12f);
        [Header("Diagonal bands")]
        [Range(0, .2f)] public float BandAlpha;
        public float BandWidth = .16f;
        public float BandAngle = 24;
        [Header("Vignette")]
        public Color Vignette = new Color32(6, 14, 70, 170);
        [Range(.2f, 1)] public float VignetteStart = .55f;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (r.width <= 0 || r.height <= 0) return;
            Gradient(vh, r);
            if (BandAlpha > 0) Bands(vh, r);
            float unit = r.width;
            Glow(vh, Point(r, LightCenter), new Vector2(LightRadius * unit, LightRadius * unit), Light);
            Glow(vh, Point(r, FloorCenter), new Vector2(FloorSize.x * unit * .5f, FloorSize.y * r.height * .5f), Floor);
            VignetteRing(vh, r);
        }

        private static Vector2 Point(Rect r, Vector2 normalised) =>
            new Vector2(r.xMin + r.width * normalised.x, r.yMin + r.height * normalised.y);

        private void Gradient(VertexHelper vh, Rect r)
        {
            float mid = Mathf.Lerp(r.yMin, r.yMax, MiddleAt);
            Quad(vh, r.xMin, r.yMin, r.xMax, mid, Bottom, Middle);
            Quad(vh, r.xMin, mid, r.xMax, r.yMax, Middle, Top);
        }

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color bottom, Color top)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), bottom, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), bottom, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), top, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), top, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        /// <summary>Wide faint bands at an angle, fading out toward the bottom.</summary>
        private void Bands(VertexHelper vh, Rect r)
        {
            float width = BandWidth * r.width;
            float slant = Mathf.Tan(BandAngle * Mathf.Deg2Rad) * r.height;
            var top = new Color(1, 1, 1, BandAlpha);
            var bottom = new Color(1, 1, 1, 0);
            for (float x = r.xMin - slant - width; x < r.xMax + width; x += width * 2)
            {
                int i = vh.currentVertCount;
                vh.AddVert(new Vector3(x, r.yMin), bottom, Vector2.zero);
                vh.AddVert(new Vector3(x + width, r.yMin), bottom, Vector2.zero);
                vh.AddVert(new Vector3(x + width + slant, r.yMax), top, Vector2.zero);
                vh.AddVert(new Vector3(x + slant, r.yMax), top, Vector2.zero);
                vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
            }
        }

        /// <summary>Soft elliptical light: full colour at the centre, a smooth shoulder, gone at the edge.</summary>
        private static void Glow(VertexHelper vh, Vector2 center, Vector2 radius, Color tint)
        {
            if (tint.a <= 0) return;
            const int sides = 72;
            float[] rings = { 0, .35f, .7f, 1 };
            float[] alpha = { 1, .62f, .2f, 0 };
            int start = vh.currentVertCount;
            for (int ring = 0; ring < rings.Length; ring++)
            {
                var c = tint; c.a *= alpha[ring];
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2 / sides;
                    vh.AddVert(center + new Vector2(Mathf.Cos(a) * radius.x, Mathf.Sin(a) * radius.y) * rings[ring], c, Vector2.zero);
                }
            }
            for (int ring = 0; ring < rings.Length - 1; ring++)
                for (int s = 0; s < sides; s++)
                {
                    int a = start + ring * sides + s, b = start + ring * sides + (s + 1) % sides;
                    int c = a + sides, d = b + sides;
                    vh.AddTriangle(a, c, d); vh.AddTriangle(a, d, b);
                }
        }

        /// <summary>Transparent inside an ellipse fitted to the screen, darkening outward past the corners.</summary>
        private void VignetteRing(VertexHelper vh, Rect r)
        {
            if (Vignette.a <= 0) return;
            const int sides = 72;
            var center = r.center;
            var half = new Vector2(r.width, r.height) * .5f;
            var clear = Vignette; clear.a = 0;
            int start = vh.currentVertCount;
            for (int s = 0; s < sides; s++)
            {
                float a = s * Mathf.PI * 2 / sides;
                var dir = new Vector2(Mathf.Cos(a) * half.x, Mathf.Sin(a) * half.y);
                vh.AddVert(center + dir * VignetteStart, clear, Vector2.zero);
                vh.AddVert(center + dir * 1.5f, Vignette, Vector2.zero);
            }
            for (int s = 0; s < sides; s++)
            {
                int a = start + s * 2, b = start + (s + 1) % sides * 2;
                vh.AddTriangle(a, a + 1, b + 1); vh.AddTriangle(a, b + 1, b);
            }
        }
    }
}
