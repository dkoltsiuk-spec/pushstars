using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Renders a 3D character into a RenderTexture and pipes it to a UI RawImage, so the
    /// avatar appears to live "inside" the flat Screen-Space UI of the main VS screen with a
    /// transparent background. The camera and the model sit in the 3D world on a dedicated
    /// layer; everything else (top bar, Find-Opponent button, mode chips, nav) is normal UI
    /// drawn on top.
    ///
    /// The model on the stage is the owner's own character, imported and animated by the
    /// editor tool (MainMan.prefab). Swapping it for a different one at runtime — another
    /// skin, an opponent's character — goes through <see cref="SetAvatar"/>: the camera
    /// framing, render texture and UI wiring stay exactly the same; the composition built
    /// around the character does not move.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterStage : MonoBehaviour
    {
        [Header("Scene references")]
        [Tooltip("Camera that renders ONLY the character layer into the render texture.")]
        [SerializeField] private Camera _stageCamera;

        [Tooltip("Parent transform the model hangs under. SetAvatar replaces its children.")]
        [SerializeField] private Transform _avatarRoot;

        [Tooltip("UI surface that displays the rendered character (RawImage in the Duel panel).")]
        [SerializeField] private RawImage _targetImage;

        [Header("Render texture")]
        [Tooltip("Portrait render target. Match the on-screen character area aspect (~9:16).")]
        [SerializeField] private int _width  = 720;
        [SerializeField] private int _height = 1280;
        [Range(0, 8)]
        [SerializeField] private int _antiAliasing = 2;

        [Header("Idle motion (placeholder only — a rigged character animates itself)")]
        [Tooltip("Sine bob for a model with no animation. Off for the real character: it " +
                 "already breathes, and the two motions fight each other.")]
        [SerializeField] private bool  _idleBob  = false;
        [SerializeField] private float _bobAmplitude = 0.025f;
        [SerializeField] private float _bobSpeed     = 1.1f;

        private RenderTexture _rt;
        private Vector3 _avatarBaseLocalPos;

        /// <summary>Where the avatar model lives. <see cref="SetAvatar"/> parents new ones here.</summary>
        public Transform AvatarRoot => _avatarRoot;

        /// <summary>The live render target. Useful for snapshots or wardrobe previews later.</summary>
        public RenderTexture RenderTarget => _rt;

        private void Awake()
        {
            CreateRenderTexture();
            if (_avatarRoot != null)
                _avatarBaseLocalPos = _avatarRoot.localPosition;
        }

        private void Update()
        {
            if (!_idleBob || _avatarRoot == null) return;
            float y = Mathf.Sin(Time.time * _bobSpeed) * _bobAmplitude;
            _avatarRoot.localPosition = _avatarBaseLocalPos + new Vector3(0f, y, 0f);
        }

        private void OnDestroy()
        {
            if (_stageCamera != null)
                _stageCamera.targetTexture = null;

            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
        }

        /// <summary>
        /// Swaps the model on the stage for another one. The new model is parented under
        /// <see cref="AvatarRoot"/>, re-centred, and moved onto the stage's render layer so the
        /// stage camera picks it up. The root's yaw (facing the lens) is inherited.
        /// </summary>
        public void SetAvatar(GameObject avatar)
        {
            if (avatar == null || _avatarRoot == null) return;

            for (int i = _avatarRoot.childCount - 1; i >= 0; i--)
                Destroy(_avatarRoot.GetChild(i).gameObject);

            avatar.transform.SetParent(_avatarRoot, false);
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.identity;

            SetLayerRecursive(avatar, _avatarRoot.gameObject.layer);
            _avatarBaseLocalPos = _avatarRoot.localPosition;
        }

        private void CreateRenderTexture()
        {
            _rt = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGB32)
            {
                name         = "CharacterStageRT",
                antiAliasing = Mathf.Max(1, _antiAliasing),
            };
            _rt.Create();

            if (_stageCamera != null)
            {
                _stageCamera.targetTexture   = _rt;
                _stageCamera.clearFlags      = CameraClearFlags.SolidColor;
                _stageCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent
            }

            if (_targetImage != null)
            {
                _targetImage.texture = _rt;
                _targetImage.color   = Color.white;
            }
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}
