using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArenaRoundedGraphic : MaskableGraphic
    {
        public float Radius = 12;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = rectTransform.rect;
            float radius = Mathf.Min(Radius, Mathf.Min(r.width, r.height)*.5f);
            vh.AddVert(r.center, color, Vector2.zero);
            for(int corner=0;corner<4;corner++)
            {
                var c = new Vector2(corner==0 || corner==3 ? r.xMax-radius : r.xMin+radius,
                    corner<2 ? r.yMax-radius : r.yMin+radius);
                for(int i=0;i<=8;i++)
                {
                    float a=(corner*90+i*90f/8)*Mathf.Deg2Rad;
                    vh.AddVert(c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,color,Vector2.zero);
                }
            }
            for(int i=1;i<=36;i++) vh.AddTriangle(0,i,i==36?1:i+1);
        }
    }
}
