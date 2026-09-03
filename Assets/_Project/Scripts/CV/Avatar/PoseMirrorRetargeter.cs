using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Puts the character where the person is and, once the whole body is in frame, moves its
    /// limbs with theirs.
    ///
    /// <para><b>Two stages, and only the first one is on by default.</b> Position and distance come
    /// from <see cref="AvatarMirrorAnchor"/> and are solid: they need two hip points and a torso
    /// length, which landmark data gives reliably. Limbs are the fragile half and are gated behind
    /// <see cref="_mirrorLimbs"/> — with it off the body holds one clean stance and simply follows
    /// you around the frame, which is the behaviour that cannot break.</para>
    ///
    /// <para><b>Flat by default, and that is the fix for the twisting.</b> A world landmark's z at
    /// the two metres this app asks people to stand at is mostly noise, and it used to drive both
    /// the hips' full 3D orientation and every limb direction — so the body turned and folded on
    /// readings that were not measurements. With <see cref="_planarOnly"/> the depth is dropped:
    /// the torso becomes one angle (how far the shoulder line is off level on screen) and a limb
    /// becomes one swing about one axis, which has no twist left to get wrong. Reaching at the lens
    /// no longer foreshortens; that is the whole price.</para>
    ///
    /// <para><b>Zeroed on the person, not on the rig.</b> The reference is the stance they are
    /// standing in when the skeleton first holds still, captured together with the character's own
    /// bone rotations at that instant. Standing at that stance puts the character exactly there;
    /// moving swings a limb off it by what moved, clamped to <see cref="_maxSwingDeg"/> so one bad
    /// frame cannot throw an arm somewhere a person could not reach.</para>
    ///
    /// <para><b>Nothing plays underneath.</b> The Animator is stopped for the whole mirror phase
    /// (<see cref="_freezeClipWhileMirroring"/>). Leaving it running is what crossed the idle clip
    /// with the person and had the body doing a bit of both; stopped, it also holds the neutral
    /// pose the rig was left in at bind, which is the stance the anchor carries while the limbs are
    /// off. It starts again on the arm, where the push-up scrub needs it.</para>
    ///
    /// <para><see cref="_flipX"/> and <see cref="_flipZ"/> stay as live corrections for a limb that
    /// moves the wrong way; which way is up is worked out from the 2D landmarks rather than
    /// configured — see <see cref="ResolveUpSign"/>.</para>
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

        [Header("What the camera drives")]
        [Tooltip("Drive the limbs from the camera at all. Off, the body holds one clean stance " +
                 "and only the anchor moves it - where you are and how far away, which are the " +
                 "two things landmark data is actually solid about. Nothing that can twist.")]
        [SerializeField] private bool _mirrorLimbs = false;

        [Tooltip("Swing the limbs in the plane of the screen and ignore the landmarks' depth. A " +
                 "world landmark's z at two metres is mostly noise, and it is what was turning " +
                 "the body: it fed the hips' full 3D orientation and every limb direction. Flat, " +
                 "a limb rotation is one angle about one axis and there is nothing left to " +
                 "twist. The cost is foreshortening - an arm reaching at the lens stays long.")]
        [SerializeField] private bool _planarOnly = true;

        [Tooltip("Most a limb may swing off the stance it was calibrated in. A landmark that " +
                 "jumps cannot throw an arm further than a person could.")]
        [SerializeField, Range(15f, 180f)] private float _maxSwingDeg = 120f;

        [Tooltip("Hold the Animator still for the whole mirror phase. The clip underneath is " +
                 "what crossed the animation with the person; with it stopped, what is on screen " +
                 "is the calibrated stance plus exactly what the camera saw, and nothing else.")]
        [SerializeField] private bool _freezeClipWhileMirroring = true;

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
            public Quaternion NeutralRotInRoot;
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
        private Quaternion _neutralHipsRotInRoot = Quaternion.identity;

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

        private void OnDisable()
        {
            // Whatever owns the body next brings its own clips; leaving the Animator off would
            // hand it a rig that cannot move.
            if (_animator != null) _animator.enabled = true;
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

            // The clip stops for the whole mirror phase. NeutralizePose left the rig in the
            // humanoid neutral at bind, so a stopped Animator holds that stance - one clean pose
            // for the anchor to carry - and there is nothing underneath any more for the mirror to
            // be crossed with. It starts again on the arm, where the push-up scrub needs it.
            if (_freezeClipWhileMirroring && _animator != null && _animator.enabled == armed)
                _animator.enabled = armed;

            if (!_mirrorLimbs)
            {
                MirrorWeight = 0f;
                _limbsReady = false;
                _wholeSince = -1f;
                return;
            }

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
                    if (TorsoRotation(_hipsSmoothedRight, _hipsSmoothedUp, out var torso))
                    {
                        Quaternion target = _calibrated
                            ? rootRot * (torso * Quaternion.Inverse(_neutralHips)) * _neutralHipsRotInRoot
                            : rootRot * torso * _hipsRestRotInRoot;
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

                Quaternion target = _calibrated
                    ? rootRot * Swing(seg.NeutralDir, seg.SmoothedDir) * seg.NeutralRotInRoot
                    : rootRot * Swing(seg.RestDirInRoot, seg.SmoothedDir) * seg.RestRotInRoot;
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
        /// exactly what the level test showed. Captured here, standing normally maps onto the
        /// character standing in its own idle stance, and only what the person actually does moves
        /// it off that.</para>
        ///
        /// <para><b>A frozen frame of the idle, not the idle.</b> Both halves of the zero are taken
        /// in one instant and never read again. Laying the deviation on a clip that keeps playing
        /// was the previous attempt, and it crossed the animation with the person — the body did a
        /// bit of both at once. Once the mirror has the body, the movement on screen is the
        /// person's; the clip only ever said where they were standing when it started.</para>
        ///
        /// <para>Taken at the moment the whole skeleton first holds still, which is the one moment
        /// this screen can be sure the person is in frame and settled. Re-taken every time the gate
        /// re-opens: someone who walks out of shot and back has re-arranged themselves.</para>
        /// </summary>
        private void Calibrate(in PoseFrame frame)
        {
            // Both halves of the zero, taken in the same instant and never read again: the
            // person's stance, and the character's. Nothing has written to the bones yet this
            // frame, so what is read here is the idle clip and only the idle clip.
            Quaternion invRoot = Quaternion.Inverse(_root.rotation);

            for (int i = 0; i < _segments.Length; i++)
            {
                ref var seg = ref _segments[i];
                Vector3 dir = MapDir(World(frame, seg.LmB) - World(frame, seg.LmA));
                if (dir.sqrMagnitude < 1e-6f) return; // nothing usable — keep the rig's rest pose
                seg.NeutralDir = dir.normalized;
                seg.NeutralRotInRoot = invRoot * seg.Bone.rotation;
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

            if (!TorsoRotation(right.normalized, up.normalized, out _neutralHips)) return;
            _neutralHipsRotInRoot = invRoot * _hips.rotation;
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

        /// <summary>The rotation the torso is standing at. Flat, that is one angle - how far
        /// the shoulder line is off level in the plane of the screen - and a body cannot be turned
        /// inside out by a noisy depth reading, because no depth reading is used.</summary>
        private bool TorsoRotation(Vector3 right, Vector3 up, out Quaternion rotation)
        {
            if (_planarOnly)
            {
                rotation = Quaternion.AngleAxis(
                    Mathf.Atan2(right.y, right.x) * Mathf.Rad2Deg, Vector3.forward);
                return right.sqrMagnitude > 1e-6f;
            }

            Vector3 fwd = Vector3.Cross(right, up);
            bool ok = fwd.sqrMagnitude > 1e-6f;
            rotation = ok ? Quaternion.LookRotation(fwd, up) : Quaternion.identity;
            return ok;
        }

        /// <summary>The rotation that swings a limb from where it was calibrated to where it is,
        /// with a ceiling on how far one frame of landmarks may claim it moved.</summary>
        private Quaternion Swing(Vector3 from, Vector3 to)
        {
            var swing = Quaternion.FromToRotation(from, to);
            swing.ToAngleAxis(out float angle, out Vector3 axis);
            if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) return Quaternion.identity;

            if (angle > 180f) angle -= 360f;
            float clamped = Mathf.Clamp(angle, -_maxSwingDeg, _maxSwingDeg);
            return Mathf.Approximately(clamped, angle) ? swing : Quaternion.AngleAxis(clamped, axis);
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
            => new Vector3(_flipX ? -w.x : w.x,
                           _upSign * w.y,
                           _planarOnly ? 0f : (_flipZ ? -w.z : w.z));

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
