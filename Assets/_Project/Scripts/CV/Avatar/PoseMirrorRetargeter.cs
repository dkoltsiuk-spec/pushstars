using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Variant B of the avatar experiment (owner's request after seeing variant A): NO canned
    /// animation — the character's limbs LIVE-MIRROR the user's skeleton from the camera.
    ///
    /// <para><b>How:</b> MediaPipe world landmarks (3D, meters, hip-centered) give a direction for
    /// every limb segment (shoulder→elbow, elbow→wrist, hip→knee, knee→ankle). At bind time the
    /// character's rest-pose bone directions are captured in root space; each frame the bone gets
    /// the rotation that swings its rest direction onto the (smoothed) live direction:
    /// <c>bone.rotation = root.rotation * FromToRotation(restDir, liveDir) * restRot</c>.
    /// The hips take a full orientation from the torso frame (shoulders line × spine). Absolute
    /// assignments parent-first, so the hierarchy stays consistent.</para>
    ///
    /// <para><b>Coordinate mapping:</b> world landmarks are camera-frame (x right, y down, z away
    /// from viewer); the character faces the camera, so the same-side, sign-flipped mapping
    /// (−x, −y, −z) makes the character move like a MIRROR: your left hand rises — the hand on the
    /// same side of the screen rises. <see cref="_flipX"/>/<see cref="_flipZ"/> are exposed for
    /// live correction, same spirit as the old orientation buttons.</para>
    ///
    /// <para>The Animator must be a Humanoid with NO controller (the stand builds it that way) —
    /// bones rest in the import pose and this component owns them in LateUpdate. Per-segment
    /// smoothing (slerp toward target) plus visibility hold keep it from twitching; the
    /// <see cref="AvatarMirrorAnchor"/> keeps owning root position/scale.</para>
    /// </summary>
    public sealed class PoseMirrorRetargeter : MonoBehaviour
    {
        [SerializeField] private PushupSession _session;
        [SerializeField] private Animator _animator;

        [Header("Mapping (flip live if a limb moves the wrong way)")]
        [SerializeField] private bool _flipX = true;
        [SerializeField] private bool _flipZ = true;

        [Header("Feel")]
        [Tooltip("How fast a limb converges on the live direction (1/s). High = tighter mirror.")]
        [SerializeField, Range(4f, 40f)] private float _followRate = 16f;
        [Tooltip("Segments whose landmarks fall below this visibility hold their last pose.")]
        [SerializeField, Range(0f, 1f)] private float _minJointVis = 0.35f;

        /// <summary>True once the rig is bound and at least one live frame has been applied.</summary>
        public bool Mirroring { get; private set; }

        private struct Segment
        {
            public Transform Bone;
            public PoseLandmark LmA, LmB;
            public Quaternion RestRotInRoot;
            public Vector3 RestDirInRoot;
            public Vector3 SmoothedDir;
            public bool HasDir;
        }

        private Segment[] _segments;
        private Transform _root;
        private Transform _hips;
        private Quaternion _hipsRestRotInRoot;
        private Vector3 _hipsSmoothedUp, _hipsSmoothedRight;
        private bool _bound;

        private void Start()
        {
            TryBind();
        }

        private void TryBind()
        {
            if (_animator == null || !_animator.isHuman) return;
            _root = _animator.transform;

            // The stand's model is the "@Push Up" FBX — its import default pose is a PLANK frame,
            // not a stand. Binding rest directions off a plank made FromToRotation swing every
            // limb through huge twisted arcs ("персонажа крутит") and the idle look was a push-up.
            // Neutralize to the humanoid default pose (all muscles = 0 ≈ upright T/A-pose) BEFORE
            // capturing the rest snapshot.
            NeutralizePose();

            Quaternion invRoot = Quaternion.Inverse(_root.rotation);

            _hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
            if (_hips == null) return;
            _hipsRestRotInRoot = invRoot * _hips.rotation;
            _hipsSmoothedUp = Vector3.up;
            _hipsSmoothedRight = Vector3.right;

            // (parent bone, child bone for the rest direction, landmark pair)
            var defs = new (HumanBodyBones parent, HumanBodyBones child, PoseLandmark a, PoseLandmark b)[]
            {
                (HumanBodyBones.LeftUpperArm,  HumanBodyBones.LeftLowerArm,  PoseLandmark.LeftShoulder,  PoseLandmark.LeftElbow),
                (HumanBodyBones.LeftLowerArm,  HumanBodyBones.LeftHand,      PoseLandmark.LeftElbow,     PoseLandmark.LeftWrist),
                (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, PoseLandmark.RightShoulder, PoseLandmark.RightElbow),
                (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,     PoseLandmark.RightElbow,    PoseLandmark.RightWrist),
                (HumanBodyBones.LeftUpperLeg,  HumanBodyBones.LeftLowerLeg,  PoseLandmark.LeftHip,       PoseLandmark.LeftKnee),
                (HumanBodyBones.LeftLowerLeg,  HumanBodyBones.LeftFoot,      PoseLandmark.LeftKnee,      PoseLandmark.LeftAnkle),
                (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, PoseLandmark.RightHip,      PoseLandmark.RightKnee),
                (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,     PoseLandmark.RightKnee,     PoseLandmark.RightAnkle),
            };

            var list = new System.Collections.Generic.List<Segment>(defs.Length);
            foreach (var d in defs)
            {
                var bone = _animator.GetBoneTransform(d.parent);
                var child = _animator.GetBoneTransform(d.child);
                if (bone == null || child == null) continue;
                Vector3 restDir = invRoot * (child.position - bone.position);
                if (restDir.sqrMagnitude < 1e-8f) continue;
                list.Add(new Segment
                {
                    Bone = bone,
                    LmA = d.a,
                    LmB = d.b,
                    RestRotInRoot = invRoot * bone.rotation,
                    RestDirInRoot = restDir.normalized,
                    SmoothedDir = restDir.normalized,
                    HasDir = false,
                });
            }
            _segments = list.ToArray();
            _bound = _segments.Length > 0;
        }

        private void LateUpdate()
        {
            if (!_bound || _session == null) return;

            var frame = _session.LastFrame;
            if (!frame.IsValid || !frame.HasWorldLandmarks) return;

            float k = 1f - Mathf.Exp(-Time.deltaTime * _followRate);
            Quaternion rootRot = _root.rotation;

            // ── Hips: full torso orientation from the shoulder line + spine ──
            bool torsoOk =
                frame.Visibility(PoseLandmark.LeftShoulder)  >= _minJointVis &&
                frame.Visibility(PoseLandmark.RightShoulder) >= _minJointVis &&
                frame.Visibility(PoseLandmark.LeftHip)  >= _minJointVis &&
                frame.Visibility(PoseLandmark.RightHip) >= _minJointVis;
            if (torsoOk)
            {
                Vector3 ls = World(frame, PoseLandmark.LeftShoulder);
                Vector3 rs = World(frame, PoseLandmark.RightShoulder);
                Vector3 lh = World(frame, PoseLandmark.LeftHip);
                Vector3 rh = World(frame, PoseLandmark.RightHip);
                Vector3 up = MapDir((ls + rs) * 0.5f - (lh + rh) * 0.5f);
                Vector3 right = MapDir(rs - ls);
                if (up.sqrMagnitude > 1e-6f && right.sqrMagnitude > 1e-6f)
                {
                    _hipsSmoothedUp = Vector3.Slerp(_hipsSmoothedUp, up.normalized, k);
                    _hipsSmoothedRight = Vector3.Slerp(_hipsSmoothedRight, right.normalized, k);
                    Vector3 fwd = Vector3.Cross(_hipsSmoothedRight, _hipsSmoothedUp);
                    if (fwd.sqrMagnitude > 1e-6f)
                        _hips.rotation = rootRot
                            * Quaternion.LookRotation(fwd, _hipsSmoothedUp)
                            * _hipsRestRotInRoot;
                }
            }

            // ── Limb segments, parent-first (array order) ──
            for (int i = 0; i < _segments.Length; i++)
            {
                ref var seg = ref _segments[i];
                if (frame.Visibility(seg.LmA) < _minJointVis || frame.Visibility(seg.LmB) < _minJointVis)
                    continue; // hold last pose

                Vector3 dir = MapDir(World(frame, seg.LmB) - World(frame, seg.LmA));
                if (dir.sqrMagnitude < 1e-6f) continue;
                dir.Normalize();

                seg.SmoothedDir = seg.HasDir ? Vector3.Slerp(seg.SmoothedDir, dir, k) : dir;
                seg.HasDir = true;

                Quaternion delta = Quaternion.FromToRotation(seg.RestDirInRoot, seg.SmoothedDir);
                seg.Bone.rotation = rootRot * delta * seg.RestRotInRoot;
            }

            Mirroring = true;
        }

        private void NeutralizePose()
        {
            var handler = new HumanPoseHandler(_animator.avatar, _root);
            var pose = new HumanPose();
            handler.GetHumanPose(ref pose);
            for (int i = 0; i < pose.muscles.Length; i++) pose.muscles[i] = 0f;
            pose.bodyRotation = Quaternion.identity;
            handler.SetHumanPose(ref pose);
            handler.Dispose();
        }

        private static Vector3 World(in PoseFrame f, PoseLandmark id)
        {
            var lm = f.GetWorld(id);
            return new Vector3(lm.X, lm.Y, lm.Z);
        }

        /// <summary>Camera-frame world direction → character-root space. Same-side mapping with
        /// all-axis flip makes the character behave like a mirror (see class doc); the toggles
        /// let the user fix a wrong-way limb live without recompiling.</summary>
        private Vector3 MapDir(Vector3 w)
            => new Vector3(_flipX ? -w.x : w.x, -w.y, _flipZ ? -w.z : w.z);
    }
}
