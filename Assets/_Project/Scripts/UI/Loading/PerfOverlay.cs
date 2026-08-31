using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// One line in the corner: frame rate, worst frame in the last window, render resolution and
    /// managed heap. Created in Boot and kept across scene loads, so the same number is on screen
    /// in the intro, the menu and a duel.
    ///
    /// <para><b>Why it exists.</b> "It lags" is not a measurement, and a device build has no
    /// console to ask. The difference between 25 fps (an unoptimised player) and 2 fps (something
    /// pathological) points at completely different causes, and neither can be told apart by eye
    /// from a description. This is the cheapest instrument that settles it.</para>
    ///
    /// <para>Its own cost is one <c>Text</c> rewrite every half second on a private canvas, so it
    /// cannot meaningfully skew what it measures. <see cref="Enabled"/> is a PlayerPrefs flag —
    /// tapping the readout hides it, and it stays hidden across launches.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerfOverlay : MonoBehaviour
    {
        private const string PrefsKey = "debug.perf_overlay";
        private const float SampleWindowSec = 0.5f;

        /// <summary>Whether the readout shows. Survives restarts; tapping it turns it off.</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefsKey, 1) != 0;
            set { PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        private static PerfOverlay _instance;

        private TextMeshProUGUI _label;
        private readonly StringBuilder _sb = new StringBuilder(96);
        private float _windowStart;
        private int _frames;
        private float _worstFrameMs;

        /// <summary>Builds the overlay if it isn't up yet. Called from the boot scene.</summary>
        public static void Ensure()
        {
            if (_instance != null || !Enabled) return;

            var go = new GameObject("PerfOverlay");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PerfOverlay>();
            _instance.Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue; // above every screen, including the fight HUD
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390f, 844f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            var plate = new GameObject("Plate", typeof(RectTransform), typeof(CanvasRenderer),
                                       typeof(Image), typeof(Button));
            plate.transform.SetParent(transform, false);
            var plateRt = (RectTransform)plate.transform;
            plateRt.anchorMin = plateRt.anchorMax = plateRt.pivot = new Vector2(1f, 1f);
            plateRt.anchoredPosition = new Vector2(-6f, -44f); // clear of the notch / status bar
            plateRt.sizeDelta = new Vector2(196f, 44f);
            plate.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            plate.GetComponent<Button>().onClick.AddListener(Hide);

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(plate.transform, false);
            _label = labelGo.AddComponent<TextMeshProUGUI>();
            var labelRt = _label.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(6f, 2f);
            labelRt.offsetMax = new Vector2(-6f, -2f);
            _label.fontSize = 11f;
            _label.color = new Color(0.55f, 1f, 0.6f);
            _label.alignment = TextAlignmentOptions.Left;
            _label.raycastTarget = false;
            _label.text = "…";

            _windowStart = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            _frames++;
            float frameMs = Time.unscaledDeltaTime * 1000f;
            if (frameMs > _worstFrameMs) _worstFrameMs = frameMs;

            float elapsed = Time.realtimeSinceStartup - _windowStart;
            if (elapsed < SampleWindowSec) return;

            float fps = _frames / elapsed;

            _sb.Clear();
            _sb.Append(fps.ToString("0")).Append(" fps");
            _sb.Append("  worst ").Append(_worstFrameMs.ToString("0")).Append(" ms");
            _sb.Append('\n');
            _sb.Append(Screen.width).Append('x').Append(Screen.height);
            _sb.Append("  cap ").Append(Application.targetFrameRate);
            _sb.Append("  heap ").Append((System.GC.GetTotalMemory(false) / 1048576L)).Append(" MB");
            _sb.Append('\n');
            // The app answering for itself what the build settings are: a Development Build has
            // its C++ compiled without optimisations on iOS and runs several times slower in every
            // scene. Asking the dashboard is guesswork; this is the build talking.
            _sb.Append(Debug.isDebugBuild ? "DEV BUILD" : "RELEASE");
            _sb.Append("  ").Append(SystemInfo.deviceModel);
            _label.text = _sb.ToString();

            // Red once the frame budget is blown badly enough to be felt as lag.
            _label.color = fps >= 45f ? new Color(0.55f, 1f, 0.6f)
                         : fps >= 20f ? new Color(1f, 0.8f, 0.3f)
                                      : new Color(1f, 0.45f, 0.4f);

            _frames = 0;
            _worstFrameMs = 0f;
            _windowStart = Time.realtimeSinceStartup;
        }

        private void Hide()
        {
            Enabled = false;
            _instance = null;
            Destroy(gameObject);
        }
    }
}
