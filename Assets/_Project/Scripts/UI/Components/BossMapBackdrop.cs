using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Texture-free atmosphere sampled in map space, so biomes and lights follow scrolling.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BossMapBackdrop : MaskableGraphic
    {
        public BossMapController Owner;
        public BossMapBiome[] Biomes;
        private Vector3 _lastOrigin;
        private Vector2 _lastSize;
        private bool _lastMap;

        private void LateUpdate() => Refresh();

        public void Refresh()
        {
            if (Owner == null || Owner.MapContent == null) return;
            bool map = Owner.IsMapOpen;
            Vector3 origin = Owner.MapContent.InverseTransformPoint(rectTransform.position);
            Vector2 size = rectTransform.rect.size;
            if (origin != _lastOrigin || size != _lastSize || map != _lastMap)
            {
                _lastOrigin = origin; _lastSize = size; _lastMap = map;
                SetVerticesDirty();
            }
        }

        public Color Sample(Vector2 mapPoint, float horizontal)
        {
            if (Biomes == null || Biomes.Length == 0) return new Color(.065f, .105f, .045f);
            Color background = Biomes[0].Background, mist = Biomes[0].Mist;
            for (int i = 1; i < Biomes.Length; i++)
            {
                var biome = Biomes[i];
                float boundary = Owner.MapContent.InverseTransformPoint(biome.transform.position).y;
                float scale = ((RectTransform)biome.transform).localScale.y;
                float span = Mathf.Max(1, biome.TransitionHeight * scale);
                float blend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(boundary - span * .5f, boundary + span * .5f, mapPoint.y));
                background = Color.Lerp(background, biome.Background, blend);
                mist = Color.Lerp(mist, biome.Mist, blend);
            }
            float widthScale = Mathf.Max(.01f, Owner.MapContent.rect.width / 390f);
            var p = mapPoint / widthScale;
            float clouds = Mathf.PerlinNoise(p.x * .007f + 31, p.y * .004f + 17);
            float center = 1 - Mathf.Pow(Mathf.Abs(horizontal * 2 - 1), 1.5f);
            var result = Color.Lerp(background, mist, .12f + clouds * .30f + center * .13f);
            if (Owner.IsMapOpen)
            {
                foreach (var biome in Biomes)
                {
                    if (biome.LightAnchors == null) continue;
                    foreach (var anchor in biome.LightAnchors)
                    {
                        if (anchor == null) continue;
                        Vector2 position = Owner.MapContent.InverseTransformPoint(anchor.position);
                        Vector2 delta = (mapPoint - position) / widthScale;
                        float glow = Mathf.Exp(-(delta.x * delta.x / 16000 + delta.y * delta.y / 26000));
                        result = Color.Lerp(result, biome.Light, glow * .25f);
                    }
                }
            }
            else
            {
                float glow = Mathf.Exp(-(p.x * p.x / 22000 + (p.y - 330) * (p.y - 330) / 60000));
                result = Color.Lerp(result, Biomes[0].Light, glow * .28f);
            }
            result.a = 1;
            return result;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Owner == null) return;
            const int columns = 24, rows = 64;
            var rect = rectTransform.rect;
            // Convert just two corners; all map-space samples use the same affine transform.
            Vector2 bottom = Owner.MapContent.InverseTransformPoint(rectTransform.TransformPoint(rect.min));
            Vector2 top = Owner.MapContent.InverseTransformPoint(rectTransform.TransformPoint(rect.max));
            for (int y = 0; y <= rows; y++)
                for (int x = 0; x <= columns; x++)
                {
                    float u = x / (float)columns, v = y / (float)rows;
                    var position = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, u), Mathf.Lerp(rect.yMin, rect.yMax, v));
                    var point = Owner.IsMapOpen
                        ? new Vector2(Mathf.Lerp(bottom.x, top.x, u), Mathf.Lerp(bottom.y, top.y, v))
                        : new Vector2((u - .5f) * Owner.MapContent.rect.width, v * 844 * Owner.MapContent.rect.width / 390f);
                    mesh.AddVert(position, Sample(point, u), Vector2.zero);
                }
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    int index = y * (columns + 1) + x;
                    mesh.AddTriangle(index, index + columns + 1, index + 1);
                    mesh.AddTriangle(index + 1, index + columns + 1, index + columns + 2);
                }
        }
    }
}
