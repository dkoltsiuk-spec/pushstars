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
    public sealed class PoseMirrorRetargeter : MonoBehaviour, IAvatarAnimator
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

        [Header("Skeleton gate (the limbs only join once the whole body is in frame)")]
        [Tooltip("Every segment's landmarks must be at least this visible before the limbs are " +
                 "driven at all. Below it they stay on the idle clip, and the anchor alone carries " +
                 "the body — which is the point: half a skeleton drives half the limbs, and a " +
                 "character with two of its four limbs guessing reads as broken, not as tracking.")]
        [SerializeField, Range(0f, 1f)] private float _minSkeletonVis = 0.5f;

        [Tooltip("The whole skeleton has to hold that visibility this long before the limbs join, " +
                 "so one lucky frame at the edge of the shot cannot snap them into place.")]
        [SerializeField, Range(0f, 2f)] private float _skeletonStableSec = 0.4f;

        [Tooltip("How long the limbs take to join and to let go. Losing a foot for a moment fades " +
                 "them back to idle rather than dropping them.")]
        [SerializeField, Range(0.05f, 1.5f)] private float _skeletonBlendSec = 0.35f;

        [Header("Calibration")]
        [Tooltip("Zero the mirror on the stance the person is actually standing in, captured the " +
                 "moment the whole skeleton first holds still, and drive the body by the difference " +
                 "from it. Off, the reference is the rig's own rest pose, and standing normally " +
                 "already reads as a deviation from it: a rig whose legs rest apart gets its thighs " +
                 "pulled together the moment a real pair of nearly-vertical ones is mapped onto it.")]
        [SerializeField] private bool _calibrateToStance = true;

        [Header("Hybrid handoff (owner's flow: mirror until the plank arms, then the animation)")]
        [Tooltip("When the session ARMS, the mirror blends OUT over this time and the Animator " +
                 "(push-up scrub, PushupAvatarDriver) takes the body; on disarm it blends back in. " +
                 "0 = hard switch.")]
        [SerializeField, Range(0f, 1.5f)] private float _armedBlendSec = 0.35f;

        /// <summary>True once the rig is bound and at least one live frame has been applied.</summary>
        public bool Mirroring { get; private set; }

        /// <summary>1 = full live mirror, 0 = the Animator owns the body. Blends between.</summary>
        public float MirrorWeight { get; private set; } = 1f;

        /// <summary>
        /// True for as long as the mirror phase owns the character — everything before the plank
        /// arms — whether or not the whole skeleton is in frame yet.
        ///
        /// <para>Not the same question as <see cref="MirrorWeight"/>, and the difference matters to
        /// anything framing a shot: the anchor is carrying the body through the whole phase, limbs
        /// or no limbs, so a camera that re-centres on the body has to hold still for all of it,
        /// not just for the part where the arms are tracking.</para>
        /// </summary>
        public bool MirrorPhase { get; private set; }

        private struct Segment
        {
            public Transform Bone;
            public PoseLandmark LmA, LmB;
            public Quaternion RestRotInRoot;
            public Vector3 RestDirInRoot;
            public Vector3 NeutralDir;
            public Vector3 SmoothedDir;
            public bool HasDir;
        }

        /// <summary>How much of the frame the torso has to span vertically before it counts as
        /// upright enough to answer which way up the world frame is.</summary>
        private const float UprightTorso2D = 0.08f;

        /// <summary>Sign the world frame's y needs to read as "up on screen". Starts at the
        /// documented y-down convention and is confirmed or corrected against the 2D landmarks the
        /// first time the person stands clearly upright in frame.</summary>
        private float _upSign = -1f;
        private bool _upResolved;

        private float _wholeSince = -1f;
        private bool _limbsReady;
        private bool _calibrated;
        private Quaternion _neutralHips = Quaternion.identity;

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

        /// <summary>
        /// Binds to a body that did not exist when the scene was saved.
        ///
        /// <para>The stand wires its animator in the Inspector and never calls this. The fight
        /// screen cannot: which body stands there depends on a saved preference, so it is
        /// instantiated at load and handed out afterwards — the same reason
        /// <see cref="IAvatarAnimator"/> exists at all.</para>
        /// </summary>
        public void BindAnimator(Animator animator)
        {
            _animator = animator;
            _bound = false;
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

            // Hybrid handoff: armed → the animation owns the body (weight → 0); the mirror poses
            // are still computed during the blend so the two cross-fade instead of snapping. Runs
            // in LateUpdate AFTER the Animator evaluated, so a partial weight Slerps FROM the
            // animator-written pose TOWARD the live-mirrored one.
            bool armed = _session.Armer != null && _session.Armer.IsArmed;
            MirrorPhase = !armed;

            var frame = _session.LastFrame;
            bool live = frame.IsValid && frame.HasWorldLandmarks;

            // The limbs are a second stage, behind the anchor. Until the whole skeleton has been
            // in frame long enough to trust, they stay on the idle clip and only the anchor moves
            // the body — so walking up to the camera, where the shot cuts the legs off, glides
            // instead of throwing half a body around the screen.
            bool whole = live && WholeSkeletonVisible(in frame);
            if (whole)
            {
                if (_wholeSince < 0f) _wholeSince = Time.time;
            }
            else _wholeSince = -1f;

            bool limbsReady = _wholeSince >= 0f && Time.time - _wholeSince >= _skeletonStableSec;

            // Snap the smoothing to the live pose as the gate opens: the stored directions are as
            // old as the last time the body was fully in shot, and slerping out of them is a limb
            // swinging through an arc that never happened.
            if (limbsReady && !_limbsReady && _segments != null)
            {
                for (int i = 0; i < _segments.Length; i++) _segments[i].HasDir = false;
                if (_calibrateToStance) Calibrate(in frame);
            }
            _limbsReady = limbsReady;

            float targetWeight = armed || !limbsReady ? 0f : 1f;
            float blendSec = armed ? _armedBlendSec : _skeletonBlendSec;
            MirrorWeight = blendSec > 1e-3f
                ? Mathf.MoveTowards(MirrorWeight, targetWeight, Time.deltaTime / blendSec)
                : targetWeight;

            if (MirrorWeight <= 0.001f) return; // idle or animation owns the bones
            if (!live) return;

            float w = MirrorWeight;
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
                ResolveUpSign(in frame);

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
                    {
                        // Against the stance that was captured, not against the world: at the
                        // captured neutral this is identity, and the hips sit exactly where the
                        // rig rests them.
                        Quaternion torso = Quaternion.LookRotation(fwd, _hipsSmoothedUp);
                        Quaternion delta = _calibrated
                            ? torso * Quaternion.Inverse(_neutralHips)
                            : torso;
                        Quaternion target = rootRot * delta * _hipsRestRotInRoot;
                        _hips.rotation = Quaternion.Slerp(_hips.rotation, target, w);
                    }
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

                Vector3 reference = _calibrated ? seg.NeutralDir : seg.RestDirInRoot;
                Quaternion delta = Quaternion.FromToRotation(reference, seg.SmoothedDir);
                Quaternion target = rootRot * delta * seg.RestRotInRoot;
                seg.Bone.rotation = Quaternion.Slerp(seg.Bone.rotation, target, w);
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

        /// <summary>
        /// Zeroes the mirror on the stance the person is standing in right now.
        ///
        /// <para>Without it the reference is the rig's rest pose, and every difference between that
        /// pose and a real body reads as movement the person is not making. The legs are the
        /// obvious one: the rig rests with its thighs apart, a person standing normally has them
        /// close to vertical, and swinging one onto the other pulls the knees together — which is
        /// exactly what the level test showed. Captured here, standing normally maps onto the rig
        /// standing normally, and only what the person actually does moves the body.</para>
        ///
        /// <para>Taken at the moment the whole skeleton first holds still, which is the one moment
        /// this screen can be sure the person is in frame and settled. Re-taken every time the gate
        /// re-opens: someone who walks out of shot and back has re-arranged themselves.</para>
        /// </summary>
        private void Calibrate(in PoseFrame frame)
        {
            for (int i = 0; i < _segments.Length; i++)
            {
                ref var seg = ref _segments[i];
                Vector3 dir = MapDir(World(frame, seg.LmB) - World(frame, seg.LmA));
                if (dir.sqrMagnitude < 1e-6f) return; // nothing usable — keep the rig's rest pose
                seg.NeutralDir = dir.normalized;
            }

            Vector3 up = MapDir(World(frame, PoseLandmark.LeftShoulder)
                              + World(frame, PoseLandmark.RightShoulder)
                              - World(frame, PoseLandmark.LeftHip)
                              - World(frame, PoseLandmark.RightHip));
            Vector3 right = MapDir(World(frame, PoseLandmark.RightShoulder)
                                 - World(frame, PoseLandmark.LeftShoulder));
            if (up.sqrMagnitude < 1e-6f || right.sqrMagnitude < 1e-6f) return;

            Vector3 fwd = Vector3.Cross(right.normalized, up.normalized);
            if (fwd.sqrMagnitude < 1e-6f) return;

            _neutralHips = Quaternion.LookRotation(fwd, up.normalized);
            _calibrated = true;
        }

        /// <summary>Whether every joint the limbs are driven from is in shot at once — both ends of
        /// all eight segments, plus the torso the hips are built from. All of them or none: the
        /// gate is about the body being wholly in frame, and a per-limb version of it is the same
        /// half-tracked character this exists to avoid.</summary>
        private bool WholeSkeletonVisible(in PoseFrame frame)
        {
            if (_segments == null) return false;

            if (frame.Visibility(PoseLandmark.LeftShoulder) < _minSkeletonVis ||
                frame.Visibility(PoseLandmark.RightShoulder) < _minSkeletonVis ||
                frame.Visibility(PoseLandmark.LeftHip) < _minSkeletonVis ||
                frame.Visibility(PoseLandmark.RightHip) < _minSkeletonVis) return false;

            for (int i = 0; i < _segments.Length; i++)
            {
                if (frame.Visibility(_segments[i].LmA) < _minSkeletonVis) return false;
                if (frame.Visibility(_segments[i].LmB) < _minSkeletonVis) return false;
            }
            return true;
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
            => new Vector3(_flipX ? -w.x : w.x, _upSign * w.y, _flipZ ? -w.z : w.z);

        /// <summary>
        /// Settles which way is up in the world-landmark frame, by asking the 2D landmarks.
        ///
        /// <para>The world landmarks arrive rotated for the device's orientation, and a rotation
        /// that is 180 degrees out turns the whole body over — which is what the level test showed:
        /// a person standing upright, mirrored head-down. Nothing in the frame itself says which
        /// way that went. The 2D landmarks do: their y-down orientation is what the plank detector,
        /// the rep counter and the anti-cheat all measure against, so if they disagree with the
        /// world frame about where the shoulders are relative to the hips, it is the world frame
        /// that is upside down.</para>
        ///
        /// <para>Resolved once, and only off a torso that is clearly vertical on screen — in a
        /// plank the shoulders and hips sit at the same height and the question has no answer.
        /// Which is fine: the mirror only ever runs before the plank arms.</para>
        /// </summary>
        private void ResolveUpSign(in PoseFrame frame)
        {
            if (_upResolved) return;

            Vector2 shoulder2D = 0.5f * (frame.Get(PoseLandmark.LeftShoulder).Pos2D
                                       + frame.Get(PoseLandmark.RightShoulder).Pos2D);
            Vector2 hip2D = 0.5f * (frame.Get(PoseLandmark.LeftHip).Pos2D
                                  + frame.Get(PoseLandmark.RightHip).Pos2D);

            float upOnScreen = hip2D.y - shoulder2D.y; // 2D y is down, so upright reads positive
            if (Mathf.Abs(upOnScreen) < UprightTorso2D) return; // lying down: no answer to give

            Vector3 worldUp = World(frame, PoseLandmark.LeftShoulder)
                            + World(frame, PoseLandmark.RightShoulder)
                            - World(frame, PoseLandmark.LeftHip)
                            - World(frame, PoseLandmark.RightHip);
            if (Mathf.Abs(worldUp.y) < 1e-4f) return;

            // Screen says the torso points up; the mapped world frame must agree.
            _upSign = Mathf.Sign(upOnScreen) * Mathf.Sign(worldUp.y) > 0f ? 1f : -1f;
            _upResolved = true;
        }
    }
}
