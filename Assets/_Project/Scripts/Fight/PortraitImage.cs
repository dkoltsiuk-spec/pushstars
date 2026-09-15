using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>A portrait crop may extend beyond its camera; that area must stay transparent.</summary>
    public sealed class PortraitImage : RawImage
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect uv = uvRect;
            if (uv.width <= 0f || uv.height <= 0f) return;
            float left = Mathf.Max(0f, uv.xMin), right = Mathf.Min(1f, uv.xMax);
            float bottom = Mathf.Max(0f, uv.yMin), top = Mathf.Min(1f, uv.yMax);
            if (left >= right || bottom >= top) return;
            Rect r = GetPixelAdjustedRect();
            float x0 = r.xMin + r.width * (left - uv.xMin) / uv.width;
            float x1 = r.xMin + r.width * (right - uv.xMin) / uv.width;
            float y0 = r.yMin + r.height * (bottom - uv.yMin) / uv.height;
            float y1 = r.yMin + r.height * (top - uv.yMin) / uv.height;
            vh.AddVert(new Vector3(x0, y0), color, new Vector2(left, bottom));
            vh.AddVert(new Vector3(x1, y0), color, new Vector2(right, bottom));
            vh.AddVert(new Vector3(x1, y1), color, new Vector2(right, top));
            vh.AddVert(new Vector3(x0, y1), color, new Vector2(left, top));
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
        }
    }
}
