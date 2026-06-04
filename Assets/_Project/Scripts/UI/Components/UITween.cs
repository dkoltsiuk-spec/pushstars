using System;
using System.Collections;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Tiny self-contained tween toolkit (no DOTween dependency) for the game-feel
    /// "juice" used across Push Stars screens — pop-ins, slide-ins, fades.
    ///
    /// All tweens run on <b>unscaled</b> time so UI animation keeps playing even when
    /// Time.timeScale is 0 (pause / countdown). Drive them with StartCoroutine from any
    /// MonoBehaviour. Easing functions are static and allocation-free.
    /// </summary>
    public static class UITween
    {
        // ── Easing (https://easings.net) ─────────────────────────────────────────────
        public static float Linear(float x)     => x;
        public static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);
        public static float EaseInQuad(float x)  => x * x;
        public static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
        public static float EaseInCubic(float x)  => x * x * x;

        /// <summary>Smooth decelerating settle, no overshoot — the calmest "arrive" curve.</summary>
        public static float EaseOutSine(float x) => Mathf.Sin((x * Mathf.PI) / 2f);

        /// <summary>Overshoots past the target then settles — the classic "pop" feel.</summary>
        public static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float xm = x - 1f;
            return 1f + c3 * xm * xm * xm + c1 * xm * xm;
        }

        /// <summary>Gentle overshoot — half the kick of <see cref="EaseOutBack"/>; reads as alive, not jumpy.</summary>
        public static float EaseOutBackSoft(float x)
        {
            const float c1 = 0.9f;
            const float c3 = c1 + 1f;
            float xm = x - 1f;
            return 1f + c3 * xm * xm * xm + c1 * xm * xm;
        }

        /// <summary>Springy overshoot with a couple of bounces — great for hero elements.</summary>
        public static float EaseOutElastic(float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            const float c4 = (2f * Mathf.PI) / 3f;
            return Mathf.Pow(2f, -10f * x) * Mathf.Sin((x * 10f - 0.75f) * c4) + 1f;
        }

        // ── Tweens ───────────────────────────────────────────────────────────────────
        public static IEnumerator Scale(Transform t, Vector3 from, Vector3 to,
                                         float duration, float delay, Func<float, float> ease)
        {
            if (t == null) yield break;
            t.localScale = from;                                   // hide/offset NOW so the
            if (delay > 0f) yield return WaitUnscaled(delay);      // stagger delay holds the
            t.localScale = from;                                   // start state, not the rest state
            for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
            {
                float k = ease(Mathf.Clamp01(e / duration));
                t.localScale = Vector3.LerpUnclamped(from, to, k);
                yield return null;
            }
            t.localScale = to;
        }

        public static IEnumerator Move(RectTransform rt, Vector2 from, Vector2 to,
                                       float duration, float delay, Func<float, float> ease)
        {
            if (rt == null) yield break;
            rt.anchoredPosition = from;                            // offset NOW so the stagger
            if (delay > 0f) yield return WaitUnscaled(delay);      // delay holds the start state,
            rt.anchoredPosition = from;                            // not the rest position
            for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
            {
                float k = ease(Mathf.Clamp01(e / duration));
                rt.anchoredPosition = Vector2.LerpUnclamped(from, to, k);
                yield return null;
            }
            rt.anchoredPosition = to;
        }

        public static IEnumerator Fade(CanvasGroup cg, float from, float to,
                                       float duration, float delay)
        {
            if (cg == null) yield break;
            cg.alpha = from;                                       // hide NOW so the stagger
            if (delay > 0f) yield return WaitUnscaled(delay);      // delay holds it hidden,
            cg.alpha = from;                                       // not at full opacity
            for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
            {
                cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(e / duration));
                yield return null;
            }
            cg.alpha = to;
        }

        private static IEnumerator WaitUnscaled(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
                yield return null;
        }
    }
}
