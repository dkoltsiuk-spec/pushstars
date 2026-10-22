using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// The blurred screen frosted-glass UI shows through (shader "PushStars/UI Frosted", global
    /// <c>_PushStarsUIBlur</c>). UGUI cannot read what is drawn behind it, so the frame is taken
    /// once — <see cref="Capture"/> right before the glass appears, at the end of a frame — shrunk
    /// to an eighth and blurred. A still frame is enough: under the blur and the dark tint the
    /// idle character behind barely moves.
    /// </summary>
    public static class FrostedBackdrop
    {
        private static readonly int BlurId = Shader.PropertyToID("_PushStarsUIBlur");
        private static readonly int OffsetId = Shader.PropertyToID("_Offset");
        private static readonly int FlipId = Shader.PropertyToID("_FlipY");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static Material _blur;
        private static RenderTexture _frost;

        /// <summary>A frosted-glass material for one graphic; null when the shader is missing.</summary>
        public static Material CreateMaterial(Color tint)
        {
            var shader = Resources.Load<Shader>("UIFrosted");
            if (shader == null) return null;
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            material.SetColor(TintId, tint);
            return material;
        }

        /// <summary>Takes the current frame as the glass's backdrop. Call after WaitForEndOfFrame,
        /// while the glass itself is still invisible.</summary>
        public static void Capture()
        {
            if (_blur == null)
            {
                var shader = Resources.Load<Shader>("UIBlur");
                if (shader == null) return;
                _blur = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            int w = Screen.width, h = Screen.height;
            if (w <= 0 || h <= 0) return;
            int fw = Mathf.Max(1, w / 8), fh = Mathf.Max(1, h / 8);
            if (_frost == null || _frost.width != fw || _frost.height != fh)
            {
                if (_frost != null) _frost.Release();
                _frost = new RenderTexture(fw, fh, 0) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            }

            var screen = RenderTexture.GetTemporary(w, h, 0);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(screen);
            var half = RenderTexture.GetTemporary(Mathf.Max(1, w / 2), Mathf.Max(1, h / 2), 0);
            var quarter = RenderTexture.GetTemporary(Mathf.Max(1, w / 4), Mathf.Max(1, h / 4), 0);
            var small = RenderTexture.GetTemporary(fw, fh, 0);

            // The capture comes upside down where texture rows start at the top (Metal, D3D, Vulkan).
            _blur.SetFloat(FlipId, SystemInfo.graphicsUVStartsAtTop ? 1f : 0f);
            _blur.SetFloat(OffsetId, 0f);
            Graphics.Blit(screen, half, _blur);
            _blur.SetFloat(FlipId, 0f);
            Graphics.Blit(half, quarter, _blur);
            Graphics.Blit(quarter, small, _blur);
            _blur.SetFloat(OffsetId, 1f);
            Graphics.Blit(small, _frost, _blur);
            _blur.SetFloat(OffsetId, 2f);
            Graphics.Blit(_frost, small, _blur);
            Graphics.Blit(small, _frost, _blur);

            RenderTexture.ReleaseTemporary(screen);
            RenderTexture.ReleaseTemporary(half);
            RenderTexture.ReleaseTemporary(quarter);
            RenderTexture.ReleaseTemporary(small);
            Shader.SetGlobalTexture(BlurId, _frost);
        }
    }
}
