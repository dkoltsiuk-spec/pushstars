using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>A gold rarity star with a dark silhouette; does not depend on font glyphs.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RewardStarGraphic : MaskableGraphic
    {
        public Texture2D Artwork;
        public override Texture mainTexture => Artwork != null ? Artwork : base.mainTexture;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Artwork != null)
            {
                Rect r = rectTransform.rect;
                float size = Mathf.Min(r.width, r.height);
                Vector2 min = r.center - Vector2.one * size * .5f;
                vh.AddVert(min, Color.white, Vector2.zero);
                vh.AddVert(min + new Vector2(size, 0), Color.white, Vector2.right);
                vh.AddVert(min + Vector2.one * size, Color.white, Vector2.one);
                vh.AddVert(min + new Vector2(0, size), Color.white, Vector2.up);
                vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
                return;
            }
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
