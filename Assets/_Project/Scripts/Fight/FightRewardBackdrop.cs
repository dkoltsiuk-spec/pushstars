using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Resolution-independent reward gradients and a restrained radial glow.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FightRewardBackdrop : MaskableGraphic
    {
        public enum Style { Summary, Case, Gems }
        public Style Appearance;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Color top, bottom, glow;
            switch (Appearance)
            {
                case Style.Gems:
                    top = new Color32(46, 172, 0, 255);
                    bottom = new Color32(35, 151, 0, 255);
                    glow = new Color32(168, 255, 30, 130);
                    break;
                case Style.Case:
                    top = new Color32(20, 23, 29, 255);
                    bottom = new Color32(25, 29, 36, 255);
                    glow = new Color32(98, 105, 145, 105);
                    break;
                default:
                    top = new Color32(12, 14, 21, 255);
                    bottom = new Color32(15, 24, 149, 255);
                    glow = new Color32(130, 105, 190, 100);
                    break;
            }
            vh.AddVert(new Vector3(r.xMin, r.yMin), bottom, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), bottom, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), top, Vector2.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), top, Vector2.zero);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);

            var center = new Vector2(r.center.x, r.center.y + r.height * 0.04f);
            float rx = r.width * 0.9f, ry = r.height * 0.47f;
            int start = vh.currentVertCount;
            vh.AddVert(center, glow, Vector2.zero);
            Color edge = new Color(glow.r, glow.g, glow.b, 0f);
            const int steps = 64;
            for (int i = 0; i <= steps; i++)
            {
                float angle = i * Mathf.PI * 2f / steps;
                vh.AddVert(center + new Vector2(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry), edge, Vector2.zero);
                if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
            }
        }
    }
}
