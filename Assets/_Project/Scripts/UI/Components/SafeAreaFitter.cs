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
            var size = new Vector2Int(Screen.width, Screen.height);
            if (!TryGetAnchors(Screen.safeArea, size, out var anchorMin, out var anchorMax)) return;

            _lastSafeArea = Screen.safeArea;
            _lastOrientation = Screen.orientation;
            _lastScreenSize = size;

            _rt.anchorMin = anchorMin;
            _rt.anchorMax = anchorMax;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
        }

        /// <summary>Ignore transient invalid reports during startup, rotation or app resume.
        /// Keeping the last valid layout prevents the entire UI from collapsing to zero size.</summary>
        public static bool TryGetAnchors(Rect area, Vector2Int size, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.one;
            if (size.x <= 0 || size.y <= 0 || !Finite(area.x) || !Finite(area.y)
                || !Finite(area.width) || !Finite(area.height) || area.width <= 0 || area.height <= 0)
                return false;
            min = new Vector2(Mathf.Clamp01(area.xMin / size.x), Mathf.Clamp01(area.yMin / size.y));
            max = new Vector2(Mathf.Clamp01(area.xMax / size.x), Mathf.Clamp01(area.yMax / size.y));
            return max.x > min.x && max.y > min.y;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
