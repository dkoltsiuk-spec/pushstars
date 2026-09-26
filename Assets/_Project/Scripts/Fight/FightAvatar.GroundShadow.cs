using UnityEngine;
using UnityEngine.Rendering;

namespace PushStars.Fight
{
    public sealed partial class FightAvatar
    {
        // Mockup contact shadow (boss, duel, training, assessment): one flat, crisp, dark ellipse
        // under the push-up — as wide as ~0.95 of the hands' outer span, ~0.17 as tall as wide,
        // centred on the palms' ground line, the body drawn over it.
        private const float ShadowWidthOfHandSpan = .95f;
        private const float ShadowAspect = .17f;
        private const float ShadowEdge = .012f; // anti-aliasing rim, share of the radius
        private static Color ShadowColor = new Color(.02f, .03f, .06f, .7f);

        private GameObject _pushupGroundShadow;
        private Mesh _groundShadowMesh;
        private Material _groundShadowMaterial;

        private void UpdatePushupGroundShadow(bool visible)
        {
            if (!visible)
            {
                if (_pushupGroundShadow != null) _pushupGroundShadow.SetActive(false);
                return;
            }
            if (_pushupGroundShadow == null) CreatePushupGroundShadow();
            _pushupGroundShadow.SetActive(true);
        }

        /// <summary>Lives in the stage camera's space, square to the lens: at the low push-up view a
        /// floor-plane ellipse would flatten to a line, while the mockups show a legible oval. It
        /// is placed whenever the push-up shot is (re)fitted — the camera then holds still.</summary>
        private void PlacePushupGroundShadow()
        {
            if (_pushupGroundShadow == null || _animator == null || _stageCamera == null) return;
            var left = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var right = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (left == null || right == null) return;

            var cam = _stageCamera.transform;
            var root = _animator.transform;
            // The palms rest on the support plane (the root's floor): drop each wrist onto it.
            Vector3 Floor(Transform bone)
            {
                Vector3 local = root.InverseTransformPoint(bone.position);
                local.y = 0f;
                return cam.InverseTransformPoint(root.TransformPoint(local));
            }
            Vector3 l = Floor(left), r = Floor(right);
            float depth = Mathf.Max(_stageCamera.nearClipPlane * 2f, (l.z + r.z) * .5f);
            // Wrist-to-wrist plus a palm on each side ≈ the hands' outer span.
            float palm = PalmWidth(HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftLittleProximal, left);
            float width = (Mathf.Abs(l.x - r.x) + 2f * palm) * ShadowWidthOfHandSpan;

            var t = _pushupGroundShadow.transform;
            t.localPosition = new Vector3((l.x + r.x) * .5f, (l.y + r.y) * .5f, depth);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(width * .5f, width * ShadowAspect * .5f, 1f);
        }

        private float PalmWidth(HumanBodyBones index, HumanBodyBones little, Transform hand)
        {
            var a = _animator.GetBoneTransform(index);
            var b = _animator.GetBoneTransform(little);
            if (a != null && b != null) return Vector3.Distance(a.position, b.position) * 1.3f;
            return .09f * hand.lossyScale.x;
        }

        private void CreatePushupGroundShadow()
        {
            const int segments = 96;
            var vertices = new Vector3[1 + segments * 2];
            var colors = new Color[vertices.Length];
            var triangles = new int[segments * 9];
            Color clear = ShadowColor; clear.a = 0f;
            vertices[0] = Vector3.zero; colors[0] = ShadowColor;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices[1 + i] = dir * (1f - ShadowEdge); colors[1 + i] = ShadowColor;
                vertices[1 + segments + i] = dir; colors[1 + segments + i] = clear;
                int next = (i + 1) % segments, k = i * 9;
                triangles[k] = 0; triangles[k + 1] = 1 + next; triangles[k + 2] = 1 + i;
                triangles[k + 3] = 1 + i; triangles[k + 4] = 1 + next; triangles[k + 5] = 1 + segments + i;
                triangles[k + 6] = 1 + next; triangles[k + 7] = 1 + segments + next; triangles[k + 8] = 1 + segments + i;
            }
            _groundShadowMesh = new Mesh { name = "Pushup contact shadow", vertices = vertices, colors = colors, triangles = triangles };
            _groundShadowMesh.RecalculateBounds();
            // Drawn before the opaque body, without depth: the body simply paints over it.
            _groundShadowMaterial = new Material(Shader.Find("Sprites/Default"))
                { name = "Pushup ground shadow", renderQueue = 1999 };
            _pushupGroundShadow = new GameObject("Pushup ground shadow", typeof(MeshFilter), typeof(MeshRenderer));
            _pushupGroundShadow.layer = _animator.gameObject.layer;
            _pushupGroundShadow.transform.SetParent(_stageCamera.transform, false);
            _pushupGroundShadow.GetComponent<MeshFilter>().sharedMesh = _groundShadowMesh;
            var renderer = _pushupGroundShadow.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _groundShadowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            PlacePushupGroundShadow();
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                Destroy(_pushupGroundShadow);
                Destroy(_groundShadowMesh);
                Destroy(_groundShadowMaterial);
            }
            else
            {
                DestroyImmediate(_pushupGroundShadow);
                DestroyImmediate(_groundShadowMesh);
                DestroyImmediate(_groundShadowMaterial);
            }
        }
    }
}
