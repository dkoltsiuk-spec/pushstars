using System.Collections.Generic;
using PushStars.CV;
using PushStars.UI;
using UnityEngine;

namespace PushStars.Fight
{
    public sealed partial class FightAvatar
    {
        /// <summary>Camera rise over the look direction for every push-up shot. The mockups (boss,
        /// duel, training, assessment) all show the phone-on-the-floor view: feet between the
        /// hands and the chest. From the old .30 the far floor rose toward the horizon and the
        /// feet read at shoulder height.</summary>
        private static float _pushupElevation = .04f;
        private static Quaternion PushupView => Quaternion.LookRotation(
            -new Vector3(0f, _pushupElevation, 1f).normalized, Vector3.up);
        /// <summary>Lens for the push-up shot. The stage's 40° lens, pulled in to fit the span,
        /// magnified the near head and shoulders and shrank the far feet; the mockups have the
        /// flat perspective of a longer lens (big shoes under the chest). Restored on leaving.</summary>
        private static float _pushupFieldOfView = 22f;
        private float _stageFieldOfView = -1f;
        private PushupPoseCorrection _pushupCorrection;
        private Vector3[] _pushupSilhouette;
        private bool _pushupFramed;
        private float _pushupAspect, _pushupFov;
        private Rect _pushupUv;
        private Matrix4x4 _pushupRootMatrix;
        // Fallback shot (no mockup size given): the whole motion fits with these margins.
        private const float ShotMargin = .08f;
        private float _shotHandSpan, _shotFloor;
        private Vector2 _pushupImageUnits;

        /// <summary>Stands the push-up exactly as its screen's mockup draws it, in the canvas's
        /// 390-wide design units: the hands' outer span is <paramref name="handSpan"/> wide
        /// (centred in the displayed image) and the lowest contacts rest
        /// <paramref name="floor"/> above the image's bottom edge. Camera distance follows from
        /// the span, so both bodies (and any skin) come out the same size.</summary>
        public void SetPushupShot(float handSpan, float floor)
        {
            _shotHandSpan = Mathf.Max(0f, handSpan);
            _shotFloor = Mathf.Max(0f, floor);
            _pushupFramed = false;
        }

        /// <summary>The mockup hand span this stage was given (0 = fit the whole motion).</summary>
        public float PushupHandSpan => _shotHandSpan;

        /// <summary>Size of the surface showing this stage in root-canvas units (0 when unknown).</summary>
        private static Vector2 DisplayUnits(CharacterStage stage)
        {
            var image = stage != null ? stage.TargetImage : null;
            var canvas = image != null ? image.canvas : null;
            if (canvas == null) return Vector2.zero;
            var rootScale = canvas.rootCanvas.transform.lossyScale;
            var scale = image.rectTransform.lossyScale;
            var size = image.rectTransform.rect.size;
            if (Mathf.Abs(rootScale.x) < 1e-6f || Mathf.Abs(rootScale.y) < 1e-6f) return Vector2.zero;
            return new Vector2(Mathf.Abs(size.x * scale.x / rootScale.x), Mathf.Abs(size.y * scale.y / rootScale.y));
        }

        /// <summary>Hands in the push-up camera's orientation (root-relative metres) and a palm's
        /// width. The pose correction pins the hands, so any depth gives the same answer.</summary>
        private bool TryHandsInView(Transform root, Quaternion inverse, out Vector3 left, out Vector3 right, out float palm)
        {
            left = right = default; palm = 0f;
            var l = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var r = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (l == null || r == null) return false;
            left = inverse * (l.position - root.position);
            right = inverse * (r.position - root.position);
            palm = PalmWidth(HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal, l);
            return true;
        }

        private void CachePushupSilhouette()
        {
            _pushupCorrection = _animator != null ? _animator.GetComponent<PushupPoseCorrection>() : null;
            if (_pushupCorrection == null) return;
            var transforms = _animator.GetComponentsInChildren<Transform>(true);
            var positions = new Vector3[transforms.Length];
            var rotations = new Quaternion[transforms.Length];
            var scales = new Vector3[transforms.Length];
            for (int i = 0; i < transforms.Length; i++)
            {
                positions[i] = transforms[i].localPosition;
                rotations[i] = transforms[i].localRotation;
                scales[i] = transforms[i].localScale;
            }
            var points = new List<Vector3>();
            var vertices = new List<Vector3>();
            var skins = _animator.GetComponentsInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh();
            try
            {
                // BakeMesh includes the rig's inherited scale in its vertex data on these
                // imported skins. Sample at unit world scale, then apply the current mirror
                // scale exactly once when fitting the camera.
                var root = _animator.transform;
                Vector3 worldScale = root.lossyScale;
                root.localScale = new Vector3(root.localScale.x / worldScale.x,
                    root.localScale.y / worldScale.y, root.localScale.z / worldScale.z);
                // Sample the final corrected poses, including palms, hair and shoes. Do this
                // once on binding, not during every rep. Restoring transforms leaves the live
                // mirror, Animator state and correction's handoff state untouched.
                for (int sample = 0; sample <= 8; sample++)
                {
                    _pushupCorrection.Apply(sample / 8f);
                    foreach (var skin in skins)
                    {
                        if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                        skin.BakeMesh(baked);
                        baked.GetVertices(vertices);
                        var toRoot = _animator.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
                        foreach (var vertex in vertices) points.Add(toRoot.MultiplyPoint3x4(vertex));
                    }
                }
                _pushupSilhouette = points.ToArray();
            }
            finally
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    transforms[i].localPosition = positions[i];
                    transforms[i].localRotation = rotations[i];
                    transforms[i].localScale = scales[i];
                }
                if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
            }
        }

        private bool FrameAuthoredPushup()
        {
            bool active = _pushupCorrection != null && _pushupCorrection.IsActive
                && !IsMirroring && _pushupSilhouette != null && _pushupSilhouette.Length > 0;
            UpdatePushupGroundShadow(active);
            if (!active)
            {
                if (_pushupFramed)
                {
                    // Idle/rest and a later mirror setup must frame themselves again.
                    _framed = false;
                    _pushupFramed = false;
                }
                if (_stageFieldOfView > 0f)
                {
                    _stageCamera.fieldOfView = _stageFieldOfView;
                    _stageFieldOfView = -1f;
                }
                return false;
            }
            if (_stageFieldOfView < 0f) _stageFieldOfView = _stageCamera.fieldOfView;
            _stageCamera.fieldOfView = _pushupFieldOfView;

            var stage = _stageCamera.GetComponentInParent<CharacterStage>();
            Rect uv = stage != null ? stage.DisplayUv : new Rect(0f, 0f, 1f, 1f);
            var root = _animator.transform;
            Matrix4x4 rootMatrix = root.localToWorldMatrix;
            Vector2 imageUnits = DisplayUnits(stage);
            if (_pushupFramed && Mathf.Approximately(_pushupAspect, _stageCamera.aspect)
                && Mathf.Approximately(_pushupFov, _stageCamera.fieldOfView)
                && _pushupUv == uv && _pushupRootMatrix == rootMatrix
                && Vector2.Distance(_pushupImageUnits, imageUnits) < .5f) return true;

            Quaternion rotation = root.rotation * PushupView;
            Quaternion inverse = Quaternion.Inverse(rotation);
            // Camera-oriented metres, independent of where the mirror previously put the rig.
            var points = new Vector3[_pushupSilhouette.Length];
            float minZ = float.PositiveInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = inverse * root.TransformVector(_pushupSilhouette[i]);
                minZ = Mathf.Min(minZ, points[i].z);
            }

            // Fit inside the ACTUAL visible UVs. Duel's bottom-anchored 125% crop otherwise
            // clips the crown even when the uncropped render texture fits perfectly.
            float tanY = Mathf.Tan(_stageCamera.fieldOfView * Mathf.Deg2Rad * .5f);
            float tanX = tanY * _stageCamera.aspect;
            float left = (uv.xMin + uv.width * ShotMargin - .5f) * 2f * tanX;
            float right = (uv.xMax - uv.width * ShotMargin - .5f) * 2f * tanX;
            float bottom = (uv.yMin + uv.height * ShotMargin - .5f) * 2f * tanY;
            float top = (uv.yMax - uv.height * ShotMargin - .5f) * 2f * tanY;

            // Each vertex gives an interval of valid camera X/Y positions at a distance.
            // Their intersection fits the whole motion; midpoint centres that fixed shot.
            bool Fit(float distance, out Vector2 centre)
            {
                float xMin = float.NegativeInfinity, xMax = float.PositiveInfinity;
                float yMin = float.NegativeInfinity, yMax = float.PositiveInfinity;
                foreach (var p in points)
                {
                    float z = p.z + distance;
                    xMin = Mathf.Max(xMin, p.x - right * z);
                    xMax = Mathf.Min(xMax, p.x - left * z);
                    yMin = Mathf.Max(yMin, p.y - top * z);
                    yMax = Mathf.Min(yMax, p.y - bottom * z);
                }
                // Highest camera the interval allows = lowest body: contacts on the floor margin.
                centre = new Vector2((xMin + xMax) * .5f, yMax);
                return xMin <= xMax && yMin <= yMax;
            }
            float near = Mathf.Max(.01f, _stageCamera.nearClipPlane + .02f - minZ);
            float far;
            Vector2 centre;
            if (_shotHandSpan > 0f && imageUnits.x > 1f && imageUnits.y > 1f
                && TryHandsInView(root, inverse, out Vector3 leftHand, out Vector3 rightHand, out float palm))
            {
                // Mockup shot: the distance puts the hands' outer span at the requested share of
                // the image width, centred; the lowest point of the whole motion on the floor line.
                float viewLeft = (uv.xMin - .5f) * 2f * tanX, viewRight = (uv.xMax - .5f) * 2f * tanX;
                float span = Mathf.Abs(leftHand.x - rightHand.x) + 2f * palm;
                float spanShare = Mathf.Clamp(_shotHandSpan / imageUnits.x, .05f, 1.5f);
                float handsZ = (leftHand.z + rightHand.z) * .5f;
                far = Mathf.Max(near, span / (spanShare * (viewRight - viewLeft)) - handsZ);
                float handsDepth = far + handsZ;
                float floorSlope = (uv.yMin + uv.height * Mathf.Clamp01(_shotFloor / imageUnits.y) - .5f) * 2f * tanY;
                float cy = float.PositiveInfinity;
                foreach (var p in points) cy = Mathf.Min(cy, p.y - floorSlope * (p.z + far));
                centre = new Vector2((leftHand.x + rightHand.x) * .5f - (viewLeft + viewRight) * .5f * handsDepth, cy);
            }
            else
            {
                far = Mathf.Max(1f, near);
                while (!Fit(far, out _) && far < 1024f) far *= 2f;
                for (int i = 0; i < 22; i++)
                {
                    float middle = (near + far) * .5f;
                    if (Fit(middle, out _)) far = middle; else near = middle;
                }
                Fit(far, out centre);
            }
            _stageCamera.transform.SetPositionAndRotation(
                root.position + rotation * new Vector3(centre.x, centre.y, -far), rotation);
            _focus = root.position + rotation * new Vector3(centre.x, centre.y, 0f);
            _distance = far;
            _focusVelocity = Vector3.zero;
            _distanceVelocity = 0f;
            _framed = _pushupFramed = true;
            _settleUntil = Time.time;
            _pushupAspect = _stageCamera.aspect;
            _pushupFov = _stageCamera.fieldOfView;
            _pushupUv = uv;
            _pushupRootMatrix = rootMatrix;
            _pushupImageUnits = imageUnits;
            PlacePushupGroundShadow();
            return true;
        }
    }
}
