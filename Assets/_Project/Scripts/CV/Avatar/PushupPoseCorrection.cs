using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// A rig-proportioned push-up pose table. The depth drivers retain their timing, while
    /// this pass supplies a straight plank and fixed, symmetric hand/foot contacts.
    /// Runs after Animator evaluation and before the live mirror's handoff blend (100).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class PushupPoseCorrection : MonoBehaviour
    {
        [Tooltip("Downward gaze angle from horizontal. The default faces the front camera with a slight chin tuck.")]
        [SerializeField, Range(-15f, 60f)] private float _gazeDownAngle = 10f;

        [Tooltip("Ankle spacing relative to hip spacing. Tune on the character prefab to leave room for its shoes.")]
        [SerializeField, Range(.5f, 1.5f)] private float _footSpacing = .90f;

        private sealed class Chain
        {
            public Transform Upper, Lower, End;
            public Vector3 UpperDirection, LowerDirection, Normal;
            public Quaternion UpperRotation, LowerRotation;
            public float A, B;
        }

        private Animator _animator;
        private Transform _root, _hips, _neck, _head;
        private Transform[] _bones;
        private Vector3[] _referencePositions, _shownPositions;
        private Quaternion[] _referenceRotations, _shownRotations;
        private Chain[] _arms, _legs;
        private Quaternion[] _handRotations, _footRotations;
        private Transform[][] _fingers;
        private Vector3 _referenceHips, _plankHipsOffset;
        private Quaternion _hipsRotation, _headReferenceRotation;
        private float _shoulderHeight, _shoulderForward, _shoulderHalfWidth, _footHalfWidth;
        private float _armLength, _ankleHeight, _wristHeight, _ankleZ, _wristZ, _topHeight;
        private bool _ready, _active, _hasShown;
        private float _depth, _fadeRemaining, _fadeDuration;

        public static PushupPoseCorrection Bind(Animator animator)
        {
            if (animator == null || !animator.isHuman) return null;
            var correction = animator.GetComponent<PushupPoseCorrection>();
            if (correction == null) correction = animator.gameObject.AddComponent<PushupPoseCorrection>();
            correction.Initialize(animator);
            return correction;
        }

        public void Initialize(Animator animator)
        {
            if (_ready && _animator == animator) return;
            if (animator == null || !animator.isHuman || animator.avatar == null) return;
            _animator = animator;
            _root = animator.transform;
            var all = _root.GetComponentsInChildren<Transform>(true);
            _bones = Array.FindAll(all, t => t != _root);
            _referencePositions = new Vector3[_bones.Length];
            _referenceRotations = new Quaternion[_bones.Length];
            _shownPositions = new Vector3[_bones.Length];
            _shownRotations = new Quaternion[_bones.Length];
            Capture(_shownPositions, _shownRotations);
            try
            {
                // Read anatomical axes from this Avatar, regardless of Mixamo/CC bone names.
                // Restore the displayed pose afterwards so binding cannot disturb the mirror.
                using (var handler = new HumanPoseHandler(animator.avatar, _root))
                {
                    var pose = new HumanPose();
                    handler.GetHumanPose(ref pose);
                    Array.Clear(pose.muscles, 0, pose.muscles.Length);
                    for (int i = 0; i < pose.muscles.Length; i++)
                    {
                        // Muscle zero leaves the fingers curled. A supporting palm needs
                        // extended fingers; the pass below keeps the Avatar's finger spread.
                        string muscle = HumanTrait.MuscleName[i];
                        if (muscle.Contains("Stretched")) pose.muscles[i] = .75f;
                    }
                    pose.bodyRotation = Quaternion.identity;
                    handler.SetHumanPose(ref pose);
                }
                Capture(_referencePositions, _referenceRotations);
                _hips = Bone(HumanBodyBones.Hips);
                _neck = Bone(HumanBodyBones.Neck);
                _head = Bone(HumanBodyBones.Head);
                _referenceHips = Position(_hips);
                _hipsRotation = Rotation(_hips);
                if (_head != null) _headReferenceRotation = Rotation(_head);
                _arms = new[]
                {
                    MakeChain(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
                    MakeChain(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand)
                };
                _legs = new[]
                {
                    MakeChain(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                    MakeChain(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot)
                };
                Vector3 shoulders = (Position(_arms[0].Upper) + Position(_arms[1].Upper)) * .5f;
                Vector3 legCentre = (Position(_legs[0].Upper) + Position(_legs[1].Upper)) * .5f;
                _shoulderHalfWidth = Mathf.Abs(Position(_arms[1].Upper).x - Position(_arms[0].Upper).x) * .5f;
                float hipHalfWidth = Mathf.Abs(Position(_legs[1].Upper).x - Position(_legs[0].Upper).x) * .5f;
                _footHalfWidth = hipHalfWidth * _footSpacing;
                // Neutral Humanoid muscles have bent knees. Use the actual chain lengths,
                // rather than that shortened pose's hip-to-ankle vertical projection.
                float legLength = Mathf.Min(_legs[0].A + _legs[0].B, _legs[1].A + _legs[1].B);
                float lateral = hipHalfWidth - _footHalfWidth;
                float legReach = Mathf.Sqrt(legLength * legLength - lateral * lateral) * .9998f;
                _plankHipsOffset = _referenceHips - legCentre + Vector3.up * legReach;
                _plankHipsOffset.x = 0f;
                Vector3 shoulderOffset = _plankHipsOffset + shoulders - _referenceHips;
                _shoulderHeight = shoulderOffset.y;
                _shoulderForward = shoulderOffset.z;
                _armLength = Mathf.Min(_arms[0].A + _arms[0].B, _arms[1].A + _arms[1].B);
                _wristHeight = _armLength * .055f;
                float footLength = (Vector3.Distance(Position(_legs[0].End), Position(Bone(HumanBodyBones.LeftToes)))
                    + Vector3.Distance(Position(_legs[1].End), Position(Bone(HumanBodyBones.RightToes)))) * .5f;
                _ankleHeight = footLength * .85f + _wristHeight * .3f;
                _ankleZ = -legReach * .85f;
                // Reserve a little vertical reach for the wider hand placement.
                _topHeight = _wristHeight + _armLength * .987f;
                float angle = PlankAngle(_topHeight);
                _wristZ = _ankleZ + Mathf.Cos(angle) * _shoulderHeight
                    + Mathf.Sin(angle) * _shoulderForward + _armLength * .06f;

                _handRotations = new[]
                {
                    HandRotation(HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal, -1f),
                    HandRotation(HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightLittleProximal, 1f)
                };
                _footRotations = new[] { FootRotation(_legs[0].End, Bone(HumanBodyBones.LeftToes)),
                    FootRotation(_legs[1].End, Bone(HumanBodyBones.RightToes)) };
                _fingers = new Transform[10][];
                for (int side = 0; side < 2; side++)
                    for (int finger = 0; finger < 5; finger++)
                    {
                        int first = (int)(side == 0 ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal) + finger * 3;
                        _fingers[side * 5 + finger] = new[] { Bone((HumanBodyBones)first),
                            Bone((HumanBodyBones)(first + 1)), Bone((HumanBodyBones)(first + 2)) };
                    }
                _ready = _shoulderHeight > .01f && _armLength > .01f;
            }
            finally { Restore(_shownPositions, _shownRotations); }
        }

        public void SetDepth(float depth, bool active, float fadeSeconds)
        {
            _depth = Mathf.Clamp01(depth);
            if (_active && !active && _hasShown)
            {
                _fadeDuration = Mathf.Max(0f, fadeSeconds);
                _fadeRemaining = _fadeDuration;
            }
            if (active) _fadeRemaining = 0f;
            else if (fadeSeconds <= 0f) _fadeRemaining = 0f;
            _active = active;
        }

        private void LateUpdate()
        {
            if (!_ready || !_animator.isActiveAndEnabled) return;
            if (_active)
            {
                Apply(_depth);
                Capture(_shownPositions, _shownRotations);
                _hasShown = true;
            }
            else if (_fadeRemaining > 0f)
            {
                _fadeRemaining = Mathf.Max(0f, _fadeRemaining - Time.deltaTime);
                float weight = _fadeRemaining / Mathf.Max(.001f, _fadeDuration);
                for (int i = 0; i < _bones.Length; i++)
                {
                    _bones[i].localPosition = Vector3.Lerp(_bones[i].localPosition, _shownPositions[i], weight);
                    _bones[i].localRotation = Quaternion.Slerp(_bones[i].localRotation, _shownRotations[i], weight);
                }
            }
        }

        /// <summary>Deterministic evaluation, also used by the editor visual regression.</summary>
        public void Apply(float depth)
        {
            if (!_ready || !PoseRetargetMath.Finite(depth)) return;
            depth = Mathf.Clamp01(depth);
            Restore(_referencePositions, _referenceRotations);
            float height = Mathf.Lerp(_topHeight, _wristHeight + _armLength * .64f, depth);
            float angle = PlankAngle(height);
            Quaternion plank = Quaternion.Euler(90f - angle * Mathf.Rad2Deg, 0f, 0f);
            Vector3 ankleCentre = new Vector3(0f, _ankleHeight, _ankleZ);
            _hips.position = _root.TransformPoint(ankleCentre + plank * _plankHipsOffset);
            _hips.rotation = _root.rotation * plank * _hipsRotation;

            // Raise the gaze gently toward the front. Share the extension with the neck,
            // and keep the final head angle steady as the plank moves through a repetition.
            if (_head != null)
            {
                float lift = _gazeDownAngle - (90f - angle * Mathf.Rad2Deg);
                if (_neck != null)
                    _neck.rotation = Quaternion.AngleAxis(lift * .35f, _root.right) * _neck.rotation;
                _head.rotation = _root.rotation * Quaternion.Euler(_gazeDownAngle, 0f, 0f) * _headReferenceRotation;
            }

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var foot = new Vector3(side * _footHalfWidth, _ankleHeight, _ankleZ);
                Solve(_legs[i], foot, Vector3.down);
                _legs[i].End.rotation = _root.rotation * _footRotations[i];

                var wrist = new Vector3(side * (_shoulderHalfWidth + _armLength * .12f), _wristHeight, _wristZ);
                // Elbows fold back and modestly out; the two sides use mirrored bend planes.
                Solve(_arms[i], wrist, new Vector3(side * .55f, 0f, -1f));
                _arms[i].End.rotation = _root.rotation * _handRotations[i];
            }
            // Flatten phalanges onto the support plane without changing finger spread or
            // bone lengths. Humanoid stretch limits differ between the two imported rigs.
            foreach (var finger in _fingers)
                for (int joint = 0; joint < 2; joint++)
                {
                    if (finger[joint] == null || finger[joint + 1] == null) continue;
                    Vector3 direction = finger[joint + 1].position - finger[joint].position;
                    Vector3 flat = Vector3.ProjectOnPlane(direction, _root.up);
                    if (flat.sqrMagnitude > .00000001f)
                        finger[joint].rotation = Quaternion.FromToRotation(direction, flat) * finger[joint].rotation;
                }
        }

        private Chain MakeChain(HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones end)
        {
            var u = Bone(upper); var l = Bone(lower); var e = Bone(end);
            Vector3 a = Position(l) - Position(u), b = Position(e) - Position(l);
            Vector3 normal = Vector3.Cross(a.normalized, b.normalized);
            if (normal.sqrMagnitude < .000001f)
                normal = -Vector3.Cross(a.normalized, Vector3.forward);
            return new Chain
            {
                Upper = u, Lower = l, End = e, A = a.magnitude, B = b.magnitude,
                UpperDirection = a.normalized, LowerDirection = b.normalized,
                Normal = normal.normalized,
                UpperRotation = Rotation(u), LowerRotation = Rotation(l)
            };
        }

        private void Solve(Chain chain, Vector3 target, Vector3 bend)
        {
            Vector3 start = Position(chain.Upper), offset = target - start;
            float distance = Mathf.Clamp(offset.magnitude, Mathf.Abs(chain.A - chain.B) + .00001f, chain.A + chain.B - .00001f);
            Vector3 direction = offset.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(bend, direction).normalized;
            float along = (chain.A * chain.A - chain.B * chain.B + distance * distance) / (2f * distance);
            float across = Mathf.Sqrt(Mathf.Max(0f, chain.A * chain.A - along * along));
            Vector3 middle = start + direction * along + pole * across;
            Vector3 end = start + direction * distance;
            Vector3 upper = (middle - start).normalized, lower = (end - middle).normalized;
            Vector3 normal = Vector3.Cross(upper, lower).normalized;
            chain.Upper.rotation = _root.rotation * Align(chain.UpperDirection, chain.Normal, upper, normal) * chain.UpperRotation;
            chain.Lower.rotation = _root.rotation * Align(chain.LowerDirection, chain.Normal, lower, normal) * chain.LowerRotation;
        }

        private static Quaternion Align(Vector3 from, Vector3 fromNormal, Vector3 to, Vector3 toNormal)
            => Quaternion.LookRotation(to, toNormal) * Quaternion.Inverse(Quaternion.LookRotation(from, fromNormal));

        private Quaternion HandRotation(HumanBodyBones handId, HumanBodyBones middleId,
            HumanBodyBones indexId, HumanBodyBones littleId, float side)
        {
            var hand = Bone(handId); var middle = Bone(middleId);
            var index = Bone(indexId); var little = Bone(littleId);
            if (middle == null || index == null || little == null) return Quaternion.Euler(90f, 0f, 0f) * Rotation(hand);
            Vector3 forward = (Position(middle) - Position(hand)).normalized;
            Vector3 normal = Vector3.Cross(forward, Position(index) - Position(little)).normalized * -side;
            Vector3 fingers = new Vector3(side * .10f, 0f, 1f).normalized;
            return Align(forward, normal, fingers, Vector3.up) * Rotation(hand);
        }

        private Quaternion FootRotation(Transform foot, Transform toes)
        {
            Vector3 forward = (Position(toes) - Position(foot)).normalized;
            // Muscle-zero feet can be banked. Recover their up axis from the Avatar's
            // standing reference; a world-up assumption rolls the shoes onto their edges.
            Vector3 soleUp = FootSurfaceUp(foot, forward);
            return Align(forward, soleUp, new Vector3(0f, -.85f, .526783f), Vector3.up) * Rotation(foot);
        }

        private Vector3 FootSurfaceUp(Transform foot, Vector3 forward)
        {
            // Avatar reference rotations remain available in builds with non-readable meshes.
            // Accumulate the imported standing reference, then transport its up axis into
            // muscle-zero space. This retains each rig's own ankle roll convention.
            var skeleton = _animator.avatar.humanDescription.skeleton;
            Quaternion reference = Quaternion.identity;
            for (Transform node = foot; node != null && node != _root; node = node.parent)
            {
                Quaternion local = node.localRotation;
                foreach (var bone in skeleton)
                    if (bone.name == node.name) { local = bone.rotation; break; }
                reference = local * reference;
            }
            Vector3 up = Rotation(foot) * Quaternion.Inverse(reference) * Vector3.up;
            up = Vector3.ProjectOnPlane(up, forward);
            return up.sqrMagnitude > .00000001f ? up.normalized : Vector3.up;
        }

        private float PlankAngle(float shoulderY)
        {
            float radius = Mathf.Sqrt(_shoulderHeight * _shoulderHeight + _shoulderForward * _shoulderForward);
            return Mathf.Asin(Mathf.Clamp((shoulderY - _ankleHeight) / radius, -.8f, .8f))
                + Mathf.Atan2(_shoulderForward, _shoulderHeight);
        }

        private Transform Bone(HumanBodyBones id) => _animator.GetBoneTransform(id);
        private Vector3 Position(Transform bone) => _root.InverseTransformPoint(bone.position);
        private Quaternion Rotation(Transform bone) => Quaternion.Inverse(_root.rotation) * bone.rotation;

        private void Capture(Vector3[] positions, Quaternion[] rotations)
        {
            for (int i = 0; i < _bones.Length; i++)
            { positions[i] = _bones[i].localPosition; rotations[i] = _bones[i].localRotation; }
        }

        private void Restore(Vector3[] positions, Quaternion[] rotations)
        {
            for (int i = 0; i < _bones.Length; i++)
            { _bones[i].localPosition = positions[i]; _bones[i].localRotation = rotations[i]; }
        }
    }
}
