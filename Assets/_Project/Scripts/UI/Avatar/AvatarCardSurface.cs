using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Card and footer share one coordinate system, with no baked highlights or skew.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AvatarCardSurface : MaskableGraphic
    {
        public bool Footer;
        public bool Selected;
        public bool Locked;

        public void SetSelected(bool selected) { Selected = selected; SetVerticesDirty(); }
        public void SetLocked(bool locked) { Locked = locked; SetVerticesDirty(); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float scale = r.width / 166f;
            var body = new Rect(r.x, r.y + 4 * scale, r.width, r.height - 4 * scale);
            var inner = new Rect(body.x + 2 * scale, body.y + 2 * scale,
                body.width - 4 * scale, body.height - 4 * scale);
            if (Footer)
            {
                inner.height = 39 * scale;
                Rounded(vh, inner, 6 * scale, true, Locked ? new Color32(72, 41, 151, 255) :
                    Selected ? new Color32(255, 215, 0, 255) : new Color32(40, 29, 131, 255));
                return;
            }
            Rounded(vh, r, 8 * scale, false, new Color32(10, 13, 30, 255));
            Rounded(vh, body, 8 * scale, false, Locked ? new Color32(12, 14, 20, 255) :
                Selected ? new Color32(231, 149, 0, 255) : new Color32(32, 24, 91, 255));
            Rounded(vh, inner, 6 * scale, false, Locked ? new Color32(39, 43, 55, 255) :
                Selected ? new Color32(255, 184, 0, 255) : new Color32(65, 0, 255, 255));
        }

        private static void Rounded(VertexHelper vh, Rect rect, float radius, bool squareTop, Color32 tint)
        {
            int center = vh.currentVertCount;
            vh.AddVert(rect.center, tint, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                bool right = corner == 0 || corner == 3;
                bool top = corner >= 2;
                float cr = squareTop && top ? 0 : radius;
                var origin = new Vector2(right ? rect.xMax - cr : rect.xMin + cr,
                    top ? rect.yMax - cr : rect.yMin + cr);
                float start = -90 - corner * 90;
                for (int j = 0; j <= 8; j++)
                {
                    float angle = (start - j * 90f / 8f) * Mathf.Deg2Rad;
                    // Clockwise corners: bottom-right, bottom-left, top-left, top-right.
                    // The first arc runs from right to bottom, then bottom to left, etc.
                    angle += Mathf.PI / 2;
                    vh.AddVert(origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cr, tint, Vector2.zero);
                }
            }
            int count = vh.currentVertCount - center - 1;
            for (int i = 0; i < count; i++) vh.AddTriangle(center, center + 1 + i, center + 1 + (i + 1) % count);
        }
    }
}
