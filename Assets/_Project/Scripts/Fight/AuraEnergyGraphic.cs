using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Full-screen violet fire with a private material and deterministic unscaled time.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class AuraEnergyGraphic : MaskableGraphic
    {
        [System.NonSerialized] public float Clock, Power, Release;
        private Material _instance, _source;
        private static readonly int Flow = Shader.PropertyToID("_Flow");
        private static readonly int Viewport = Shader.PropertyToID("_Viewport");

        public void Sample(float clock, float power, float release)
        {
            Clock = clock; Power = power; Release = release;
            UpdateFlow();
        }
        private void UpdateFlow()
        {
            if (_instance == null)
            {
                _source = material;
                if (_source == null || !_source.HasProperty(Flow)) return;
                _instance = new Material(_source) { name = "Aura fire instance", hideFlags = HideFlags.HideAndDontSave };
                material = _instance;
            }
            var r = rectTransform.rect;
            float s = Mathf.Max(1, Mathf.Min(r.width, r.height));
            _instance.SetVector(Flow, new Vector4(Clock, Power, Release, 0));
            _instance.SetVector(Viewport, new Vector4(r.width / s, r.height / s, 0, 0));
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            UpdateFlow();
            vh.Clear();
            Rect r = rectTransform.rect;
            vh.AddVert(new Vector3(r.xMin, r.yMin), color, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), color, Vector2.right);
            vh.AddVert(new Vector3(r.xMax, r.yMax), color, Vector2.one);
            vh.AddVert(new Vector3(r.xMin, r.yMax), color, Vector2.up);
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3);
        }
        protected override void OnDestroy()
        {
            if (_instance != null)
            {
                material = _source;
                if (Application.isPlaying) Destroy(_instance); else DestroyImmediate(_instance);
            }
            base.OnDestroy();
        }
    }
}
