using PushStars.Core;
using UnityEngine;

namespace PushStars.Fight
{
    [DefaultExecutionOrder(200)]
    public sealed class GhostPosePlayback : MonoBehaviour
    {
        private Animator _animator;
        private HumanPoseHandler _handler;
        private HumanPose _pose;
        private IOpponentPose _ghost;
        private GhostMotionClip _preparation;
        private float _start;
        private Transform _head, _hips;
        private Vector3 _restPosition;
        private Quaternion _restRotation = Quaternion.identity;
        public bool UsesPushupFraming { get; private set; }
        /// <summary>Switches the body to another timeline — a live friend replacing the scene's ghost.</summary>
        public void SetSource(IOpponentPose source) => _ghost = source;
        public static void Attach(Animator animator, IOpponentPose ghost)
        {
            if (animator.GetComponent<GhostPosePlayback>() != null || animator.avatar == null || !animator.avatar.isHuman) return;
            var playback = animator.gameObject.AddComponent<GhostPosePlayback>();
            playback._animator = animator; playback._ghost = ghost; playback._start = Time.time;
            playback._handler = new HumanPoseHandler(animator.avatar, animator.transform);
            playback._restPosition = animator.transform.localPosition;
            playback._restRotation = animator.transform.localRotation;
            playback._head = animator.GetBoneTransform(HumanBodyBones.Head);
            playback._hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            // A recorded bot also waits on the preparation stage the way it was recorded doing.
            var bot = FightRequest.TestBot ?? FightRequest.StrangerBot;
            if (animator.gameObject.scene.name == FightConfig.PreparationSceneName && bot != null)
                playback._preparation = GhostMotionClip.Decode(bot.fight.motionBase64);
        }
        private void LateUpdate()
        {
            var clip = _preparation ?? (_ghost != null && _ghost.PlaybackActive ? _ghost.Motion : null);
            UsesPushupFraming = false;
            if (clip == null || _handler == null) return;
            byte phase = _preparation != null ? GhostMotionClip.Preparation : _ghost.MotionPhase;
            float time = _preparation != null ? Time.time - _start : _ghost.Elapsed;
            if (!clip.Sample(phase, time, ref _pose, out var position, out var rotation)) return;
            if (_ghost is LiveOpponent)
            {
                // A live pose is sent as if the sender's rig stood at rest (FightPoseRecorder); this
                // stage keeps its own rig at rest too and frames it itself. Applying the sender's
                // root, which its stage moves and scales for its own shot, put the opponent
                // off-screen as soon as the set began.
                _animator.transform.localPosition = _restPosition; _animator.transform.localRotation = _restRotation;
            }
            else { _animator.transform.localPosition = position; _animator.transform.localRotation = rotation; }
            _handler.SetHumanPose(ref _pose);
            if (_preparation == null && phase == GhostMotionClip.Live && _head != null && _hips != null)
            {
                var torso = (_head.position - _hips.position).normalized;
                UsesPushupFraming = Mathf.Abs(Vector3.Dot(torso, _animator.transform.up)) < .6f;
            }
        }
        private void OnDestroy() => _handler?.Dispose();
    }
}
