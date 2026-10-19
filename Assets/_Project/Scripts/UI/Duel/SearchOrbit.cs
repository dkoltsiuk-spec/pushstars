using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// "Searching" motion around the VS badge: two soft comet arcs — a fading tail ending in a
    /// small spark — circle the badge. When the opponent is found they spin up, pull into the
    /// badge and fade while the badge punches once.
    ///
    /// <para>Built at runtime under RingWrap (behind the badge), sprites drawn procedurally once,
    /// so the serialized Main scene needs no regeneration.</para>
    /// </summary>
    public sealed class SearchOrbit : MonoBehaviour
    {
        const float Radius = 100, Speed = 200, FoundTime = .45f;
        const float ArcSpan = 115; // degrees of tail behind each spark
        const int TexSize = 256;
        const float TexRadius = 116; // ring radius inside the texture, in texels
        static readonly Color Warm = new Color(1f, .76f, .26f);

        static Sprite _arc, _dot;
        RectTransform _spin, _badge;
        Image _arcImage;
        Image[] _sparks, _glows;
        float _start, _foundAt = -1;

        /// <summary>Adds (once) the orbit under <paramref name="ringWrap"/>, behind the VS badge.</summary>
        public static SearchOrbit Ensure(RectTransform ringWrap, RectTransform badge)
        {
            var existing = ringWrap.Find("SearchOrbit");
            var orbit = existing != null ? existing.GetComponent<SearchOrbit>() : null;
            if (orbit == null)
            {
                var root = new GameObject("SearchOrbit", typeof(RectTransform)).GetComponent<RectTransform>();
                root.SetParent(ringWrap, false);
                root.SetAsFirstSibling();
                root.anchorMin = root.anchorMax = root.pivot = new Vector2(.5f, .5f);
                root.sizeDelta = Vector2.zero;
                orbit = root.gameObject.AddComponent<SearchOrbit>();
                orbit.Build();
            }
            orbit._badge = badge;
            return orbit;
        }

        /// <summary>Restarts the searching loop.</summary>
        public void Play()
        {
            _start = Time.unscaledTime;
            _foundAt = -1;
            if (_badge != null) _badge.localScale = Vector3.one;
            gameObject.SetActive(true);
        }

        /// <summary>Opponent found: the arcs pull into the badge and fade, the badge punches.</summary>
        public void Found()
        {
            if (_foundAt < 0) _foundAt = Time.unscaledTime;
        }

        void Build()
        {
            _arc = _arc != null ? _arc : ArcSprite();
            _dot = _dot != null ? _dot : DotSprite();
            _spin = new GameObject("Spin", typeof(RectTransform)).GetComponent<RectTransform>();
            _spin.SetParent(transform, false);
            _spin.sizeDelta = Vector2.zero;
            float size = TexSize * Radius / TexRadius;
            _arcImage = Part("Arcs", _arc, size, Vector2.zero);
            _glows = new Image[2]; _sparks = new Image[2];
            for (int i = 0; i < 2; i++)
            {
                // The sparks sit at the leading end of each arc (texture angle ArcSpan, and +180).
                float a = (ArcSpan + i * 180) * Mathf.Deg2Rad;
                var at = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Radius;
                _glows[i] = Part("Glow" + i, _dot, 26, at);
                _sparks[i] = Part("Spark" + i, _dot, 9, at);
            }
        }

        Image Part(string name, Sprite sprite, float size, Vector2 at)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            image.rectTransform.SetParent(_spin, false);
            image.rectTransform.sizeDelta = new Vector2(size, size);
            image.rectTransform.anchoredPosition = at;
            image.sprite = sprite; image.raycastTarget = false;
            image.color = Color.clear;
            return image;
        }

        void Update()
        {
            float t = Time.unscaledTime - _start;
            float found = _foundAt < 0 ? 0 : Mathf.Clamp01((Time.unscaledTime - _foundAt) / FoundTime);
            // Ease in so the screen's own entrance stays clean.
            float alpha = Mathf.SmoothStep(0, 1, t / .6f) * (1 - found * found);
            float spinUp = found * found * 360f; // extra turn while collapsing
            _spin.localEulerAngles = new Vector3(0, 0, Speed * t + spinUp);
            _spin.localScale = Vector3.one * (1 - found * found * found * .85f);

            _arcImage.color = new Color(Warm.r, Warm.g, Warm.b, .75f * alpha);
            for (int i = 0; i < 2; i++)
            {
                _glows[i].color = new Color(Warm.r, Warm.g, Warm.b, .5f * alpha);
                _sparks[i].color = new Color(1f, .97f, .86f, alpha);
            }

            if (_badge != null)
            {
                float b = _foundAt < 0 ? 0 : Mathf.Clamp01((Time.unscaledTime - _foundAt) / .5f);
                _badge.localScale = Vector3.one * (1 + .16f * Mathf.Sin(b * Mathf.PI));
            }
        }

        // Two comet tails on one ring: each fades in over ArcSpan degrees and thickens toward its
        // spark, so the whole thing reads as two soft streaks rather than dots.
        static Sprite ArcSprite()
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false) { name = "SearchOrbitArc", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                var p = new Vector2(x + .5f - TexSize / 2f, y + .5f - TexSize / 2f);
                float angle = Mathf.Repeat(Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg, 180f); // both arcs share the shape
                float k = angle / ArcSpan; // 0 at tail end, 1 at the spark
                float a = 0;
                if (k <= 1)
                {
                    float half = Mathf.Lerp(.6f, 2.6f, k);
                    float d = Mathf.Abs(p.magnitude - TexRadius);
                    a = Mathf.Clamp01(half + .5f - d) * k * k;
                }
                px[y * TexSize + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), new Vector2(.5f, .5f), 100);
        }

        static Sprite DotSprite()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SearchOrbitDot", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x + .5f - n / 2f, y + .5f - n / 2f).magnitude / (n / 2f);
                float a = Mathf.Clamp01(1 - d);
                a = a * a * (3 - 2 * a); // soft round falloff, brightest in the middle
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), 100);
        }
    }
}
