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
    /// <para><see cref="PushupPoseCorrection"/> evaluates the final planted, symmetric pose
    /// from the same smoothed depth before the live mirror's handoff blend.</para>
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class PushupAvatarDriver : MonoBehaviour, IAvatarAnimator
    {
        public enum AvatarMode { Idle = 0, Pushup = 1, Rest = 2 }

        [SerializeField] private PushupSession _session;
        [SerializeField] private Animator _animator;

        [Header("Animator state names (must exist in the controller)")]
        [SerializeField] private string _pushupStateName = "PushUp";
        [SerializeField] private string _idleStateName = "StandIdle";
        [SerializeField] private string _restStateName = "SittingIdle";

        /// <summary>The idle this asked for before the controller carried the menu's standing
        /// idle: a fighter's guard, which was the idle only because it was the one looping clip
        /// the test stand had imported.</summary>
        private const string LegacyIdleState = "WarriorIdle";
        private const string StandIdleState = "StandIdle";

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
        [Tooltip("Seconds the character's clap push-up spends in the air, takeoff to landing. A " +
                 "real flight is 0.25–0.3 s and is only noticed a few frames in, so this runs a " +
                 "little past the real landing: long enough to read on a small figure, and the " +
                 "palms meet about when the landing confirms the clap.")]
        [SerializeField, Range(0.2f, 0.8f)] private float _clapFlightSec = 0.32f;

        /// <summary>A flight is a clap push-up's only when it comes off a push: the top latches
        /// 0.1–0.2 s before the hands leave the floor. The detector opens a "flight" on any rise
        /// of the wrists and only throws it out later — getting up after the set is one.</summary>
        private const float PushToTakeoffSec = 0.6f;

        /// <summary>The character's hands close as far as the player's did: palms together at the
        /// detector's own touch threshold, still planted-width apart from here up. Measured
        /// closest approach: claps 0.02–0.07 of the planted spacing, lifts without one 0.21–0.51.</summary>
        private const float HandsApartGap = 0.6f;
        /// <summary>Seconds for the hands to follow a change in that reading (it arrives in
        /// detector-rate steps, and the touch itself a frame or two into the flight).</summary>
        private const float HandsCloseSec = 0.07f;

        /// <summary>Current mode — surfaced on the test-stand status line.</summary>
        public AvatarMode Mode { get; private set; } = AvatarMode.Idle;

        /// <summary>The smoothed depth actually driving the clip this frame (0=top, 1=bottom).</summary>
        public float SmoothedDepth { get; private set; }

        private int _pushupHash;
        private int _idleHash;
        private int _restHash;
        private float _targetDepth;
        private float _depthVel;
        private float _flightPhase = -1f;                      // < 0 = hands planted
        private float _flightTakeoff = float.NegativeInfinity; // takeoff already judged
        private float _flightGap = 1f;                         // closest the player's hands came
        private float _handsClosed;                            // 0 = apart, 1 = palms together
        private float _clock;                                  // seconds stepped in the push-up
        private float _lastTopClock = float.NegativeInfinity;
        private bool _started;
        private bool _restPresentation;
        private PushupPoseCorrection _poseCorrection;

        private void Awake() => RehashStates();

        /// <summary>Binds the driver to a session and an animator built at runtime. The fight
        /// screen instantiates the player's body after the scene loads (the choice of body is a
        /// saved preference, not a scene authoring decision), so the references cannot be
        /// serialized there the way the editor test stand serializes them.</summary>
        public void Configure(PushupSession session, Animator animator)
        {
            ReleaseCorrection();
            _session = session;
            _animator = animator;
            _poseCorrection = PushupPoseCorrection.Bind(animator);
            _started = false;
            Mode = AvatarMode.Idle;
            RehashStates();
        }

        /// <summary>Late binding of the body, for a character instantiated after the scene loaded.
        /// The session is serialized (it is a scene object); only the Animator arrives late.</summary>
        public void BindAnimator(Animator animator)
        {
            ReleaseCorrection();
            _animator = animator;
            _poseCorrection = PushupPoseCorrection.Bind(animator);
            _started = false;
            Mode = AvatarMode.Idle;
            RehashStates();
        }

        /// <summary>Forces the seated rest pose used by the training screen between sets.
        /// The live session is disabled while the rest screen is shown, so the normal session
        /// state cannot select the rest clip on its own during that interval.</summary>
        public void SetRestPresentation(bool resting)
        {
            _restPresentation = resting;
            _started = false;
        }

        private void OnDisable() => ReleaseCorrection();

        private void ReleaseCorrection()
        {
            if (_poseCorrection != null) _poseCorrection.SetDepth(0f, false, 0f);
        }

        private void RehashStates()
        {
            _pushupHash = Animator.StringToHash(_pushupStateName);
            _idleHash = IdleStateHash();
            _restHash = Animator.StringToHash(_restStateName);
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
            if (_session == null || _animator == null || !_animator.isActiveAndEnabled) return;
            Step(Time.deltaTime);
        }

        /// <summary>One frame of the driver on an explicit clock, so a recorded session can be
        /// replayed through it outside play mode.</summary>
        private void Step(float deltaTime)
        {
            if (_poseCorrection == null) _poseCorrection = PushupPoseCorrection.Bind(_animator);

            AvatarMode target = _restPresentation ? AvatarMode.Rest : ResolveMode();
            if (!_started || target != Mode)
            {
                _started = true;
                SwitchMode(target);
            }

            if (Mode == AvatarMode.Pushup) { AdvanceClapFlight(deltaTime); ScrubPushup(deltaTime); }
            else _flightPhase = -1f;
            if (_poseCorrection != null)
            {
                _poseCorrection.SetDepth(SmoothedDepth, Mode == AvatarMode.Pushup, _crossFadeSec);
                _poseCorrection.SetFlight(Mathf.Max(0f, _flightPhase), _handsClosed);
            }
        }

        /// <summary>The clap push-up. Depth cannot scrub it — the flight is a quarter of a second
        /// and the elbow signal means nothing with the hands in the air — so the detector's
        /// takeoff starts a canned flight and the pose table plays it on its own clock. The one
        /// live input is how close the hands came: an explosive lift without a clap is a flight
        /// too, and a character clapping for it would promise an x2 that never lands.</summary>
        private void AdvanceClapFlight(float deltaTime)
        {
            var tracker = _session.Tracker;
            _clock += deltaTime;
            if (tracker.TopLatchedThisTick) _lastTopClock = _clock;

            var clap = _session.Clap;
            if (_flightPhase >= 0f)
            {
                if (clap.InFlight && clap.TakeoffSec == _flightTakeoff)
                    _flightGap = Mathf.Min(_flightGap, clap.FlightMinGapOfPlanted);
                float closed = Mathf.InverseLerp(HandsApartGap, CVConstants.ClapMaxHandGapOfPlanted, _flightGap);
                _handsClosed = Mathf.MoveTowards(_handsClosed, closed, deltaTime / HandsCloseSec);
                _flightPhase += deltaTime / _clapFlightSec;
                if (_flightPhase >= 1f) _flightPhase = -1f;
                return;
            }
            if (!clap.InFlight || clap.TakeoffSec == _flightTakeoff) return;
            _flightTakeoff = clap.TakeoffSec;
            // Thrown off the floor before the lockout: the rep is still on its way up.
            bool pushed = tracker.ArcState == DepthArcState.AwaitTop
                          || _clock - _lastTopClock <= PushToTakeoffSec;
            if (!pushed) return;
            _flightPhase = 0f;
            _flightGap = clap.FlightMinGapOfPlanted;
            _handsClosed = 0f;
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

        private void ScrubPushup(float deltaTime)
        {
            var tracker = _session.Tracker;
            // Hold the last pose through invalid frames — the tracker freezes its signal too.
            if (tracker.SignalValid) _targetDepth = tracker.CurrentDepth01;
            // Airborne, the elbow angle is not a depth (it can dip far enough to fake a bottom).
            // The real depth takes over again on landing, which is the dip that absorbs it.
            if (_flightPhase >= 0f) _targetDepth = 0f;

            SmoothedDepth = _depthSmoothTime > 0f
                ? Mathf.SmoothDamp(SmoothedDepth, _targetDepth, ref _depthVel, _depthSmoothTime,
                    Mathf.Infinity, deltaTime)
                : _targetDepth;

            float t = Mathf.Lerp(_clipTimeAtTop, _clipTimeAtBottom, SmoothedDepth);
            _animator.Play(_pushupHash, 0, t);
        }
    }
}
