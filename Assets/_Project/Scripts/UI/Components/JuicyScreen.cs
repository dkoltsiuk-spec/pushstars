using System;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Reusable game-feel screen transition (Brawl Stars / Clash of Clans style).
    ///
    /// On <see cref="PlayIn"/> the whole panel fades in and pops from a slightly smaller
    /// scale, while each registered <see cref="Element"/> animates in on a staggered delay
    /// (drop from top, slide up from bottom, or an overshooting pop). On <see cref="PlayOut"/>
    /// the panel snaps shut with a quick scale-down + fade.
    ///
    /// Designed to be dropped on any overlay/panel root and configured by the Editor build
    /// tools — nothing here is specific to the Search-Opponent screen.
    /// </summary>
    public class JuicyScreen : MonoBehaviour
    {
        public enum Entrance { Pop, PopBig, FromTop, FromBottom, FromLeft, FromRight, FadeOnly }

        [Serializable]
        public class Element
        {
            public RectTransform target;
            public Entrance       entrance     = Entrance.Pop;
            public float          moveDistance = 100f; // px travelled for slide entrances
            public float          delay        = 0f;   // stagger offset
            public float          duration     = 0.4f;

            // Runtime cache (resolved in Awake).
            [NonSerialized] public CanvasGroup cg;
            [NonSerialized] public Vector2     basePos;
            [NonSerialized] public Vector3     baseScale;
        }

        [Header("Whole-screen motion")]
        [SerializeField] private CanvasGroup   _overlayGroup;
        [SerializeField] private RectTransform _root;       // scaled for the panel pop
        [SerializeField] private float _bgFadeIn     = 0.26f;
        [SerializeField] private float _rootPopFrom  = 0.97f;
        [SerializeField] private float _rootPopTime  = 0.42f;
        [SerializeField] private float _outDuration  = 0.20f;
        [SerializeField] private float _rootPopOut   = 0.94f;

        [Header("Staggered elements")]
        [SerializeField] private Element[] _elements;

        private bool _cached;

        private void Awake() => Cache();

        private void Cache()
        {
            if (_cached || _elements == null) return;
            foreach (var el in _elements)
            {
                if (el?.target == null) continue;
                el.cg = el.target.GetComponent<CanvasGroup>();
                if (el.cg == null) el.cg = el.target.gameObject.AddComponent<CanvasGroup>();
                el.basePos   = el.target.anchoredPosition;
                el.baseScale = el.target.localScale;
            }
            _cached = true;
        }

        /// <summary>Plays the staggered entrance. Call right after enabling the panel.</summary>
        public void PlayIn()
        {
            Cache();
            StopAllCoroutines();

            if (_root != null) _root.localScale = Vector3.one;
            if (_overlayGroup != null)
            {
                _overlayGroup.alpha = 0f;
                StartCoroutine(UITween.Fade(_overlayGroup, 0f, 1f, _bgFadeIn, 0f));
            }
            if (_root != null)
                StartCoroutine(UITween.Scale(_root, Vector3.one * _rootPopFrom, Vector3.one,
                                             _rootPopTime, 0f, UITween.EaseOutSine));

            if (_elements == null) return;
            foreach (var el in _elements)
                AnimateIn(el);
        }

        /// <summary>Snaps the panel shut, then invokes <paramref name="onComplete"/>.</summary>
        public void PlayOut(Action onComplete)
        {
            StopAllCoroutines();
            StartCoroutine(PlayOutRoutine(onComplete));
        }

        private void AnimateIn(Element el)
        {
            if (el?.target == null) return;

            // Every element fades in over its own window.
            if (el.cg != null)
                StartCoroutine(UITween.Fade(el.cg, 0f, 1f, el.duration, el.delay));

            switch (el.entrance)
            {
                case Entrance.Pop:
                    StartCoroutine(UITween.Scale(el.target, el.baseScale * 0.85f, el.baseScale,
                                                 el.duration, el.delay, UITween.EaseOutBackSoft));
                    break;
                case Entrance.PopBig:
                    StartCoroutine(UITween.Scale(el.target, el.baseScale * 0.7f, el.baseScale,
                                                 el.duration, el.delay, UITween.EaseOutBackSoft));
                    break;
                case Entrance.FromTop:
                    StartCoroutine(UITween.Move(el.target, el.basePos + Vector2.up * el.moveDistance,
                                                el.basePos, el.duration, el.delay, UITween.EaseOutCubic));
                    break;
                case Entrance.FromBottom:
                    StartCoroutine(UITween.Move(el.target, el.basePos + Vector2.down * el.moveDistance,
                                                el.basePos, el.duration, el.delay, UITween.EaseOutCubic));
                    break;
                case Entrance.FromLeft:
                    StartCoroutine(UITween.Move(el.target, el.basePos + Vector2.left * el.moveDistance,
                                                el.basePos, el.duration, el.delay, UITween.EaseOutCubic));
                    break;
                case Entrance.FromRight:
                    StartCoroutine(UITween.Move(el.target, el.basePos + Vector2.right * el.moveDistance,
                                                el.basePos, el.duration, el.delay, UITween.EaseOutCubic));
                    break;
                case Entrance.FadeOnly:
                    break; // fade above is enough
            }
        }

        private System.Collections.IEnumerator PlayOutRoutine(Action onComplete)
        {
            if (_root != null)
                StartCoroutine(UITween.Scale(_root, _root.localScale, Vector3.one * _rootPopOut,
                                             _outDuration, 0f, UITween.EaseInCubic));

            if (_overlayGroup != null)
                yield return UITween.Fade(_overlayGroup, _overlayGroup.alpha, 0f, _outDuration, 0f);
            else
                yield return null;

            onComplete?.Invoke();
        }
    }
}
