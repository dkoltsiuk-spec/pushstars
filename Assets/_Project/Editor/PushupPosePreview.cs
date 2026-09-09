using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>Disposable, deterministic visual and geometry audit of the imported push-up.</summary>
    public static class PushupPosePreview
    {
        private const string ControllerPath = "Assets/_Project/Art/Characters/Mixamo/AvatarOverlayTest.controller";
        private const int CellWidth = 320, CellHeight = 420, Gap = 16;
        private static readonly float[] Times = { 0.5f, 0.25f, 0f };
        private static readonly string[] PhaseNames = { "TOP", "MIDDLE", "BOTTOM" };
        private static readonly HumanBodyBones[] Contacts = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
        private static readonly HumanBodyBones[,] Segments = {
            { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm },
            { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
            { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm },
            { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
            { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg },
            { HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot },
            { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg },
            { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot } };

        [MenuItem("Tools/Push Stars/CV/Preview Push-up Poses", priority = 315)]
        public static void Run()
        {
            Directory.CreateDirectory("Logs");
            var report = new StringBuilder("Push-up pose audit / " + DateTime.UtcNow.ToString("u") + "\n");
            report.AppendLine("Root-local metres. Columns: front TOP/MIDDLE/BOTTOM, side TOP/MIDDLE/BOTTOM. Rows: male/female.");
            report.AppendLine("Before/after use identical framing, fitted to the union of all three phases of both versions.");
            report.AppendLine("Images use disposable CPU-skinned meshes built from current bone matrices and imported bind poses, bypassing Animator/GPU skinning caches.");
            Type correctionType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PushStars.CV.PushupPoseCorrection"))
                .FirstOrDefault(t => t != null);
            var sheets = new[] { MakeSheet(false), correctionType == null ? null : MakeSheet(true) };
            var failures = new List<string>();
            try
            {
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                if (controller == null) throw new InvalidOperationException("Missing " + ControllerPath);
                for (int genderIndex = 0; genderIndex < 2; genderIndex++)
                {
                    var gender = genderIndex == 0 ? CharacterGender.Male : CharacterGender.Female;
                    using (var fixture = new Fixture(gender, controller, correctionType))
                    {
                        var projected = new Bounds[2];
                        var found = new bool[2];
                        for (int version = 0; version < sheets.Length; version++)
                        {
                            if (sheets[version] == null) continue;
                            for (int phase = 0; phase < Times.Length; phase++)
                            {
                                fixture.Sample(Times[phase], version == 1);
                                report.AppendLine($"\n{gender} / {(version == 0 ? "BEFORE" : "AFTER")} / {PhaseNames[phase]}");
                                var pose = fixture.ReadPose();
                                foreach (var bone in pose)
                                    report.AppendLine(bone.Key + " " + Format(bone.Value));
                                report.AppendLine(fixture.DescribeSkinnedContacts());
                                foreach (var point in fixture.SkinVertices())
                                    for (int view = 0; view < 2; view++)
                                    {
                                        Vector3 local = Quaternion.Inverse(ViewRotation(view)) * point;
                                        if (!found[view]) { projected[view] = new Bounds(local, Vector3.zero); found[view] = true; }
                                        else projected[view].Encapsulate(local);
                                    }
                            }
                            Audit(fixture, version == 1, gender.ToString(), report, failures);
                        }
                        if (correctionType != null) fixture.AuditLifecycle(gender.ToString(), report, failures);
                        for (int version = 0; version < sheets.Length; version++)
                        {
                            if (sheets[version] == null) continue;
                            for (int view = 0; view < 2; view++)
                            {
                                if (!found[view]) throw new InvalidOperationException("No visible skinned mesh on " + gender);
                                fixture.Frame(view, ViewRotation(view), projected[view], correctionType != null);
                                for (int phase = 0; phase < Times.Length; phase++)
                                {
                                    fixture.Sample(Times[phase], version == 1);
                                    var cell = fixture.Render();
                                    try { sheets[version].SetPixels32(Gap + (view * 3 + phase) * (CellWidth + Gap),
                                        sheets[version].height - (120 + genderIndex * 484) - CellHeight,
                                        CellWidth, CellHeight, cell.GetPixels32()); }
                                    finally { Object.DestroyImmediate(cell); }
                                }
                            }
                        }
                        if (correctionType != null)
                        {
                            fixture.Sample(.5f, true);
                            fixture.FrameHeroPerspective();
                            var hero = fixture.Render(640, 840);
                            try { File.WriteAllBytes(genderIndex == 0 ? "Logs/pushup-hero.png" : "Logs/pushup-female-hero.png", hero.EncodeToPNG()); }
                            finally { Object.DestroyImmediate(hero); }
                        }
                    }
                }
                for (int version = 0; version < sheets.Length; version++)
                {
                    if (sheets[version] == null) continue;
                    sheets[version].Apply();
                    File.WriteAllBytes(version == 0 ? "Logs/pushup-before.png" : "Logs/pushup-after.png", sheets[version].EncodeToPNG());
                }
                report.AppendLine(correctionType == null ? "\nCorrection type not yet present; baseline only." : "\nBoth versions rendered successfully.");
                report.AppendLine(failures.Count == 0 ? "RESULT: PASS" : $"RESULT: FAIL / {failures.Count} checks");
                Debug.Log("[PushupPosePreview] Wrote push-up contact sheets and geometry measurements to Logs.");
            }
            catch (Exception exception)
            {
                report.AppendLine("\nERROR " + exception);
                throw;
            }
            finally
            {
                File.WriteAllText("Logs/pushup-pose-measurements.txt", report.ToString());
                foreach (var sheet in sheets) if (sheet != null) Object.DestroyImmediate(sheet);
            }
            // Keep useful visual evidence even when a numerical regression fails in batch mode.
            if (failures.Count > 0) throw new InvalidOperationException("Push-up audit failed: " + string.Join("; ", failures));
        }

        private static Quaternion ViewRotation(int view) => Quaternion.LookRotation(
            -(view == 0 ? new Vector3(0f, .05f, 1f) : new Vector3(2.8f, 0.85f, 0f)).normalized, Vector3.up);

        private static void Audit(Fixture fixture, bool corrected, string gender, StringBuilder report, List<string> failures)
        {
            fixture.Sample(0.5f, false);
            var original = fixture.ReadPose();
            fixture.Sample(0.5f, corrected);
            var first = fixture.ReadPose();
            var drifts = Contacts.ToDictionary(b => b, b => 0f);
            float maxSegmentError = 0f, maxSymmetryError = 0f;
            float minKneeClearance = float.PositiveInfinity, minChainClearance = float.PositiveInfinity;
            float minKneeAngle = 180f, maxToeHeightDifference = 0f;
            float minShoeGap = float.PositiveInfinity;
            bool finite = true;
            for (int sample = 0; sample <= 20; sample++)
            {
                fixture.Sample(0.5f * (1f - sample / 20f), corrected);
                minShoeGap = Mathf.Min(minShoeGap, fixture.ShoeGap());
                var pose = fixture.ReadPose();
                foreach (var pair in pose)
                    finite &= !float.IsNaN(pair.Value.sqrMagnitude) && !float.IsInfinity(pair.Value.sqrMagnitude);
                if (pose.ContainsKey(HumanBodyBones.LeftToes) && pose.ContainsKey(HumanBodyBones.RightToes))
                {
                    float leftFloor = pose[HumanBodyBones.LeftToes].y, rightFloor = pose[HumanBodyBones.RightToes].y;
                    float floor = (leftFloor + rightFloor) * .5f;
                    maxToeHeightDifference = Mathf.Max(maxToeHeightDifference, Mathf.Abs(leftFloor - rightFloor));
                    foreach (var bone in pose) minChainClearance = Mathf.Min(minChainClearance, bone.Value.y - floor);
                    foreach (var leg in new[] {
                        new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot },
                        new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot } })
                    {
                        Vector3 knee = pose[leg[1]];
                        minKneeClearance = Mathf.Min(minKneeClearance, knee.y - floor);
                        minKneeAngle = Mathf.Min(minKneeAngle, Vector3.Angle(pose[leg[0]] - knee, pose[leg[2]] - knee));
                    }
                }
                foreach (var bone in Contacts)
                    if (pose.ContainsKey(bone) && first.ContainsKey(bone))
                        drifts[bone] = Mathf.Max(drifts[bone], Vector3.Distance(first[bone], pose[bone]));
                for (int i = 0; i < Segments.GetLength(0); i++)
                {
                    var a = Segments[i, 0]; var b = Segments[i, 1];
                    if (pose.ContainsKey(a) && pose.ContainsKey(b))
                        maxSegmentError = Mathf.Max(maxSegmentError, Mathf.Abs(Vector3.Distance(original[a], original[b])
                            - Vector3.Distance(pose[a], pose[b])));
                }
                foreach (var pair in new[] { new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand },
                    new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot },
                    new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm },
                    new[] { HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg } })
                {
                    if (!pose.ContainsKey(pair[0]) || !pose.ContainsKey(pair[1])) continue;
                    Vector3 left = pose[pair[0]], right = pose[pair[1]];
                    maxSymmetryError = Mathf.Max(maxSymmetryError,
                        new Vector3(left.x + right.x, left.y - right.y, left.z - right.z).magnitude);
                }
            }
            report.AppendLine($"\nGEOMETRY / {(corrected ? "AFTER" : "BEFORE")} / 21 evenly spaced depths");
            Check(report, failures, gender + (corrected ? " corrected" : " original") + " finite transforms", finite);
            foreach (var drift in drifts)
                report.AppendLine(drift.Key + " max drift from top: " + drift.Value.ToString("F6", CultureInfo.InvariantCulture) + " m");
            report.AppendLine("Max limb segment length change from uncorrected rig: " + maxSegmentError.ToString("F6", CultureInfo.InvariantCulture) + " m");
            report.AppendLine("Max mirrored pair error about root X=0: " + maxSymmetryError.ToString("F6", CultureInfo.InvariantCulture) + " m");
            report.AppendLine("Minimum gap between skinned shoes: " + minShoeGap.ToString("F6", CultureInfo.InvariantCulture) + " m");
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Knees: minimum angle {0:F3} deg, minimum toe-floor clearance {1:F6} m. All bones minimum clearance {2:F6} m. Toe height difference {3:F6} m.",
                minKneeAngle, minKneeClearance, minChainClearance, maxToeHeightDifference));
            if (corrected)
            {
                if (gender == nameof(CharacterGender.Female))
                    Check(report, failures, gender + " shoes remain separated (> 0.003 m)", minShoeGap > .003f);
                Check(report, failures, gender + " fixed wrists (< 0.010 m)", drifts[HumanBodyBones.LeftHand] < .01f && drifts[HumanBodyBones.RightHand] < .01f);
                Check(report, failures, gender + " segment lengths (< 0.002 m)", maxSegmentError < .002f);
                Check(report, failures, gender + " knees remain above the toe floor (2 mm tolerance)", minKneeClearance >= -.002f);
                Check(report, failures, gender + " knees remain extended (> 170 deg)", minKneeAngle > 170f);
                Check(report, failures, gender + " toes share floor height (< 0.005 m)", maxToeHeightDifference < .005f);
                Check(report, failures, gender + " fixed toe contacts (< 0.005 m)", drifts[HumanBodyBones.LeftToes] < .005f && drifts[HumanBodyBones.RightToes] < .005f);
                Check(report, failures, gender + " complete skeleton above toe floor (0.020 m skin clearance tolerance)", minChainClearance >= -.02f);
                report.AppendLine("Bilateral skeletal symmetry is also reported numerically; imported finger/foot proportions need not be identical.");
            }
        }

        private static string Format(Vector3 value) => string.Format(CultureInfo.InvariantCulture, "({0:F6}, {1:F6}, {2:F6})", value.x, value.y, value.z);

        private static void Check(StringBuilder report, List<string> failures, string label, bool passed)
        {
            report.AppendLine((passed ? "PASS " : "FAIL ") + label);
            if (!passed) failures.Add(label);
        }

        private struct PoseError
        {
            public float Position, Rotation, Scale;
            public bool WithinTolerance => Position < .00002f && Rotation < .1f && Scale < .000001f;
            public void Include(PoseError error)
            {
                Position = Mathf.Max(Position, error.Position);
                Rotation = Mathf.Max(Rotation, error.Rotation);
                Scale = Mathf.Max(Scale, error.Scale);
            }
            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "position {0:F7} m, rotation {1:F5} deg, scale {2:F7}", Position, Rotation, Scale);
        }

        private sealed class PoseSnapshot
        {
            private readonly Transform[] transforms;
            private readonly Vector3[] positions, scales;
            private readonly Quaternion[] rotations;
            public PoseSnapshot(Transform root, bool hierarchy = true)
            {
                transforms = hierarchy ? root.GetComponentsInChildren<Transform>(true) : new[] { root };
                positions = transforms.Select(t => t.localPosition).ToArray();
                rotations = transforms.Select(t => t.localRotation).ToArray();
                scales = transforms.Select(t => t.localScale).ToArray();
            }
            public PoseError Compare()
            {
                var error = new PoseError();
                for (int i = 0; i < transforms.Length; i++)
                {
                    error.Include(new PoseError {
                        Position = Vector3.Distance(positions[i], transforms[i].localPosition),
                        Rotation = Quaternion.Angle(rotations[i], transforms[i].localRotation),
                        Scale = Vector3.Distance(scales[i], transforms[i].localScale) });
                }
                return error;
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene scene;
            private readonly GameObject model;
            private readonly Animator animator;
            private readonly Camera camera;
            private readonly Component correction;
            private readonly MethodInfo apply;
            private readonly PoseError initialBindError;
            private readonly Vector3[] cameraPositions = new Vector3[2];
            private readonly float[] cameraSizes = new float[2];
            private readonly bool[] cameraReady = new bool[2];
            private readonly List<PreviewSkin> previewSkins = new List<PreviewSkin>();

            public Fixture(CharacterGender gender, RuntimeAnimatorController controller, Type correctionType)
            {
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var prefab = MainCharacterSetup.LoadCharacterPrefab(gender);
                    if (prefab == null) throw new InvalidOperationException("Missing " + gender + " character");
                    model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    animator = model.GetComponentInChildren<Animator>();
                    if (animator == null || !animator.isHuman) throw new InvalidOperationException("Character must have a valid humanoid Animator");
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.Rebind();
                    Sample(0.5f, false);
                    if (correctionType != null)
                    {
                        var bind = correctionType.GetMethod("Bind", new[] { typeof(Animator) });
                        apply = correctionType.GetMethod("Apply", new[] { typeof(float) });
                        if (bind == null || apply == null) throw new MissingMethodException("PushupPoseCorrection requires public static Bind(Animator) and Apply(float)");
                        var beforeBind = new PoseSnapshot(animator.transform);
                        correction = (Component)bind.Invoke(null, new object[] { animator });
                        if (correction == null) throw new InvalidOperationException("PushupPoseCorrection.Bind rejected a valid humanoid");
                        initialBindError = beforeBind.Compare();
                        if (correction is Behaviour behaviour) behaviour.enabled = false;
                    }
                    var cameraObject = NewObject("Push-up preview camera");
                    camera = cameraObject.AddComponent<Camera>();
                    camera.enabled = false;
                    camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                    camera.orthographic = true;
                    camera.aspect = (float)CellWidth / CellHeight;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.055f, .071f, .103f);
                    camera.nearClipPlane = .03f;
                    camera.farClipPlane = 40f;
                    camera.allowHDR = false;
                    AddLight("Stage key", new Vector3(35f, 140f, 0f), CharacterLighting.KeyIntensity, CharacterLighting.KeyColor);
                    AddLight("Stage fill", new Vector3(20f, -110f, 0f), CharacterLighting.FillIntensity, CharacterLighting.FillColor);
                    foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        if (skin.enabled && skin.sharedMesh != null) previewSkins.Add(new PreviewSkin(skin));
                    RefreshGeometry();
                }
                catch
                {
                    foreach (var skin in previewSkins) skin.Dispose();
                    EditorSceneManager.ClosePreviewScene(scene);
                    throw;
                }
            }

            private GameObject NewObject(string name)
            {
                var obj = new GameObject(name);
                SceneManager.MoveGameObjectToScene(obj, scene);
                return obj;
            }

            private void AddLight(string name, Vector3 euler, float intensity, Color color)
            {
                var light = NewObject(name).AddComponent<Light>();
                light.transform.rotation = Quaternion.Euler(euler);
                light.type = LightType.Directional;
                light.color = color;
                light.intensity = intensity;
                light.shadows = LightShadows.None;
            }

            public void Sample(float normalizedTime, bool corrected)
            {
                animator.Play("PushUp", 0, normalizedTime);
                animator.Update(0f);
                if (corrected) apply.Invoke(correction, new object[] { 1f - normalizedTime * 2f });
                RefreshGeometry();
            }

            private void RefreshGeometry()
            {
                foreach (var skin in previewSkins) skin.UpdateGeometry();
            }

            public Dictionary<HumanBodyBones, Vector3> ReadPose()
            {
                var result = new Dictionary<HumanBodyBones, Vector3>();
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone != null) result.Add((HumanBodyBones)i, animator.transform.InverseTransformPoint(bone.position));
                }
                return result;
            }

            public void AuditLifecycle(string gender, StringBuilder report, List<string> failures)
            {
                report.AppendLine("\nLIFECYCLE / " + gender);
                report.AppendLine("Full local transform comparison tolerance: 0.00002 m, 0.1 degree, 0.000001 scale.");
                Check(report, failures, gender + " first Bind preserves displayed pose / " + initialBindError, initialBindError.WithinTolerance);

                Sample(.31f, true);
                var beforeRebind = new PoseSnapshot(animator.transform);
                var rebound = correction.GetType().GetMethod("Bind", new[] { typeof(Animator) })
                    .Invoke(null, new object[] { animator });
                PoseError rebindError = beforeRebind.Compare();
                Check(report, failures, gender + " repeated Bind preserves component and pose / " + rebindError,
                    ReferenceEquals(rebound, correction) && rebindError.WithinTolerance);

                var downward = new PoseSnapshot[21];
                for (int i = 0; i < downward.Length; i++)
                {
                    Sample(.5f * (1f - i / 20f), true);
                    downward[i] = new PoseSnapshot(animator.transform);
                }
                var reversalError = new PoseError();
                for (int i = downward.Length - 1; i >= 0; i--)
                {
                    Sample(.5f * (1f - i / 20f), true);
                    reversalError.Include(downward[i].Compare());
                }
                Check(report, failures, gender + " same-depth pose after direction reversal / " + reversalError,
                    reversalError.WithinTolerance);

                Transform root = animator.transform;
                Vector3 originalPosition = root.localPosition, originalScale = root.localScale;
                Quaternion originalRotation = root.localRotation;
                var rootError = new PoseError();
                try
                {
                    root.localPosition = new Vector3(3f, -.7f, -2f);
                    root.localRotation = Quaternion.Euler(13f, 47f, 9f);
                    root.localScale = originalScale * 1.31f;
                    var expectedRoot = new PoseSnapshot(root, false);
                    foreach (float time in new[] { .5f, .25f, 0f, .25f, .5f })
                    {
                        Sample(time, true);
                        rootError.Include(expectedRoot.Compare());
                    }
                }
                finally
                {
                    root.localPosition = originalPosition;
                    root.localRotation = originalRotation;
                    root.localScale = originalScale;
                    Sample(.5f, false);
                }
                Check(report, failures, gender + " translated/rotated/scaled root remains unchanged / " + rootError,
                    rootError.WithinTolerance);

                var correctionScript = MonoScript.FromMonoBehaviour((MonoBehaviour)correction);
                var mirrorScript = AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/_Project/Scripts/CV/Avatar/PoseMirrorRetargeter.cs");
                int correctionOrder = correctionScript == null ? int.MaxValue : MonoImporter.GetExecutionOrder(correctionScript);
                int mirrorOrder = mirrorScript == null ? int.MinValue : MonoImporter.GetExecutionOrder(mirrorScript);
                Check(report, failures, gender + $" imported execution order correction {correctionOrder} before mirror {mirrorOrder}",
                    correctionOrder < mirrorOrder && correctionOrder < 100);
            }

            public List<Vector3> SkinVertices()
            {
                var points = new List<Vector3>();
                foreach (var skin in previewSkins) skin.CollectWorldVertices(points);
                return points;
            }

            public string DescribeSkinnedContacts()
            {
                var feet = new List<Vector3>();
                var hands = new List<Vector3>();
                foreach (var skin in previewSkins)
                {
                    skin.CollectRegionVertices(new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                        animator.GetBoneTransform(HumanBodyBones.RightFoot) }, feet);
                    skin.CollectRegionVertices(new[] { animator.GetBoneTransform(HumanBodyBones.LeftHand),
                        animator.GetBoneTransform(HumanBodyBones.RightHand) }, hands);
                }
                string Range(List<Vector3> vertices) => vertices.Count == 0 ? "no weighted vertices" : string.Format(CultureInfo.InvariantCulture,
                    "X [{0:F5}, {1:F5}], min Y {2:F5}, {3} vertices", vertices.Min(v => animator.transform.InverseTransformPoint(v).x),
                    vertices.Max(v => animator.transform.InverseTransformPoint(v).x), vertices.Min(v => animator.transform.InverseTransformPoint(v).y), vertices.Count);
                return "Rendered skin regions / feet: " + Range(feet) + " / hands: " + Range(hands)
                    + "\n" + DescribeToeDirection(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes)
                    + "\n" + DescribeToeDirection(HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            }

            public float ShoeGap()
            {
                var left = new List<Vector3>();
                var right = new List<Vector3>();
                foreach (var skin in previewSkins)
                {
                    skin.CollectRegionVertices(new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot) }, left);
                    skin.CollectRegionVertices(new[] { animator.GetBoneTransform(HumanBodyBones.RightFoot) }, right);
                }
                if (left.Count == 0 || right.Count == 0) throw new InvalidOperationException("Missing shoe vertices");
                return right.Min(v => animator.transform.InverseTransformPoint(v).x)
                    - left.Max(v => animator.transform.InverseTransformPoint(v).x);
            }

            private string DescribeToeDirection(HumanBodyBones footId, HumanBodyBones toeId)
            {
                Transform foot = animator.GetBoneTransform(footId), toe = animator.GetBoneTransform(toeId);
                Vector3 ankleCentroid = Vector3.zero, toeCentroid = Vector3.zero;
                float ankleWeight = 0f, toeWeight = 0f;
                foreach (var skin in previewSkins)
                {
                    skin.AccumulateWeightedCentroid(foot, false, ref ankleCentroid, ref ankleWeight);
                    skin.AccumulateWeightedCentroid(toe, true, ref toeCentroid, ref toeWeight);
                }
                if (ankleWeight < .0001f || toeWeight < .0001f) return footId + " has no separately weighted toe mesh region.";
                Vector3 skinDirection = (toeCentroid / toeWeight - ankleCentroid / ankleWeight).normalized;
                Vector3 boneDirection = (toe.position - foot.position).normalized;
                return footId + " weighted ankle-to-toe skin direction " + Format(animator.transform.InverseTransformDirection(skinDirection))
                    + " / bone direction " + Format(animator.transform.InverseTransformDirection(boneDirection))
                    + " / difference " + Vector3.Angle(skinDirection, boneDirection).ToString("F2", CultureInfo.InvariantCulture) + " deg";
            }

            public void FrameHeroPerspective()
            {
                // Match the fight camera's lens, view direction and centre-to-head aim bias.
                // Fit the actual posed silhouette so this shareable portrait has useful margins.
                camera.orthographic = false;
                camera.fieldOfView = 40f;
                camera.aspect = 640f / 840f;
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                foreach (var bone in ReadPose())
                {
                    Vector3 world = animator.transform.TransformPoint(bone.Value);
                    min = Vector3.Min(min, world); max = Vector3.Max(max, world);
                }
                Vector3 aim = (min + max) * .5f;
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null) aim.y = Mathf.Lerp(aim.y, head.position.y, .55f);
                Vector3 direction = new Vector3(0f, .05f, 1f).normalized;
                Quaternion rotation = Quaternion.LookRotation(-direction, Vector3.up);
                var points = SkinVertices();
                bool Fits(float distance)
                {
                    camera.transform.SetPositionAndRotation(aim + direction * distance, rotation);
                    foreach (Vector3 point in points)
                    {
                        Vector3 viewport = camera.WorldToViewportPoint(point);
                        if (viewport.z < camera.nearClipPlane + .01f || viewport.x < .07f || viewport.x > .93f
                            || viewport.y < .07f || viewport.y > .93f) return false;
                    }
                    return true;
                }
                float near = .03f, far = 1f;
                while (!Fits(far) && far < 128f) far *= 2f;
                for (int step = 0; step < 24; step++)
                {
                    float middle = (near + far) * .5f;
                    if (Fits(middle)) far = middle; else near = middle;
                }
                Fits(far);
            }

            public void Frame(int view, Quaternion rotation, Bounds projected, bool correctedAvailable)
            {
                camera.aspect = (float)CellWidth / CellHeight;
                if (!cameraReady[view])
                {
                    camera.transform.SetPositionAndRotation(rotation * (projected.center + Vector3.back * 8f), rotation);
                    camera.orthographicSize = Mathf.Max(projected.extents.y, projected.extents.x / camera.aspect) * 1.12f;
                    TightenFrame(correctedAvailable);
                    cameraPositions[view] = camera.transform.position;
                    cameraSizes[view] = camera.orthographicSize;
                    cameraReady[view] = true;
                }
                camera.transform.SetPositionAndRotation(cameraPositions[view], rotation);
                camera.orthographicSize = cameraSizes[view];
            }

            public void TightenFrame(bool correctedAvailable, bool currentPoseOnly = false)
            {
                // Fit actual rasterized silhouettes so imported renderer scales and outlines
                // cannot leave large empty margins. One union still frames both versions fairly.
                int minX = CellWidth, minY = CellHeight, maxX = -1, maxY = -1;
                for (int version = 0; version < (currentPoseOnly ? 1 : correctedAvailable ? 2 : 1); version++)
                    for (int phase = 0; phase < (currentPoseOnly ? 1 : Times.Length); phase++)
                    {
                        if (!currentPoseOnly) Sample(Times[phase], version == 1);
                        var image = Render();
                        try
                        {
                            Color32[] pixels = image.GetPixels32();
                            Color32 background = pixels[0];
                            for (int y = 0; y < CellHeight; y++)
                                for (int x = 0; x < CellWidth; x++)
                                {
                                    Color32 p = pixels[y * CellWidth + x];
                                    if (Math.Abs(p.r - background.r) + Math.Abs(p.g - background.g) + Math.Abs(p.b - background.b) < 25) continue;
                                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                                }
                        }
                        finally { Object.DestroyImmediate(image); }
                    }
                if (maxX < minX || maxY < minY) throw new InvalidOperationException("Preview rendered no visible character");
                float pixelSize = 2f * camera.orthographicSize / CellHeight;
                camera.transform.position += camera.transform.right * (((minX + maxX) * .5f - CellWidth * .5f) * pixelSize)
                    + camera.transform.up * (((minY + maxY) * .5f - CellHeight * .5f) * pixelSize);
                camera.orthographicSize *= Mathf.Max((maxX - minX + 1f) / CellWidth, (maxY - minY + 1f) / CellHeight) * 1.12f;
            }

            public Texture2D Render(int width = CellWidth, int height = CellHeight)
            {
                var target = new RenderTexture(width, height, 24) { antiAliasing = 4 };
                var previous = RenderTexture.active;
                try
                {
                    camera.targetTexture = target;
                    camera.Render();
                    RenderTexture.active = target;
                    var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                    return image;
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    target.Release();
                    Object.DestroyImmediate(target);
                }
            }

            public void Dispose()
            {
                foreach (var skin in previewSkins) skin.Dispose();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Editor Camera.Render can use a skinning buffer from the previous Animator evaluation.
        /// These temporary MeshRenderers draw a direct snapshot of the exact transforms audited
        /// above. Bind-pose matrices retain the source renderer's coordinate system and scale.
        /// </summary>
        private sealed class PreviewSkin : IDisposable
        {
            private readonly SkinnedMeshRenderer source;
            private readonly Mesh mesh;
            private readonly Transform[] bones;
            private readonly Matrix4x4[] bindPoses, matrices;
            private readonly BoneWeight[] weights;
            private readonly Vector3[] vertices, normals, skinnedVertices, skinnedNormals;
            private readonly Vector4[] tangents, skinnedTangents;

            public PreviewSkin(SkinnedMeshRenderer source)
            {
                this.source = source;
                mesh = Object.Instantiate(source.sharedMesh);
                mesh.name = source.sharedMesh.name + " / push-up CPU snapshot";
                vertices = source.sharedMesh.vertices;
                normals = source.sharedMesh.normals;
                tangents = source.sharedMesh.tangents;
                weights = source.sharedMesh.boneWeights;
                bones = source.bones;
                bindPoses = source.sharedMesh.bindposes;
                matrices = new Matrix4x4[bones.Length];
                skinnedVertices = new Vector3[vertices.Length];
                skinnedNormals = new Vector3[normals.Length];
                skinnedTangents = new Vector4[tangents.Length];

                // Both imported figures use four weights per vertex, as does the game skinning
                // quality. Retain authored blend-shape offsets when a prefab supplies any.
                var deltaVertices = new Vector3[vertices.Length];
                var deltaNormals = new Vector3[vertices.Length];
                var deltaTangents = new Vector3[vertices.Length];
                for (int shape = 0; shape < source.sharedMesh.blendShapeCount; shape++)
                {
                    float weight = source.GetBlendShapeWeight(shape);
                    if (Mathf.Abs(weight) < .00001f) continue;
                    int frame = source.sharedMesh.GetBlendShapeFrameCount(shape) - 1;
                    float frameWeight = source.sharedMesh.GetBlendShapeFrameWeight(shape, frame);
                    source.sharedMesh.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, deltaTangents);
                    float factor = weight / Mathf.Max(.00001f, frameWeight);
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        vertices[i] += deltaVertices[i] * factor;
                        if (i < normals.Length) normals[i] += deltaNormals[i] * factor;
                        if (i < tangents.Length) tangents[i] += (Vector4)(deltaTangents[i] * factor);
                    }
                }
                var proxy = new GameObject(source.name + " / pose snapshot");
                proxy.transform.SetParent(source.transform, false);
                proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = proxy.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                source.enabled = false;
            }

            public void UpdateGeometry()
            {
                Matrix4x4 toLocal = source.transform.worldToLocalMatrix;
                for (int i = 0; i < matrices.Length; i++)
                    matrices[i] = toLocal * (bones[i] == null ? source.transform.localToWorldMatrix : bones[i].localToWorldMatrix) * bindPoses[i];
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (weights.Length != vertices.Length)
                    {
                        skinnedVertices[i] = vertices[i];
                        if (i < normals.Length) skinnedNormals[i] = normals[i];
                        if (i < tangents.Length) skinnedTangents[i] = tangents[i];
                        continue;
                    }
                    BoneWeight w = weights[i];
                    skinnedVertices[i] = Point(vertices[i], w.boneIndex0, w.weight0) + Point(vertices[i], w.boneIndex1, w.weight1)
                        + Point(vertices[i], w.boneIndex2, w.weight2) + Point(vertices[i], w.boneIndex3, w.weight3);
                    if (i < normals.Length) skinnedNormals[i] = (Direction(normals[i], w.boneIndex0, w.weight0)
                        + Direction(normals[i], w.boneIndex1, w.weight1) + Direction(normals[i], w.boneIndex2, w.weight2)
                        + Direction(normals[i], w.boneIndex3, w.weight3)).normalized;
                    if (i < tangents.Length)
                    {
                        Vector3 t = (Direction(tangents[i], w.boneIndex0, w.weight0) + Direction(tangents[i], w.boneIndex1, w.weight1)
                            + Direction(tangents[i], w.boneIndex2, w.weight2) + Direction(tangents[i], w.boneIndex3, w.weight3)).normalized;
                        skinnedTangents[i] = new Vector4(t.x, t.y, t.z, tangents[i].w);
                    }
                }
                mesh.vertices = skinnedVertices;
                if (skinnedNormals.Length == vertices.Length) mesh.normals = skinnedNormals;
                if (skinnedTangents.Length == vertices.Length) mesh.tangents = skinnedTangents;
                mesh.RecalculateBounds();
            }

            private Vector3 Point(Vector3 point, int bone, float weight) => weight <= 0f ? Vector3.zero : matrices[bone].MultiplyPoint3x4(point) * weight;
            private Vector3 Direction(Vector3 direction, int bone, float weight) => weight <= 0f ? Vector3.zero : matrices[bone].MultiplyVector(direction) * weight;

            public void CollectWorldVertices(List<Vector3> result)
            {
                foreach (var vertex in skinnedVertices) result.Add(source.transform.TransformPoint(vertex));
            }

            public void CollectRegionVertices(Transform[] roots, List<Vector3> result)
            {
                if (weights.Length != skinnedVertices.Length) return;
                for (int i = 0; i < weights.Length; i++)
                {
                    BoneWeight w = weights[i];
                    int index = w.boneIndex0;
                    float dominant = w.weight0;
                    if (w.weight1 > dominant) { index = w.boneIndex1; dominant = w.weight1; }
                    if (w.weight2 > dominant) { index = w.boneIndex2; dominant = w.weight2; }
                    if (w.weight3 > dominant) index = w.boneIndex3;
                    Transform bone = bones[index];
                    if (bone != null && roots.Any(root => root != null && (bone == root || bone.IsChildOf(root))))
                        result.Add(source.transform.TransformPoint(skinnedVertices[i]));
                }
            }

            public void AccumulateWeightedCentroid(Transform target, bool descendants, ref Vector3 sum, ref float totalWeight)
            {
                if (target == null || weights.Length != skinnedVertices.Length) return;
                float Weight(int index, float value) => value > 0f && bones[index] != null
                    && (bones[index] == target || descendants && bones[index].IsChildOf(target)) ? value : 0f;
                for (int i = 0; i < weights.Length; i++)
                {
                    BoneWeight w = weights[i];
                    float contribution = Weight(w.boneIndex0, w.weight0) + Weight(w.boneIndex1, w.weight1)
                        + Weight(w.boneIndex2, w.weight2) + Weight(w.boneIndex3, w.weight3);
                    sum += source.transform.TransformPoint(skinnedVertices[i]) * contribution;
                    totalWeight += contribution;
                }
            }

            public void Dispose() => Object.DestroyImmediate(mesh);
        }

        private static Texture2D MakeSheet(bool corrected)
        {
            var sheet = new Texture2D(Gap + 6 * (CellWidth + Gap), 1072, TextureFormat.RGB24, false);
            var pixels = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(10, 14, 22, 255);
            sheet.SetPixels32(pixels);
            Text(sheet, corrected ? "CORRECTED PUSH-UP" : "MIXAMO ORIGINAL", 18, 20, 4, Color.white);
            Text(sheet, "FRONT", 18, 63, 2, new Color(.56f, .76f, 1f));
            Text(sheet, "SIDE", 18 + 3 * (CellWidth + Gap), 63, 2, new Color(.56f, .76f, 1f));
            for (int row = 0; row < 2; row++)
                for (int column = 0; column < 6; column++)
                    Text(sheet, (row == 0 ? "MALE / " : "FEMALE / ") + PhaseNames[column % 3],
                        Gap + column * (CellWidth + Gap), 96 + row * 484, 2, new Color(.80f, .85f, .92f));
            Text(sheet, "SAME CAMERA / SAME SCALE / TOP - MIDDLE - BOTTOM", 18, 1048, 2, new Color(.46f, .55f, .68f));
            return sheet;
        }

        // A tiny bitmap alphabet keeps the batch-mode artifact independent of GUI/font rendering.
        private static readonly Dictionary<char, string> Glyphs = new Dictionary<char, string>
        {
            ['A']="01110100011000111111100011000110001", ['B']="11110100011000111110100011000111110",
            ['C']="01111100001000010000100001000001111", ['D']="11110100011000110001100011000111110",
            ['E']="11111100001000011110100001000011111", ['F']="11111100001000011110100001000010000",
            ['G']="01111100001000010111100011000101111", ['H']="10001100011000111111100011000110001",
            ['I']="11111001000010000100001000010011111", ['J']="00111000100001000010100101001001100",
            ['K']="10001100101010011000101001001010001", ['L']="10000100001000010000100001000011111",
            ['M']="10001110111010110101100011000110001", ['N']="10001110011010110011100011000110001",
            ['O']="01110100011000110001100011000101110", ['P']="11110100011000111110100001000010000",
            ['Q']="01110100011000110001101011001001101", ['R']="11110100011000111110101001001010001",
            ['S']="01111100001000001110000010000111110", ['T']="11111001000010000100001000010000100",
            ['U']="10001100011000110001100011000101110", ['V']="10001100011000110001100010101000100",
            ['W']="10001100011000110101101011101110001", ['X']="10001100010101000100010101000110001",
            ['Y']="10001100010101000100001000010000100", ['Z']="11111000010001000100010001000011111",
            ['/']="00001000010001000100010001000010000", ['-']="00000000000000011111000000000000000"
        };

        private static void Text(Texture2D image, string text, int x, int y, int scale, Color color)
        {
            foreach (char c in text)
            {
                if (Glyphs.TryGetValue(c, out string glyph))
                    for (int row = 0; row < 7; row++)
                        for (int column = 0; column < 5; column++)
                            if (glyph[row * 5 + column] == '1')
                                for (int sy = 0; sy < scale; sy++)
                                    for (int sx = 0; sx < scale; sx++)
                                        image.SetPixel(x + column * scale + sx, image.height - 1 - y - row * scale - sy, color);
                x += 6 * scale;
            }
        }
    }
}
