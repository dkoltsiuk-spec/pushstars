using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BossBackdropGraphic : MaskableGraphic
    {
        public bool Preparation;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var r = rectTransform.rect;
            void Quad(float bottom, float top, Color lower, Color upper)
            {
                int i = mesh.currentVertCount;
                mesh.AddVert(new Vector2(r.xMin, r.yMin + bottom * r.height), lower, Vector2.zero);
                mesh.AddVert(new Vector2(r.xMax, r.yMin + bottom * r.height), lower, Vector2.zero);
                mesh.AddVert(new Vector2(r.xMax, r.yMin + top * r.height), upper, Vector2.zero);
                mesh.AddVert(new Vector2(r.xMin, r.yMin + top * r.height), upper, Vector2.zero);
                mesh.AddTriangle(i, i + 1, i + 2); mesh.AddTriangle(i, i + 2, i + 3);
            }
            Quad(0, .52f, new Color32(15, 18, 119, 255), new Color32(35, 32, 153, 255));
            Quad(.52f, 1, new Color32(56, 44, 74, 255), new Color32(113, 19, 43, 255));
            void Poly(Color tint, params Vector2[] points)
            {
                int first = mesh.currentVertCount;
                foreach (var p in points) mesh.AddVert(new Vector2(r.xMin + p.x * r.width, r.yMin + p.y * r.height), tint, Vector2.zero);
                for (int p = 1; p < points.Length - 1; p++) mesh.AddTriangle(first, first + p, first + p + 1);
            }
            if (Preparation)
            {
                // The supplied jagged VS sprite is layered over the portraits by the screen.
                return;
            }
            else
            {
                Poly(new Color(.42f,0,.62f),new Vector2(0,.50f),new Vector2(.69f,.55f),new Vector2(.53f,.557f),new Vector2(1,.606f),new Vector2(.71f,.565f),new Vector2(1,.58f),new Vector2(1,.556f),new Vector2(0,.485f));
            }
        }
    }
}
