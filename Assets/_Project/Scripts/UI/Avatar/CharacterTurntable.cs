using UnityEngine;
using UnityEngine.EventSystems;

namespace PushStars.UI
{
    /// <summary>
    /// Swipe across the character to spin him on the spot, so the player can look at what they are
    /// wearing from any side. A flick keeps spinning and coasts to a stop.
    ///
    /// <para>Lives on the RawImage rather than on the model: the character is rendered into a
    /// RenderTexture by an off-screen camera, so nothing in the 3D scene is under the finger — the
    /// UI surface showing him is the only thing that can receive the drag.</para>
    ///
    /// <para>Turn rate is a fraction of the screen's width, not a pixel count, so the same swipe
    /// covers the same arc on every device.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterTurntable : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [Tooltip("Transform that spins — the stage's AvatarRoot. Its authored rotation is kept as " +
                 "the zero point, so the character still starts facing the camera.")]
        [SerializeField] private Transform _target;

        [Header("Feel")]
        [Tooltip("Degrees turned by a swipe across the full width of the screen.")]
        [SerializeField] private float _degreesPerScreenWidth = 360f;

        [Tooltip("How quickly a flick's spin dies away. Higher stops sooner.")]
        [SerializeField, Range(0.5f, 20f)] private float _spinDamping = 5f;

        [Tooltip("Fastest a flick may spin, degrees per second.")]
        [SerializeField] private float _maxSpinSpeed = 540f;

        [Tooltip("How much of each frame's speed folds into the flick estimate. Low values ignore " +
                 "a single stuttered frame; high values react instantly and inherit its noise.")]
        [SerializeField, Range(0.05f, 1f)] private float _spinResponsiveness = 0.35f;

        private Quaternion _baseRotation;
        private float _yaw;
        private float _spin;
        private bool _dragging;

        /// <summary>Current offset from the character's authored facing, in degrees.</summary>
        public float Yaw => _yaw;

        private void Awake()
        {
            if (_target != null) _baseRotation = _target.localRotation;
        }

        /// <summary>Returns the character to the facing he was built with.</summary>
        public void ResetFacing()
        {
            _yaw = 0f;
            _spin = 0f;
            Apply();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            _dragging = true;
            _spin = 0f;
        }

        public void OnDrag(PointerEventData eventData)
        {
            float degrees = DegreesFor(eventData.delta.x);
            _yaw += degrees;

            // Carry the swipe's speed so releasing mid-flick keeps the spin going. Averaged
            // rather than taken raw: one long frame — a hitch, or a synthetic drag that arrives as
            // a single jump — otherwise reads as a violent flick and sends him spinning.
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            float instant = Mathf.Clamp(degrees / dt, -_maxSpinSpeed, _maxSpinSpeed);
            _spin = Mathf.Lerp(_spin, instant, _spinResponsiveness);
            Apply();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            _dragging = false;
        }

        private void Update()
        {
            if (_dragging || Mathf.Abs(_spin) < 1f) return;

            float dt = Time.unscaledDeltaTime;
            _yaw += _spin * dt;
            _spin = Mathf.Lerp(_spin, 0f, Mathf.Clamp01(dt * _spinDamping));
            Apply();
        }

        /// <summary>Dragging right turns the character's near side to the right, the way a finger
        /// on a physical turntable would.</summary>
        private float DegreesFor(float pixelsX)
            => -pixelsX / Mathf.Max(Screen.width, 1) * _degreesPerScreenWidth;

        private void Apply()
        {
            if (_target == null) return;
            _target.localRotation = _baseRotation * Quaternion.Euler(0f, _yaw, 0f);
        }
    }
}
