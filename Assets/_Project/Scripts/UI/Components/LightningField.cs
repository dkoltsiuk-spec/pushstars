using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Animates a pre-built lattice of background lightning bolts: a slow, infinite upward
    /// drift. Each bolt scrolls up and wraps back to the bottom by a whole lattice height,
    /// so the field never repeats visibly.
    ///
    /// The sprite keeps its own transparency across the centre; only a light edge fade is
    /// applied near the screen borders (so bolts gently disappear at the edges as they scroll).
    ///
    /// All distances are in canvas reference points (390×844). Build the lattice with
    /// <see cref="Build"/> — one implementation shared by the Main VS screen, its overlays and
    /// the pre-duel card.
    /// </summary>
    public class LightningField : MonoBehaviour
    {
        // Reference canvas the lattice geometry and the edge fade are sized against.
        private const float RefW = 390f;
        private const float RefH = 844f;

        [Header("Motion")]
        [SerializeField] private float _speed   = 16f;    // upward points / second
        [SerializeField] private float _spanY   = 1024f;  // lattice height — wrap distance
        [SerializeField] private float _topWrap = 524f;   // y above which a bolt wraps down

        [Header("Light edge fade")]
        [SerializeField] private float _halfW     = 195f;
        [SerializeField] private float _halfH     = 422f;
        [SerializeField] private float _edgeStart = 0.15f; // only the small centre core stays full
        [SerializeField] private float _edgeEnd   = 1.0f;  // max fade reached at the screen edge
        [SerializeField] private float _edgeFade  = 0.90f; // up to -90% opacity at the very edge

        private RectTransform[] _rts;
        private Image[]         _imgs;

        private void Awake()
        {
            int n = transform.childCount;
            _rts  = new RectTransform[n];
            _imgs = new Image[n];
            for (int i = 0; i < n; i++)
            {
                var child = transform.GetChild(i);
                _rts[i]  = child as RectTransform;
                _imgs[i] = child.GetComponent<Image>();
            }
        }

        private void Update()
        {
            float dy = _speed * Time.deltaTime;

            for (int i = 0; i < _rts.Length; i++)
            {
                var rt = _rts[i];
                if (rt == null) continue;

                var p = rt.anchoredPosition;
                p.y += dy;
                if (p.y > _topWrap) p.y -= _spanY; // wrap one full lattice height down
                rt.anchoredPosition = p;

                if (_imgs[i] != null)
                {
                    // Centre is untouched (a=1); only the screen border loses _edgeFade (15%).
                    float edge = Mathf.Max(Mathf.Abs(p.x) / _halfW, Mathf.Abs(p.y) / _halfH);
                    float a    = 1f - _edgeFade * Mathf.SmoothStep(_edgeStart, _edgeEnd, edge);

                    var c = _imgs[i].color;
                    c.a = a;
                    _imgs[i].color = c;
                }
            }
        }

        /// <summary>
        /// Builds the full drifting-bolt lattice as a stretched <c>LightningPattern</c> child of
        /// <paramref name="parent"/> and returns the field that animates it. Bolts sit in a
        /// staggered checkerboard (every other row shifted half a cell), the same edge-fade
        /// gradient baked into their start colour so the look reads before the first frame.
        ///
        /// <para>Shared by <c>MainVsScreenSetup</c> (edit-mode scene authoring) and the pre-duel
        /// card (runtime): the geometry is tied to the project's 390×844 reference canvas, so it is
        /// the same field everywhere and there is one place to tune it.</para>
        /// </summary>
        public static LightningField Build(RectTransform parent, Sprite bolt)
        {
            const float iconSize = 76f, spacingX = 90f, spacingY = 102f, tilt = -10f;
            const float scrollSpeed = 16f;
            const float edgeStart = 0.15f, edgeEnd = 1f, edgeFade = 0.90f;

            var container = new GameObject("LightningPattern", typeof(RectTransform))
                .GetComponent<RectTransform>();
            container.SetParent(parent, false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.offsetMin = container.offsetMax = Vector2.zero;

            float halfW = RefW * 0.5f, halfH = RefH * 0.5f;
            int cols = Mathf.CeilToInt(RefW / spacingX) + 2;

            // Tall enough to cover the screen plus a margin top & bottom; even row count so
            // wrapping by the full lattice height preserves the checkerboard stagger.
            int rows = Mathf.CeilToInt((RefH + 2f * spacingY) / spacingY) + 1;
            if (rows % 2 != 0) rows++;
            float spanY   = rows * spacingY;  // wrap distance for the animation
            float topWrap = halfH + spacingY; // bolts wrap once they pass this (off-screen top)

            for (int row = 0; row < rows; row++)
            {
                float y         = topWrap - (row + 0.5f) * spacingY; // tiles [topWrap - spanY, topWrap]
                float rowOffset = (row % 2 == 0) ? 0f : spacingX * 0.5f; // checkerboard stagger
                for (int col = -1; col < cols; col++)
                {
                    float x = -halfW + (col + 0.5f) * spacingX + rowOffset;

                    var rt = new GameObject($"L{row}_{col}", typeof(RectTransform), typeof(Image))
                        .GetComponent<RectTransform>();
                    rt.SetParent(container, false);
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(x, y);
                    rt.sizeDelta        = new Vector2(iconSize, iconSize);
                    rt.localRotation    = Quaternion.Euler(0f, 0f, tilt);

                    // Bake the same edge-fade gradient so the transition shows before the first
                    // Update; the field recomputes it per frame as the bolts scroll.
                    float edge  = Mathf.Max(Mathf.Abs(x) / halfW, Mathf.Abs(y) / halfH);
                    float alpha = 1f - edgeFade * Mathf.SmoothStep(edgeStart, edgeEnd, edge);

                    var img = rt.GetComponent<Image>();
                    img.sprite         = bolt;
                    img.color          = new Color(1f, 1f, 1f, alpha);
                    img.preserveAspect = true; // keep the bolt's real proportions (no vertical squish)
                    img.raycastTarget  = false;
                }
            }

            var field = container.gameObject.AddComponent<LightningField>();
            field._speed     = scrollSpeed;
            field._spanY     = spanY;
            field._topWrap   = topWrap;
            field._halfW     = halfW;
            field._halfH     = halfH;
            field._edgeStart = edgeStart;
            field._edgeEnd   = edgeEnd;
            field._edgeFade  = edgeFade;
            return field;
        }
    }
}
