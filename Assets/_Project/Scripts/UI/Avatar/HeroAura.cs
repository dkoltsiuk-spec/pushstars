using PushStars.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// The visible aura behind the player's hero: a soft glow and rim around the silhouette in the
    /// hero's render texture, coloured and widened by tier. The tier comes from peak Aura
    /// (<see cref="AuraTiers"/>): white GLOW from 10K, blue 50K, violet 250K, violet-to-burgundy
    /// INFINITE 1M. The shader can also draw compact cartoon flames and embers; the looks keep
    /// them off for now.
    ///
    /// Lives on its own RawImage, a sibling placed just before the hero's portrait so the hero
    /// covers the inside. Layout and UVs are copied from the portrait every frame (entrances and
    /// framing keep moving it), padded so flames can rise above the head. The silhouette is read
    /// from a quarter-size, mip-mapped copy of the portrait texture; a soft distance to the body
    /// is baked from it once per frame into a small texture (HeroAuraField.shader), so the
    /// on-screen pass stays cheap.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(2100)]
    public sealed class HeroAura : MonoBehaviour
    {
        private struct Look
        {
            public Color Core, Flame, Edge, Spark;
            /// <summary>Glow colour halfway out; left clear it is the midpoint of Core and Flame.</summary>
            public Color Mid;
            public float Glow, Radius, Intensity, Reach, Speed, Rise, Sparks, Darken, Pulse;
            /// <summary>Energy marks around the hero (swooshes, sparkles, lightning); null = none.</summary>
            public HeroAuraFx.Style Fx;
        }

        // Index = AuraTier.Index. Radius/Reach are in units of the visible portrait height.
        // Tiers differ by glow colour and width; from BLUE FLAME up the hero also gets energy
        // marks (Fx), denser at every step. The shader can also draw flames (Intensity), embers (Sparks) and a
        // contrasting edge; those stay off.
        private static readonly Look[] Looks =
        {
            default,
            // GLOW: white.
            new Look { Core = Color.white, Flame = new Color(.75f, .88f, 1f), Edge = Color.white, Spark = Color.white,
                Glow = .9f, Radius = .01f, Reach = .03f, Darken = .1f, Pulse = .18f },
            // BLUE FLAME: blue, a little wider, with the first gentle swooshes and sparkles.
            new Look { Core = new Color(.5f, .85f, 1f), Flame = new Color(.1f, .5f, 1f), Edge = Color.white, Spark = Color.white,
                Glow = 1.1f, Radius = .011f, Reach = .045f, Darken = .14f, Pulse = .16f, Fx = HeroAuraFx.Blue },
            // VIOLET STORM: violet, wider.
            new Look { Core = new Color(.9f, .55f, 1f), Flame = new Color(.7f, .2f, 1f), Edge = Color.white, Spark = Color.white,
                Glow = 1.05f, Radius = .012f, Reach = .05f, Darken = .14f, Pulse = .15f, Fx = HeroAuraFx.Violet },
            // INFINITE (1M): dark violet against the body fading through plum to burgundy red,
            // widest, darkening the world behind it; constant lightning, particles, sharp lines.
            new Look { Core = new Color(.6f, .15f, 1f), Mid = new Color(.55f, .05f, .55f), Flame = new Color(.85f, .04f, .2f),
                Edge = Color.white, Spark = Color.white,
                Glow = 1.35f, Radius = .013f, Reach = .075f, Darken = .5f, Pulse = .15f, Fx = HeroAuraFx.Dark },
        };

        // Room around the portrait, as a share of its size: flames reach a little past the outline.
        private const float PadSide = .08f, PadTop = .12f, PadBottom = .04f;
        private const int Downsample = 4;

        private static readonly int InnerId = Shader.PropertyToID("_Inner");
        private static readonly int OuterId = Shader.PropertyToID("_Outer");
        private static readonly int EdgeId = Shader.PropertyToID("_Edge");
        private static readonly int MidId = Shader.PropertyToID("_Mid");
        private static readonly int SparkColorId = Shader.PropertyToID("_SparkColor");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");
        private static readonly int FlameId = Shader.PropertyToID("_Flame");
        private static readonly int ReachId = Shader.PropertyToID("_Reach");
        private static readonly int FlameSpeedId = Shader.PropertyToID("_FlameSpeed");
        private static readonly int RiseId = Shader.PropertyToID("_Rise");
        private static readonly int SparksId = Shader.PropertyToID("_Sparks");
        private static readonly int DarkenId = Shader.PropertyToID("_Darken");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int ZoomId = Shader.PropertyToID("_Zoom");
        private static readonly int FieldTexId = Shader.PropertyToID("_FieldTex");
        private static readonly int FieldRectId = Shader.PropertyToID("_FieldRect");
        // What the column bake reads from the look, mirrored onto its own material each frame.
        private static readonly int[] FieldInputs = { ReachId, AspectId };

        /// <summary>-1 follows the save; 0–4 pins a tier (validation renders, previews).</summary>
        public int ForceTier = -1;

        private RawImage _target;
        private RawImage _image;
        private Material _material, _fieldMaterial;
        private RenderTexture _small, _field;
        private HeroAuraFx _fx;
        private int _tier = -1;
        private float _flare;

        public int Tier => _tier;

        /// <summary>Adds the aura behind <paramref name="portrait"/> (once; later calls return the
        /// same one). The portrait must show a texture with a transparent background.</summary>
        public static HeroAura Attach(RawImage portrait)
        {
            if (portrait == null || portrait.transform.parent == null) return null;
            foreach (var existing in portrait.transform.parent.GetComponentsInChildren<HeroAura>(true))
                if (existing._target == portrait) return existing;

            var shader = Resources.Load<Shader>("HeroAura");
            var fieldShader = Resources.Load<Shader>("HeroAuraField");
            if (shader == null || fieldShader == null) { Debug.LogWarning("[HeroAura] Resources/HeroAura*.shader is missing."); return null; }

            var go = new GameObject("HeroAura", typeof(RectTransform), typeof(RawImage), typeof(HeroAura));
            go.layer = portrait.gameObject.layer;
            var aura = go.GetComponent<HeroAura>();
            aura._target = portrait;
            aura._image = go.GetComponent<RawImage>();
            aura._image.raycastTarget = false;
            aura._material = new Material(shader) { name = "HeroAura (runtime)" };
            aura._image.material = aura._material;
            aura._fieldMaterial = new Material(fieldShader) { name = "HeroAuraField (runtime)" };
            go.transform.SetParent(portrait.transform.parent, false);
            aura.Sync();
            return aura;
        }

        private void LateUpdate() => Sync();

        private void Sync()
        {
            if (_target == null) { Destroy(gameObject); return; }

            int tier = ForceTier >= 0 ? Mathf.Clamp(ForceTier, 0, Looks.Length - 1) : AuraTiers.ForPeak(CaseRewards.AuraPeak).Index;
            if (tier != _tier)
            {
                if (_tier > 0 && tier > _tier) _flare = 1f; // a tier-up flares once
                _tier = tier;
            }

            var source = _target.texture;
            bool show = _tier > 0 && source != null && _target.isActiveAndEnabled && _target.color.a > .01f;
            if (_image.enabled != show) _image.enabled = show;
            var fxStyle = show ? Looks[_tier].Fx : null;
            if (fxStyle != null && _fx == null) _fx = HeroAuraFx.Create(_target);
            if (_fx != null) { if (fxStyle != null) _fx.Show(fxStyle); else _fx.Hide(); }
            if (!show) return;

            CopyLayout();
            Apply(Looks[_tier]);
            Downscale(source);
            _flare = Mathf.Max(0f, _flare - Time.unscaledDeltaTime * .8f);
        }

        private void CopyLayout()
        {
            var from = _target.rectTransform;
            var to = (RectTransform)transform;
            // Just behind the hero — or behind the energy layer that sits just behind the hero.
            Transform below = _fx != null && _fx.Visible ? _fx.transform : from;
            int index = below.GetSiblingIndex();
            if (to.GetSiblingIndex() < index) index--;
            if (to.GetSiblingIndex() != index) to.SetSiblingIndex(index);

            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.localRotation = from.localRotation;
            to.localScale = from.localScale;
            Vector2 size = from.rect.size;
            to.offsetMin = from.offsetMin - new Vector2(PadSide * size.x, PadBottom * size.y);
            to.offsetMax = from.offsetMax + new Vector2(PadSide * size.x, PadTop * size.y);

            var uv = from.GetComponent<RawImage>().uvRect;
            _image.uvRect = new Rect(uv.x - PadSide * uv.width, uv.y - PadBottom * uv.height,
                uv.width * (1 + 2 * PadSide), uv.height * (1 + PadTop + PadBottom));

            // The portrait may be widened by a mesh effect (AvatarWideImage); widen the aura the same way.
            bool wide = from.TryGetComponent<AvatarWideImage>(out var widen) && widen.enabled;
            var mine = GetComponent<AvatarWideImage>();
            if (wide && mine == null) mine = gameObject.AddComponent<AvatarWideImage>();
            if (mine != null && mine.enabled != wide) mine.enabled = wide;

            float width = size.x * (wide ? AvatarWideCamera.WidthMultiplier : 1f);
            float aspect = size.y > .01f && Mathf.Abs(uv.width) > .0001f
                ? width / Mathf.Abs(uv.width) / (size.y / Mathf.Abs(uv.height)) : 1f;
            _material.SetFloat(AspectId, aspect);
            _material.SetFloat(ZoomId, 1f / Mathf.Max(.05f, Mathf.Abs(uv.height)));
        }

        private void Downscale(Texture source)
        {
            int w = Mathf.Max(32, source.width / Downsample), h = Mathf.Max(32, source.height / Downsample);
            if (_small == null || _small.width != w || _small.height != h)
            {
                if (_small != null) { _small.Release(); Destroy(_small); }
                // Mips are the flame column's wide blur: one sample at a high level instead of dozens.
                _small = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
                {
                    name = "HeroAuraSilhouette", useMipMap = true, autoGenerateMips = true,
                    filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp,
                };
                _small.Create();
            }
            // The stage camera renders after LateUpdate, so this is last frame's pose: invisible in a glow.
            Graphics.Blit(source, _small);
            if (_image.texture != _small) _image.texture = _small;

            // Soft distance to the body over the whole aura quad, at half the silhouette's resolution.
            var rect = _image.uvRect;
            int fw = Mathf.Max(16, Mathf.RoundToInt(w * Mathf.Abs(rect.width) / 2)), fh = Mathf.Max(16, Mathf.RoundToInt(h * Mathf.Abs(rect.height) / 2));
            if (_field == null || _field.width != fw || _field.height != fh)
            {
                if (_field != null) { _field.Release(); Destroy(_field); }
                _field = new RenderTexture(fw, fh, 0, RenderTextureFormat.ARGB32)
                {
                    name = "HeroAuraField", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                };
                _field.Create();
            }
            _fieldMaterial.SetVector(FieldRectId, new Vector4(rect.x, rect.y, rect.width, rect.height));
            _material.SetVector(FieldRectId, new Vector4(rect.x, rect.y, rect.width, rect.height));
            foreach (int id in FieldInputs) _fieldMaterial.SetFloat(id, _material.GetFloat(id));
            Graphics.Blit(_small, _field, _fieldMaterial);
            _material.SetTexture(FieldTexId, _field);
        }

        private void Apply(Look look)
        {
            float uvHeight = Mathf.Abs(_image.uvRect.height) / (1 + PadTop + PadBottom);
            float boost = 1f + _flare * 1.2f;
            _material.SetColor(InnerId, look.Core);
            _material.SetColor(OuterId, look.Flame);
            _material.SetColor(EdgeId, look.Edge);
            _material.SetColor(MidId, look.Mid.a > 0f ? look.Mid : Color.Lerp(look.Flame, look.Core, .5f));
            _material.SetColor(SparkColorId, look.Spark);
            _material.SetFloat(GlowId, look.Glow * boost);
            _material.SetFloat(RadiusId, look.Radius * uvHeight * (1f + _flare * .6f));
            _material.SetFloat(FlameId, look.Intensity);
            _material.SetFloat(ReachId, look.Reach * uvHeight * (1f + _flare * .5f));
            _material.SetFloat(FlameSpeedId, look.Speed);
            _material.SetFloat(RiseId, look.Rise);
            _material.SetFloat(SparksId, look.Sparks);
            _material.SetFloat(DarkenId, look.Darken);
            _material.SetFloat(PulseId, look.Pulse);
        }

        private void OnDisable() { if (_image != null) _image.enabled = false; }

        private void OnDestroy()
        {
            if (_small != null) { _small.Release(); Destroy(_small); }
            if (_field != null) { _field.Release(); Destroy(_field); }
            if (_material != null) Destroy(_material);
            if (_fieldMaterial != null) Destroy(_fieldMaterial);
            if (_fx != null) Destroy(_fx.gameObject);
        }
    }
}
