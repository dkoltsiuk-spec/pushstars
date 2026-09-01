using UnityEngine;
using PushStars.CV;

namespace PushStars.Fight
{
    /// <summary>
    /// Moves the opponent's body from a <see cref="GhostOpponent"/>, the mirror of what
    /// <see cref="PushupAvatarDriver"/> does for the player: the same canned push-up clip, scrubbed
    /// by a depth signal instead of played at its own speed.
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
        [SerializeField] private string _idleStateName = "WarriorIdle";

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

        public void BindAnimator(Animator animator)
        {
            _animator = animator;
            _started = false;
            RehashStates();
        }

        private void Awake() => RehashStates();

        private void RehashStates()
        {
            _pushupHash = Animator.StringToHash(_pushupStateName);
            _idleHash = Animator.StringToHash(_idleStateName);
        }

        private void Update()
        {
            if (_ghost == null || _animator == null || !_animator.isActiveAndEnabled) return;

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

            if (!working) return;
            float t = Mathf.Lerp(_clipTimeAtTop, _clipTimeAtBottom, Mathf.Clamp01(_ghost.Depth01));
            _animator.Play(_pushupHash, 0, t);
        }
    }
}
