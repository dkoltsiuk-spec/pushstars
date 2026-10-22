using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Paints a nickname in its <see cref="NickColor"/>: a flat color or a two-tone gradient, and for
    /// ultra colors the motion on top — a gold shine sweeping across, a flowing rainbow, a pulsing
    /// neon glow or stardust sparkles twinkling around the letters. Added to the label at runtime;
    /// <see cref="Own"/> follows the player's picked color live, so a change in the nickname editor
    /// shows everywhere at once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NickStyleView : MonoBehaviour
    {
        private const int SparkleCount = 5;
        private static Sprite _sparkle;

        private TMP_Text _label;
        private Material _shared, _neon;
        private string _id;
        private bool _own, _ready;
        private NickColor _color;
        private RectTransform _sparkles;
        private Image[] _stars;
        private float[] _starPhase;

        /// <summary>The player's own name: follows the color picked in the nickname editor.</summary>
        public static void Own(TMP_Text label)
        {
            var view = For(label);
            if (view == null) return;
            view._own = true;
            view.Show(CaseRewards.NickColorId);
        }

        /// <summary>A fixed color, e.g. the picker's swatches or someone else's name.</summary>
        public static void Set(TMP_Text label, string colorId)
        {
            var view = For(label);
            if (view == null) return;
            view._own = false;
            view.Show(colorId);
        }

        private static NickStyleView For(TMP_Text label)
        {
            if (label == null) return null;
            // Not `??`: a destroyed component is fake-null to Unity but not to C#.
            return label.TryGetComponent(out NickStyleView view) ? view : label.gameObject.AddComponent<NickStyleView>();
        }

        private void Init()
        {
            if (_ready) return;
            _ready = true;
            _label = GetComponent<TMP_Text>();
            _shared = _label.fontSharedMaterial;
        }

        private void Show(string id)
        {
            Init();
            _id = id;
            _color = NickColorCatalog.Find(id) ?? NickColorCatalog.Default;
            _label.color = Color.white;
            _label.enableVertexGradient = true;
            _label.colorGradient = new VertexGradient(_color.Top, _color.Top, _color.Bottom, _color.Bottom);
            bool neon = _color.Effect == NickColorEffect.Neon;
            if (neon && _neon == null && _shared != null)
            {
                _neon = new Material(_shared) { name = _shared.name + " (Neon nick)" };
                _neon.EnableKeyword("UNDERLAY_ON");
                _neon.SetFloat("_UnderlayOffsetX", 0); _neon.SetFloat("_UnderlayOffsetY", 0);
                _neon.SetFloat("_UnderlaySoftness", .75f); _neon.SetFloat("_UnderlayDilate", .45f);
            }
            if (_neon != null) _label.fontSharedMaterial = neon ? _neon : _shared;
            if (_color.Effect == NickColorEffect.Stardust) BuildSparkles();
            if (_sparkles != null) _sparkles.gameObject.SetActive(_color.Effect == NickColorEffect.Stardust);
            _label.SetVerticesDirty();
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            if (_own && CaseRewards.NickColorId != _id) Show(CaseRewards.NickColorId);
            switch (_color.Effect)
            {
                case NickColorEffect.GoldShine:
                case NickColorEffect.Rainbow: PaintVertices(); break;
                case NickColorEffect.Neon:
                    float pulse = .6f + .35f * Mathf.Sin(Time.unscaledTime * 3.2f);
                    _neon?.SetColor("_UnderlayColor", new Color(.24f, 1f, .92f, pulse));
                    break;
                case NickColorEffect.Stardust: Twinkle(); break;
            }
        }

        /// <summary>Recolors the generated glyph quads in place: a bright band sweeping over the gold,
        /// or a rainbow flowing left to right.</summary>
        private void PaintVertices()
        {
            var info = _label.textInfo;
            if (info == null || info.characterCount == 0) return;
            var bounds = _label.textBounds;
            float width = Mathf.Max(1f, bounds.size.x), time = Time.unscaledTime;
            // The shine crosses in 1.1 s, then rests 1.5 s.
            float sweep = (time % 2.6f) / 1.1f, band = Mathf.Lerp(bounds.min.x - width * .35f, bounds.max.x + width * .35f, sweep);
            for (int c = 0; c < info.characterCount; c++)
            {
                var ch = info.characterInfo[c];
                if (!ch.isVisible) continue;
                var colors = info.meshInfo[ch.materialReferenceIndex].colors32;
                var verts = info.meshInfo[ch.materialReferenceIndex].vertices;
                for (int v = 0; v < 4; v++)
                {
                    int i = ch.vertexIndex + v;
                    bool top = v == 1 || v == 2;
                    Color color;
                    if (_color.Effect == NickColorEffect.Rainbow)
                    {
                        // A full spectrum every six letters' widths, whatever the name's length.
                        float hue = Mathf.Repeat((verts[i].x - bounds.min.x) / (_label.fontSize * 6f) - time * .35f, 1f);
                        color = Color.HSVToRGB(hue, top ? .6f : .9f, 1f);
                    }
                    else
                    {
                        color = top ? (Color)_color.Top : _color.Bottom;
                        // Slanted band: x shifted by height, like light on a bevel.
                        float distance = Mathf.Abs(verts[i].x + verts[i].y * .45f - band);
                        float shine = sweep <= 1f ? Mathf.Clamp01(1f - distance / (_label.fontSize * 1.1f)) : 0f;
                        color = Color.Lerp(color, Color.white, shine * .85f);
                    }
                    colors[i] = color;
                }
            }
            _label.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        private void BuildSparkles()
        {
            if (_sparkles != null) return;
            var go = new GameObject("NickSparkles", typeof(RectTransform));
            go.layer = gameObject.layer;
            _sparkles = (RectTransform)go.transform;
            _sparkles.SetParent(transform, false);
            _sparkles.anchorMin = _sparkles.anchorMax = _sparkles.pivot = new Vector2(.5f, .5f);
            _sparkles.anchoredPosition = Vector2.zero;
            _stars = new Image[SparkleCount];
            _starPhase = new float[SparkleCount];
            for (int i = 0; i < SparkleCount; i++)
            {
                var star = new GameObject("Sparkle" + i, typeof(RectTransform)).AddComponent<Image>();
                star.gameObject.layer = gameObject.layer;
                star.transform.SetParent(_sparkles, false);
                star.sprite = Sparkle();
                star.raycastTarget = false;
                star.color = i % 2 == 0 ? new Color(1f, .93f, 1f) : new Color(.85f, .72f, 1f);
                _stars[i] = star;
                _starPhase[i] = i / (float)SparkleCount;
                Place(i);
            }
        }

        private void Twinkle()
        {
            if (_stars == null) return;
            const float period = 1.5f;
            for (int i = 0; i < _stars.Length; i++)
            {
                float before = _starPhase[i];
                _starPhase[i] = Mathf.Repeat(before + Time.unscaledDeltaTime / period, 1f);
                if (_starPhase[i] < before) Place(i);
                float life = Mathf.Sin(_starPhase[i] * Mathf.PI);
                _stars[i].rectTransform.localScale = Vector3.one * life;
                _stars[i].rectTransform.localRotation = Quaternion.Euler(0, 0, _starPhase[i] * 90f);
            }
        }

        /// <summary>A new spot on the edge of the letters, so sparkles never sit on top of them.</summary>
        private void Place(int i)
        {
            var bounds = _label.textBounds;
            if (bounds.size.x <= 0) bounds = new Bounds(Vector3.zero, new Vector3(_label.fontSize * 4f, _label.fontSize, 0));
            float x = Random.Range(bounds.min.x - 4f, bounds.max.x + 4f);
            float y = Random.value < .5f ? bounds.max.y + Random.Range(-2f, 5f) : bounds.min.y - Random.Range(-2f, 5f);
            float size = _label.fontSize * Random.Range(.45f, .75f);
            var rect = _stars[i].rectTransform;
            rect.sizeDelta = new Vector2(size, size);
            // Text bounds are relative to the label's pivot; the container sits at the label's centre.
            var pivotShift = (new Vector2(.5f, .5f) - _label.rectTransform.pivot) * _label.rectTransform.rect.size;
            rect.anchoredPosition = new Vector2(x, y) - pivotShift;
        }

        /// <summary>A soft four-point star, drawn once.</summary>
        private static Sprite Sparkle()
        {
            if (_sparkle != null) return _sparkle;
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "NickSparkle", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size * 2f - 1f, v = (y + .5f) / size * 2f - 1f, r = Mathf.Sqrt(u * u + v * v);
                    float rays = .05f / (Mathf.Abs(u * v) * 6f + .05f) * Mathf.Clamp01(1f - r);
                    float core = Mathf.Exp(-r * r * 18f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(rays + core) * 255));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            _sparkle = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
            return _sparkle;
        }

        private void OnDestroy()
        {
            if (_neon != null) Destroy(_neon);
        }
    }
}
