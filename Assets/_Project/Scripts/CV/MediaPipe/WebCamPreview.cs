using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Debug-only full-screen preview of the camera feed driving <see cref="MediaPipePoseSource"/>,
    /// with a pose-skeleton overlay. Throwaway — the production calibration screen uses a proper
    /// RawImage (phase 08 UI).
    ///
    /// Camera AND skeleton are drawn through the SAME transform matrix (built from rotation + flips +
    /// cover-scale), so they stay pixel-locked regardless of orientation. iOS reports the camera's
    /// rotation/mirror per-device and it varies, so the on-screen buttons let you fix orientation live
    /// (no rebuild). Once a combo looks right we can bake it into the defaults.
    /// </summary>
    public sealed class WebCamPreview : MonoBehaviour
    {
        [SerializeField] private MediaPipePoseSource _source;
        [Tooltip("Flip the feed vertically.")]
        [SerializeField] private bool _flipVertical = false;
        [Tooltip("Mirror horizontally (natural for a front/selfie preview).")]
        [SerializeField] private bool _flipHorizontal = true;
        [Tooltip("Rotation in degrees (CW). -1 = auto: use WebCamTexture.videoRotationAngle.")]
        [SerializeField] private int _rotationOverride = -1;
        [Tooltip("Show the on-screen orientation controls.")]
        [SerializeField] private bool _showControls = true;
        [Tooltip("Draw the pose skeleton over the camera feed.")]
        [SerializeField] private bool _showSkeleton = true;

        // BlazePose / MediaPipe bone pairs (landmark index → index). Face landmarks omitted.
        static readonly int[] Bones =
        {
            11, 12,                 // shoulders
            11, 13, 13, 15,         // left arm
            12, 14, 14, 16,         // right arm
            11, 23, 12, 24,         // torso sides
            23, 24,                 // hips
            23, 25, 25, 27,         // left leg
            24, 26, 26, 28,         // right leg
            27, 31, 28, 32,         // feet
        };

        private bool _initialized;
        private int  _rotation;
        private bool _autoRotation;
        private bool _flipH, _flipV;

        private PoseFrame _frame;
        private bool _hasFrame;

        private void OnEnable()
        {
            if (_source != null) _source.OnFrame += OnFrame;
        }

        private void OnDisable()
        {
            if (_source != null) _source.OnFrame -= OnFrame;
        }

        private void OnFrame(PoseFrame frame)
        {
            _frame = frame;
            _hasFrame = frame.IsValid;
        }

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
            if (_autoRotation) _rotation = tex.videoRotationAngle;

            float sw = Screen.width, sh = Screen.height;
            float texW = tex.width, texH = tex.height;
            int angle = ((_rotation % 360) + 360) % 360;

            // One matrix maps the raw texture rect → its final on-screen placement (cover-scale to fill,
            // rotate, flip). Camera and skeleton both use it, so they can never drift apart.
            bool quarter = angle == 90 || angle == 270;
            float visualW = quarter ? texH : texW;
            float visualH = quarter ? texW : texH;
            float cover = Mathf.Max(sw / visualW, sh / visualH);

            Matrix4x4 M =
                Matrix4x4.Translate(new Vector3(sw * 0.5f, sh * 0.5f, 0f)) *
                Matrix4x4.Scale(new Vector3(_flipH ? -1f : 1f, _flipV ? -1f : 1f, 1f)) *
                Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, angle)) *
                Matrix4x4.Scale(new Vector3(cover, cover, 1f)) *
                Matrix4x4.Translate(new Vector3(-texW * 0.5f, -texH * 0.5f, 0f));

            // ── Camera feed (behind the HUD) ──
            GUI.depth = 1;
            var prev = GUI.matrix;
            GUI.matrix = M;
            GUI.DrawTexture(new Rect(0f, 0f, texW, texH), tex, ScaleMode.StretchToFill);
            GUI.matrix = prev;

            // ── Skeleton overlay (screen space; positions pushed through the same matrix M) ──
            if (_showSkeleton && _hasFrame && _source.Quality != TrackingQuality.Lost)
                DrawSkeleton(M, texW, texH, sw);

            if (_showControls)
                DrawControls(sw, sh, angle);
        }

        private void DrawSkeleton(Matrix4x4 M, float texW, float texH, float sw)
        {
            float lineW = Mathf.Max(3f, sw * 0.006f);
            float dotR  = Mathf.Max(4f, sw * 0.009f);

            Vector2 ToScreen(int idx)
            {
                var lm = _frame.Get((PoseLandmark)idx);
                float nx = lm.X, ny = lm.Y;
                if (_source.SourceFlipHorizontally) nx = 1f - nx;
                if (_source.SourceFlipVertically)   ny = 1f - ny;
                var p = M.MultiplyPoint3x4(new Vector3(nx * texW, ny * texH, 0f));
                return new Vector2(p.x, p.y);
            }

            bool Visible(int idx) => _frame.Visibility((PoseLandmark)idx) >= CVConstants.MinJointVisibility;

            // Bones (green).
            GUI.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            for (int i = 0; i < Bones.Length; i += 2)
            {
                int a = Bones[i], b = Bones[i + 1];
                if (!Visible(a) || !Visible(b)) continue;
                DrawLine(ToScreen(a), ToScreen(b), lineW);
            }

            // Joints (yellow dots).
            GUI.color = new Color(1f, 0.9f, 0.2f, 0.95f);
            for (int idx = 11; idx < PoseLandmarks.Count; idx++) // skip face (0..10)
            {
                if (!Visible(idx)) continue;
                var p = ToScreen(idx);
                GUI.DrawTexture(new Rect(p.x - dotR, p.y - dotR, dotR * 2f, dotR * 2f), Texture2D.whiteTexture);
            }

            GUI.color = Color.white;
        }

        // Draws a line by stretching+rotating a 1px white texture. Assumes GUI.matrix is identity.
        private static void DrawLine(Vector2 a, Vector2 b, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            var prev = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
            GUI.matrix = prev;
        }

        private void DrawControls(float sw, float sh, int angle)
        {
            GUI.depth = 0;

            float bw = Mathf.Min(140f, sw * 0.23f);
            float bh = Mathf.Max(48f, sh * 0.05f);
            float pad = 8f;
            float y = sh - bh - pad - 24f;

            GUI.Label(new Rect(pad, sh - 22f, sw - pad, 22f),
                $"rot {angle}  H:{(_flipH ? "on" : "off")}  V:{(_flipV ? "on" : "off")}  skel:{(_showSkeleton ? "on" : "off")}{(_autoRotation ? "  (auto)" : "")}");

            if (GUI.Button(new Rect(pad, y, bw, bh), "Rotate 90"))
            {
                _autoRotation = false;
                _rotation = ((_rotation + 90) % 360 + 360) % 360;
            }
            if (GUI.Button(new Rect(pad * 2 + bw, y, bw, bh), "Flip H"))   _flipH = !_flipH;
            if (GUI.Button(new Rect(pad * 3 + bw * 2, y, bw, bh), "Flip V")) _flipV = !_flipV;
            if (GUI.Button(new Rect(pad * 4 + bw * 3, y, bw, bh), "Skeleton")) _showSkeleton = !_showSkeleton;
        }
    }
}
