using UnityEngine;
using PushStars.CV;

namespace PushStars.Fight
{
    /// <summary>
    /// Moves the opponent's body from a <see cref="GhostOpponent"/>, the mirror of what
    /// <see cref="PushupAvatarDriver"/> does for the player: the same canned push-up clip, scrubbed
    /// by a depth signal instead of played at its own speed.
    /// The shared <see cref="PushupPoseCorrection"/> supplies the same contact-preserving
    /// final pose as the player's driver.
    ///
    /// <para><b>Why a separate driver rather than a mode on the CV one.</b> That driver reads a
    /// <see cref="PushupSession"/> — a live pose stack, an armer, a set tracker — and none of that
    /// exists for a recording. Teaching it a second source would mean threading "or maybe none of
    /// this is here" through every branch of a component the whole app depends on for counting.
    /// The scrub itself is a dozen lines; the coupling would have cost more than the duplication.</para>
    /// </summary>
    public sealed class GhostAvatarDriver : MonoBehaviour, IAvatarAnimator
    {
        [SerializeField] private GhostOpponent _ghost;
        [SerializeField] private Animator _animator;

        [Header("Animator state names (must exist in the controller)")]
        [SerializeField] private string _pushupStateName = "PushUp";
        [SerializeField] private string _idleStateName = "StandIdle";

        /// <summary>The idle this asked for before the controller carried the menu's standing
        /// idle: a fighter's guard, which was the idle only because it was the one looping clip
        /// the test stand had imported.</summary>
        private const string LegacyIdleState = "WarriorIdle";
        private const string StandIdleState = "StandIdle";

        [Header("Push-up clip phase mapping (normalizedTime)")]
        [Tooltip("Clip time of the plank top. Our Mixamo clip begins at the BOTTOM, so the top is " +
                 "half a cycle in — same mapping the CV driver uses.")]
        [SerializeField, Range(0f, 1f)] private float _clipTimeAtTop = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _clipTimeAtBottom = 0f;
        [SerializeField, Range(0f, 1f)] private float _crossFadeSec = 0.25f;

        private int _pushupHash;
        private int _idleHash;
        private bool _working;
        private bool _started;
        private PushupPoseCorrection _poseCorrection;

        public void BindAnimator(Animator animator)
        {
            ReleaseCorrection();
            _animator = animator;
            _poseCorrection = PushupPoseCorrection.Bind(animator);
            _started = false;
            RehashStates();
        }

        private void Awake() => RehashStates();

        private void OnDisable() => ReleaseCorrection();

        private void ReleaseCorrection()
        {
            if (_poseCorrection != null) _poseCorrection.SetDepth(0f, false, 0f);
        }

        private void RehashStates()
        {
            _pushupHash = Animator.StringToHash(_pushupStateName);
            _idleHash = IdleStateHash();
        }

        /// <summary>The idle state to actually play, reconciled against the controller in front of
        /// us. Two directions, because the scenes and the controller move independently. Fight.unity
        /// is preserved rather than regenerated, so a copy of it still asks for
        /// <see cref="LegacyIdleState"/> and the swap has to happen here. Going the other way, a
        /// controller that has not been rebuilt has no standing idle yet — and an Animator answers
        /// a request for a state it does not have by doing nothing at all, leaving the body in its
        /// bind pose, which reads as a broken rig rather than as a missing clip.</summary>
        private int IdleStateHash()
        {
            int wanted = Animator.StringToHash(
                _idleStateName == LegacyIdleState ? StandIdleState : _idleStateName);
            if (_animator == null || _animator.runtimeAnimatorController == null) return wanted;
            if (_animator.HasState(0, wanted)) return wanted;
            int authored = Animator.StringToHash(_idleStateName);
            return _animator.HasState(0, authored) ? authored : wanted;
        }

        private void Update()
        {
            if (_ghost == null || _animator == null || !_animator.isActiveAndEnabled) return;
            if (_poseCorrection == null) _poseCorrection = PushupPoseCorrection.Bind(_animator);

            bool working = _ghost.IsWorking;
            if (!_started || working != _working)
            {
                _started = true;
                _working = working;
                if (working)
                {
                    _animator.speed = 0f; // scrub mode: Update below sets the pose
                }
                else
                {
                    _animator.speed = 1f;
                    _animator.CrossFadeInFixedTime(_idleHash, _crossFadeSec, 0);
                }
            }

            if (_poseCorrection != null)
                _poseCorrection.SetDepth(_ghost.Depth01, working, _crossFadeSec);
            if (!working) return;
            float t = Mathf.Lerp(_clipTimeAtTop, _clipTimeAtBottom, Mathf.Clamp01(_ghost.Depth01));
            _animator.Play(_pushupHash, 0, t);
        }
    }
}
