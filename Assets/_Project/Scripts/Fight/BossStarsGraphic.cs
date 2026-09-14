using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BossStarsGraphic : MaskableGraphic
    {
        public int Filled = 1;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect; float cell = rect.width / 5;
            for (int star = 0; star < 5; star++)
            for (int layer = 0; layer < 2; layer++)
            {
                var center = new Vector2(rect.xMin + cell * (star + .5f), rect.center.y);
                float radius = Mathf.Min(cell * .46f, rect.height * .5f) * (layer == 0 ? 1 : .72f);
                var tint = layer == 0 ? Color.black : star < Filled ? new Color(1, .83f, 0) : new Color(.24f, .24f, .26f);
                int first = mesh.currentVertCount; mesh.AddVert(center, tint, Vector2.zero);
                for (int point = 0; point < 10; point++)
                {
                    float angle = Mathf.PI * .5f + point * Mathf.PI / 5;
                    var vertex = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * (point % 2 == 0 ? 1 : .47f);
                    mesh.AddVert(vertex, tint, Vector2.zero);
                }
                for (int point = 0; point < 10; point++) mesh.AddTriangle(first, first + 1 + point, first + 1 + (point + 1) % 10);
            }
        }
    }
}
