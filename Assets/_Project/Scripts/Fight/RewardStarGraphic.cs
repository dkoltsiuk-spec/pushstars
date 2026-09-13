using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>A gold rarity star with a dark silhouette; does not depend on font glyphs.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RewardStarGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Star(vh, 1f, new Color32(8, 12, 20, 255));
            Star(vh, 0.78f, color);
        }

        private void Star(VertexHelper vh, float scale, Color tint)
        {
            Rect r = rectTransform.rect;
            Vector2 center = r.center;
            float radius = Mathf.Min(r.width, r.height) * 0.5f * scale;
            int start = vh.currentVertCount;
            vh.AddVert(center, tint, Vector2.zero);
            for (int i = 0; i <= 10; i++)
            {
                float angle = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float length = radius * (i % 2 == 0 ? 1f : 0.46f);
                vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * length, tint, Vector2.zero);
                if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
            }
        }
    }
}
