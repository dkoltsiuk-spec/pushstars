using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Calibration "mirror" for the avatar stand: anchors the character to the USER's on-screen
    /// position — position = hip-mid, scale = shoulder↔hip torso length — while the limbs stay on
    /// canned animation. Limb retargeting is where CV jitter looks ugly; a 3-DOF anchor is easy to
    /// filter hard, so the character glides after the person and never twitches.
    ///
    /// <para><b>Anti-jitter chain</b> (the whole point of the experiment):
    /// gate (TrackingQuality.Good + torso joints visible, held <see cref="_lockAfterStableSec"/>)
    /// → median-3 → One-Euro (low cutoff — calibration wants smoothness over responsiveness)
    /// → viewport speed clamp (a single-frame landmark teleport physically cannot move the
    /// character) → freeze on signal loss, linear glide on re-acquire (never teleport).</para>
    ///
    /// <para>While the session is ARMED the anchor freezes entirely — the push-up scrub owns the
    /// character and the standing-pose scale/position assumptions don't hold in a plank.</para>
    ///
    /// Test-stand component, same family as <see cref="PushupAvatarDriver"/>.
    /// </summary>
    public sealed class AvatarMirrorAnchor : MonoBehaviour, IAvatarAnimator
    {
        public enum AnchorState { Unlocked = 0, Locking = 1, Locked = 2, Frozen = 3 }

        [SerializeField] private PushupSession _session;
        [SerializeField] private Camera _stageCamera;
        [SerializeField] private Transform _characterRoot;

        [Tooltip("Retarget mode: keep following the user even while the session is armed (there " +
                 "is no push-up scrub owning the character — the retargeter mirrors the limbs and " +
                 "this anchor keeps owning position/scale).")]
        [SerializeField] private bool _followWhileArmed = false;

        [Tooltip("Retarget mode: anchor the HIPS BONE itself to the user's hip point instead of " +
                 "the standing-pose root formula (root at the feet, hips 0.93m up). The standing " +
                 "formula pushed a scaled retargeted character to the bottom of the screen.")]
        [SerializeField] private Transform _hipsBone;

        [Header("Gate")]
        [Tooltip("Both shoulders and both hips must be at least this visible for a frame to count.")]
        [SerializeField, Range(0f, 1f)] private float _minTorsoVisibility = 0.6f;
        [Tooltip("Tracking must stay valid this long before the character latches onto the user.")]
        [SerializeField] private float _lockAfterStableSec = 0.5f;
        [Tooltip("After this long without a valid signal the anchor gives up and glides home.")]
        [SerializeField] private float _unlockAfterLostSec = 1.5f;

        [Header("Smoothing")]
        [Tooltip("One-Euro min cutoff. Low = very smooth when the person moves slowly.")]
        [SerializeField] private float _filterMinCutoffHz = 0.4f;
        [SerializeField] private float _filterBeta = 0.05f;
        [SerializeField] private float _filterDerivCutoffHz = 1f;
        [Tooltip("The anchor can cross at most this fraction of the screen per second.")]
        [SerializeField, Range(0.05f, 2f)] private float _maxViewportSpeedPerSec = 0.5f;
        [SerializeField, Range(0.05f, 3f)] private float _maxScaleSpeedPerSec = 0.8f;

        [Header("Placement")]
        [Tooltip("Flip the anchor horizontally if the character moves opposite to the user.")]
        [SerializeField] private bool _mirrorX = false;
        [Tooltip("Character plane distance from the stage camera, meters.")]
        [SerializeField] private float _characterDistance = 3.6f;

        [Tooltip("Read that distance off where the body already stands, the first time the anchor " +
                 "locks, instead of trusting the number above. The stand's camera is static and " +
                 "that number was tuned around it; the fight screen frames its own shot, so a " +
                 "constant there puts the body on the wrong plane and at the wrong size.")]
        [SerializeField] private bool _planeFromCharacter = false;
        [Tooltip("Shoulder-mid to hip-mid on the rig, meters (Mixamo Ch36 ≈ 0.47).")]
        [SerializeField] private float _rigTorsoMeters = 0.47f;
        [Tooltip("Hip height above the character's root while standing, meters.")]
        [SerializeField] private float _rigHipHeightMeters = 0.93f;
        [SerializeField] private Vector2 _scaleClamp = new Vector2(0.5f, 2.2f);

        /// <summary>Surfaced on the stand's status line.</summary>
        public AnchorState State { get; private set; } = AnchorState.Unlocked;

        private static readonly Vector2 HomeViewport = new Vector2(0.5f, 0.45f);

        private OneEuroFilter _fx, _fy, _ft;
        private readonly float[] _mx = new float[3];
        private readonly float[] _my = new float[3];
        private readonly float[] _mt = new float[3];
        private int _medCount, _medHead;

        private bool _planeCaptured;
        private float _lastSampleTime = -1f;
        private float _stableSince = -1f;
        private float _lostSince = -1f;

        private Vector2 _targetVp = HomeViewport;
        private float _targetScale = 1f;
        private Vector2 _shownVp = HomeViewport;
        private float _shownScale = 1f;

        private void Awake()
        {
            _fx = new OneEuroFilter(_filterMinCutoffHz, _filterBeta, _filterDerivCutoffHz);
            _fy = new OneEuroFilter(_filterMinCutoffHz, _filterBeta, _filterDerivCutoffHz);
            _ft = new OneEuroFilter(_filterMinCutoffHz, _filterBeta, _filterDerivCutoffHz);
        }

        private void Update()
        {
            if (_session == null || _stageCamera == null || _characterRoot == null) return;

            // The push-up driver owns the character while armed — hold everything. (Retarget mode
            // has no driver, so the anchor keeps following.)
            if (!_followWhileArmed && _session.Armer != null && _session.Armer.IsArmed)
            {
                State = AnchorState.Frozen;
                return;
            }

            var frame = _session.LastFrame;
            bool valid = frame.IsValid
                && _session.Quality == TrackingQuality.Good
                && TorsoVisible(in frame);
            float now = Time.time;

            TickStateMachine(valid, now);

            if (State == AnchorState.Locked && valid && frame.TimestampSec > _lastSampleTime)
                FeedSample(in frame);
            else if (State == AnchorState.Unlocked)
            {
                _targetVp = HomeViewport;
                _targetScale = 1f;
            }
            // Locking/Frozen: targets hold their last value — the character waits in place.

            _shownVp = Vector2.MoveTowards(_shownVp, _targetVp, _maxViewportSpeedPerSec * Time.deltaTime);
            _shownScale = Mathf.MoveTowards(_shownScale, _targetScale, _maxScaleSpeedPerSec * Time.deltaTime);
            ApplyPlacement();
        }

        /// <summary>
        /// Binds to a body built at runtime, and takes the rig's proportions off it.
        ///
        /// <para>Measured, not typed in: the fight screen instantiates a male or a female body
        /// depending on a saved preference, and the two rigs do not share a torso length. The
        /// stand wires all of this in the Inspector and never calls this.</para>
        /// </summary>
        public void BindAnimator(Animator animator)
        {
            if (animator == null) return;

            _characterRoot = animator.transform;
            _hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);

            var leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (_hipsBone == null || leftArm == null || rightArm == null) return;

            Vector3 shoulderMid = (leftArm.position + rightArm.position) * 0.5f;
            _rigTorsoMeters = Vector3.Distance(shoulderMid, _hipsBone.position);
            _rigHipHeightMeters = _hipsBone.position.y - animator.transform.position.y;
        }

        /// <summary>The plane the character is placed on, taken once from where it already stands.
        /// Once only: after the first lock the anchor is what put the body there, and reading the
        /// distance back would be reading its own output.</summary>
        private void CapturePlane()
        {
            if (!_planeFromCharacter || _planeCaptured) return;
            if (_characterRoot == null || _stageCamera == null) return;

            var cam = _stageCamera.transform;
            float depth = Vector3.Dot(_characterRoot.position - cam.position, cam.forward);
            if (depth <= 0.2f) return;

            _characterDistance = depth;
            _planeCaptured = true;
        }

        private void TickStateMachine(bool valid, float now)
        {
            switch (State)
            {
                case AnchorState.Unlocked:
                    if (valid)
                    {
                        State = AnchorState.Locking;
                        _stableSince = now;
                        ResetFilters();
                        CapturePlane();
                    }
                    break;

                case AnchorState.Locking:
                    if (!valid) { State = AnchorState.Unlocked; _stableSince = -1f; }
                    else if (now - _stableSince >= _lockAfterStableSec) State = AnchorState.Locked;
                    break;

                case AnchorState.Locked:
                    if (!valid) { State = AnchorState.Frozen; _lostSince = now; }
                    break;

                case AnchorState.Frozen:
                    if (valid) State = AnchorState.Locked; // resume — shown glides, no teleport
                    else if (now - _lostSince >= _unlockAfterLostSec) State = AnchorState.Unlocked;
                    break;
            }
        }

        private void FeedSample(in PoseFrame frame)
        {
            float dt = _lastSampleTime > 0f
                ? Mathf.Clamp(frame.TimestampSec - _lastSampleTime, 0.01f, 0.1f)
                : 0.033f;
            _lastSampleTime = frame.TimestampSec;

            Vector2 hip = 0.5f * (frame.Get(PoseLandmark.LeftHip).Pos2D
                                + frame.Get(PoseLandmark.RightHip).Pos2D);
            Vector2 shoulder = 0.5f * (frame.Get(PoseLandmark.LeftShoulder).Pos2D
                                     + frame.Get(PoseLandmark.RightShoulder).Pos2D);
            // Body scale in aspect-corrected square space. max(torso, shoulder width): the torso
            // foreshortens to almost nothing in a plank (the retarget stand follows through the
            // whole movement) while the shoulder width stays honest frontally — same trick as the
            // anti-cheat's WristAnchor scale.
            float torso = Vector2.Distance(
                PoseMath.ToSquare(hip, frame.Aspect),
                PoseMath.ToSquare(shoulder, frame.Aspect));
            float shoulderWidth = Vector2.Distance(
                PoseMath.ToSquare(frame.Get(PoseLandmark.LeftShoulder).Pos2D, frame.Aspect),
                PoseMath.ToSquare(frame.Get(PoseLandmark.RightShoulder).Pos2D, frame.Aspect));
            torso = Mathf.Max(torso, shoulderWidth);

            // median-3 kills single-frame outliers before they reach the One-Euro.
            _mx[_medHead] = hip.x;
            _my[_medHead] = hip.y;
            _mt[_medHead] = torso;
            _medHead = (_medHead + 1) % 3;
            if (_medCount < 3) _medCount++;

            float mxv = MedianOf(_mx, _medCount);
            float myv = MedianOf(_my, _medCount);
            float mtv = MedianOf(_mt, _medCount);

            float fxv = _fx.Filter(mxv, dt);
            float fyv = _fy.Filter(myv, dt);
            float ftv = _ft.Filter(mtv, dt);

            // Landmarks: x right, y DOWN → viewport: y up.
            _targetVp = new Vector2(_mirrorX ? 1f - fxv : fxv, 1f - fyv);

            float frustumH = 2f * _characterDistance
                * Mathf.Tan(_stageCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float worldTorso = ftv * frustumH;
            _targetScale = Mathf.Clamp(worldTorso / _rigTorsoMeters, _scaleClamp.x, _scaleClamp.y);
        }

        private void ApplyPlacement()
        {
            Vector3 hipWorld = _stageCamera.ViewportToWorldPoint(
                new Vector3(_shownVp.x, _shownVp.y, _characterDistance));
            _characterRoot.localScale = Vector3.one * _shownScale;

            if (_hipsBone != null)
            {
                // Retarget mode: put the HIPS BONE at the user's hip point — valid in any stance
                // (standing, plank, bottom of a rep). Scale first so the offset is current.
                Vector3 hipsOffset = _hipsBone.position - _characterRoot.position;
                _characterRoot.position = hipWorld - hipsOffset;
            }
            else
            {
                // Standing-mirror mode: root at the feet, hips at standing height above it.
                _characterRoot.position = hipWorld + Vector3.down * (_rigHipHeightMeters * _shownScale);
            }
        }

        private bool TorsoVisible(in PoseFrame frame)
            => frame.Visibility(PoseLandmark.LeftShoulder) >= _minTorsoVisibility
            && frame.Visibility(PoseLandmark.RightShoulder) >= _minTorsoVisibility
            && frame.Visibility(PoseLandmark.LeftHip) >= _minTorsoVisibility
            && frame.Visibility(PoseLandmark.RightHip) >= _minTorsoVisibility;

        private void ResetFilters()
        {
            _fx.Reset();
            _fy.Reset();
            _ft.Reset();
            _medCount = 0;
            _medHead = 0;
            _lastSampleTime = -1f;
        }

        private static float MedianOf(float[] src, int n)
        {
            if (n >= 3)
            {
                float a = src[0], b = src[1], c = src[2];
                return a + b + c - Mathf.Max(a, Mathf.Max(b, c)) - Mathf.Min(a, Mathf.Min(b, c));
            }
            return n == 2 ? 0.5f * (src[0] + src[1]) : src[0];
        }
    }
}
