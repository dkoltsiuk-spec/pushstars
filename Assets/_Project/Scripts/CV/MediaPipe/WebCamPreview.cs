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

        private void OnGUI()
        {
            GUI.depth = 1; // draw behind the HUD labels (lower depth renders on top)

            var tex = _source != null ? _source.CameraTexture : null;
            if (tex == null || tex.width <= 16) return;

            var full = new Rect(0, 0, Screen.width, Screen.height);
            var m = GUI.matrix;
            if (_flipVertical)
                GUIUtility.ScaleAroundPivot(new Vector2(1f, -1f), new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            GUI.DrawTexture(full, tex, ScaleMode.ScaleToFit);
            GUI.matrix = m;
        }
    }
}
