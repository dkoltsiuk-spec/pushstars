using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Debug-only full-screen preview of the camera feed driving <see cref="MediaPipePoseSource"/>.
    /// Draws the live <see cref="WebCamTexture"/> behind the OnGUI HUD so you can see yourself while
    /// testing. Throwaway — the production calibration screen uses a proper RawImage (phase 08 UI).
    /// </summary>
    public sealed class WebCamPreview : MonoBehaviour
    {
        [SerializeField] private MediaPipePoseSource _source;
        [Tooltip("WebCamTexture usually appears upside-down in IMGUI; flip to correct it.")]
        [SerializeField] private bool _flipVertical = true;
        [Tooltip("Mirror horizontally (natural for a front/selfie preview).")]
        [SerializeField] private bool _flipHorizontal = false;
        [Tooltip("Rotation in degrees (CW). -1 = auto: use WebCamTexture.videoRotationAngle (iOS reports 90°).")]
        [SerializeField] private int _rotationOverride = -1;

        private void OnGUI()
        {
            GUI.depth = 1; // draw behind the HUD labels (lower depth renders on top)

            var tex = _source != null ? _source.CameraTexture : null;
            if (tex == null || tex.width <= 16) return;

            float sw = Screen.width, sh = Screen.height;
            var center = new Vector2(sw * 0.5f, sh * 0.5f);
            var m = GUI.matrix;

            // iOS webcams deliver a landscape frame plus a videoRotationAngle (usually 90°) that says how
            // much to rotate it CW to be upright. Honour it so the picture stands vertical on a portrait
            // screen. _rotationOverride is an on-device escape hatch if the auto value is wrong.
            int angle = _rotationOverride >= 0 ? _rotationOverride : tex.videoRotationAngle;
            angle = ((angle % 360) + 360) % 360;
            if (angle != 0)
                GUIUtility.RotateAroundPivot(angle, center);

            // WebCamTexture has a bottom-left origin (upside-down in IMGUI) → _flipVertical corrects it;
            // _flipHorizontal mirrors the selfie view.
            float fx = _flipHorizontal ? -1f : 1f;
            float fy = _flipVertical ? -1f : 1f;
            if (fx < 0f || fy < 0f)
                GUIUtility.ScaleAroundPivot(new Vector2(fx, fy), center);

            // After a 90°/270° rotation the screen axes are swapped, so draw into a rect with width/height
            // swapped and centered; ScaleAndCrop then fills the whole screen (no black bars).
            Rect rect = (angle == 90 || angle == 270)
                ? new Rect(center.x - sh * 0.5f, center.y - sw * 0.5f, sh, sw)
                : new Rect(0f, 0f, sw, sh);

            GUI.DrawTexture(rect, tex, ScaleMode.ScaleAndCrop);
            GUI.matrix = m;
        }
    }
}
