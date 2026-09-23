using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Rounded black track: level bottom, gently rising top and solid gold fill.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AvatarShardBar : MaskableGraphic
    {
        [Range(0, 1)] public float Progress;
        public void SetProgress(float value) { Progress = Mathf.Clamp01(value); SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float rise = r.height * .12f;
            Draw(vh, r, 1, rise, Color.black);
            if (Progress <= 0) return;
            float inset = r.height * .065f;
            r = new Rect(r.x + inset, r.y + inset, r.width - 2 * inset, r.height - 2 * inset);
            Draw(vh, r, Mathf.Clamp01(Progress), rise, new Color32(255, 191, 0, 255));
        }
        private static void Draw(VertexHelper vh, Rect r, float fraction, float rise, Color32 color)
        {
            float width = r.width * fraction;
            float skew = Mathf.Min(r.height * .08f, width * .15f);
            var corners = new[] {
                new Vector2(r.x, r.y),
                new Vector2(r.x + skew, r.yMax - rise),
                new Vector2(r.x + width, r.yMax - rise + rise * fraction),
                new Vector2(r.x + width - skew, r.y)
            };
            int start = vh.currentVertCount;
            vh.AddVert((corners[0] + corners[1] + corners[2] + corners[3]) * .25f, color, Vector2.zero);
            for (int i = 0; i < 4; i++)
            {
                var corner = corners[i];
                var previous = corners[(i + 3) % 4] - corner;
                var next = corners[(i + 1) % 4] - corner;
                float radius = Mathf.Min(r.height * .19f, Mathf.Min(previous.magnitude, next.magnitude) * .45f);
                var from = corner + previous.normalized * radius;
                var to = corner + next.normalized * radius;
                for (int segment = 0; segment <= 8; segment++)
                {
                    float t = segment / 8f, u = 1 - t;
                    vh.AddVert(u * u * from + 2 * u * t * corner + t * t * to, color, Vector2.zero);
                }
            }
            int count = vh.currentVertCount - start - 1;
            for (int i = 0; i < count; i++) vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % count);
        }
    }
}
