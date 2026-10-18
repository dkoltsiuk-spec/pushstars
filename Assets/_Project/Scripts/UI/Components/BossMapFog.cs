using PushStars.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Fog over the islands the player has not reached. It starts just above the current boss, so the
    /// next one shows through its edge, and it lifts when the map is opened after a win. The fog itself
    /// is drawn by BossMapFog.shader in map space, so it scrolls with the islands; this graphic only
    /// covers the fogged part of the viewport and feeds the shader the fog line.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BossMapFog : MaskableGraphic
    {
        private const string SeenKey = "boss_map_fog_seen";

        public BossMapController Owner;
        /// <summary>The fog takes its palette from the biome under it: deep and cool in the hollows,
        /// lit by the island's own light at the edge and in the fireflies.</summary>
        public BossMapBackdrop Backdrop;
        public Shader Shader;
        [Tooltip("How far the shadows lean toward cool blue, away from the biome's mist hue.")]
        [Range(0, 1)] public float ShadowCool = .5f;
        [Tooltip("Where the fog starts between the current boss (0) and the next one (1).")]
        [Range(0, 1)] public float Lead = .55f;
        [Tooltip("Furthest the fog line sits above the current boss, in 390-wide map units.")]
        public float MaxLead = 120;
        [Tooltip("Fog line above the last boss once every playable island is free.")]
        public float ClearedLead = 320;
        public float Edge = 150;
        public float RevealDelay = .5f, RevealDuration = 2.2f;

        private Material _material;
        private float _line, _from, _to, _revealStart = -1;
        private bool _pendingOpen;
        private Vector3 _lastOrigin;
        private Vector2 _lastSize;
        private float _lastLine = float.NaN;

        /// <summary>True while the fog is lifting after a win.</summary>
        public bool Revealing => _revealStart >= 0;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_material == null && Shader != null)
                _material = new Material(Shader) { name = "BossMapFog (Instance)", hideFlags = HideFlags.DontSave };
            material = _material;
            _pendingOpen = true;
            RequestChannels();
        }

        protected override void OnCanvasHierarchyChanged()
        {
            base.OnCanvasHierarchyChanged();
            RequestChannels();
        }

        // uv1 and uv2 carry the haze and glow colours next to the shadow colour in the vertex colour.
        private void RequestChannels()
        {
            const AdditionalCanvasShaderChannels channels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            var own = canvas;
            if (own == null) return;
            own.additionalShaderChannels |= channels;
            own.rootCanvas.additionalShaderChannels |= channels;
        }

        protected override void OnDisable()
        {
            _revealStart = -1;
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material);
            }
            base.OnDestroy();
        }

        // After the controller's open coroutine (Fit, scroll reset), so map positions are final.
        private void LateUpdate()
        {
            if (Owner == null || Owner.MapContent == null || _material == null) return;
            if (_pendingOpen) { _pendingOpen = false; BeginOpen(); }
            float clear = 0;
            if (_revealStart >= 0)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - _revealStart - RevealDelay) / RevealDuration);
                _line = Mathf.Lerp(_from, _to, Mathf.SmoothStep(0, 1, k));
                clear = Mathf.Sin(k * Mathf.PI) * .8f;
                if (k >= 1) _revealStart = -1;
            }
            else _line = LineFor(BossCatalog.ClearedCount);
            _material.SetFloat("_FogLine", _line);
            _material.SetFloat("_Clear", clear);
            _material.SetFloat("_Edge", Edge);

            Vector3 origin = Owner.MapContent.InverseTransformPoint(rectTransform.position);
            Vector2 size = rectTransform.rect.size;
            if (origin != _lastOrigin || size != _lastSize || Mathf.Abs(_line - _lastLine) > .5f || float.IsNaN(_lastLine))
            {
                _lastOrigin = origin; _lastSize = size; _lastLine = _line;
                SetVerticesDirty();
            }
        }

        private void BeginOpen()
        {
            int cleared = BossCatalog.ClearedCount;
            _revealStart = -1;
            if (!Application.isPlaying) return;
            int seen = PlayerPrefs.GetInt(SeenKey, -1);
            PlayerPrefs.SetInt(SeenKey, cleared);
            // First visit, or progress restored from a save: nothing to lift.
            if (seen < 0 || seen >= cleared) return;
            _from = LineFor(seen); _to = LineFor(cleared);
            if (_to <= _from + 1) return;
            _revealStart = Time.unscaledTime;
            FocusOn(_from);
            StartCoroutine(Whoosh());
        }

        private System.Collections.IEnumerator Whoosh()
        {
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < RevealDelay) yield return null;
            GameAudio.Play(SoundCue.AuraWhoosh);
        }

        /// <summary>Scrolls so the old fog edge sits in the lower half of the view while it lifts.</summary>
        private void FocusOn(float line)
        {
            var scroll = Owner.Scroll;
            if (scroll == null) return;
            float scale = Scale, content = Owner.MapContent.rect.height, view = scroll.viewport.rect.height;
            if (content <= view + 1) return;
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = Mathf.Clamp01((line * scale - view * .38f) / (content - view));
        }

        private float Scale => Mathf.Max(.01f, Owner.MapContent.rect.width / 390f);

        /// <summary>Fog line, in 390-wide map units, for a given number of defeated ladder bosses.</summary>
        public float LineFor(int cleared)
        {
            int count = BossCatalog.Bosses.Count;
            if (count == 0) return 0;
            if (cleared >= count) return NodeY(count - 1) + ClearedLead;
            float current = NodeY(cleared);
            if (!HasNode(cleared + 1)) return current + MaxLead;
            return current + Mathf.Min((NodeY(cleared + 1) - current) * Lead, MaxLead);
        }

        private bool HasNode(int index)
        {
            foreach (var node in Owner.Nodes) if (node.BossIndex == index && node.Button != null) return true;
            return false;
        }

        private float NodeY(int index)
        {
            foreach (var node in Owner.Nodes)
            {
                if (node.BossIndex != index || node.Button == null) continue;
                var rect = (RectTransform)node.Button.transform;
                return Owner.MapContent.InverseTransformPoint(rect.TransformPoint(rect.rect.center)).y / Scale;
            }
            return 0;
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Owner == null || Owner.MapContent == null) return;
            var rect = rectTransform.rect;
            float scale = Scale;
            Vector2 bottom = Owner.MapContent.InverseTransformPoint(rectTransform.TransformPoint(rect.min)) / scale;
            Vector2 top = Owner.MapContent.InverseTransformPoint(rectTransform.TransformPoint(rect.max)) / scale;
            if (Mathf.Approximately(top.y, bottom.y)) return;
            // The torn edge reaches about this far below the line; nothing is drawn under it.
            float start = Mathf.InverseLerp(bottom.y, top.y, _line - Edge - 120);
            if (start >= 1) return;
            const int rows = 24;
            for (int y = 0; y <= rows; y++)
            {
                float v = Mathf.Lerp(start, 1, y / (float)rows);
                float localY = Mathf.Lerp(rect.yMin, rect.yMax, v);
                Palette(new Vector2(0, Mathf.Lerp(bottom.y, top.y, v) * scale), out var deep, out var haze, out var glow);
                for (int x = 0; x <= 1; x++)
                {
                    var vertex = UIVertex.simpleVert;
                    vertex.position = new Vector2(x == 0 ? rect.xMin : rect.xMax, localY);
                    vertex.color = deep * color;
                    vertex.uv0 = new Vector2(Mathf.Lerp(bottom.x, top.x, x), Mathf.Lerp(bottom.y, top.y, v));
                    vertex.uv1 = (Vector4)haze;
                    vertex.uv2 = (Vector4)glow;
                    mesh.AddVert(vertex);
                }
            }
            for (int y = 0; y < rows; y++)
            {
                int i = y * 2;
                mesh.AddTriangle(i, i + 2, i + 1);
                mesh.AddTriangle(i + 1, i + 2, i + 3);
            }
        }

        /// <summary>Deep shadow, lit haze and glow for the fog at a map height, all from the biome's own
        /// colours: green-teal over the forest, indigo over ice, smoky plum over lava.</summary>
        private void Palette(Vector2 mapPoint, out Color deep, out Color haze, out Color glow)
        {
            Color mist = new Color(.20f, .27f, .085f), light = new Color(.62f, .58f, .12f);
            if (Backdrop != null) Backdrop.SampleBiome(mapPoint, out _, out mist, out light);
            Color.RGBToHSV(mist, out float h, out float s, out _);
            Color.RGBToHSV(light, out float lh, out float ls, out _);
            deep = Color.HSVToRGB(TowardBlue(h, ShadowCool), Mathf.Clamp(s, .55f, .75f), .17f);
            haze = Color.HSVToRGB(TowardBlue(h, ShadowCool * .6f), Mathf.Clamp(s * .6f, .3f, .45f), .62f);
            glow = Color.HSVToRGB(lh, Mathf.Clamp(ls, .25f, .75f), 1);
        }

        /// <summary>Moves a hue toward blue (.6) along the shorter way round the wheel.</summary>
        private static float TowardBlue(float hue, float amount)
        {
            float delta = Mathf.Repeat(.6f - hue + .5f, 1) - .5f;
            return Mathf.Repeat(hue + delta * amount, 1);
        }
    }
}
