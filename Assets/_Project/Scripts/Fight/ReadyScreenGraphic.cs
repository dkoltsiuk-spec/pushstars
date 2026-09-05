using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    // UI-native geometry keeps the gradient, lightning pattern and button crisp at any resolution.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ReadyScreenGraphic : MaskableGraphic
    {
        public bool ButtonFace;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (ButtonFace)
            {
                Quad(vh, r, 0f, 0f, 1f, 0.92f, new Color32(170, 123, 0, 255), 0.06f);
                Quad(vh, r, 0f, 0.10f, 1f, 1f, Color.white, 0.06f);
                Quad(vh, r, 0f, 0.10f, 1f, 0.96f, new Color32(255, 213, 17, 255), 0.06f);
                return;
            }
            Color bottom = new Color32(13, 18, 136, 255);
            Color middle = new Color32(60, 49, 84, 255);
            Color top = new Color32(104, 21, 43, 255);
            const int rows = 40;
            for (int y = 0; y < rows; y++)
            {
                float a = y / (float)rows, b = (y + 1f) / rows;
                Color ca = a < 0.55f ? Color.Lerp(bottom, middle, a / 0.55f) : Color.Lerp(middle, top, (a - 0.55f) / 0.45f);
                Color cb = b < 0.55f ? Color.Lerp(bottom, middle, b / 0.55f) : Color.Lerp(middle, top, (b - 0.55f) / 0.45f);
                int n = vh.currentVertCount;
                vh.AddVert(new Vector3(r.xMin, r.yMin + r.height * a), ca, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMin + r.height * a), ca, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMin + r.height * b), cb, Vector2.zero);
                vh.AddVert(new Vector3(r.xMin, r.yMin + r.height * b), cb, Vector2.zero);
                vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
            }
            // Two convex halves form each understated lightning silhouette.
            for (int y = -1; y < 8; y++)
                for (int x = -1; x < 4; x++)
                {
                    float px = r.xMin + (x + (y % 2 == 0 ? 0.1f : 0.6f)) * r.width / 3f;
                    float py = r.yMin + y * r.height / 7f;
                    float s = r.width / 3f;
                    Color tint = new Color(1f, 1f, 1f, 0.022f);
                    Triangle(vh, new Vector2(px + s * .60f, py + s), new Vector2(px + s * .15f, py + s * .43f), new Vector2(px + s * .66f, py + s * .43f), tint);
                    Triangle(vh, new Vector2(px + s * .34f, py + s * .57f), new Vector2(px + s * .85f, py + s * .57f), new Vector2(px + s * .40f, py), tint);
                }
        }

        private static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int n = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero); vh.AddVert(b, color, Vector2.zero); vh.AddVert(c, color, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2);
        }

        private void Quad(VertexHelper vh, Rect r, float x0, float y0, float x1, float y1, Color tint, float skew)
        {
            tint *= color;
            Vector2 a = new Vector2(r.xMin + r.width * x0, r.yMin + r.height * y0);
            Vector2 b = new Vector2(r.xMin + r.width * (x1 - skew), a.y);
            Vector2 c = new Vector2(r.xMin + r.width * x1, r.yMin + r.height * y1);
            Vector2 d = new Vector2(r.xMin + r.width * (x0 + skew), c.y);
            Triangle(vh, a, b, c, tint); Triangle(vh, a, c, d, tint);
        }
    }
}
