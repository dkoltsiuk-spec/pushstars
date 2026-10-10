using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Holds a wide body's arms away from its sides while it stands. Every hero plays the same
    /// retargeted clips, and those were authored on a slim figure: on a body with a belly the
    /// hanging arms end up inside the hips. This opens both upper arms by a fixed angle on top of
    /// whatever the clip posed, so one shared idle fits a body it was never made for.
    ///
    /// <para>Fades out as the body leaves the upright, so a plank keeps the hands exactly where the
    /// push-up clip planted them. Runs after the fight pose recorder, which keeps recordings clean
    /// of it — a recorded pose played back on this body gets the spread once, not twice.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(260)]
    public sealed class StandingArmSpread : MonoBehaviour
    {
        [Tooltip("How far each upper arm opens away from the body, in degrees.")]
        [Range(0f, 40f)] public float Degrees = 12f;

        [Tooltip("How much of that the forearm gives back, so the hands hang down instead of pointing outwards.")]
        [Range(0f, 1f)] public float ForearmReturn = .5f;

        private Animator _animator;
        private Transform _hips, _head;
        private readonly Transform[] _bones = new Transform[4];
        private readonly Quaternion[] _written = new Quaternion[4];
        private readonly bool[] _hasWritten = new bool[4];

        private void Awake() => Bind();

        private bool Bind()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator == null || !_animator.isHuman) return false;
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            _bones[0] = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _bones[1] = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _bones[2] = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _bones[3] = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            return _hips != null && _head != null;
        }

        private void LateUpdate() => Apply();

        /// <summary>Opens the arms on the pose the bones hold now. Public so an editor render that
        /// samples a clip by hand can show the body the way the game does.</summary>
        public void Apply()
        {
            if (_hips == null && !Bind()) return;
            float upright = Mathf.InverseLerp(.5f, .85f, (_head.position - _hips.position).normalized.y);
            float degrees = Degrees * upright;
            if (degrees < .01f) return;

            Vector3 forward = _animator.transform.forward, right = _animator.transform.right;
            Spread(0, forward, -right, degrees);
            Spread(2, forward, right, degrees);
        }

        private void Spread(int upper, Vector3 forward, Vector3 outward, float degrees)
        {
            var arm = _bones[upper];
            var forearm = _bones[upper + 1];
            if (arm == null || forearm == null) return;

            // Nothing re-posed the arm since the last pass (animator paused or culled): the spread
            // is still in it, and adding it again would wind the arm up a little every frame.
            if (_hasWritten[upper] && arm.localRotation.Equals(_written[upper])) return;

            // A turn about the body's forward axis moves the arm along cross(forward, arm);
            // the sign picks whichever way that is away from the body.
            Vector3 along = forearm.position - arm.position;
            float sign = Mathf.Sign(Vector3.Dot(Vector3.Cross(forward, along), outward));
            arm.rotation = Quaternion.AngleAxis(sign * degrees, forward) * arm.rotation;
            forearm.rotation = Quaternion.AngleAxis(-sign * degrees * ForearmReturn, forward) * forearm.rotation;
            _written[upper] = arm.localRotation;
            _hasWritten[upper] = true;
        }
    }
}
