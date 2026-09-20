using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Resolution-independent case rays and the illustrated tap cue.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CaseTapGraphic : MaskableGraphic
    {
        public bool Rays;
        public Color RayTint = new Color(.8f, .84f, 1f, .16f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            if (Rays)
            {
                float radius = Mathf.Min(r.width, r.height) * .5f;
                for (int i = 0; i < 20; i++)
                {
                    float a = i * Mathf.PI * 2 / 20;
                    int n = vh.currentVertCount;
                    // Wider wedges with a curved fade that disappears before the outer edge.
                    for (int band = 0; band <= 4; band++)
                    {
                        float t = band / 4f;
                        var tint = RayTint;
                        tint.a *= (1 - t) * (1 - t);
                        foreach (float angle in new[] { a, a + .19f })
                            vh.AddVert(r.center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * .9f * t,
                                tint, Vector2.zero);
                        if (band == 0) continue;
                        int previous = n + (band - 1) * 2;
                        vh.AddTriangle(previous, previous + 2, previous + 3);
                        vh.AddTriangle(previous, previous + 3, previous + 1);
                    }
                }
                return;
            }

            // A pointing index finger, curled fingers and cuff, in normalized canvas coordinates.
            Vector2[] outline = {
                new Vector2(.47f,.08f), new Vector2(.34f,.16f), new Vector2(.21f,.18f),
                new Vector2(.13f,.27f), new Vector2(.13f,.35f), new Vector2(.2f,.4f),
                new Vector2(.31f,.38f), new Vector2(.07f,.73f), new Vector2(.055f,.83f),
                new Vector2(.1f,.9f), new Vector2(.18f,.92f), new Vector2(.25f,.88f),
                new Vector2(.46f,.61f), new Vector2(.49f,.73f), new Vector2(.57f,.76f),
                new Vector2(.65f,.7f), new Vector2(.72f,.73f), new Vector2(.8f,.68f),
                new Vector2(.9f,.52f), new Vector2(.92f,.35f), new Vector2(.99f,.24f),
                new Vector2(.73f,.035f)
            };
            var center = new Vector2(.56f, .43f);
            Fill(vh, r, outline, center, 1f, new Color32(8, 10, 18, 255));
            Fill(vh, r, outline, center, .9f, new Color32(255, 139, 77, 255));
            Fill(vh, r, outline, center, .79f, new Color32(255, 169, 108, 255));
        }

        private static void Fill(VertexHelper vh, Rect r, Vector2[] points, Vector2 center, float scale, Color tint)
        {
            int n = vh.currentVertCount;
            vh.AddVert(r.min + Vector2.Scale(center, r.size), tint, Vector2.zero);
            foreach (var point in points)
                vh.AddVert(r.min + Vector2.Scale(center + (point - center) * scale, r.size), tint, Vector2.zero);
            for (int i = 0; i < points.Length; i++) vh.AddTriangle(n, n + i + 1, n + (i + 1) % points.Length + 1);
        }
    }
}
