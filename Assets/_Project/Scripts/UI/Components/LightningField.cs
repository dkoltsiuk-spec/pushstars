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
    /// All distances are in canvas reference points (390×844). Built by MainVsScreenSetup.
    /// </summary>
    public class LightningField : MonoBehaviour
    {
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
    }
}
