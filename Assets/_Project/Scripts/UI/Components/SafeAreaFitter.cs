using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Adjusts this RectTransform to respect the device safe area (iPhone notch, Dynamic Island,
    /// Android display cutouts). Attach to a full-screen panel that should avoid the notch.
    /// The panel must be a direct child of a Screen Space – Overlay or Screen Space – Camera canvas.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform      _rt;
        private Rect               _lastSafeArea    = Rect.zero;
        private ScreenOrientation  _lastOrientation = ScreenOrientation.AutoRotation;
        private Vector2Int         _lastScreenSize;

        private void Awake()
        {
            _rt = GetComponent<RectTransform>();
            Apply();
        }

        private void Update()
        {
            var currentSize = new Vector2Int(Screen.width, Screen.height);
            if (_lastSafeArea != Screen.safeArea
                || _lastOrientation != Screen.orientation
                || _lastScreenSize  != currentSize)
            {
                Apply();
            }
        }

        private void Apply()
        {
            _lastSafeArea    = Screen.safeArea;
            _lastOrientation = Screen.orientation;
            _lastScreenSize  = new Vector2Int(Screen.width, Screen.height);

            // Guard against degenerate screen sizes (can happen in Device Simulator
            // before the first frame is fully initialised).
            if (Screen.width <= 0 || Screen.height <= 0) return;

            var safeArea = _lastSafeArea;

            // Clamp safe area to actual screen bounds — the Device Simulator can
            // occasionally report values slightly outside [0, screenSize].
            safeArea.xMin = Mathf.Max(safeArea.xMin, 0);
            safeArea.yMin = Mathf.Max(safeArea.yMin, 0);
            safeArea.xMax = Mathf.Min(safeArea.xMax, Screen.width);
            safeArea.yMax = Mathf.Min(safeArea.yMax, Screen.height);

            var anchorMin = safeArea.position;
            var anchorMax = safeArea.position + safeArea.size;

            anchorMin.x /= Screen.width;
            anchorMin.y /= Screen.height;
            anchorMax.x /= Screen.width;
            anchorMax.y /= Screen.height;

            // Final safety clamp so anchors never leave [0, 1].
            anchorMin.x = Mathf.Clamp01(anchorMin.x);
            anchorMin.y = Mathf.Clamp01(anchorMin.y);
            anchorMax.x = Mathf.Clamp01(anchorMax.x);
            anchorMax.y = Mathf.Clamp01(anchorMax.y);

            _rt.anchorMin = anchorMin;
            _rt.anchorMax = anchorMax;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }
    }
}
