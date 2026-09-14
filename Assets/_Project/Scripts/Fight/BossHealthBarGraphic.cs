using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>A true capsule: end caps stay circular at every screen aspect and HP value.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BossHealthBarGraphic : MaskableGraphic
    {
        [SerializeField, Range(0, 1)] private float _fill = 1;
        public float FillAmount { get => _fill; set { value = Mathf.Clamp01(value); if (_fill == value) return; _fill = value; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect;
            Capsule(mesh, rect, Color.black, rect.xMax);
            rect = Rect.MinMaxRect(rect.xMin + 3, rect.yMin + 3, rect.xMax - 3, rect.yMax - 3);
            Capsule(mesh, rect, new Color(.24f,.02f,.04f), rect.xMax);
            if (_fill > 0) Capsule(mesh, rect, new Color(.86f,0,.025f), rect.xMin + rect.width * _fill);
        }
        private static void Capsule(VertexHelper mesh, Rect rect, Color tint, float right)
        {
            const int count = 48; float radius = Mathf.Min(rect.height,rect.width)*.5f;
            int first = mesh.currentVertCount;
            mesh.AddVert(new Vector2(Mathf.Min(rect.center.x,right),rect.center.y),tint,Vector2.zero);
            for (int i=0;i<=count;i++)
            {
                float angle=i*Mathf.PI*2/count;float cosine=Mathf.Cos(angle);
                float x=(cosine>=0?rect.xMax-radius:rect.xMin+radius)+cosine*radius;
                mesh.AddVert(new Vector2(Mathf.Min(x,right),rect.center.y+Mathf.Sin(angle)*radius),tint,Vector2.zero);
                if(i>0)mesh.AddTriangle(first,first+i,first+i+1);
            }
        }
    }
}
