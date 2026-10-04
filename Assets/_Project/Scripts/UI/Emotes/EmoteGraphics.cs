using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Flat rounded rectangle (a circle when the radius covers it) for the emote UI —
    /// no sprite, so the runtime-built panel needs no art import.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EmoteShape : MaskableGraphic
    {
        [SerializeField] private float _radius = 14f;

        public float Radius { get => _radius; set { _radius = value; SetVerticesDirty(); } }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float radius = Mathf.Clamp(_radius, 0f, Mathf.Min(r.width, r.height) * .5f);
            const int steps = 10;
            vh.AddVert(r.center, color, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                var c = new Vector2(corner == 0 || corner == 3 ? r.xMax - radius : r.xMin + radius,
                    corner < 2 ? r.yMax - radius : r.yMin + radius);
                for (int i = 0; i <= steps; i++)
                {
                    float a = (corner * 90f + i * 90f / steps) * Mathf.Deg2Rad;
                    vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, Vector2.zero);
                }
            }
            int count = 4 * (steps + 1);
            for (int i = 1; i <= count; i++) vh.AddTriangle(0, i, i == count ? 1 : i + 1);
        }
    }

    /// <summary>The emote button's face: a round grin with a dark keyline, Clash-style. Drawn,
    /// not imported, so it scales crisply at any size.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EmoteFaceGraphic : MaskableGraphic
    {
        private static readonly Color32 Ink = new Color32(40, 24, 10, 255);
        private static readonly Color32 Skin = new Color32(255, 214, 64, 255);
        private static readonly Color32 Shade = new Color32(241, 164, 32, 255);
        private static readonly Color32 Mouth = new Color32(122, 36, 30, 255);
        private static readonly Color32 Tongue = new Color32(236, 92, 92, 255);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            float s = Mathf.Min(r.width, r.height) * .5f;
            var c = r.center;
            Disc(vh, c, s, Ink);
            Disc(vh, c, s * .88f, Shade);
            Disc(vh, c + new Vector2(0f, s * .06f), s * .8f, Skin);
            // Eyes: tall ovals.
            Ellipse(vh, c + new Vector2(-s * .3f, s * .22f), s * .1f, s * .17f, Ink);
            Ellipse(vh, c + new Vector2(s * .3f, s * .22f), s * .1f, s * .17f, Ink);
            // Open grin: lower half-disc with a tongue.
            HalfDisc(vh, c + new Vector2(0f, -s * .1f), s * .5f, s * .42f, Mouth);
            HalfDisc(vh, c + new Vector2(0f, -s * .38f), s * .26f, s * .14f, Tongue);
        }

        private void Disc(VertexHelper vh, Vector2 c, float radius, Color32 col) => Ellipse(vh, c, radius, radius, col);

        private static void Ellipse(VertexHelper vh, Vector2 c, float rx, float ry, Color32 col)
        {
            int start = vh.currentVertCount;
            vh.AddVert(c, col, Vector2.zero);
            const int sides = 40;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                vh.AddVert(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), col, Vector2.zero);
                if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
            }
        }

        private static void HalfDisc(VertexHelper vh, Vector2 c, float rx, float ry, Color32 col)
        {
            int start = vh.currentVertCount;
            vh.AddVert(c, col, Vector2.zero);
            const int sides = 24;
            for (int i = 0; i <= sides; i++)
            {
                float a = Mathf.PI + i * Mathf.PI / sides;
                vh.AddVert(c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry), col, Vector2.zero);
                if (i > 0) vh.AddTriangle(start, start + i, start + i + 1);
            }
        }
    }

    /// <summary>Radial cooldown sweep over the emote button.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class EmoteCooldownGraphic : MaskableGraphic
    {
        private float _fill;

        public float Fill
        {
            get => _fill;
            set { value = Mathf.Clamp01(value); if (Mathf.Approximately(value, _fill)) return; _fill = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_fill <= 0f) return;
            var r = rectTransform.rect;
            float radius = Mathf.Min(r.width, r.height) * .5f;
            vh.AddVert(r.center, color, Vector2.zero);
            int sides = Mathf.Max(2, Mathf.CeilToInt(48 * _fill));
            for (int i = 0; i <= sides; i++)
            {
                float a = Mathf.PI * .5f - i * Mathf.PI * 2f * _fill / sides;
                vh.AddVert(r.center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, Vector2.zero);
                if (i > 0) vh.AddTriangle(0, i, i + 1);
            }
        }
    }
}
