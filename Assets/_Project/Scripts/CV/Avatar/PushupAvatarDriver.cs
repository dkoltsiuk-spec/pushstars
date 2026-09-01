using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Test-stand avatar driver (variant A of the "hide the user" concept): the character does NOT
    /// mocap the skeleton — it plays a canned push-up clip whose <c>normalizedTime</c> is SCRUBBED
    /// by the CV depth signal every frame (animator speed 0, <see cref="Animator.Play(int,int,float)"/>
    /// with an explicit time). The clip becomes a pose table parameterized by push-up depth, so the
    /// character tracks the user's tempo exactly — its only latency is the pose detector's own.
    ///
    /// <para>Mode selection mirrors the session state: armed → scrub the push-up clip; resting /
    /// set complete (<see cref="WorkoutSetTracker"/>) → rest clip; otherwise → idle clip.</para>
    ///
    /// <para><b>Clip mapping.</b> A Mixamo "Push Up" clip is one full arc top→bottom→top over
    /// normalizedTime 0..1, so only the first half is used: depth 0 (plank top) maps to
    /// <see cref="_clipTimeAtTop"/>, depth 1 (chest down) to <see cref="_clipTimeAtBottom"/> —
    /// descent and ascent replay the same pose trajectory in both directions. Tune the two values
    /// in Play mode if the trimmed clip doesn't start exactly at the top.</para>
    ///
    /// Throwaway debug component like <see cref="PushupDebugHud"/> — the production character
    /// will get a proper controller; this proves the sync concept.
    /// </summary>
    public sealed class PushupAvatarDriver : MonoBehaviour, IAvatarAnimator
    {
        public enum AvatarMode { Idle = 0, Pushup = 1, Rest = 2 }

        [SerializeField] private PushupSession _session;
        [SerializeField] private Animator _animator;

        [Header("Animator state names (must exist in the controller)")]
        [SerializeField] private string _pushupStateName = "PushUp";
        [SerializeField] private string _idleStateName = "WarriorIdle";
        [SerializeField] private string _restStateName = "SittingIdle";

        [Header("Push-up clip phase mapping (normalizedTime)")]
        [Tooltip("Clip time of the plank top (arms extended). Depends on where the Mixamo trim " +
                 "started: our clip begins at the BOTTOM, so the top is half a cycle in (0.5).")]
        [SerializeField, Range(0f, 1f)] private float _clipTimeAtTop = 0.5f;
        [Tooltip("Clip time of the deepest point. 0 for a clip trimmed to start at the bottom.")]
        [SerializeField, Range(0f, 1f)] private float _clipTimeAtBottom = 0f;

        [Header("Feel")]
        [Tooltip("SmoothDamp time for the depth signal. Small = snappier, large = softer. The " +
                 "tracker's One-Euro already removed jitter; this only hides detector-rate steps.")]
        [SerializeField, Range(0f, 0.3f)] private float _depthSmoothTime = 0.05f;
        [Tooltip("Crossfade into the idle/rest clips (entering the scrub mode snaps by design).")]
        [SerializeField, Range(0f, 1f)] private float _crossFadeSec = 0.25f;

        /// <summary>Current mode — surfaced on the test-stand status line.</summary>
        public AvatarMode Mode { get; private set; } = AvatarMode.Idle;

        /// <summary>The smoothed depth actually driving the clip this frame (0=top, 1=bottom).</summary>
        public float SmoothedDepth { get; private set; }

        private int _pushupHash;
        private int _idleHash;
        private int _restHash;
        private float _targetDepth;
        private float _depthVel;
        private bool _started;

        private void Awake() => RehashStates();

        /// <summary>Binds the driver to a session and an animator built at runtime. The fight
        /// screen instantiates the player's body after the scene loads (the choice of body is a
        /// saved preference, not a scene authoring decision), so the references cannot be
        /// serialized there the way the editor test stand serializes them.</summary>
        public void Configure(PushupSession session, Animator animator)
        {
            _session = session;
            _animator = animator;
            _started = false;
            Mode = AvatarMode.Idle;
            RehashStates();
        }

        /// <summary>Late binding of the body, for a character instantiated after the scene loaded.
        /// The session is serialized (it is a scene object); only the Animator arrives late.</summary>
        public void BindAnimator(Animator animator)
        {
            _animator = animator;
            _started = false;
            Mode = AvatarMode.Idle;
            RehashStates();
        }

        private void RehashStates()
        {
            _pushupHash = Animator.StringToHash(_pushupStateName);
            _idleHash = Animator.StringToHash(_idleStateName);
            _restHash = Animator.StringToHash(_restStateName);
        }

        private void Update()
        {
            if (_session == null || _animator == null || !_animator.isActiveAndEnabled) return;

            AvatarMode target = ResolveMode();
            if (!_started || target != Mode)
            {
                _started = true;
                SwitchMode(target);
            }

            if (Mode == AvatarMode.Pushup) ScrubPushup();
        }

        private AvatarMode ResolveMode()
        {
            if (_session.Armer != null && _session.Armer.IsArmed) return AvatarMode.Pushup;
            var set = _session.SetTracker.State;
            if (set == WorkoutSetState.Resting || set == WorkoutSetState.SetComplete) return AvatarMode.Rest;
            return AvatarMode.Idle;
        }

        private void SwitchMode(AvatarMode target)
        {
            Mode = target;
            switch (target)
            {
                case AvatarMode.Pushup:
                    // Scrub mode: the animator's own clock stops; ScrubPushup() sets the pose.
                    _animator.speed = 0f;
                    _targetDepth = _session.Tracker.CurrentDepth01;
                    SmoothedDepth = _targetDepth;
                    _depthVel = 0f;
                    break;

                case AvatarMode.Rest:
                    _animator.speed = 1f;
                    _animator.CrossFadeInFixedTime(_restHash, _crossFadeSec, 0);
                    break;

                default:
                    _animator.speed = 1f;
                    _animator.CrossFadeInFixedTime(_idleHash, _crossFadeSec, 0);
                    break;
            }
        }

        private void ScrubPushup()
        {
            var tracker = _session.Tracker;
            // Hold the last pose through invalid frames — the tracker freezes its signal too.
            if (tracker.SignalValid) _targetDepth = tracker.CurrentDepth01;

            SmoothedDepth = _depthSmoothTime > 0f
                ? Mathf.SmoothDamp(SmoothedDepth, _targetDepth, ref _depthVel, _depthSmoothTime)
                : _targetDepth;

            float t = Mathf.Lerp(_clipTimeAtTop, _clipTimeAtBottom, SmoothedDepth);
            _animator.Play(_pushupHash, 0, t);
        }
    }
}
