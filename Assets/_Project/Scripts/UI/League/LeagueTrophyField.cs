using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>One UI mesh of drifting cups, faded in screen space with the backdrop.</summary>
    [ExecuteAlways]
    public sealed class LeagueTrophyField : MaskableGraphic
    {
        public Sprite Trophy;
        public float Speed = 12f;
        [Range(0, 1)] public float InvisibleBelow = .22f;
        [Range(0, 1)] public float FullOpacityAbove = .58f;
        private float _elapsed;

        public override Texture mainTexture => Trophy != null ? Trophy.texture : Texture2D.whiteTexture;

        private void Update()
        {
            if (!Application.isPlaying) return;
            _elapsed += Time.unscaledDeltaTime;
            SetVerticesDirty();
        }

        public float OpacityAtHeight(float height)
        {
            float bottom = Mathf.SmoothStep(0, 1,
                Mathf.InverseLerp(InvisibleBelow, Mathf.Max(InvisibleBelow + .001f, FullOpacityAbove), height));
            float top = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.94f, 1f, height));
            return bottom * top;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Trophy == null) return;
            var bounds = rectTransform.rect;
            if (bounds.width <= 0 || bounds.height <= 0) return;
            float scale = bounds.width / 390f;
            float pitch = 142f * scale;
            int rows = Mathf.CeilToInt(bounds.height / pitch) + 2;
            float span = rows * pitch;
            var uv = DataUtility.GetOuterUV(Trophy);
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < 4; col++)
            {
                float phase = row * 1.7f + col * 2.3f;
                float x = bounds.xMin + (col * 112f + (row % 2) * 48f - 12f
                    + Mathf.Sin(_elapsed * .18f + phase) * 10f) * scale;
                float y = bounds.yMin - pitch + Mathf.Repeat(row * pitch + col * 31f * scale
                    + _elapsed * Speed * scale, span);
                float width = (96f + (row + col) % 3 * 8f) * scale;
                float height = width * Trophy.rect.height / Trophy.rect.width;
                float angle = (-14f + Mathf.Sin(_elapsed * .12f + phase) * 9f) * Mathf.Deg2Rad;
                var right = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var up = new Vector2(-right.y, right.x);
                var center = new Vector2(x, y);
                // Subdivide vertically so a cup itself dissolves into the black region.
                // Alpha is evaluated after rotation, against the full background height.
                const int strips = 12;
                for (int strip = 0; strip < strips; strip++)
                {
                    float low = strip / (float)strips;
                    float high = (strip + 1) / (float)strips;
                    int first = vh.currentVertCount;
                    AddVertex(vh, center + right * (-width * .5f) + up * ((low - .5f) * height), new Vector2(uv.x, Mathf.Lerp(uv.y, uv.w, low)), bounds);
                    AddVertex(vh, center + right * (-width * .5f) + up * ((high - .5f) * height), new Vector2(uv.x, Mathf.Lerp(uv.y, uv.w, high)), bounds);
                    AddVertex(vh, center + right * (width * .5f) + up * ((high - .5f) * height), new Vector2(uv.z, Mathf.Lerp(uv.y, uv.w, high)), bounds);
                    AddVertex(vh, center + right * (width * .5f) + up * ((low - .5f) * height), new Vector2(uv.z, Mathf.Lerp(uv.y, uv.w, low)), bounds);
                    vh.AddTriangle(first, first + 1, first + 2);
                    vh.AddTriangle(first + 2, first + 3, first);
                }
            }
        }

        private void AddVertex(VertexHelper vh, Vector2 position, Vector2 uv, Rect bounds)
        {
            var tint = color;
            tint.a *= OpacityAtHeight((position.y - bounds.yMin) / bounds.height);
            vh.AddVert(position, tint, uv);
        }

        public static LeagueTrophyField Build(RectTransform background, Sprite trophy)
        {
            var go = new GameObject("FloatingTrophies", typeof(RectTransform), typeof(CanvasRenderer), typeof(LeagueTrophyField));
            go.layer = background.gameObject.layer;
            var field = go.GetComponent<LeagueTrophyField>();
            field.rectTransform.SetParent(background, false);
            field.rectTransform.anchorMin = Vector2.zero;
            field.rectTransform.anchorMax = Vector2.one;
            field.rectTransform.offsetMin = field.rectTransform.offsetMax = Vector2.zero;
            field.Trophy = trophy;
            // The supplied sprite already has ~6% alpha; do not attenuate it a second time.
            field.color = new Color(1f, .82f, .55f, 1f);
            field.raycastTarget = false;
            field.SetAllDirty();
            return field;
        }
    }
}
