using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AvatarLightningGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            Vector2[] points = { new Vector2(.52f, 1), new Vector2(.08f, .43f),
                new Vector2(.45f, .43f), new Vector2(.27f, 0), new Vector2(.94f, .62f), new Vector2(.58f, .62f) };
            foreach (var point in points) vh.AddVert(rect.min + Vector2.Scale(point, rect.size), color, Vector2.zero);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 5);
            vh.AddTriangle(2, 3, 4); vh.AddTriangle(2, 4, 5);
        }
    }
}
