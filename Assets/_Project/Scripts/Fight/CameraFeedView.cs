using UnityEngine;
using UnityEngine.UI;
using PushStars.CV;

namespace PushStars.Fight
{
    /// <summary>
    /// Full-screen camera background for the fight scene, rendered through UGUI (a RawImage)
    /// so the design-system HUD canvas can draw OVER it — the IMGUI WebCamPreview from the CV
    /// test always paints on top of overlay canvases and can't be used under a real UI.
    ///
    /// Orientation follows the device-verified WebCamPreview defaults: on phones the frame
    /// arrives rotated (90° CW on the iPhone front camera), on desktop webcams it's upright —
    /// the view rotates the RawImage and cover-scales it to fill its parent. The pose source is
    /// referenced as <see cref="ICameraFeed"/> (a MonoBehaviour field), so this assembly never
    /// depends on the define-gated MediaPipe adapter; with a mock source there is simply no
    /// texture and the image stays hidden.
    /// </summary>
    public sealed class CameraFeedView : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour _feedBehaviour; // must implement ICameraFeed
        [SerializeField] private RawImage _image;
        [Tooltip("Rotation (deg CW) applied on mobile. 90 = verified iPhone front-camera default.")]
        [SerializeField] private int _mobileRotation = 90;
        [Tooltip("Mirror the feed horizontally (selfie style). Off = matches the CV test preview.")]
        [SerializeField] private bool _mirror = false;

        private ICameraFeed _feed;

        private void Awake()
        {
            _feed = _feedBehaviour as ICameraFeed;
            if (_image != null) _image.enabled = false;
        }

        private void LateUpdate()
        {
            var tex = _feed != null ? _feed.CameraTexture : null;
            if (_image == null) return;
            if (tex == null || tex.width <= 16)
            {
                _image.enabled = false;
                return;
            }

            _image.enabled = true;
            if (_image.texture != tex) _image.texture = tex;

            int angle = Application.isMobilePlatform ? _mobileRotation : tex.videoRotationAngle;
            angle = ((angle % 360) + 360) % 360;

            var parent = _image.rectTransform.parent as RectTransform;
            if (parent == null) return;
            Vector2 area = parent.rect.size;

            bool quarter = angle == 90 || angle == 270;
            float visualW = quarter ? tex.height : tex.width;
            float visualH = quarter ? tex.width : tex.height;
            if (visualW < 1f || visualH < 1f) return;
            float cover = Mathf.Max(area.x / visualW, area.y / visualH);

            var rt = _image.rectTransform;
            rt.sizeDelta = new Vector2(tex.width, tex.height);
            // UGUI z-rotation is CCW (y-up); the feed rotation is specified CW in screen space.
            rt.localEulerAngles = new Vector3(0f, 0f, -angle);
            // localScale applies BEFORE the rotation, so a screen-horizontal mirror maps to a
            // local-y flip when the frame is quarter-rotated (R⁻¹·FlipX·R = FlipY at ±90°).
            float sx = cover, sy = cover;
            if (_mirror) { if (quarter) sy = -sy; else sx = -sx; }
            rt.localScale = new Vector3(sx, sy, 1f);
        }
    }
}
