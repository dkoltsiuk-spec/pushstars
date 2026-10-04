using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Fits the video under the four inner edges of the supplied skewed frame.</summary>
    [DisallowMultipleComponent]
    public sealed class PlacementVideoFrame : BaseMeshEffect
    {
        // Inner opening in the original 946 x 536 artwork, with a one-pixel overlap
        // beneath its opaque border to avoid seams. Order matches RawImage vertices.
        private static readonly Vector2[] Corners =
        {
            new Vector2(25f / 946, 31f / 536),
            new Vector2(25f / 946, 502f / 536),
            new Vector2(895f / 946, 511f / 536),
            new Vector2(907f / 946, 49f / 536)
        };

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || vertices.currentVertCount != 4) return;
            var rect = graphic.rectTransform.rect;
            var vertex = new UIVertex();
            for (int i = 0; i < 4; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                vertex.position = new Vector3(rect.xMin + Corners[i].x * rect.width,
                    rect.yMin + Corners[i].y * rect.height, vertex.position.z);
                vertices.SetUIVertex(vertex, i);
            }
        }
    }
}
