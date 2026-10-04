using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Resolution-independent league panels, medals and arena lighting.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LeagueSurface : MaskableGraphic
    {
        public enum Shape { Backdrop, Panel, Medal, Rail }
        public Shape Style;
        public Color Accent = new Color32(255, 180, 68, 255);
        public Color Shade = new Color32(61, 31, 35, 255);
        public Color Sky = new Color32(145, 57, 23, 255);
        [Range(0, 1)] public float Amount = 1;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (Style == Shape.Backdrop)
            {
                Quad(vh, r, Shade * new Color(.55f, .55f, .7f, 1), Sky);
                Vector2 center = new Vector2(r.center.x, r.yMax - r.width * .48f);
                for (int i = 0; i < 18; i++)
                {
                    float a = i * Mathf.PI * 2 / 18;
                    Triangle(vh, center, center + Dir(a) * r.height, center + Dir(a + .085f) * r.height,
                        new Color(Accent.r, Accent.g, Accent.b, .035f), new Color(Accent.r, Accent.g, Accent.b, 0));
                }
                // Broad light around the emblem, fading into the leaderboard below.
                for (int i = 0; i < 64; i++)
                    Triangle(vh, center, center + Dir(i * Mathf.PI / 32) * r.width * .64f,
                        center + Dir((i + 1) * Mathf.PI / 32) * r.width * .64f,
                        new Color(Accent.r, Accent.g, Accent.b, .12f), new Color(Accent.r, Accent.g, Accent.b, 0));
            }
            else if (Style == Shape.Panel)
            {
                Cut(vh, new Rect(r.x, r.y - 4, r.width, r.height), 10, new Color32(11, 13, 25, 255));
                Cut(vh, r, 10, Color.Lerp(Accent, Shade, .5f));
                Cut(vh, Inset(r, 1.5f), 9, Shade);
                Quad(vh, new Rect(r.x + 12, r.yMax - 3, r.width - 24, 1), Accent * new Color(1, 1, 1, .65f), Accent * new Color(1, 1, 1, .65f));
            }
            else if (Style == Shape.Rail)
            {
                Cut(vh, r, 4, new Color32(12, 15, 29, 255));
                if (Amount > 0)
                {
                    var fill = Inset(r, 3); fill.width *= Amount;
                    Quad(vh, fill, Color.Lerp(Accent, new Color(1, .36f, .06f), .35f), Accent);
                }
                for (int i = 1; i < 4; i++)
                    Quad(vh, new Rect(r.x + r.width * i / 4, r.y + 3, 1, r.height - 6), new Color(1, 1, 1, .18f), new Color(1, 1, 1, .18f));
            }
            else
            {
                Shield(vh, r, new Color32(12, 16, 31, 255));
                Shield(vh, Inset(r, 3), Accent);
                Shield(vh, Inset(r, 6), Color.Lerp(Accent, Shade, .35f));
                Vector2 c = r.center + Vector2.up * r.height * .04f;
                for (int i = 0; i < 10; i++)
                {
                    float a = Mathf.PI * .5f + i * Mathf.PI / 5;
                    float b = a + Mathf.PI / 5;
                    Triangle(vh, c, c + Dir(a) * r.width * (i % 2 == 0 ? .23f : .105f),
                        c + Dir(b) * r.width * (i % 2 == 0 ? .105f : .23f), new Color32(255, 248, 216, 255), new Color32(255, 248, 216, 255));
                }
            }
        }

        static Vector2 Dir(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        static Rect Inset(Rect r, float p) => new Rect(r.x + p, r.y + p, r.width - p * 2, r.height - p * 2);
        static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color center, Color edge)
        {
            int n = vh.currentVertCount;
            vh.AddVert(a, center, Vector2.zero); vh.AddVert(b, edge, Vector2.zero); vh.AddVert(c, edge, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2);
        }
        static void Quad(VertexHelper vh, Rect r, Color bottom, Color top)
        {
            int n = vh.currentVertCount;
            vh.AddVert(new Vector2(r.xMin, r.yMin), bottom, Vector2.zero);
            vh.AddVert(new Vector2(r.xMin, r.yMax), top, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMax), top, Vector2.zero);
            vh.AddVert(new Vector2(r.xMax, r.yMin), bottom, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2); vh.AddTriangle(n, n + 2, n + 3);
        }
        static void Cut(VertexHelper vh, Rect r, float cut, Color c)
        {
            Vector2[] p = { new Vector2(r.xMin + cut, r.yMax), new Vector2(r.xMax - cut, r.yMax),
                new Vector2(r.xMax, r.yMax - cut), new Vector2(r.xMax, r.yMin + cut),
                new Vector2(r.xMax - cut, r.yMin), new Vector2(r.xMin + cut, r.yMin),
                new Vector2(r.xMin, r.yMin + cut), new Vector2(r.xMin, r.yMax - cut) };
            for (int i = 0; i < p.Length; i++) Triangle(vh, r.center, p[i], p[(i + 1) % p.Length], c, c);
        }
        static void Shield(VertexHelper vh, Rect r, Color c)
        {
            Vector2[] p = { new Vector2(r.xMin, r.yMax - r.height * .15f), new Vector2(r.center.x, r.yMax),
                new Vector2(r.xMax, r.yMax - r.height * .15f), new Vector2(r.xMax - r.width * .08f, r.yMin + r.height * .3f),
                new Vector2(r.center.x, r.yMin), new Vector2(r.xMin + r.width * .08f, r.yMin + r.height * .3f) };
            for (int i = 0; i < p.Length; i++) Triangle(vh, r.center, p[i], p[(i + 1) % p.Length], c, c);
        }
    }
}
