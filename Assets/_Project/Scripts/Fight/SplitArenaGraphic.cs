using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// Splits one portrait arena into the boss half (above the seam) and the player half (below),
    /// each cover-cropped so the art is never stretched on wide or tall viewports.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class SplitArenaGraphic : MaskableGraphic
    {
        public Sprite Sprite;
        [Range(0, 1)] public float Seam = .505f;
        /// <summary>Preferred source regions in sprite UVs; the crop keeps <see cref="TopPivot"/> and <see cref="BottomPivot"/> of the height when trimming.</summary>
        public Rect TopSource = new Rect(0, .37f, 1, .5f), BottomSource = new Rect(0, 0, 1, .5f);
        [Range(0, 1)] public float TopPivot = .25f, BottomPivot = .1f;
        public Color TopTint = Color.white, BottomTint = Color.white;
        public override Texture mainTexture => Sprite != null ? Sprite.texture : s_WhiteTexture;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Sprite == null) return;
            Rect r = rectTransform.rect;
            float seam = r.yMin + r.height * Seam;
            Half(mesh, new Rect(r.xMin, seam, r.width, r.yMax - seam), TopSource, TopPivot, TopTint);
            Half(mesh, new Rect(r.xMin, r.yMin, r.width, seam - r.yMin), BottomSource, BottomPivot, BottomTint);
        }

        private void Half(VertexHelper mesh, Rect area, Rect source, float pivot, Color tint)
        {
            if (area.width <= 0 || area.height <= 0) return;
            Rect sprite = Sprite.textureRect;
            float sourceAspect = source.width * sprite.width / (source.height * sprite.height);
            float targetAspect = area.width / area.height;
            if (targetAspect > sourceAspect)
            {
                float height = source.height * sourceAspect / targetAspect;
                source = new Rect(source.x, source.y + (source.height - height) * pivot, source.width, height);
            }
            else
            {
                float width = source.width * targetAspect / sourceAspect;
                source = new Rect(source.x + (source.width - width) * .5f, source.y, width, source.height);
            }
            var texture = Sprite.texture;
            Vector2 uvMin = new Vector2((sprite.x + source.xMin * sprite.width) / texture.width, (sprite.y + source.yMin * sprite.height) / texture.height);
            Vector2 uvMax = new Vector2((sprite.x + source.xMax * sprite.width) / texture.width, (sprite.y + source.yMax * sprite.height) / texture.height);
            Color32 c = color * tint;
            int start = mesh.currentVertCount;
            mesh.AddVert(new Vector2(area.xMin, area.yMin), c, uvMin);
            mesh.AddVert(new Vector2(area.xMin, area.yMax), c, new Vector2(uvMin.x, uvMax.y));
            mesh.AddVert(new Vector2(area.xMax, area.yMax), c, uvMax);
            mesh.AddVert(new Vector2(area.xMax, area.yMin), c, new Vector2(uvMax.x, uvMin.y));
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
