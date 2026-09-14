using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>A subdivided UI sprite with rooted wind deformation. No per-frame allocations.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ForestWindGraphic : MaskableGraphic
    {
        public Texture2D Texture;
        public Rect Uv = new Rect(0, 0, 1, 1);
        public bool Mirror;
        public bool PinRight;
        [Range(0, 20)] public float Amplitude = 4;
        [Range(.1f, 2)] public float Speed = .65f;
        public float Phase;
        [Range(0, .8f)] public float FixedBase = .12f;
        private float _sampleTime, _nextFrame;
        public override Texture mainTexture => Texture != null ? Texture : s_WhiteTexture;

        private void Update()
        {
            if (!Application.isPlaying || Amplitude <= 0 || Time.unscaledTime < _nextFrame) return;
            _nextFrame = Time.unscaledTime + 1f / 30;
            SetWindTime(Time.unscaledTime);
        }

        public void SetWindTime(float seconds)
        {
            _sampleTime = seconds;
            SetVerticesDirty();
        }

        public Vector2 WindOffset(float u, float v, float seconds)
        {
            float free = PinRight ? 1 - u : Mathf.InverseLerp(FixedBase, 1, v);
            float weight = free * free;
            float t = seconds * Speed + Phase;
            float wave = Mathf.Sin(t * 1.7f) * .72f + Mathf.Sin(t * 2.9f + u * 3.4f) * .28f;
            return PinRight
                ? new Vector2(wave * Amplitude * .15f, wave * Amplitude) * weight
                : new Vector2(wave * Amplitude, Mathf.Sin(t * 2 + u * 4) * Amplitude * .1f) * weight;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            const int columns = 24, rows = 16;
            Rect r = rectTransform.rect;
            Color32 tint = color;
            for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                float u = x / (float)columns, v = y / (float)rows;
                Vector2 position = new Vector2(r.xMin + u * r.width, r.yMin + v * r.height);
                position += WindOffset(u, v, _sampleTime);
                mesh.AddVert(position, tint, new Vector2(Uv.x + (Mirror ? 1 - u : u) * Uv.width, Uv.y + v * Uv.height));
                if (x == columns || y == rows) continue;
                int i = y * (columns + 1) + x;
                mesh.AddTriangle(i, i + columns + 1, i + 1);
                mesh.AddTriangle(i + 1, i + columns + 1, i + columns + 2);
            }
        }
    }
}
