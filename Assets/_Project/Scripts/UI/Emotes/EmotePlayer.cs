using System;
using PushStars.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Experimental.Animations;
using UnityEngine.Playables;

namespace PushStars.UI
{
    /// <summary>
    /// Plays an emote on any humanoid Animator without touching its controller.
    ///
    /// <para>The emote runs in its own PlayableGraph whose output is sorted after the Animator's
    /// controller and blended over it by output weight. The controller keeps running underneath —
    /// idle, victory, whatever the screen drives — so the fade out lands on the pose the body would
    /// have had anyway, and none of the per-hero controllers (menu, fight, boss) needs an emote
    /// state. Every clip is humanoid, so one clip suits every hero.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EmotePlayer : MonoBehaviour
    {
        private const float BlendIn = .22f, BlendOut = .35f;

        private Animator _animator;
        private PlayableGraph _graph;
        private AnimationPlayableOutput _output;
        private AnimationClipPlayable _clip;
        private float _duration, _start;

        public EmoteDef Current { get; private set; }
        public bool IsPlaying => Current != null;
        public Animator Animator => _animator;

        /// <summary>Raised when any player starts an emote — opponents listen to answer it.</summary>
        public static event Action<EmotePlayer, EmoteDef> Started;

        public static EmotePlayer For(Animator animator)
        {
            if (animator == null) return null;
            var player = animator.GetComponent<EmotePlayer>();
            if (player == null) player = animator.gameObject.AddComponent<EmotePlayer>();
            player._animator = animator;
            return player;
        }

        public bool Play(EmoteDef emote, bool sound = true)
        {
            if (emote == null || emote.Clip == null || _animator == null || !_animator.isActiveAndEnabled
                || !_animator.isHuman) return false;
            Stop();
            _graph = PlayableGraph.Create("Emote " + emote.Id);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
            _output = AnimationPlayableOutput.Create(_graph, "Emote", _animator);
            _output.SetSortingOrder(1000);
            _output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
            _clip = AnimationClipPlayable.Create(_graph, emote.Clip);
            _clip.SetApplyFootIK(false);
            _start = Mathf.Clamp(emote.StartSeconds, 0f, emote.Clip.length);
            _clip.SetTime(_start);
            _clip.SetTime(_start);
            _output.SetSourcePlayable(_clip);
            _output.SetWeight(0f);
            _graph.Play();
            _duration = emote.Duration;
            Current = emote;
            if (sound && emote.Sound != null) GameAudio.PlayClip(emote.Sound, emote.SoundVolume);
            Started?.Invoke(this, emote);
            return true;
        }

        public void Stop()
        {
            if (_graph.IsValid()) _graph.Destroy();
            Current = null;
        }

        private void LateUpdate()
        {
            if (Current == null) return;
            if (!_graph.IsValid() || _animator == null) { Current = null; return; }
            float t = (float)_clip.GetTime() - _start;
            if (t >= _duration) { Stop(); return; }
            float weight = Mathf.Min(1f, t / BlendIn, (_duration - t) / BlendOut);
            _output.SetWeight(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(weight)));
        }

        private void OnDisable() => Stop();
        private void OnDestroy() => Stop();
    }
}
