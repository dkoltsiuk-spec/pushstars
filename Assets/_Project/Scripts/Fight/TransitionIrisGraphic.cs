using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Full-screen cover for <see cref="ScreenTransition"/>: a flat colour whose
    /// <see cref="Cover"/> fades it in, and whose <see cref="Hole"/> opens a soft-edged circle from
    /// the centre to reveal the next screen.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TransitionIrisGraphic : MaskableGraphic
    {
        private const int Segments = 72;
        private const float Feather = 60f;
        private float _cover, _hole;

        /// <summary>0 = transparent, 1 = fully covered.</summary>
        public float Cover { get => _cover; set { _cover = Mathf.Clamp01(value); SetVerticesDirty(); } }
        /// <summary>0 = closed, 1 = the circle clears the farthest screen corner.</summary>
        public float Hole { get => _hole; set { _hole = Mathf.Clamp01(value); SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_cover <= 0) return;
            Rect r = rectTransform.rect;
            var solid = color; solid.a *= _cover;
            if (_hole <= 0)
            {
                vh.AddVert(new Vector3(r.xMin, r.yMin), solid, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMin), solid, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMax), solid, Vector2.zero);
                vh.AddVert(new Vector3(r.xMin, r.yMax), solid, Vector2.zero);
                vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
                return;
            }
            // An annulus: clear inside the hole, feathered edge, solid out past the corners.
            float far = r.size.magnitude * .5f + Feather;
            float inner = _hole * far, edge = inner + Feather, outer = Mathf.Max(edge, far) + 10;
            var clear = solid; clear.a = 0;
            Vector2 c = r.center;
            for (int s = 0; s <= Segments; s++)
            {
                float angle = s * Mathf.PI * 2 / Segments;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(c + dir * inner, clear, Vector2.zero);
                vh.AddVert(c + dir * edge, solid, Vector2.zero);
                vh.AddVert(c + dir * outer, solid, Vector2.zero);
                if (s == 0) continue;
                int p = (s - 1) * 3, q = s * 3;
                vh.AddTriangle(p, p + 1, q + 1); vh.AddTriangle(p, q + 1, q);
                vh.AddTriangle(p + 1, p + 2, q + 2); vh.AddTriangle(p + 1, q + 2, q + 1);
            }
        }
    }
}
