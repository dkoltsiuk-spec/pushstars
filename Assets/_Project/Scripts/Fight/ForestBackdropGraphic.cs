using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Fills the viewport, extending the opaque edge of the rounded source at its corners.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ForestBackdropGraphic : MaskableGraphic
    {
        public Texture2D Texture;
        public Vector2 SourceX;
        public Vector2[] OpaqueRows;
        public override Texture mainTexture => Texture != null ? Texture : s_WhiteTexture;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Texture == null || OpaqueRows == null || OpaqueRows.Length < 2) return;
            const int columns = 32;
            int rows = OpaqueRows.Length - 1;
            Rect r = rectTransform.rect;
            for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                float u = x / (float)columns, v = y / (float)rows;
                float sx = Mathf.Clamp(Mathf.Lerp(SourceX.x, SourceX.y, u), OpaqueRows[y].x, OpaqueRows[y].y);
                float sy = Mathf.Lerp(.5f / Texture.height, 1 - .5f / Texture.height, v);
                mesh.AddVert(new Vector2(r.xMin + u * r.width, r.yMin + v * r.height), color, new Vector2(sx, sy));
                if (x == columns || y == rows) continue;
                int i = y * (columns + 1) + x;
                mesh.AddTriangle(i, i + columns + 1, i + 1);
                mesh.AddTriangle(i + 1, i + columns + 1, i + columns + 2);
            }
        }
    }
}
