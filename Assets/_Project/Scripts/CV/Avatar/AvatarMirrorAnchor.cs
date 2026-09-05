using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Places the retargeted hips in the camera image and estimates viewing scale independently
    /// of body rotation. Runs after bone retargeting, so this frame's hip offset is used.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class AvatarMirrorAnchor : MonoBehaviour, IAvatarAnimator
    {
        public enum AnchorState { Unlocked = 0, Locking = 1, Locked = 2, Frozen = 3 }

        [SerializeField] private PushupSession _session;
        [SerializeField] private Camera _stageCamera;
        [SerializeField] private Transform _characterRoot;
        [Tooltip("Keep anchoring while armed when no push-up animation owns the character.")]
        [SerializeField] private bool _followWhileArmed;
        [SerializeField] private Transform _hipsBone;

        [Header("Gate")]
        [Tooltip("Required confidence on a usable shoulder/hip pair; the hidden side may hold.")]
        [SerializeField, Range(0f, 1f)] private float _minTorsoVisibility = 0.6f;
        [SerializeField] private float _lockAfterStableSec = 0.2f;
        [Tooltip("Release tracking after this gap. Keep the last placement until new data locks.")]
        [SerializeField] private float _unlockAfterLostSec = 1.5f;
        [Tooltip("A repeated LastFrame is stale after this many real-time seconds.")]
        [SerializeField, Range(0.1f, 1f)] private float _maxFrameAgeSec = 0.35f;

        [Header("Smoothing")]
        [SerializeField] private float _filterMinCutoffHz = 1.5f;
        [SerializeField] private float _filterBeta = 0.6f;
        [SerializeField] private float _filterDerivCutoffHz = 1f;
        [SerializeField, Range(0.05f, 2f)] private float _maxViewportSpeedPerSec = 1.2f;
        [SerializeField, Range(0.05f, 3f)] private float _maxScaleSpeedPerSec = 1.2f;
        [SerializeField, Range(4f, 40f)] private float _renderResponse = 18f;

        [Header("Placement")]
        [Tooltip("Selfie reflection, shared by the anchor and the bone retargeter.")]
        [SerializeField] private bool _mirrorX = true;
        [SerializeField] private float _characterDistance = 3.6f;
        [Tooltip("Capture the camera plane from the authored character position, once per rig.")]
        [SerializeField] private bool _planeFromCharacter;
        [Tooltip("Fallback torso length when the rig cannot be measured at bind.")]
        [SerializeField] private float _rigTorsoMeters = 0.47f;
        [SerializeField] private float _rigHipHeightMeters = 0.93f;
        [SerializeField] private Vector2 _scaleClamp = new Vector2(0.5f, 2.2f);

        public AnchorState State { get; private set; } = AnchorState.Unlocked;
        public bool MirrorHorizontally => _mirrorX;
        public Camera StageCamera => _stageCamera;
        public bool HasFreshPose { get; private set; }
        public float AppliedScale => _shownScale;

        private OneEuroFilter _fx, _fy, _ft;
        private readonly float[] _mx = new float[3];
        private readonly float[] _my = new float[3];
        private readonly float[] _mt = new float[3];
        private int _medCount, _medHead;
        private int _rigBoundFrame;
        private Animator _boundAnimator;
        private bool _planeCaptured, _baseScaleTaken, _hasPlacement, _pausedForArm;
        private Vector3 _baseScale = Vector3.one;
        private float _lastSampleTime = -1f;
        private float _observedTimestamp, _receivedAt = float.NegativeInfinity;
        private bool _hasObservedFrame, _sampleUsable;
        private float _stableSince = -1f, _lostSince = -1f;
        private int _stableFrames;
        private Vector2 _targetVp, _shownVp;
        private float _targetScale = 1f, _shownScale = 1f;
        private Vector2 _filteredVp, _lastHipCenter;
        private float _filteredScale = 1f;
        private bool _hasHipCenter, _hasMetricReference;
        private float _humanTorsoMeters;

        private void Awake()
        {
            _fx = new OneEuroFilter(_filterMinCutoffHz, _filterBeta, _filterDerivCutoffHz);
            _fy = new OneEuroFilter(_filterMinCutoffHz, _filterBeta, _filterDerivCutoffHz);
            _ft = new OneEuroFilter(0.8f, 0.15f, _filterDerivCutoffHz);
        }

        private void OnEnable() => ResetTracking();

        private void OnDisable()
        {
            HasFreshPose = false;
            State = AnchorState.Unlocked;
        }

        private void Start()
        {
            if (_baseScaleTaken || _characterRoot == null) return;
            var animator = _characterRoot.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman) BindAnimator(animator);
            else CaptureRig();
        }

        public void BindAnimator(Animator animator)
        {
            if (animator == null || !animator.isHuman) return;
            if (_boundAnimator == animator && _baseScaleTaken) return;
            _boundAnimator = animator;
            _characterRoot = animator.transform;
            _hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
            _baseScaleTaken = _planeCaptured = _hasPlacement = _hasMetricReference = false;

            // Bind after the retargeter's neutral-pose capture, never measure a moving pose.
            var leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (_hipsBone != null && leftArm != null && rightArm != null)
            {
                float torso = Vector3.Distance((leftArm.position + rightArm.position) * 0.5f,
                    _hipsBone.position);
                float hipHeight = Vector3.Dot(_hipsBone.position - _characterRoot.position,
                    _characterRoot.up);
                if (Finite(torso) && torso > 0.01f) _rigTorsoMeters = torso;
                if (Finite(hipHeight) && hipHeight > 0.01f) _rigHipHeightMeters = hipHeight;
            }
            CaptureRig();
            ResetTracking();
        }

        private void LateUpdate()
        {
            if (_session == null || _stageCamera == null || _characterRoot == null) return;
            if (!_baseScaleTaken) CaptureRig();
            float now = Time.realtimeSinceStartup;
            var frame = _session.LastFrame;
            bool newFrame = ObserveFrame(in frame, now);
            bool armed = !_followWhileArmed && _session.Armer != null && _session.Armer.IsArmed;
            if (armed)
            {
                _pausedForArm = true;
                _sampleUsable = false;
                HasFreshPose = false;
                _lostSince = now;
                State = AnchorState.Frozen;
                return;
            }
            if (_pausedForArm)
            {
                _pausedForArm = false;
                State = AnchorState.Unlocked;
                _stableSince = _lostSince = -1f;
                _stableFrames = 0;
                _sampleUsable = _hasHipCenter = false;
                ResetFilters();
                CaptureShownPlacement();
            }

            if (newFrame)
            {
                _sampleUsable = TrySample(in frame, out Vector2 hip, out float scale);
                if (_sampleUsable) FeedSample(hip, scale, frame.TimestampSec);
            }
            HasFreshPose = frame.IsValid && Finite(frame.TimestampSec)
                && now - _receivedAt <= _maxFrameAgeSec && _sampleUsable;
            TickStateMachine(HasFreshPose, newFrame, now);
            if (State == AnchorState.Locked && HasFreshPose)
            {
                _targetVp = _filteredVp;
                _targetScale = _filteredScale;
                _hasPlacement = true;
            }
            if (!_hasPlacement) return;

            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.1f);
            float blend = 1f - Mathf.Exp(-Mathf.Max(1f, _renderResponse) * dt);
            _shownVp = Vector2.MoveTowards(_shownVp, Vector2.Lerp(_shownVp, _targetVp, blend),
                Mathf.Max(0.01f, _maxViewportSpeedPerSec) * dt);
            _shownScale = Mathf.MoveTowards(_shownScale, Mathf.Lerp(_shownScale, _targetScale, blend),
                Mathf.Max(0.01f, _maxScaleSpeedPerSec) * dt);
            ApplyPlacement();
        }

        private bool ObserveFrame(in PoseFrame frame, float now)
        {
            if (!frame.IsValid || !Finite(frame.TimestampSec))
            {
                _sampleUsable = false;
                return false;
            }
            if (_hasObservedFrame && frame.TimestampSec == _observedTimestamp) return false;
            // A camera restart may reset capture timestamps. Real time still bounds freshness.
            if (_hasObservedFrame && frame.TimestampSec < _observedTimestamp)
            {
                _sampleUsable = _hasHipCenter = false;
                ResetFilters();
                State = AnchorState.Unlocked;
                _stableFrames = 0;
            }
            _observedTimestamp = frame.TimestampSec;
            _hasObservedFrame = true;
            _receivedAt = now;
            return true;
        }

        private void TickStateMachine(bool valid, bool newFrame, float now)
        {
            if (!valid)
            {
                _stableSince = -1f;
                _stableFrames = 0;
                if (State == AnchorState.Locked || State == AnchorState.Locking)
                {
                    State = AnchorState.Frozen;
                    _lostSince = now;
                    _targetVp = _shownVp;
                    _targetScale = _shownScale;
                }
                else if (State == AnchorState.Frozen && now - _lostSince >= _unlockAfterLostSec)
                    State = AnchorState.Unlocked;
                return;
            }
            if (State == AnchorState.Locked) return;
            bool beginningLock = State != AnchorState.Locking;
            if (State != AnchorState.Locking)
            {
                State = AnchorState.Locking;
                _stableSince = now;
                _stableFrames = newFrame ? 1 : 0;
            }
            else if (newFrame) _stableFrames++;
            bool hadPlane = _planeCaptured;
            CapturePlane();
            if (!_hasPlacement && (beginningLock || (!hadPlane && _planeCaptured)))
                CaptureShownPlacement();
            if (_planeFromCharacter && !_planeCaptured) return;
            if (_stableFrames >= 3 && now - _stableSince >= Mathf.Max(0f, _lockAfterStableSec))
                State = AnchorState.Locked;
        }

        private bool TrySample(in PoseFrame frame, out Vector2 hip, out float scale)
        {
            hip = _lastHipCenter;
            scale = _targetScale;
            bool ls = Usable(in frame, PoseLandmark.LeftShoulder);
            bool rs = Usable(in frame, PoseLandmark.RightShoulder);
            bool lh = Usable(in frame, PoseLandmark.LeftHip);
            bool rh = Usable(in frame, PoseLandmark.RightHip);
            if ((!ls || !lh) && (!rs || !rh)) return false;

            bool hasProjection = TryProjection(in frame, out float projection, out float humanTorso);
            if (hasProjection)
            {
                if ((!_hasMetricReference || !_hasPlacement) && humanTorso > 0f)
                {
                    _humanTorsoMeters = _hasMetricReference
                        ? Mathf.Lerp(_humanTorsoMeters, humanTorso, 0.25f) : humanTorso;
                    _hasMetricReference = true;
                }
                float frustumHeight = _stageCamera.orthographic ? 2f * _stageCamera.orthographicSize
                    : 2f * _characterDistance * Mathf.Tan(_stageCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                if (_hasMetricReference && Finite(frustumHeight) && frustumHeight > 0f)
                    scale = Mathf.Clamp(projection * _humanTorsoMeters * frustumHeight
                        / Mathf.Max(0.01f, _rigTorsoMeters), Mathf.Max(0.01f, _scaleClamp.x),
                        Mathf.Max(_scaleClamp.x, _scaleClamp.y));
            }
            // If the torso points into the lens, preserve scale rather than making the body shrink.
            if (lh && rh)
                hip = (frame.Get(PoseLandmark.LeftHip).Pos2D + frame.Get(PoseLandmark.RightHip).Pos2D) * 0.5f;
            else if (hasProjection && TryVisibleHipCenter(in frame, lh, projection, out Vector2 center))
                hip = center;
            else if (!_hasHipCenter) return false;
            if (!Finite(hip.x) || !Finite(hip.y) || !Finite(scale)) return false;
            _lastHipCenter = hip;
            _hasHipCenter = true;
            return true;
        }

        private bool TryProjection(in PoseFrame frame, out float projection, out float torso)
        {
            projection = torso = 0f;
            if (!frame.HasWorldLandmarks || !Finite(frame.Aspect)) return false;
            float weightedProjection = 0f, weight = 0f, weightedTorso = 0f, torsoWeight = 0f;
            AddProjectionSegment(in frame, PoseLandmark.LeftShoulder, PoseLandmark.LeftHip, true,
                ref weightedProjection, ref weight, ref weightedTorso, ref torsoWeight);
            AddProjectionSegment(in frame, PoseLandmark.RightShoulder, PoseLandmark.RightHip, true,
                ref weightedProjection, ref weight, ref weightedTorso, ref torsoWeight);
            AddProjectionSegment(in frame, PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder, false,
                ref weightedProjection, ref weight, ref weightedTorso, ref torsoWeight);
            if (weight <= 1e-5f) return false;
            projection = weightedProjection / weight;
            torso = torsoWeight > 0f ? weightedTorso / torsoWeight : 0f;
            return Finite(projection) && projection > 0.01f && projection < 8f;
        }

        private void AddProjectionSegment(in PoseFrame frame, PoseLandmark a, PoseLandmark b, bool isTorso,
            ref float sum, ref float weights, ref float torsoSum, ref float torsoWeights)
        {
            if (!Usable(in frame, a) || !Usable(in frame, b)) return;
            var wa = frame.GetWorld(a);
            var wb = frame.GetWorld(b);
            if (!WorldFinite(wa) || !WorldFinite(wb)) return;
            Vector3 delta = new Vector3(wa.X - wb.X, wa.Y - wb.Y, wa.Z - wb.Z);
            float fullLength = delta.magnitude;
            if (fullLength < (isTorso ? 0.15f : 0.12f) || fullLength > 0.95f) return;
            float projectedLength = new Vector2(delta.x, delta.y).magnitude;
            if (projectedLength < 0.06f || projectedLength / fullLength < 0.25f) return;
            float imageLength = Vector2.Distance(PoseMath.ToSquare(frame.Get(a).Pos2D, frame.Aspect),
                PoseMath.ToSquare(frame.Get(b).Pos2D, frame.Aspect));
            if (!Finite(imageLength) || imageLength < 0.01f) return;
            float ratio = imageLength / projectedLength;
            if (ratio < 0.01f || ratio > 8f) return;
            // Matching projected spans cancel yaw/pitch foreshortening. Long visible spans
            // carry more weight than a narrow shoulder line when the user turns into profile.
            float confidence = Mathf.Min(frame.Visibility(a), frame.Visibility(b));
            float weight = projectedLength * projectedLength * confidence * confidence;
            sum += ratio * weight;
            weights += weight;
            if (isTorso)
            {
                torsoSum += fullLength * weight;
                torsoWeights += weight;
            }
        }

        private bool TryVisibleHipCenter(in PoseFrame frame, bool leftVisible, float projection, out Vector2 center)
        {
            center = default;
            var id = leftVisible ? PoseLandmark.LeftHip : PoseLandmark.RightHip;
            var world = frame.GetWorld(id);
            if (!WorldFinite(world) || world.Pos2D.sqrMagnitude > 0.35f * 0.35f) return false;
            // World landmarks are hip-centered. Remove the visible hip's metric offset instead
            // of moving the avatar center to that hip whenever the far side is occluded.
            Vector2 square = PoseMath.ToSquare(frame.Get(id).Pos2D, frame.Aspect) - world.Pos2D * projection;
            center = new Vector2(square.x / frame.Aspect, square.y);
            return Finite(center.x) && Finite(center.y);
        }

        private void FeedSample(Vector2 hip, float scale, float timestamp)
        {
            float dt = _lastSampleTime >= 0f ? Mathf.Clamp(timestamp - _lastSampleTime, 0.01f, 0.1f) : 1f / 30f;
            _lastSampleTime = timestamp;
            _mx[_medHead] = hip.x;
            _my[_medHead] = hip.y;
            _mt[_medHead] = scale;
            _medHead = (_medHead + 1) % 3;
            _medCount = Mathf.Min(3, _medCount + 1);
            _fx.MinCutoffHz = _fy.MinCutoffHz = Mathf.Max(0.01f, _filterMinCutoffHz);
            _fx.Beta = _fy.Beta = Mathf.Max(0f, _filterBeta);
            _fx.DerivCutoffHz = _fy.DerivCutoffHz = Mathf.Max(0.01f, _filterDerivCutoffHz);
            float x = _fx.Filter(MedianOf(_mx, _medCount), dt);
            float y = _fy.Filter(MedianOf(_my, _medCount), dt);
            _filteredVp = new Vector2(_mirrorX ? 1f - x : x, 1f - y);
            _filteredScale = _ft.Filter(MedianOf(_mt, _medCount), dt);
        }

        private void CaptureRig()
        {
            if (_baseScaleTaken || _characterRoot == null) return;
            _baseScale = _characterRoot.localScale;
            _baseScaleTaken = true;
            _rigBoundFrame = Time.frameCount;
            CaptureShownPlacement();
        }

        private void CapturePlane()
        {
            if (!_planeFromCharacter || _planeCaptured || _characterRoot == null || _stageCamera == null) return;
            // FightAvatar completes its initial camera framing at the end of the bind frame.
            if (Time.frameCount <= _rigBoundFrame) return;
            float depth = Vector3.Dot(_characterRoot.position - _stageCamera.transform.position,
                _stageCamera.transform.forward);
            if (!Finite(depth) || depth <= 0.2f) return;
            _characterDistance = depth;
            _planeCaptured = true;
        }

        private void CaptureShownPlacement()
        {
            if (_stageCamera == null || _characterRoot == null) return;
            Vector3 hipWorld = _hipsBone != null ? _hipsBone.position
                : _characterRoot.position + _characterRoot.up * _rigHipHeightMeters;
            Vector3 viewport = _stageCamera.WorldToViewportPoint(hipWorld);
            if (Finite(viewport.x) && Finite(viewport.y)) _shownVp = new Vector2(viewport.x, viewport.y);
            float scale = Mathf.Abs(_baseScale.x) > 1e-5f ? _characterRoot.localScale.x / _baseScale.x : 1f;
            _shownScale = Finite(scale) && scale > 0f ? scale : 1f;
            _targetVp = _filteredVp = _shownVp;
            _targetScale = _filteredScale = _shownScale;
        }

        private void ApplyPlacement()
        {
            if (!Finite(_shownScale) || !Finite(_shownVp.x) || !Finite(_shownVp.y)) return;
            Vector3 hipWorld = _stageCamera.ViewportToWorldPoint(new Vector3(_shownVp.x, _shownVp.y,
                Mathf.Max(0.2f, _characterDistance)));
            if (!Finite(hipWorld.x) || !Finite(hipWorld.y) || !Finite(hipWorld.z)) return;
            _characterRoot.localScale = _baseScale * _shownScale;
            Vector3 offset = _hipsBone != null ? _hipsBone.position - _characterRoot.position
                : _characterRoot.up * (_rigHipHeightMeters * _shownScale);
            _characterRoot.position = hipWorld - offset;
        }

        private bool Usable(in PoseFrame frame, PoseLandmark id)
        {
            var point = frame.Get(id);
            return Finite(point.X) && Finite(point.Y) && Finite(point.Visibility)
                && point.Visibility >= _minTorsoVisibility
                && point.X >= -0.3f && point.X <= 1.3f && point.Y >= -0.3f && point.Y <= 1.3f;
        }

        private void ResetTracking()
        {
            State = AnchorState.Unlocked;
            HasFreshPose = false;
            _sampleUsable = _hasHipCenter = _pausedForArm = false;
            _stableSince = _lostSince = -1f;
            _stableFrames = 0;
            _receivedAt = float.NegativeInfinity;
            // Enabling during a stopped source must not relabel its old snapshot as fresh.
            var frame = _session != null ? _session.LastFrame : default;
            _hasObservedFrame = frame.IsValid && Finite(frame.TimestampSec);
            _observedTimestamp = _hasObservedFrame ? frame.TimestampSec : 0f;
            ResetFilters();
            CaptureShownPlacement();
        }

        private void ResetFilters()
        {
            _fx.Reset();
            _fy.Reset();
            _ft.Reset();
            _medCount = _medHead = 0;
            _lastSampleTime = -1f;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool WorldFinite(Landmark point) => Finite(point.X) && Finite(point.Y) && Finite(point.Z);

        private static float MedianOf(float[] values, int count)
        {
            if (count < 3) return count == 2 ? 0.5f * (values[0] + values[1]) : values[0];
            float a = values[0], b = values[1], c = values[2];
            return a + b + c - Mathf.Max(a, Mathf.Max(b, c)) - Mathf.Min(a, Mathf.Min(b, c));
        }
    }
}
