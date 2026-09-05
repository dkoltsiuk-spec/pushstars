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
    /// Sensor correction and upright rotation come from the source's captured metadata; selfie
    /// reflection comes from the avatar anchor. The view cover-scales its parent. The source is
    /// referenced as <see cref="ICameraFeed"/> (a MonoBehaviour field), so this assembly never
    /// depends on the define-gated MediaPipe adapter; with a mock source there is simply no
    /// texture and the image stays hidden.
    /// </summary>
    public sealed class CameraFeedView : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour _feedBehaviour; // must implement ICameraFeed
        [SerializeField] private RawImage _image;
        [Tooltip("Use the same selfie reflection as the avatar and the skeleton overlay.")]
        [SerializeField] private AvatarMirrorAnchor _anchor;
        [Tooltip("Standalone preview fallback. Ignored when an avatar anchor is connected.")]
        [SerializeField] private bool _mirror = false;

        private ICameraFeed _feed;
        private WebCamTexture _fallbackTexture;
        private CameraFrameOrientation _fallbackOrientation;
        private bool _hasFallbackOrientation;
        public bool MirrorHorizontally => _anchor != null ? _anchor.MirrorHorizontally : _mirror;

        private void Awake()
        {
            _feed = _feedBehaviour as ICameraFeed;
            ResolveAnchor();
            if (_image != null) _image.enabled = false;
        }

        private void LateUpdate()
        {
            if (_feed == null) _feed = _feedBehaviour as ICameraFeed;
            ResolveAnchor();
            var tex = _feed != null ? _feed.CameraTexture : null;
            if (_image == null) return;
            if (tex == null || !tex.isPlaying || tex.width <= 16 || tex.height <= 16)
            {
                _image.enabled = false;
                return;
            }
            if (!TryGetOrientation(tex, out CameraFrameOrientation orientation))
            {
                _image.enabled = false;
                return;
            }

            _image.enabled = true;
            if (_image.texture != tex) _image.texture = tex;

            int angle = orientation.RotationDegrees;

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
            // UV flips act in raw sensor axes, before the transform rotates the image upright.
            _image.uvRect = new Rect(orientation.RawFlipHorizontally ? 1f : 0f,
                orientation.RawFlipVertically ? 1f : 0f,
                orientation.RawFlipHorizontally ? -1f : 1f,
                orientation.RawFlipVertically ? -1f : 1f);
            // UGUI z-rotation is CCW (y-up); the feed rotation is specified CW in screen space.
            rt.localEulerAngles = new Vector3(0f, 0f, -angle);
            // localScale applies BEFORE the rotation, so a screen-horizontal mirror maps to a
            // local-y flip when the frame is quarter-rotated (R⁻¹·FlipX·R = FlipY at ±90°).
            float sx = cover, sy = cover;
            if (MirrorHorizontally) { if (quarter) sy = -sy; else sx = -sx; }
            rt.localScale = new Vector3(sx, sy, 1f);
        }

        private bool TryGetOrientation(WebCamTexture texture, out CameraFrameOrientation orientation)
        {
            if (_feed is ICameraFrameOrientationProvider provider)
                return provider.TryGetCameraOrientation(out orientation);
            if (_fallbackTexture != texture)
            {
                _fallbackTexture = texture;
                _hasFallbackOrientation = false;
            }
            if (texture.didUpdateThisFrame)
            {
                _fallbackOrientation = new CameraFrameOrientation(texture.videoRotationAngle,
                    false, true, texture.videoVerticallyMirrored, texture.width, texture.height);
                _hasFallbackOrientation = true;
            }
            orientation = _fallbackOrientation;
            return _hasFallbackOrientation;
        }

        private void ResolveAnchor()
        {
            if (_anchor == null && _feedBehaviour != null)
                _anchor = _feedBehaviour.GetComponent<AvatarMirrorAnchor>();
        }
    }
}
