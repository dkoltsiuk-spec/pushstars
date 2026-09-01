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
    /// rides down to the face as the body goes down. That is not a flourish — it is the same view
    /// the player's own phone has of them from the floor, so what they see on screen and what the
    /// detector sees are the same shot. It falls out of one rule: aim between the body's centre and
    /// the head. Standing, the head is far above the centre and the camera settles around chest
    /// height, framing the whole figure; prone, head and centre are a hand's width apart and both
    /// near the floor, so the same rule puts the lens level with the face.</para>
    /// </summary>
    public sealed class FightAvatar : MonoBehaviour
    {
        [Header("Bindings")]
        [Tooltip("Component driving this body. Must implement IAvatarAnimator.")]
        [SerializeField] private MonoBehaviour _driverBehaviour;
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
        [SerializeField, Range(1f, 2.5f)] private float _padding = 1.45f;
        [Tooltip("Seconds for the camera to reach a new framing. 0 snaps.")]
        [SerializeField, Range(0f, 2f)] private float _easeTime = 0.55f;
        [Tooltip("Camera direction relative to the character: where the phone would be standing. " +
                 "Dead ahead, barely above the aim point — a phone propped on the floor in front of you.")]
        [SerializeField] private Vector3 _viewDirection = new Vector3(0f, 0.05f, 1f);

        [Tooltip("How far the camera's aim rises from the body's centre toward the head. 0 frames " +
                 "the whole figure evenly, 1 stares at the face.")]
        [SerializeField, Range(0f, 1f)] private float _faceBias = 0.5f;
        [SerializeField, Range(1f, 12f)] private float _minDistance = 1.6f;
        [SerializeField, Range(2f, 30f)] private float _maxDistance = 9f;

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
        private Transform[] _bones;
        private Transform _head;
        private Vector3 _focus;
        private Vector3 _focusVelocity;
        private float _distance;
        private float _distanceVelocity;
        private bool _framed;

        /// <summary>The instantiated body, for anything that wants to decorate it later.</summary>
        public GameObject Character { get; private set; }

        private void Start() => Build();

        private void LateUpdate()
        {
            if (_animator == null || _stageCamera == null) return;
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

            CacheBones();
            if (_driverBehaviour is IAvatarAnimator driver) driver.BindAnimator(_animator);
            else if (_driverBehaviour != null)
                Debug.LogError($"[FightAvatar] {_driverBehaviour.GetType().Name} does not implement IAvatarAnimator.");
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
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", _shadowTint);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", _shadowTint);
                }
                renderer.materials = mats;
            }
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
