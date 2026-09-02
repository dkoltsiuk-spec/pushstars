using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// The leader line that ties a callout to the thing it is talking about: a dot on the label, an
    /// elbow, a dot on the subject.
    ///
    /// <para><b>Why it is computed and not drawn.</b> The comp puts the line at one exact place on
    /// one exact screen size, and the moment the canvas scaler or a safe area moves either end by a
    /// few points, a baked line points at nothing — worse than no line, because it now claims the
    /// wrong thing. Both ends are named as a fraction of a RectTransform instead, so the line is
    /// re-derived from wherever those two rects actually ended up. Move the callout, resize the
    /// artwork, run it on a tablet: the line still lands on the corner it was aimed at.</para>
    ///
    /// <para>Built from four plain Images rather than a generated mesh. An elbow is two rectangles
    /// and two dots; a custom <c>MaskableGraphic</c> would buy dashes and mitred joins, and cost a
    /// renderer nobody can see into from the Inspector.</para>
    /// </summary>
    [ExecuteAlways]
    public sealed class CalloutLine : MonoBehaviour
    {
        [Header("Ends")]
        [SerializeField] private RectTransform _from;

        [Tooltip("Where on _from the line starts, as a fraction of its rect: (0,0) is the " +
                 "bottom-left corner, (1,1) the top-right.")]
        [SerializeField] private Vector2 _fromPoint = new Vector2(0.09f, 0.66f);

        [SerializeField] private RectTransform _to;

        [Tooltip("Where on _to the line lands, in the same fractions.")]
        [SerializeField] private Vector2 _toPoint = new Vector2(0.88f, 0.92f);

        [Header("Parts")]
        [SerializeField] private RectTransform _horizontal;
        [SerializeField] private RectTransform _vertical;
        [SerializeField] private RectTransform _fromDot;
        [SerializeField] private RectTransform _toDot;

        [SerializeField, Range(1f, 8f)] private float _thickness = 2f;
        [SerializeField, Range(2f, 20f)] private float _dotSize = 8f;

        private RectTransform _rt;
        private Vector2 _lastFrom;
        private Vector2 _lastTo;
        private bool _drawn;

        private void OnEnable()
        {
            _rt = (RectTransform)transform;
            _drawn = false;
            Rebuild();
        }

        // Every frame, but almost never any work: the two ends are compared first and the whole
        // thing early-outs while they sit still. A layout pass can move a parent at any time —
        // orientation, safe area, a font that finished loading — and none of those announce
        // themselves to a component that is not in the layout system.
        private void LateUpdate() => Rebuild();

        /// <summary>Re-derives the elbow from where its two ends currently are. Public so the tool
        /// that builds the scene can draw the line once, at edit time, instead of saving a scene
        /// whose line only appears on the first frame of play.</summary>
        public void Rebuild()
        {
            if (_from == null || _to == null) return;
            if (_rt == null) _rt = (RectTransform)transform;

            Vector2 a = LocalPoint(_from, _fromPoint);
            Vector2 b = LocalPoint(_to, _toPoint);
            if (_drawn && a == _lastFrom && b == _lastTo) return;
            _lastFrom = a;
            _lastTo = b;
            _drawn = true;

            // Sideways out of the callout, then straight down onto the subject. Dropping first
            // would run the line through the label's own text on the way out.
            var corner = new Vector2(b.x, a.y);

            // Each segment is a hair longer than the gap it spans, so the corner is filled by both
            // of them and the join has no notch in it.
            Place(_horizontal, (a + corner) * 0.5f, new Vector2(Mathf.Abs(b.x - a.x) + _thickness, _thickness));
            Place(_vertical, (corner + b) * 0.5f, new Vector2(_thickness, Mathf.Abs(b.y - a.y) + _thickness));
            Place(_fromDot, a, new Vector2(_dotSize, _dotSize));
            Place(_toDot, b, new Vector2(_dotSize, _dotSize));
        }

        /// <summary>A fraction of <paramref name="target"/>'s rect, expressed in this line's own
        /// space. Goes through world coordinates because the two are rarely siblings — the callout
        /// and its subject sit wherever the page put them.</summary>
        private Vector2 LocalPoint(RectTransform target, Vector2 normalized)
        {
            Rect r = target.rect;
            Vector3 world = target.TransformPoint(new Vector3(
                Mathf.Lerp(r.xMin, r.xMax, normalized.x),
                Mathf.Lerp(r.yMin, r.yMax, normalized.y), 0f));
            return _rt.InverseTransformPoint(world);
        }

        /// <summary>Centres a part on a point in this line's space. The anchors are rewritten every
        /// time rather than trusted: these four rects exist only to be positioned from here, and a
        /// stray anchor edit in the Inspector would otherwise bend the line for good.</summary>
        private static void Place(RectTransform rt, Vector2 center, Vector2 size)
        {
            if (rt == null) return;
            var middle = new Vector2(0.5f, 0.5f);
            rt.anchorMin = middle;
            rt.anchorMax = middle;
            rt.pivot = middle;
            rt.anchoredPosition = center;
            rt.sizeDelta = size;
        }
    }
}
