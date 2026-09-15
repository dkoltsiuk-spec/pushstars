using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>A translucent contact ellipse with a crisp, unblurred edge.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class HardEllipseGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            vh.AddVert(rect.center, color, Vector2.zero);
            const int sides = 64;
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                vh.AddVert(rect.center + new Vector2(Mathf.Cos(angle) * rect.width * .5f,
                    Mathf.Sin(angle) * rect.height * .5f), color, Vector2.zero);
                if (i > 0) vh.AddTriangle(0, i, i + 1);
            }
        }
    }
}
