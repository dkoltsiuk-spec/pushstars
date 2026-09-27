using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>The loading reference's black track, gold gradient and moving cream/white cap.</summary>
    [AddComponentMenu("Push Stars/UI/Loading Progress Graphic")]
    public sealed class LoadingProgressGraphic : MaskableGraphic
    {
        [SerializeField, Range(0f, 1f)] private float _progress;

        public float Progress
        {
            get => _progress;
            set
            {
                value = Mathf.Clamp01(value);
                if (Mathf.Approximately(value, _progress)) return;
                _progress = value;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f) return;
            float unit = rect.height / 46f;
            DrawShape(mesh, rect, 12f * unit, 3f * unit, false, unit);
            float width = (rect.width - 5f * unit) * _progress;
            if (width <= 0f) return;
            var fill = new Rect(rect.x + 2.5f * unit, rect.y + 5f * unit,
                width, rect.height - 7.5f * unit);
            DrawShape(mesh, fill, 10f * unit, 3f * unit, true, unit);
        }

        private void DrawShape(VertexHelper mesh, Rect rect, float radius, float skew,
            bool fill, float unit)
        {
            radius = Mathf.Min(radius, rect.height * .5f, rect.width * .5f);
            // Horizontal strips follow the rounded silhouette and keep the highlight bands sharp.
            const int rows = 96;
            float creamStart = Mathf.Max(rect.xMin, rect.xMax - 28f * unit);
            float whiteStart = Mathf.Max(rect.xMin, rect.xMax - 10f * unit);
            for (int row = 0; row < rows; row++)
            {
                float y0 = rect.yMin + rect.height * row / rows;
                float y1 = rect.yMin + rect.height * (row + 1) / rows;
                Bounds(rect, radius, skew, y0, out float l0, out float r0);
                Bounds(rect, radius, skew, y1, out float l1, out float r1);
                if (!fill)
                    Quad(mesh, l0, r0, l1, r1, y0, y1, Color.black, Color.black);
                else
                {
                    Band(mesh, l0, r0, l1, r1, y0, y1, rect.xMin - skew,
                        creamStart, new Color32(255, 200, 0, 255), new Color32(246, 171, 40, 255));
                    Band(mesh, l0, r0, l1, r1, y0, y1, creamStart,
                        whiteStart, new Color32(255, 239, 184, 255), new Color32(255, 239, 184, 255));
                    Band(mesh, l0, r0, l1, r1, y0, y1, whiteStart,
                        rect.xMax + skew, Color.white, Color.white);
                }
            }
        }

        private static void Bounds(Rect rect, float radius, float skew, float y, out float left, out float right)
        {
            float dy = Mathf.Max(0f, Mathf.Abs(y - rect.center.y) - (rect.height * .5f - radius));
            float inset = radius - Mathf.Sqrt(Mathf.Max(0f, radius * radius - dy * dy));
            float shift = skew * ((y - rect.yMin) / rect.height - .5f);
            left = rect.xMin + inset + shift;
            right = rect.xMax - inset + shift;
        }

        private void Band(VertexHelper mesh, float l0, float r0, float l1, float r1,
            float y0, float y1, float start, float end, Color left, Color right)
        {
            float a = Mathf.Clamp(start, l0, r0), b = Mathf.Clamp(end, l0, r0);
            float c = Mathf.Clamp(start, l1, r1), d = Mathf.Clamp(end, l1, r1);
            if (b <= a && d <= c) return;
            int index = mesh.currentVertCount;
            Add(mesh, a, y0, Color.Lerp(left, right, Mathf.InverseLerp(start, end, a)));
            Add(mesh, b, y0, Color.Lerp(left, right, Mathf.InverseLerp(start, end, b)));
            Add(mesh, d, y1, Color.Lerp(left, right, Mathf.InverseLerp(start, end, d)));
            Add(mesh, c, y1, Color.Lerp(left, right, Mathf.InverseLerp(start, end, c)));
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }

        private void Quad(VertexHelper mesh, float l0, float r0, float l1, float r1,
            float y0, float y1, Color left, Color right)
        {
            int index = mesh.currentVertCount;
            Add(mesh, l0, y0, left); Add(mesh, r0, y0, right);
            Add(mesh, r1, y1, right); Add(mesh, l1, y1, left);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }

        private void Add(VertexHelper mesh, float x, float y, Color tint)
            => mesh.AddVert(new Vector3(x, y), tint * color, Vector2.zero);
    }
}
