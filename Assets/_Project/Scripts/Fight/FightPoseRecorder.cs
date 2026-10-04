using PushStars.Core;
using UnityEngine;

namespace PushStars.Fight
{
    /// <summary>After final pose correction/mirroring, before camera framing.</summary>
    [DefaultExecutionOrder(250)]
    public sealed class FightPoseRecorder : MonoBehaviour
    {
        private Animator _animator;
        private HumanPoseHandler _handler;
        private HumanPose _pose;
        private FightController _fight;
        private float _sceneStart, _lastTime = -1;
        private GhostMotionClip _lastClip;
        private byte _lastPhase = byte.MaxValue;
        public void Bind(Animator animator, FightController fight)
        {
            _animator = animator; _fight = fight; _sceneStart = Time.time;
            if (animator.avatar != null && animator.avatar.isHuman && HumanTrait.MuscleCount == GhostMotionClip.MuscleCount)
                _handler = new HumanPoseHandler(animator.avatar, animator.transform);
        }
        private void LateUpdate()
        {
            if (_handler == null || _animator == null) return;
            byte phase; float elapsed; GhostMotionClip clip;
            if (_fight != null)
            {
                if (!_fight.TryGetRecordingClock(out phase, out elapsed)) return;
                clip = _fight.RecordedMotion;
            }
            else
            {
                if (!FightRequest.IsBotRecording || gameObject.scene.name != FightConfig.PreparationSceneName) return;
                phase = GhostMotionClip.Preparation; elapsed = Time.time - _sceneStart; clip = BotRecorderFlow.Motion;
            }
            if (elapsed > 60 || clip.Frames.Count >= GhostMotionClip.MaxFrames) return;
            if (_lastClip == clip && _lastPhase == phase && elapsed - _lastTime + .0001f < 1f / GhostMotionClip.SampleRate) return;
            clip.Frames.Add(CaptureFrame(_animator, _handler, ref _pose, phase, elapsed));
            _lastClip = clip; _lastPhase = phase; _lastTime = elapsed;
        }
        public static GhostMotionClip.Frame CaptureFrame(Animator animator, HumanPoseHandler handler, ref HumanPose pose, byte phase, float time)
        {
            handler.GetHumanPose(ref pose);
            // Preserve the handler's root-relative pose verbatim. Converting it through world
            // space again displaces the replay when fighter stages are far apart (regression-tested).
            var root = animator.transform;
            return new GhostMotionClip.Frame
            {
                phase = phase, time = time,
                bodyPosition = pose.bodyPosition,
                bodyRotation = pose.bodyRotation,
                rootPosition = root.localPosition, rootRotation = root.localRotation,
                muscles = (float[])pose.muscles.Clone()
            };
        }
        private void OnDestroy() => _handler?.Dispose();
    }
}
