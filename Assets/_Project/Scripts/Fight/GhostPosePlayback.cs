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
        private GhostOpponent _ghost;
        private GhostMotionClip _preparation;
        private float _start;
        private Transform _head, _hips;
        public bool UsesPushupFraming { get; private set; }
        public static void Attach(Animator animator, GhostOpponent ghost)
        {
            if (animator.GetComponent<GhostPosePlayback>() != null || animator.avatar == null || !animator.avatar.isHuman) return;
            var playback = animator.gameObject.AddComponent<GhostPosePlayback>();
            playback._animator = animator; playback._ghost = ghost; playback._start = Time.time;
            playback._handler = new HumanPoseHandler(animator.avatar, animator.transform);
            playback._head = animator.GetBoneTransform(HumanBodyBones.Head);
            playback._hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (animator.gameObject.scene.name == FightConfig.PreparationSceneName && FightRequest.TestBot != null)
                playback._preparation = GhostMotionClip.Decode(FightRequest.TestBot.fight.motionBase64);
        }
        private void LateUpdate()
        {
            var clip = _preparation ?? (_ghost != null && _ghost.PlaybackActive ? _ghost.Motion : null);
            UsesPushupFraming = false;
            if (clip == null || _handler == null) return;
            byte phase = _preparation != null ? GhostMotionClip.Preparation : _ghost.MotionPhase;
            float time = _preparation != null ? Time.time - _start : _ghost.Elapsed;
            if (!clip.Sample(phase, time, ref _pose, out var position, out var rotation)) return;
            _animator.transform.localPosition = position; _animator.transform.localRotation = rotation;
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
