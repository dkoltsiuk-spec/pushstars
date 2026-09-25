using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PushStars.Fight
{
    public sealed partial class FightAvatar
    {
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

        private void CreatePushupGroundShadow()
        {
            var root = _animator.transform;
            Vector3 Local(HumanBodyBones bone) => root.InverseTransformPoint(_animator.GetBoneTransform(bone).position);
            Vector3 leftHand = Local(HumanBodyBones.LeftHand), rightHand = Local(HumanBodyBones.RightHand);
            Vector3 leftToe = Local(HumanBodyBones.LeftToes), rightToe = Local(HumanBodyBones.RightToes);
            Vector3 hands = (leftHand + rightHand) * .5f, toes = (leftToe + rightToe) * .5f;
            float length = Mathf.Abs(hands.z - toes.z);
            float halfWidth = Mathf.Abs(rightHand.x - leftHand.x) * .5f;
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            // All patches lie on the pose correction's support plane. Their perspective and
            // scale therefore follow the body/portrait, while contacts stay still during reps.
            void Ellipse(Vector3 centre, float width, float depth, float opacity)
            {
                const int segments = 48, rings = 4;
                int start = vertices.Count;
                vertices.Add(new Vector3(centre.x, .002f, centre.z));
                colors.Add(new Color(.015f, .025f, .045f, opacity));
                for (int ring = 1; ring <= rings; ring++)
                {
                    float radius = ring / (float)rings;
                    float alpha = opacity * (1f - radius * radius) * (1f - radius * radius);
                    for (int i = 0; i < segments; i++)
                    {
                        float angle = i * Mathf.PI * 2f / segments;
                        vertices.Add(new Vector3(centre.x + Mathf.Cos(angle) * width * radius,
                            .002f, centre.z + Mathf.Sin(angle) * depth * radius));
                        colors.Add(new Color(.015f, .025f, .045f, alpha));
                        int current = start + 1 + (ring - 1) * segments + i;
                        int next = start + 1 + (ring - 1) * segments + (i + 1) % segments;
                        if (ring == 1) { triangles.Add(start); triangles.Add(next); triangles.Add(current); }
                        else
                        {
                            triangles.Add(current - segments); triangles.Add(next); triangles.Add(current);
                            triangles.Add(current - segments); triangles.Add(next - segments); triangles.Add(next);
                        }
                    }
                }
            }
            Ellipse((hands + toes) * .5f, halfWidth * 1.45f, length * .72f, .48f);
            Ellipse(leftHand + Vector3.forward * length * .035f, length * .12f, length * .13f, .65f);
            Ellipse(rightHand + Vector3.forward * length * .035f, length * .12f, length * .13f, .65f);
            Ellipse(leftToe, length * .10f, length * .11f, .55f);
            Ellipse(rightToe, length * .10f, length * .11f, .55f);
            _groundShadowMesh = new Mesh { name = "Pushup contact shadow" };
            _groundShadowMesh.SetVertices(vertices);
            _groundShadowMesh.SetColors(colors);
            _groundShadowMesh.SetTriangles(triangles, 0);
            _groundShadowMesh.RecalculateBounds();
            _groundShadowMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Pushup ground shadow" };
            _pushupGroundShadow = new GameObject("Pushup ground shadow", typeof(MeshFilter), typeof(MeshRenderer));
            _pushupGroundShadow.layer = root.gameObject.layer;
            _pushupGroundShadow.transform.SetParent(root, false);
            _pushupGroundShadow.GetComponent<MeshFilter>().sharedMesh = _groundShadowMesh;
            var renderer = _pushupGroundShadow.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _groundShadowMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void OnDestroy()
        {
            if (Application.isPlaying)
            {
                Destroy(_groundShadowMesh);
                Destroy(_groundShadowMaterial);
            }
            else
            {
                DestroyImmediate(_groundShadowMesh);
                DestroyImmediate(_groundShadowMaterial);
            }
        }
    }
}
