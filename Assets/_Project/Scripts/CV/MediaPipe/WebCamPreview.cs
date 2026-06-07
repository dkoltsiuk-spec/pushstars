using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Debug-only full-screen preview of the camera feed driving <see cref="MediaPipePoseSource"/>.
    /// Draws the live <see cref="WebCamTexture"/> behind the OnGUI HUD so you can see yourself while
    /// testing. Throwaway — the production calibration screen uses a proper RawImage (phase 08 UI).
    ///
    /// iOS webcams deliver a landscape frame plus a <see cref="WebCamTexture.videoRotationAngle"/> and a
    /// mirror flag that vary by device. Rather than hard-code one combination, the on-screen buttons let
    /// you fix the orientation live (no rebuild). Tweak until upright, then we can bake the defaults.
    /// </summary>
    public sealed class WebCamPreview : MonoBehaviour
    {
        [SerializeField] private MediaPipePoseSource _source;
        [Tooltip("WebCamTexture usually appears upside-down in IMGUI; flip to correct it.")]
        [SerializeField] private bool _flipVertical = false;
        [Tooltip("Mirror horizontally (natural for a front/selfie preview).")]
        [SerializeField] private bool _flipHorizontal = true;
        [Tooltip("Rotation in degrees (CW). -1 = auto: use WebCamTexture.videoRotationAngle (iOS reports 90°).")]
        [SerializeField] private int _rotationOverride = -1;
        [Tooltip("Show the on-screen orientation controls (⟳ / Flip H / Flip V).")]
        [SerializeField] private bool _showControls = true;

        // Live state (seeded from the serialized defaults) so the buttons can adjust orientation at
        // runtime without a rebuild. -1 in _rotationOverride means "use the device's reported angle".
        private bool _initialized;
        private int  _rotation;       // resolved CW degrees actually applied
        private bool _autoRotation;   // true while following videoRotationAngle
        private bool _flipH, _flipV;

        private void EnsureInit(WebCamTexture tex)
        {
            if (_initialized) return;
            _autoRotation = _rotationOverride < 0;
            _rotation     = _autoRotation ? tex.videoRotationAngle : _rotationOverride;
            _flipH        = _flipHorizontal;
            _flipV        = _flipVertical;
            _initialized  = true;
        }

        private void OnGUI()
        {
            var tex = _source != null ? _source.CameraTexture : null;
            if (tex == null || tex.width <= 16) return;

            EnsureInit(tex);
            if (_autoRotation) _rotation = tex.videoRotationAngle; // keep following the device until overridden

            float sw = Screen.width, sh = Screen.height;
            var center = new Vector2(sw * 0.5f, sh * 0.5f);

            // ── Draw the camera feed behind the HUD ──
            GUI.depth = 1; // lower depth renders on top → 1 = behind the HUD labels
            var m = GUI.matrix;

            int angle = ((_rotation % 360) + 360) % 360;
            if (angle != 0)
                GUIUtility.RotateAroundPivot(angle, center);

            float fx = _flipH ? -1f : 1f;
            float fy = _flipV ? -1f : 1f;
            if (fx < 0f || fy < 0f)
                GUIUtility.ScaleAroundPivot(new Vector2(fx, fy), center);

            // After a 90°/270° rotation the screen axes are swapped, so draw into a rect with width/height
            // swapped and centered; ScaleAndCrop then fills the whole screen (no black bars).
            Rect rect = (angle == 90 || angle == 270)
                ? new Rect(center.x - sh * 0.5f, center.y - sw * 0.5f, sh, sw)
                : new Rect(0f, 0f, sw, sh);

            GUI.DrawTexture(rect, tex, ScaleMode.ScaleAndCrop);
            GUI.matrix = m;

            // ── Live orientation controls (on top, not transformed) ──
            if (_showControls)
                DrawControls(sw, sh, angle);
        }

        private void DrawControls(float sw, float sh, int angle)
        {
            GUI.depth = 0; // on top of the feed

            float bw = Mathf.Min(150f, sw * 0.28f);
            float bh = Mathf.Max(48f, sh * 0.05f);
            float pad = 8f;
            float y = sh - bh - pad - 24f;

            var label = $"rot {angle}  H:{(_flipH ? "on" : "off")}  V:{(_flipV ? "on" : "off")}{(_autoRotation ? "  (auto)" : "")}";
            GUI.Label(new Rect(pad, sh - 22f, sw - pad, 22f), label);

            if (GUI.Button(new Rect(pad, y, bw, bh), "Rotate 90"))
            {
                _autoRotation = false;                 // manual from now on
                _rotation = ((_rotation + 90) % 360 + 360) % 360;
            }
            if (GUI.Button(new Rect(pad * 2 + bw, y, bw, bh), "Flip H"))
                _flipH = !_flipH;
            if (GUI.Button(new Rect(pad * 3 + bw * 2, y, bw, bh), "Flip V"))
                _flipV = !_flipV;
        }
    }
}
