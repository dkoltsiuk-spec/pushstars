using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Two expanding yellow rings behind the currently available boss.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BossPulseRings : MaskableGraphic
    {
        public GameObject ActiveMarker;
        private float _elapsed;
        private bool _wasActive;

        protected override void OnEnable()
        {
            base.OnEnable();
            _elapsed = 0;
            raycastTarget = false;
        }

        private void Update()
        {
            bool active = ActiveMarker != null && ActiveMarker.activeSelf;
            if (active || _wasActive != active)
            {
                _elapsed = active ? _elapsed + Time.unscaledDeltaTime : 0;
                SetVerticesDirty();
            }
            _wasActive = active;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (ActiveMarker == null || !ActiveMarker.activeSelf) return;
            var rect = rectTransform.rect;
            const int segments = 64;
            for (int wave = 0; wave < 2; wave++)
            {
                float progress = Mathf.Repeat(_elapsed / 1.6f + wave * .5f, 1);
                float radius = Mathf.Lerp(.31f, .49f, progress);
                var outer = new Vector2(rect.width * radius, rect.height * radius);
                var inner = outer - Vector2.one * Mathf.Lerp(2.4f, 1.2f, progress);
                var tint = color; tint.a *= Mathf.Pow(1 - progress, 1.4f);
                for (int i = 0; i < segments; i++)
                {
                    float a = i * 2 * Mathf.PI / segments, b = (i + 1) * 2 * Mathf.PI / segments;
                    var start = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var end = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                    int index = mesh.currentVertCount;
                    mesh.AddVert(rect.center + Vector2.Scale(start, outer), tint, Vector2.zero);
                    mesh.AddVert(rect.center + Vector2.Scale(end, outer), tint, Vector2.zero);
                    mesh.AddVert(rect.center + Vector2.Scale(end, inner), tint, Vector2.zero);
                    mesh.AddVert(rect.center + Vector2.Scale(start, inner), tint, Vector2.zero);
                    mesh.AddTriangle(index, index + 1, index + 2);
                    mesh.AddTriangle(index, index + 2, index + 3);
                }
            }
        }
    }
}
