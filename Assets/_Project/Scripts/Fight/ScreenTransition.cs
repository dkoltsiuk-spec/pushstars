using System.Collections;
using PushStars.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// Seam between reward scenes. The old screen is covered before the (synchronous) load, so the
    /// load hitch is never seen, and the new screen is revealed from the centre once its first
    /// frames have settled. Sound effects live on the persistent GameAudio object, so a transition
    /// sound started on the old screen keeps playing through the load and finishes on the new one.
    /// Taps are swallowed while the cover is up.
    /// </summary>
    public sealed class ScreenTransition : MonoBehaviour
    {
        public enum Reveal
        {
            /// <summary>Iris opens from the centre, where the next screen's hero element sits.</summary>
            Iris,
            /// <summary>The next screen starts dark itself (the Aura stamp); just drop the cover.</summary>
            Instant
        }

        private const float RevealSeconds = .42f;
        private static readonly Color Ink = new Color(.02f, .01f, .05f, 1);
        private static ScreenTransition _instance;
        private TransitionIrisGraphic _iris;
        private AsyncOperation _preload;
        private string _preloadScene;
        private bool _busy;
        private float _revealProgress = 1;

        /// <summary>True from the moment the cover starts until the reveal completes.</summary>
        public static bool IsBusy => _instance != null && _instance._busy;
        /// <summary>0..1 through the reveal of the new screen; 1 when no transition is running.</summary>
        public static float RevealProgress => _instance != null ? _instance._revealProgress : 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <param name="coverDelay">The old screen keeps playing its exit this long before the
        /// cover starts. The next scene already loads in the background meanwhile.</param>
        /// <param name="coverSeconds">How long the old screen takes to go dark. Callers time this
        /// so the cover completes on the beat of their exit sound.</param>
        /// <param name="sound">Played as the cover starts; its tail carries across the load.</param>
        public static void Go(string scene, float coverDelay, float coverSeconds, Reveal reveal, SoundCue? sound = null)
        {
            var transition = Ensure();
            if (transition._busy) return;
            transition.StartCoroutine(transition.Run(scene, Mathf.Max(0, coverDelay), Mathf.Max(0, coverSeconds), reveal, sound));
        }

        /// <summary>
        /// Starts streaming the scene a screen will most likely leave to, while the player is still
        /// looking at it, so the eventual transition only has to activate it. Call it at a moment
        /// where a loading hitch cannot be seen (a dark first frame, a scene switch). Unity keeps
        /// later loads queued behind a held preload, so only preload the screen's real exit.
        /// </summary>
        public static void Preload(string scene)
        {
            var transition = Ensure();
            if (transition._preload != null || string.IsNullOrEmpty(scene) ||
                !Application.CanStreamedLevelBeLoaded(scene)) return;
            transition._preload = SceneManager.LoadSceneAsync(scene);
            transition._preload.allowSceneActivation = false;
            transition._preloadScene = scene;
        }

        /// <summary>
        /// Waits out a freshly loaded scene's heavy first frames (activation, first render, asset
        /// uploads, editor bookkeeping) until a few frames in a row run smoothly. Entrances start
        /// after it, so a load stall lands on a still frame instead of freezing mid-animation.
        /// </summary>
        public static IEnumerator Settle(float maxSeconds = 1f)
        {
            int smooth = 0;
            for (float waited = 0; waited < maxSeconds && smooth < SmoothFramesNeeded; waited += Time.unscaledDeltaTime)
            {
                yield return null;
                smooth = Time.unscaledDeltaTime < SmoothFrameSeconds ? smooth + 1 : 0;
            }
        }
        private const int SmoothFramesNeeded = 3;
        private const float SmoothFrameSeconds = .05f;

        private static ScreenTransition Ensure()
        {
            if (_instance != null) return _instance;
            var host = new GameObject("Screen Transition", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            DontDestroyOnLoad(host);
            var canvas = host.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above every scene canvas; the home reward flight (30000) starts during the reveal.
            canvas.sortingOrder = 31000;
            var iris = new GameObject("Iris", typeof(RectTransform), typeof(CanvasRenderer), typeof(TransitionIrisGraphic));
            iris.transform.SetParent(host.transform, false);
            var rect = (RectTransform)iris.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            _instance = host.AddComponent<ScreenTransition>();
            _instance._iris = iris.GetComponent<TransitionIrisGraphic>();
            _instance._iris.color = Ink;
            _instance._iris.raycastTarget = false;
            return _instance;
        }

        private IEnumerator Run(string scene, float coverDelay, float coverSeconds, Reveal reveal, SoundCue? sound)
        {
            _busy = true; _revealProgress = 0;
            _iris.raycastTarget = true;
            _iris.Hole = 0;
            // Normally the scene was preloaded; only activation (Awake, Start, first frame) is left
            // for the moment under the cover.
            var preload = _preload;
            bool preloaded = preload != null && _preloadScene == scene;
            _preload = null; _preloadScene = null;
            for (float t = 0; t < coverDelay; t += Time.unscaledDeltaTime) yield return null;
            if (sound.HasValue) GameAudio.Play(sound.Value);
            for (float t = 0; t < coverSeconds; t += Time.unscaledDeltaTime)
            {
                float u = t / coverSeconds;
                _iris.Cover = u * u * (3 - 2 * u);
                yield return null;
            }
            _iris.Cover = 1;
            // Present one fully covered frame before activation freezes the screen on it.
            yield return null;
            if (preload != null)
            {
                // A held preload blocks every later load, so it must be released even if unused.
                preload.allowSceneActivation = true;
                while (!preload.isDone) yield return null;
            }
            if (!preloaded) SceneManager.LoadScene(scene);
            // Let the new scene run Start and its heavy first frames under the cover.
            yield return Settle();
            if (reveal == Reveal.Iris)
            {
                for (float t = 0; t < RevealSeconds; t += Time.unscaledDeltaTime)
                {
                    _revealProgress = t / RevealSeconds;
                    float eased = 1 - Mathf.Pow(1 - _revealProgress, 3);
                    _iris.Hole = eased;
                    yield return null;
                }
            }
            _iris.Cover = 0; _iris.Hole = 0;
            _iris.raycastTarget = false;
            _revealProgress = 1; _busy = false;
        }
    }
}
