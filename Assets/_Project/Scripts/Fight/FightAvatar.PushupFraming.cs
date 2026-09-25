using System.Collections.Generic;
using PushStars.CV;
using PushStars.UI;
using UnityEngine;

namespace PushStars.Fight
{
    public sealed partial class FightAvatar
    {
        // An elevated frontal view shows the face, both hands and the feet, as in the duel art.
        private static readonly Quaternion PushupView = Quaternion.LookRotation(
            -new Vector3(0f, .30f, 1f).normalized, Vector3.up);
        private PushupPoseCorrection _pushupCorrection;
        private Vector3[] _pushupSilhouette;
        private bool _pushupFramed;
        private float _pushupAspect, _pushupFov;
        private Rect _pushupUv;
        private Matrix4x4 _pushupRootMatrix;

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
                return false;
            }

            var stage = _stageCamera.GetComponentInParent<CharacterStage>();
            Rect uv = stage != null ? stage.DisplayUv : new Rect(0f, 0f, 1f, 1f);
            var root = _animator.transform;
            Matrix4x4 rootMatrix = root.localToWorldMatrix;
            if (_pushupFramed && Mathf.Approximately(_pushupAspect, _stageCamera.aspect)
                && Mathf.Approximately(_pushupFov, _stageCamera.fieldOfView)
                && _pushupUv == uv && _pushupRootMatrix == rootMatrix) return true;

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
            const float margin = .08f;
            float tanY = Mathf.Tan(_stageCamera.fieldOfView * Mathf.Deg2Rad * .5f);
            float tanX = tanY * _stageCamera.aspect;
            float left = (uv.xMin + uv.width * margin - .5f) * 2f * tanX;
            float right = (uv.xMax - uv.width * margin - .5f) * 2f * tanX;
            float bottom = (uv.yMin + uv.height * margin - .5f) * 2f * tanY;
            float top = (uv.yMax - uv.height * margin - .5f) * 2f * tanY;

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
                centre = new Vector2((xMin + xMax) * .5f, (yMin + yMax) * .5f);
                return xMin <= xMax && yMin <= yMax;
            }
            float near = Mathf.Max(.01f, _stageCamera.nearClipPlane + .02f - minZ);
            float far = Mathf.Max(1f, near);
            while (!Fit(far, out _) && far < 1024f) far *= 2f;
            for (int i = 0; i < 22; i++)
            {
                float middle = (near + far) * .5f;
                if (Fit(middle, out _)) far = middle; else near = middle;
            }
            Fit(far, out Vector2 centre);
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
            return true;
        }
    }
}
