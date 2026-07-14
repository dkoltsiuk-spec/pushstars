using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Test-stand picture-in-picture panel for the avatar overlay experiment: renders the stage
    /// camera into a RenderTexture and draws it via IMGUI ON TOP of the full-screen
    /// <see cref="WebCamPreview"/> (which paints at GUI.depth 5 — lower depth renders on top),
    /// so the user and the CV-driven character are visible side by side for eyeballing the sync.
    ///
    /// Placed on the LEFT side by default: the amplitude gauge owns the right edge and the
    /// bottom strip shows rep-veto messages (see <see cref="PushupDebugHud"/>).
    ///
    /// Throwaway debug UI, same as the rest of the CV stand.
    /// </summary>
    public sealed class AvatarStagePreview : MonoBehaviour
    {
        [SerializeField] private Camera _stageCamera;
        [Tooltip("Optional — adds a mode/depth status line under the panel.")]
        [SerializeField] private PushupAvatarDriver _driver;
        [Tooltip("Optional — appends the mirror-anchor state to the status line.")]
        [SerializeField] private AvatarMirrorAnchor _anchor;

        [Header("Mode")]
        [Tooltip("Full-screen transparent overlay (mirror mode: the character walks over the " +
                 "camera feed) instead of the picture-in-picture panel. Needs the stage camera's " +
                 "background alpha at 0.")]
        [SerializeField] private bool _fullScreenOverlay = false;

        [Header("Panel placement (fractions of the screen, PiP mode only)")]
        [SerializeField, Range(0.15f, 0.6f)] private float _widthFrac = 0.34f;
        [SerializeField, Range(0.15f, 0.9f)] private float _heightFrac = 0.44f;
        [SerializeField, Range(0f, 0.85f)] private float _leftFrac = 0.015f;
        [SerializeField, Range(0f, 0.85f)] private float _topFrac = 0.30f;

        [Header("Render texture")]
        [SerializeField, Range(0, 8)] private int _antiAliasing = 2;

        private RenderTexture _rt;
        private GUIStyle _statusStyle;

        private void OnDestroy()
        {
            if (_stageCamera != null) _stageCamera.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        private void OnGUI()
        {
            if (_stageCamera == null) return;

            float sw = Screen.width, sh = Screen.height;
            var panel = _fullScreenOverlay
                ? new Rect(0f, 0f, sw, sh)
                : new Rect(sw * _leftFrac, sh * _topFrac, sw * _widthFrac, sh * _heightFrac);

            EnsureRenderTexture(Mathf.RoundToInt(panel.width), Mathf.RoundToInt(panel.height));
            if (_rt == null) return;

            GUI.depth = 4; // on top of WebCamPreview (5), under the HUD text (negative)

            GUI.DrawTexture(panel, _rt, ScaleMode.StretchToFill);

            if (!_fullScreenOverlay)
            {
                // Thin frame so the panel reads as a panel over the busy camera feed.
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(panel.x, panel.yMax - 1f, panel.width, 1f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(panel.x, panel.y, 1f, panel.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(panel.xMax - 1f, panel.y, 1f, panel.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            if (_driver != null)
            {
                _statusStyle ??= new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };
                _statusStyle.fontSize = Mathf.RoundToInt(Mathf.Clamp(sh * 0.02f, 13f, 26f));
                _statusStyle.normal.textColor = new Color(0.4f, 1f, 0.6f);

                string status = $"AVATAR {_driver.Mode}  depth {_driver.SmoothedDepth:0.00}";
                if (_anchor != null) status += $"  anchor {_anchor.State}";
                // Full-screen mode: tuck the line above the veto strip (0.72 sh) on the left.
                var line = _fullScreenOverlay
                    ? new Rect(8f, sh * 0.66f, sw * 0.5f, _statusStyle.fontSize * 1.5f)
                    : new Rect(panel.x, panel.yMax + 2f, panel.width, _statusStyle.fontSize * 1.5f);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(line, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(line.x + 6f, line.y, line.width - 6f, line.height), status, _statusStyle);
            }
        }

        private void EnsureRenderTexture(int w, int h)
        {
            if (w < 16 || h < 16) return;
            if (_rt != null && _rt.width == w && _rt.height == h) return;

            if (_stageCamera != null) _stageCamera.targetTexture = null;
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }

            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
            {
                name = "AvatarStageRT",
                antiAliasing = Mathf.Max(1, _antiAliasing),
            };
            _rt.Create();
            _stageCamera.targetTexture = _rt;
        }
    }
}
