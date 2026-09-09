using UnityEngine;
using PushStars.CV;
using PushStars.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// One body on the fight screen. Instantiates the player's chosen character, hands it the
    /// push-up controller, binds it to whichever <see cref="IAvatarAnimator"/> is driving it, and
    /// keeps its stage camera framed on it.
    ///
    /// <para>Used twice: once for the player, driven by <see cref="PushupAvatarDriver"/> off the CV
    /// depth signal, and once for the opponent, driven by <see cref="GhostAvatarDriver"/> off the
    /// recording. Everything else about the two is identical, which is the point — the duel screen
    /// shows two bodies doing the same exercise, and only the source of the movement differs.</para>
    ///
    /// <para><b>Why this and not <see cref="CharacterRoster"/>.</b> The menu stage shows a standing
    /// hero and only ever needs idle clips, so its controller has no push-up state and its camera is
    /// framed once, for a standing figure. Here the character spends the duel prone and moving, and
    /// the controller has to carry the clip the driver scrubs.</para>
    ///
    /// <para><b>Framing follows the bones, not the mesh.</b> A standing figure and a figure in a
    /// plank need very different shots, and this project has already been bitten by skinned-mesh
    /// bounds that do not track the pose. Humanoid bone positions always do, so the camera fits
    /// itself to a handful of them and eases toward the target — the shot pulls in as the body drops
    /// into the plank without anybody tuning two camera positions by hand.</para>
    ///
    /// <para><b>The lens stands where the phone does:</b> dead ahead of the character, level, and it
    /// rides down to the face as the body drops into the plank. That is not a flourish — it is the
    /// same view the player's own phone has of them from the floor, so what they see on screen and
    /// what the detector sees are the same shot. It falls out of one rule: aim between the body's
    /// centre and the head. Standing, the head is far above the centre and the camera settles around
    /// chest height, framing the whole figure; prone, head and centre are a hand's width apart and
    /// both near the floor, so the same rule puts the lens level with the face.</para>
    ///
    /// <para><b>Then it stops.</b> That aim travels through a rep as well, and a shot still chasing
    /// it slides down with the chest and back up on the way out — which the eye reads as the ground
    /// moving under a body that is holding still, and as hands the clip has planted coming unstuck.
    /// So the shot pulls in when the plank arms, settles, and holds for the set.</para>
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class FightAvatar : MonoBehaviour
    {
        [Header("Bindings")]
        [Tooltip("Component driving this body. Must implement IAvatarAnimator.")]
        [SerializeField] private MonoBehaviour _driverBehaviour;

        [Tooltip("Handed the same body as the driver: the live mirror and the anchor that owns " +
                 "where it stands, which run until the plank arms. Each must implement " +
                 "IAvatarAnimator. Empty on the opponent — a ghost has nobody to mirror.")]
        [SerializeField] private MonoBehaviour[] _alsoBound;

        [Tooltip("While this mirror owns the body, the shot holds where it first framed. The whole " +
                 "point of the mirror is that the character stands where you do in frame, and a " +
                 "camera that re-centres it every frame cancels exactly that. Framing resumes on " +
                 "the arm, which is what rides the lens down into the plank.")]
        [SerializeField] private PoseMirrorRetargeter _holdFramingWhileMirroring;

        [SerializeField] private Camera _stageCamera;
        [Tooltip("Transform the instantiated body is parented to.")]
        [SerializeField] private Transform _avatarRoot;

        [Header("Bodies")]
        [SerializeField] private GameObject _malePrefab;
        [SerializeField] private GameObject _femalePrefab;

        [Tooltip("Controller carrying the states the drivers play: PushUp / WarriorIdle / SittingIdle.")]
        [SerializeField] private RuntimeAnimatorController _fightController;

        [Header("Look")]
        [Tooltip("Render this body as a dark silhouette. The ghost opponent IS the player's own " +
                 "character, and two identical figures read as a bug rather than as a duel — the " +
                 "shadow says whose reps those are without needing a second character.")]
        [SerializeField] private bool _shadow;
        [SerializeField] private Color _shadowTint = new Color(0.09f, 0.10f, 0.16f, 1f);

        [Header("Framing")]
        [Tooltip("Headroom around the character. 1 = the bones exactly touch the frame edges.")]
        [SerializeField, Range(1f, 2.5f)] private float _padding = 1.05f;
        [Tooltip("Seconds for the camera to reach a new framing. 0 snaps.")]
        [SerializeField, Range(0f, 2f)] private float _easeTime = 0.55f;
        [Tooltip("How long the shot keeps pulling in after the plank arms, before it locks for the " +
                 "rest of the set. Long enough for the ease to land on the plank, short enough that " +
                 "the first rep is already on a still camera.")]
        [SerializeField, Range(0f, 3f)] private float _settleSeconds = 0.9f;
        [Tooltip("Camera direction relative to the character: where the phone would be standing. " +
                 "Dead ahead, barely above the aim point — a phone propped on the floor in front of you.")]
        [SerializeField] private Vector3 _viewDirection = new Vector3(0f, 0.05f, 1f);

        [Tooltip("How far the camera's aim rises from the body's centre toward the head. 0 frames " +
                 "the whole figure evenly, 1 stares at the face.")]
        [SerializeField, Range(0f, 1f)] private float _faceBias = 0.55f;
        [Tooltip("Floor on how close the shot may get. Lowered alongside padding — the old 1.6m " +
                 "floor was clamping the shot back out at the bottom of a rep, exactly the moment " +
                 "the padding cut was meant to bring the body closer.")]
        [SerializeField, Range(1f, 12f)] private float _minDistance = 1.0f;
        [SerializeField, Range(2f, 30f)] private float _maxDistance = 9f;

        /// <summary>Fallback for how far the body reaches past the bones, used only when the
        /// posed mesh cannot be measured (see <see cref="TryMeshBounds"/>). The rig report puts
        /// this character's ankles at 0.089 m under a skin 1.86 m tall, i.e. a sole roughly 0.06
        /// and a crown roughly 0.15 of the ankle-to-head span — as shares of that span so they
        /// hold for a rig of any height, and so they cost almost nothing in a plank, where the
        /// span is small and the silhouette is bounded by the hands and feet instead.</summary>
        private const float CrownAboveHeadBone = 0.15f;
        private const float SoleBelowFootBone = 0.06f;

        private static readonly HumanBodyBones[] FrameBones =
        {
            HumanBodyBones.Head,
            HumanBodyBones.Hips,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm,
        };

        private Animator _animator;
        private Renderer[] _renderers;
        private Transform[] _bones;
        private Transform _head;
        private Vector3 _focus;
        private Vector3 _focusVelocity;
        private float _distance;
        private float _distanceVelocity;
        private bool _framed;
        private bool _wasMirroring;
        private float _settleUntil;
        private bool _preparation;
        private Vector3 _bodyRestPos;
        private Quaternion _bodyRestRot;
        private Vector3 _bodyRestScale = Vector3.one;
        private bool _bodyRestCaptured;
        private readonly System.Collections.Generic.List<(Material material, string property, Color original)> _shadowColors
            = new System.Collections.Generic.List<(Material, string, Color)>();

        /// <summary>
        /// The pre-duel card's presentation for this body: shadow tint off (both fighters read as
        /// themselves on the card), and — for the player — the live mirror and the anchor stood
        /// down so the character simply stands in its idle clip instead of tracking whatever the
        /// person is doing in front of the camera. Reversed the moment ГОТОВ is pressed.
        ///
        /// <para>May be called before <see cref="Build"/> has run (the controller opens the card
        /// from its own Start, this component builds at execution order 300), so it stays a no-op
        /// on the parts that need the instantiated body and <see cref="Build"/> re-applies it.</para>
        /// </summary>
        public void SetPreparationPresentation(bool preparation)
        {
            _preparation = preparation;
            // Each phase frames itself once, from where the body is now — the card cannot inherit
            // a shot aimed at a plank, and the duel cannot inherit one locked on a standing figure.
            _framed = false;
            _settleUntil = Time.time + _settleSeconds;

            foreach (var entry in _shadowColors)
                if (entry.material != null)
                    entry.material.SetColor(entry.property, preparation ? entry.original : _shadowTint);

            if (_holdFramingWhileMirroring != null) _holdFramingWhileMirroring.Suppressed = preparation;
            if (_alsoBound != null)
                foreach (var bound in _alsoBound)
                    if (bound is PushStars.CV.IMirrorSuppressible suppressible)
                        suppressible.Suppressed = preparation;

            if (preparation && _animator != null)
            {
                // Undo any placement the anchor applied before it was told to hold, and hand the
                // clip driver a live Animator so the idle clip starts this frame rather than after
                // the mirror's blend-out.
                if (_bodyRestCaptured)
                {
                    var body = _animator.transform;
                    body.localPosition = _bodyRestPos;
                    body.localRotation = _bodyRestRot;
                    body.localScale = _bodyRestScale;
                }
                _animator.enabled = true;
            }
        }

        /// <summary>The instantiated body, for anything that wants to decorate it later.</summary>
        public GameObject Character { get; private set; }

        /// <summary>The camera whose render texture this body appears in. Lets a surface showing
        /// that texture — the ready card's portraits — tell which of the two stages it is looking
        /// at without a second serialized reference.</summary>
        public Camera StageCamera => _stageCamera;

        /// <summary>Where this body lands inside its own stage render, in viewport coordinates
        /// (0 = left/bottom, 1 = right/top). Anything cropping that render can use it to keep the
        /// whole figure — and anything standing the figure on something can use its floor.
        ///
        /// <para>Measured off the posed mesh (<see cref="TryMeshBounds"/>) rather than the bones,
        /// because the bones stop at the ankle and the skull base and a plate placed under those
        /// is a plate the shoes hang above.</para>
        ///
        /// <para>Projected down the body's own depth rather than as the eight corners of its box:
        /// under perspective the near-bottom corner lands lower in frame than the soles actually
        /// are, and a floor a few units low is exactly the gap that reads as a figure hovering
        /// over its own shadow.</para></summary>
        public bool TryGetBodyViewport(out Rect viewport)
        {
            viewport = default;
            if (_bones == null || _stageCamera == null) return false;

            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            int found = 0;
            foreach (var bone in _bones)
            {
                if (bone == null) continue;
                min = Vector3.Min(min, bone.position);
                max = Vector3.Max(max, bone.position);
                found++;
            }
            if (found == 0) return false;

            Vector3 lo = min, hi = max;
            if (TryMeshBounds(min, max, out var mesh)) { lo = mesh.min; hi = mesh.max; }
            else
            {
                float span = max.y - min.y;
                lo.y -= span * SoleBelowFootBone;
                hi.y += span * CrownAboveHeadBone;
            }

            float depth = (lo.z + hi.z) * 0.5f;
            float middle = (lo.y + hi.y) * 0.5f;
            Vector3 sole = _stageCamera.WorldToViewportPoint(new Vector3((lo.x + hi.x) * 0.5f, lo.y, depth));
            Vector3 crown = _stageCamera.WorldToViewportPoint(new Vector3((lo.x + hi.x) * 0.5f, hi.y, depth));
            Vector3 left = _stageCamera.WorldToViewportPoint(new Vector3(lo.x, middle, depth));
            Vector3 right = _stageCamera.WorldToViewportPoint(new Vector3(hi.x, middle, depth));
            if (sole.z <= 0f || crown.z <= 0f || crown.y <= sole.y) return false;

            viewport = Rect.MinMaxRect(Mathf.Min(left.x, right.x), sole.y,
                                       Mathf.Max(left.x, right.x), crown.y);
            return true;
        }

        private void Start() => Build();

        private void LateUpdate()
        {
            if (_animator == null || _stageCamera == null) return;

            // The ready card. The shot settles onto the standing body and then holds, which is
            // the fixed framing the menu stage gives its hero — and the reason that figure reads
            // as standing still. The aim rides between the body's centre and its head, and both
            // of those breathe with the idle, so a camera left easing after them tows the whole
            // figure around inside its own frame. On a card where each fighter now stands on a
            // plate, that is the difference between standing on it and drifting over it. The
            // settle is there because the idle needs a moment to cross-fade in; framing on the
            // first frame would lock the shot onto a bind pose.
            if (_preparation)
            {
                if (!_framed || Time.time < _settleUntil) FrameCharacter();
                return;
            }

            // Nothing to hold the shot for — the opponent's body, which is on a recording and
            // stays where its stage puts it. Frame it every frame.
            if (_holdFramingWhileMirroring == null || _holdFramingWhileMirroring.Suppressed)
            {
                FrameCharacter();
                return;
            }

            // Framed once regardless, so the shot starts on the body rather than wherever the
            // scene's camera was authored — then held for the whole mirror phase. The phase, not
            // the limb weight: the anchor is moving the body from the moment it locks on, long
            // before the limbs join, and a camera re-centring through that is the one thing that
            // cancels the anchor.
            bool mirroring = _holdFramingWhileMirroring.MirrorPhase;
            if (mirroring != _wasMirroring)
            {
                _wasMirroring = mirroring;
                // Leaving the mirror phase means the plank just armed: let the shot pull in for a
                // moment, then stop.
                if (!mirroring) _settleUntil = Time.time + _settleSeconds;
            }

            // Locked for the set, and this is the point of it. The aim rides between the body's
            // centre and its head, both of which travel through a rep — so a shot that keeps
            // re-framing slides down as the chest goes down and back up on the way up, and what
            // the eye reads is the floor moving under a body that is standing still. It also
            // unpins the hands, which the clip has planted. One pull-in, then the ground stays
            // where it is.
            if (_framed && (mirroring || Time.time >= _settleUntil)) return;

            FrameCharacter();
        }

        private void Build()
        {
            var gender = CharacterRoster.SavedGender;
            var prefab = gender == CharacterGender.Female ? _femalePrefab : _malePrefab;
            if (prefab == null) prefab = gender == CharacterGender.Female ? _malePrefab : _femalePrefab;
            if (prefab == null)
            {
                Debug.LogError("[FightAvatar] No character prefab assigned - run " +
                               "Tools > Push Stars > Character > Import Main Characters, then rebuild the fight screen.");
                return;
            }
            if (_avatarRoot == null)
            {
                Debug.LogError("[FightAvatar] No avatar root - the fight screen was built incorrectly.");
                return;
            }

            Character = Instantiate(prefab, _avatarRoot);
            Character.name = prefab.name + (_shadow ? " (Shadow)" : "");
            Character.transform.localPosition = Vector3.zero;
            Character.transform.localRotation = Quaternion.identity;
            SetLayerRecursive(Character, _avatarRoot.gameObject.layer);

            _animator = Character.GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                Debug.LogError($"[FightAvatar] {prefab.name} has no Animator - nothing to drive.");
                return;
            }

            // The duel controller replaces the menu one: same rig, different clip set. Without it
            // the drivers' Animator.Play("PushUp") is a silent no-op and the body never moves.
            if (_fightController != null) _animator.runtimeAnimatorController = _fightController;
            else Debug.LogWarning("[FightAvatar] No fight AnimatorController assigned - the push-up clip will not play.");

            // Asserted rather than inherited from the prefab. Root motion would walk the body out
            // of its own framing, and the character is only ever seen through a render texture —
            // the default culling mode can decide it is off-screen and stop evaluating the pose
            // the driver is scrubbing.
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (_shadow) ApplyShadowTint();

            _renderers = Character.GetComponentsInChildren<Renderer>(true);
            CacheBones();

            // The pose the stage framed this body in — restored if the ready card needs to undo an
            // anchor placement (the anchor moves this same transform).
            var bodyTransform = _animator.transform;
            _bodyRestPos = bodyTransform.localPosition;
            _bodyRestRot = bodyTransform.localRotation;
            _bodyRestScale = bodyTransform.localScale;
            _bodyRestCaptured = true;

            if (_driverBehaviour is IAvatarAnimator driver) driver.BindAnimator(_animator);
            else if (_driverBehaviour != null)
                Debug.LogError($"[FightAvatar] {_driverBehaviour.GetType().Name} does not implement IAvatarAnimator.");

            if (_alsoBound != null)
                foreach (var extra in _alsoBound)
                {
                    if (extra is IAvatarAnimator bound) bound.BindAnimator(_animator);
                    else if (extra != null)
                        Debug.LogError($"[FightAvatar] {extra.GetType().Name} does not implement IAvatarAnimator.");
                }

            // The card may have asked for the standing-idle presentation before this body existed.
            if (_preparation) SetPreparationPresentation(true);
        }

        /// <summary>Darkens this body's materials on the instance only. <c>renderer.materials</c>
        /// returns per-instance copies, so the prefab's shared materials — and the player's body
        /// standing next to it — are untouched.</summary>
        private void ApplyShadowTint()
        {
            foreach (var renderer in Character.GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.materials;
                foreach (var mat in mats)
                {
                    if (mat == null) continue;
                    foreach (string property in new[] { "_Color", "_BaseColor" })
                    {
                        if (!mat.HasProperty(property)) continue;
                        _shadowColors.Add((mat, property, mat.GetColor(property)));
                        if (!_preparation) mat.SetColor(property, _shadowTint);
                    }
                }
                renderer.materials = mats;
            }
        }

        /// <summary>The posed mesh's world-space bounds — the body's actual outline, crown and
        /// soles included, rather than the skeleton inside it.
        ///
        /// <para>The class doc above says framing follows the bones because skinned bounds do not
        /// track the pose. That is true of the bounds these FBXs ship, which sit about two metres
        /// to one side of the body; it stops being true once the renderer is told to recompute
        /// them, which <c>MainCharacterSetup.FixSkinnedBounds</c> does — every character prefab is
        /// saved with <c>updateWhenOffscreen</c> on. So the bounds are read, and then checked
        /// against the bones rather than trusted blind: a silhouette that does not contain its own
        /// skeleton is the stale kind, and the caller falls back to the allowance above.</para>
        /// </summary>
        private bool TryMeshBounds(Vector3 boneMin, Vector3 boneMax, out Bounds mesh)
        {
            mesh = default;
            if (_renderers == null) return false;

            bool any = false;
            foreach (var renderer in _renderers)
            {
                // Switched-off parts are skipped for their bounds as much as for their pixels, and
                // an inactive one's are worse than stale: they sit at the world origin, which for
                // the opponent's stage — parked 200 m out — would swallow the whole scene and
                // still pass the containment test below.
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                if (any) mesh.Encapsulate(renderer.bounds);
                else { mesh = renderer.bounds; any = true; }
            }
            return any && mesh.Contains(boneMin) && mesh.Contains(boneMax);
        }

        private void CacheBones()
        {
            _bones = new Transform[FrameBones.Length];
            for (int i = 0; i < FrameBones.Length; i++)
                _bones[i] = _animator.GetBoneTransform(FrameBones[i]);
            _head = _animator.GetBoneTransform(HumanBodyBones.Head);
        }

        /// <summary>Fits the camera around the bones that are actually posed this frame.</summary>
        private void FrameCharacter()
        {
            if (_bones == null) return;

            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;
            int found = 0;
            foreach (var bone in _bones)
            {
                if (bone == null) continue;
                min = Vector3.Min(min, bone.position);
                max = Vector3.Max(max, bone.position);
                found++;
            }
            if (found == 0) return;

            Vector3 centre = (min + max) * 0.5f;

            // Aim: lifted from the body's centre toward the head. This is what makes the shot
            // descend into the plank on its own — standing, the head is far above the centre and
            // the camera settles around chest height, which frames a whole figure; prone, head and
            // centre are within a hand's width of each other and both near the floor, so the same
            // rule puts the lens level with the face. No second camera position, and nothing that
            // has to know which pose the body is in.
            Vector3 aim = centre;
            if (_head != null) aim.y = Mathf.Lerp(centre.y, _head.position.y, _faceBias);

            // Radius measured from the AIM, not across the box: aiming away from the centre and
            // still fitting a sphere around the centre is how a foot ends up out of frame.
            float radius = 0.25f;
            foreach (var bone in _bones)
            {
                if (bone == null) continue;
                radius = Mathf.Max(radius, Vector3.Distance(bone.position, aim));
            }

            // And the body is not its skeleton. A sphere fitted to the bones alone puts the crown
            // and the soles OUTSIDE the frame — which is how a standing figure kept coming back with
            // its shoes sliced off no matter how the portrait was cropped afterwards. There is
            // nothing to crop to if the pixels were never rendered. Measured off the mesh where it
            // can be, so the allowance is the body's own rather than an average human's.
            radius += TryMeshBounds(min, max, out var silhouette)
                ? Mathf.Max(silhouette.max.y - max.y, min.y - silhouette.min.y)
                : (max.y - min.y) * CrownAboveHeadBone;

            // Distance that fits a sphere of that radius in the NARROWER of the two FOVs - in
            // portrait that is the horizontal one, which is exactly the axis a prone body fills.
            float vFov = _stageCamera.fieldOfView * Mathf.Deg2Rad;
            float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * Mathf.Max(0.1f, _stageCamera.aspect));
            float fov = Mathf.Min(vFov, hFov);
            float wanted = Mathf.Clamp(radius * _padding / Mathf.Sin(fov * 0.5f), _minDistance, _maxDistance);

            if (!_framed)
            {
                _framed = true;
                _focus = aim;
                _distance = wanted;
            }
            else if (_easeTime > 0f)
            {
                _focus = Vector3.SmoothDamp(_focus, aim, ref _focusVelocity, _easeTime);
                _distance = Mathf.SmoothDamp(_distance, wanted, ref _distanceVelocity, _easeTime);
            }
            else
            {
                _focus = aim;
                _distance = wanted;
            }

            Vector3 dir = _viewDirection.sqrMagnitude < 1e-4f ? Vector3.forward : _viewDirection.normalized;
            _stageCamera.transform.position = _focus + dir * _distance;
            _stageCamera.transform.rotation =
                Quaternion.LookRotation(_focus - _stageCamera.transform.position, Vector3.up);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }
    }
}


