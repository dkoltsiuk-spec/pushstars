using System.Collections.Generic;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Confidence-aware 3D mirror. A camera-space body frame carries independently tracked limbs;
    /// occluded limbs hold their body-local pose and then relax. Metric Z is preserved in full.
    /// All filtering is visual only and never changes the session's scoring landmarks.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class PoseMirrorRetargeter : MonoBehaviour, IAvatarAnimator
    {
        [SerializeField] private PushupSession _session;
        [SerializeField] private Animator _animator;
        [SerializeField] private Camera _stageCamera;
        [Tooltip("Used only without an AvatarMirrorAnchor. The anchor owns the shared selfie setting.")]
        [SerializeField] private bool _mirrorHorizontally = true;
        [SerializeField] private bool _mirrorLimbs = true;
        [SerializeField, Range(4f, 40f)] private float _followRate = 16f;
        [SerializeField, Range(0.1f, 0.8f)] private float _minJointVis = 0.35f;
        [SerializeField, Range(0.1f, 1f)] private float _staleFrameSec = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _occlusionHoldSec = 0.4f;
        [SerializeField, Range(0.1f, 3f)] private float _occlusionRelaxSec = 1.2f;
        [SerializeField, Range(0.05f, 0.8f)] private float _skeletonStableSec = 0.15f;
        [SerializeField, Range(0.05f, 1f)] private float _skeletonBlendSec = 0.25f;
        [SerializeField, Range(0.05f, 1.5f)] private float _armedBlendSec = 0.35f;
        [SerializeField, Range(90f, 720f)] private float _maxJointSpeed = 360f;

        public bool Mirroring { get; private set; }
        public float MirrorWeight { get; private set; }
        public bool MirrorPhase { get; private set; } = true;
        public int TrackedSegments { get; private set; }
        public bool HasFreshPose { get; private set; }
        public bool MirrorHorizontally => _anchor != null ? _anchor.MirrorHorizontally : _mirrorHorizontally;

        private struct Joint
        {
            public OneEuroFilter X, Y, Z;
            public Vector3 Position;
            public float Visibility, SeenAt;
            public bool Initialized;
        }

        private sealed class Segment
        {
            public Transform Bone;
            public PoseLandmark A, B;
            public Vector3 RestDirection, NeutralDirection, TargetDirection, ShownDirection;
            public Quaternion RestRotation;
            public float SeenAt = -100f, StableSince = -1f, Weight;
            public bool Visible;
        }

        private sealed class Chain
        {
            public Segment Upper, Lower;
            public Vector3 RestNormal;
            public float Twist;
        }

        private AvatarMirrorAnchor _anchor;
        private Transform _root, _hips;
        private Quaternion _restBasis, _bodyTarget, _bodyShown, _hipsRestInBody;
        private bool _bound, _animatorWasEnabled, _armed, _bodyAcquired;
        private float _lastStamp = float.NaN, _arrival = -100f, _bodySeen = -100f, _bodyStableSince = -1f;
        private float _transitionAt = -100f;
        private float _sampleDt;
        private int _bodySamples;
        private Joint[] _joints;
        private Chain[] _chains;
        private Transform[] _poseBones;
        private Quaternion[] _neutralLocal, _transitionLocal;
        private Vector3[] _neutralPositions, _transitionPositions;

        private void Start() { if (!_bound) TryBind(); }

        public void BindAnimator(Animator animator)
        {
            if (_bound && _animator != null) _animator.enabled = _animatorWasEnabled;
            _animator = animator;
            _bound = false;
            TryBind();
        }

        private void TryBind()
        {
            if (_animator == null || !_animator.isHuman || _animator.avatar == null) return;
            _root = _animator.transform;
            _anchor = GetComponent<AvatarMirrorAnchor>();
            _animatorWasEnabled = _animator.enabled;
            _animator.enabled = false;
            using (var handler = new HumanPoseHandler(_animator.avatar, _root))
            {
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                for (int i = 0; i < pose.muscles.Length; i++) pose.muscles[i] = 0f;
                pose.bodyRotation = Quaternion.identity;
                handler.SetHumanPose(ref pose);
            }
            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            var leftArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var rightArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (_hips == null || leftArm == null || rightArm == null) { _animator.enabled = _animatorWasEnabled; return; }
            Quaternion invRoot = Quaternion.Inverse(_root.rotation);
            if (!PoseRetargetMath.TryBasis(invRoot * (rightArm.position - leftArm.position),
                invRoot * ((leftArm.position + rightArm.position) * 0.5f - _hips.position), out _restBasis))
            { _animator.enabled = _animatorWasEnabled; return; }
            _bodyTarget = _bodyShown = _restBasis;
            _hipsRestInBody = Quaternion.Inverse(_restBasis) * invRoot * _hips.rotation;
            _chains = new[]
            {
                BindChain(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                    PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist, true),
                BindChain(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                    PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist, true),
                BindChain(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                    PoseLandmark.LeftHip, PoseLandmark.LeftKnee, PoseLandmark.LeftAnkle, false),
                BindChain(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
                    PoseLandmark.RightHip, PoseLandmark.RightKnee, PoseLandmark.RightAnkle, false)
            };
            var bones = new List<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = _animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null && !bones.Contains(bone)) bones.Add(bone);
            }
            _poseBones = bones.ToArray();
            _neutralLocal = new Quaternion[bones.Count]; _transitionLocal = new Quaternion[bones.Count];
            _neutralPositions = new Vector3[bones.Count]; _transitionPositions = new Vector3[bones.Count];
            Capture(_neutralLocal, _neutralPositions);
            _joints = new Joint[PoseLandmarks.Count];
            for (int i = 0; i < _joints.Length; i++)
            {
                _joints[i].X = new OneEuroFilter(1.8f, 1.5f, 1f);
                _joints[i].Y = new OneEuroFilter(1.8f, 1.5f, 1f);
                _joints[i].Z = new OneEuroFilter(1.4f, 1.5f, 1f);
                _joints[i].SeenAt = -100f;
            }
            _lastStamp = float.NaN; _arrival = _bodySeen = _transitionAt = -100f;
            _bodyStableSince = -1f; _bodyAcquired = false; _armed = false; _bodySamples = 0;
            MirrorPhase = true; MirrorWeight = 0f; Mirroring = false;
            _bound = true;
        }

        private Chain BindChain(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones end,
            PoseLandmark a, PoseLandmark b, PoseLandmark c, bool arm)
        {
            var u = _animator.GetBoneTransform(upper);
            var l = _animator.GetBoneTransform(lower);
            var e = _animator.GetBoneTransform(end);
            if (u == null || l == null || e == null) return null;
            var chain = new Chain { Upper = BindSegment(u, l, a, b, arm), Lower = BindSegment(l, e, b, c, arm) };
            chain.RestNormal = Vector3.Cross(chain.Upper.RestDirection, chain.Lower.RestDirection).normalized;
            if (chain.RestNormal.sqrMagnitude < 0.5f)
                chain.RestNormal = Vector3.Cross(chain.Upper.RestDirection, Vector3.forward).normalized;
            return chain;
        }

        private Segment BindSegment(Transform bone, Transform child, PoseLandmark a, PoseLandmark b, bool arm)
        {
            Quaternion invBody = Quaternion.Inverse(_root.rotation * _restBasis);
            Vector3 rest = (invBody * (child.position - bone.position)).normalized;
            Vector3 neutral = new Vector3(Mathf.Sign(rest.x) * (arm ? 0.12f : 0.025f), -1f, 0f).normalized;
            return new Segment { Bone = bone, A = a, B = b, RestDirection = rest, RestRotation = invBody * bone.rotation,
                NeutralDirection = neutral, TargetDirection = neutral, ShownDirection = neutral };
        }

        private void Update()
        {
            if (!_bound || _session == null) return;
            // Source/session Update runs first; the clip driver runs at 150, after this owner switch.
            SetPhase(_session.Armer != null && _session.Armer.IsArmed, Time.unscaledTime);
        }

        private void SetPhase(bool armed, float now)
        {
            if (_armed != armed)
            {
                Capture(_transitionLocal, _transitionPositions);
                _transitionAt = now;
                _armed = armed;
                if (!armed)
                {
                    _bodyStableSince = -1f; _bodyAcquired = false; _bodySamples = 0;
                    _arrival = _bodySeen = -100f;
                }
            }
            MirrorPhase = !armed;
            _animator.enabled = armed;
        }

        private void LateUpdate()
        {
            if (!_bound || _session == null) return;
            // Rep-scoring quality requires both arms; it must not switch off a valid profile.
            Step(_session.LastFrame, true,
                _session.Armer != null && _session.Armer.IsArmed, Time.unscaledTime, Time.unscaledDeltaTime);
        }

        // Deterministic seam used by the editor regression runner with synthetic frames and real rigs.
        private void Step(PoseFrame frame, bool tracking, bool armed, float now, float dt)
        {
            if (!_bound) return;
            dt = Mathf.Clamp(dt, 0f, 0.05f);
            SetPhase(armed, now);
            // Observe capture timestamps even during clip playback. Disarming must not relabel
            // a cached pre-battle pose as fresh when the camera has stopped producing frames.
            bool newFrame = frame.IsValid && frame.HasWorldLandmarks && PoseRetargetMath.Finite(frame.TimestampSec)
                && frame.TimestampSec != _lastStamp;
            if (newFrame)
            {
                _sampleDt = PoseRetargetMath.Finite(_lastStamp) && frame.TimestampSec > _lastStamp
                    ? Mathf.Clamp(frame.TimestampSec - _lastStamp, 0.008f, 0.15f) : 1f / 30f;
                _lastStamp = frame.TimestampSec;
                _arrival = now;
            }
            if (armed)
            {
                MirrorWeight = 1f - Mathf.Clamp01((now - _transitionAt) / Mathf.Max(0.001f, _armedBlendSec));
                if (MirrorWeight > 0f) BlendSnapshot(MirrorWeight);
                Mirroring = false; HasFreshPose = false; TrackedSegments = 0;
                return; // No writes at weight zero: the evaluated animation owns every bone.
            }

            if (newFrame && tracking) Sample(frame, now);
            HasFreshPose = tracking && frame.IsValid && frame.HasWorldLandmarks && now - _arrival <= _staleFrameSec;
            bool torsoLive = HasFreshPose && now - _bodySeen <= _staleFrameSec;
            if (!torsoLive) { _bodyStableSince = -1f; _bodySamples = 0; }
            bool active = _mirrorLimbs && (_bodyAcquired || (torsoLive && _bodyStableSince >= 0f
                && _bodySamples >= 3 && now - _bodyStableSince >= _skeletonStableSec));
            if (active) _bodyAcquired = true;
            if (!torsoLive && now - _bodySeen > _occlusionHoldSec + _occlusionRelaxSec) _bodyAcquired = false;
            float targetWeight = active && (torsoLive || now - _bodySeen <= _occlusionHoldSec) ? 1f : 0f;
            MirrorWeight = Mathf.MoveTowards(MirrorWeight, targetWeight, dt / Mathf.Max(0.05f,
                targetWeight > MirrorWeight ? _skeletonBlendSec : _occlusionRelaxSec));
            var bodyGoal = Quaternion.Slerp(_restBasis, _bodyTarget, MirrorWeight);
            _bodyShown = PoseRetargetMath.Follow(_bodyShown, bodyGoal, _followRate * 0.7f, 240f, dt);

            // Reset local bones, including wrists/spine/fingers left over from the push-up clip.
            // Restore locals before solving parents; never accumulate rotations from last render.
            for (int i = 0; i < _poseBones.Length; i++)
            { _poseBones[i].localRotation = _neutralLocal[i]; _poseBones[i].localPosition = _neutralPositions[i]; }
            Quaternion bodyWorld = _root.rotation * _bodyShown;
            _hips.rotation = bodyWorld * _hipsRestInBody;
            TrackedSegments = 0;
            foreach (var chain in _chains) if (chain != null) SolveChain(chain, bodyWorld, active && HasFreshPose, now, dt);
            float transition = 1f - Mathf.Clamp01((now - _transitionAt) / Mathf.Max(0.001f, _armedBlendSec));
            if (transition > 0f) BlendSnapshot(transition);
            Mirroring = MirrorWeight > 0.01f && HasFreshPose;
        }

        private void Sample(PoseFrame frame, float now)
        {
            for (int i = 0; i < _joints.Length; i++)
            {
                ref Joint joint = ref _joints[i];
                var lm = frame.WorldLandmarks[i];
                var image = frame.Landmarks[i];
                Vector3 raw = new Vector3(lm.X, lm.Y, lm.Z);
                joint.Visibility = 0f;
                if (!PoseRetargetMath.Finite(raw) || raw.sqrMagnitude > 9f || !PoseRetargetMath.Finite(image.Visibility)
                    || !PoseRetargetMath.Finite(image.X) || !PoseRetargetMath.Finite(image.Y)
                    || image.X < -0.05f || image.X > 1.05f || image.Y < -0.05f || image.Y > 1.05f || image.Visibility < 0.12f) continue;
                if (!joint.Initialized || now - joint.SeenAt > 0.8f)
                { joint.X.Reset(); joint.Y.Reset(); joint.Z.Reset(); }
                else raw = Vector3.MoveTowards(joint.Position, raw, 5f * _sampleDt);
                joint.Position = new Vector3(joint.X.Filter(raw.x, _sampleDt), joint.Y.Filter(raw.y, _sampleDt), joint.Z.Filter(raw.z, _sampleDt));
                joint.Visibility = image.Visibility;
                joint.SeenAt = now; joint.Initialized = true;
            }
            if (!TryBody(out var body)) return;
            _bodyTarget = body;
            if (now - _bodySeen > _staleFrameSec) { _bodyStableSince = now; _bodySamples = 0; }
            _bodySeen = now;
            _bodySamples++;
            if (_bodyStableSince < 0f) _bodyStableSince = now;
            foreach (var chain in _chains)
                if (chain != null) { SampleSegment(chain.Upper, now); SampleSegment(chain.Lower, now); }
        }

        private Quaternion CameraRotation => _anchor != null && _anchor.StageCamera != null ? _anchor.StageCamera.transform.rotation
            : _stageCamera != null ? _stageCamera.transform.rotation : _root.rotation * Quaternion.Euler(0f, 180f, 0f);

        private Joint GetJoint(PoseLandmark id) => _joints[(int)(MirrorHorizontally ? PoseRetargetMath.SwapSide(id) : id)];
        private Vector3 Position(Joint joint) => Quaternion.Inverse(_root.rotation)
            * PoseRetargetMath.MapDirection(joint.Position, MirrorHorizontally, CameraRotation);

        private bool TryBody(out Quaternion body)
        {
            body = _bodyTarget;
            var ls = GetJoint(PoseLandmark.LeftShoulder); var rs = GetJoint(PoseLandmark.RightShoulder);
            var lh = GetJoint(PoseLandmark.LeftHip); var rh = GetJoint(PoseLandmark.RightHip);
            float left = Mathf.Min(ls.Visibility, lh.Visibility), right = Mathf.Min(rs.Visibility, rh.Visibility);
            if (Mathf.Max(left, right) < _minJointVis) return false;
            Vector3 up = Vector3.zero;
            if (left >= _minJointVis) up += (Position(ls) - Position(lh)) * left;
            if (right >= _minJointVis) up += (Position(rs) - Position(rh)) * right;
            Vector3 across = Vector3.zero;
            float shoulderVis = Mathf.Min(ls.Visibility, rs.Visibility), hipVis = Mathf.Min(lh.Visibility, rh.Visibility);
            if (shoulderVis >= 0.15f) across += (Position(rs) - Position(ls)) * shoulderVis;
            if (hipVis >= 0.15f) across += (Position(rh) - Position(lh)) * hipVis;
            // Fully occluded profile: keep last heading while still following visible torso tilt.
            if (across.sqrMagnitude < 0.0001f) across = _bodyTarget * Vector3.right;
            // A shoulder is wider than its same-side hip. Do not interpret that taper as
            // sideways lean when only the near side is visible.
            up = Vector3.ProjectOnPlane(up, across);
            if (up.sqrMagnitude < 0.0025f) return false;
            return PoseRetargetMath.TryBasis(across, up, out body);
        }

        private void SampleSegment(Segment segment, float now)
        {
            var a = GetJoint(segment.A); var b = GetJoint(segment.B);
            float confidence = Mathf.Min(a.Visibility, b.Visibility);
            float threshold = segment.Visible ? Mathf.Max(0.18f, _minJointVis - 0.1f) : _minJointVis;
            Vector3 direction = Position(b) - Position(a);
            float length = direction.magnitude;
            bool usable = confidence >= threshold && length >= 0.06f && length <= 0.85f;
            segment.Visible = usable;
            if (!usable) { segment.StableSince = -1f; return; }
            if (segment.StableSince < 0f) segment.StableSince = now;
            if (now - segment.StableSince < 0.06f) return;
            segment.TargetDirection = Quaternion.Inverse(_bodyTarget) * (direction / length);
            segment.SeenAt = now;
        }

        private void FollowSegment(Segment segment, bool live, float now, float dt)
        {
            bool recent = live && now - segment.SeenAt <= _staleFrameSec;
            float weight = recent || now - segment.SeenAt <= _occlusionHoldSec ? MirrorWeight : 0f;
            segment.Weight = Mathf.MoveTowards(segment.Weight, weight, dt / (weight > segment.Weight ? _skeletonBlendSec : _occlusionRelaxSec));
            Vector3 target = Vector3.Slerp(segment.NeutralDirection, segment.TargetDirection, segment.Weight);
            segment.ShownDirection = Vector3.RotateTowards(segment.ShownDirection,
                Vector3.Slerp(segment.ShownDirection, target, 1f - Mathf.Exp(-_followRate * dt)), _maxJointSpeed * Mathf.Deg2Rad * dt, 0f).normalized;
            if (recent) TrackedSegments++;
        }

        private void SolveChain(Chain chain, Quaternion bodyWorld, bool live, float now, float dt)
        {
            FollowSegment(chain.Upper, live, now, dt); FollowSegment(chain.Lower, live, now, dt);
            var u = chain.Upper; var l = chain.Lower;
            Quaternion swing = Quaternion.FromToRotation(u.RestDirection, u.ShownDirection);
            Vector3 normal = Vector3.Cross(u.ShownDirection, l.ShownDirection);
            // Straight elbows/knees do not define roll. Hold instead of flipping the bend plane.
            if (normal.magnitude > 0.15f && u.Weight > 0.5f && l.Weight > 0.5f)
            {
                float twist = Vector3.SignedAngle(swing * chain.RestNormal, normal.normalized, u.ShownDirection);
                chain.Twist = Mathf.MoveTowardsAngle(chain.Twist, Mathf.Clamp(twist, -100f, 100f), 240f * dt);
            }
            else if (u.Weight < 0.1f) chain.Twist = Mathf.MoveTowardsAngle(chain.Twist, 0f, 180f * dt);
            Quaternion upperDelta = Quaternion.AngleAxis(chain.Twist, u.ShownDirection) * swing;
            u.Bone.rotation = bodyWorld * upperDelta * u.RestRotation;
            Quaternion lowerSwing = Quaternion.FromToRotation(upperDelta * l.RestDirection, l.ShownDirection);
            l.Bone.rotation = bodyWorld * lowerSwing * upperDelta * l.RestRotation;
        }

        private void Capture(Quaternion[] rotations, Vector3[] positions)
        {
            for (int i = 0; i < _poseBones.Length; i++)
            { rotations[i] = _poseBones[i].localRotation; positions[i] = _poseBones[i].localPosition; }
        }

        private void BlendSnapshot(float weight)
        {
            for (int i = 0; i < _poseBones.Length; i++)
            {
                _poseBones[i].localRotation = Quaternion.Slerp(_poseBones[i].localRotation, _transitionLocal[i], weight);
                _poseBones[i].localPosition = Vector3.Lerp(_poseBones[i].localPosition, _transitionPositions[i], weight);
            }
        }

        private void OnDisable()
        {
            if (_bound && _animator != null) _animator.enabled = _animatorWasEnabled;
            Mirroring = false; MirrorPhase = false;
        }
    }
}
