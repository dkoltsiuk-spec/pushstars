using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using PushStars.CV;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Exercises the actual imported humanoids with deterministic camera landmarks. Everything
    /// lives in a disposable preview scene: no webcam, Play-mode switch, saved scene, or prefab
    /// is changed. This tests the runtime solver, not a duplicate implementation of it.
    /// </summary>
    public static class RetargetRegression
    {
        private const string ReportPath = "Logs/retarget-regression.txt";
        private const float Dt = 1f / 60f;
        private static readonly MethodInfo StepMethod = typeof(PoseMirrorRetargeter).GetMethod(
            "Step", BindingFlags.Instance | BindingFlags.NonPublic);

        [MenuItem("Tools/Push Stars/CV/Validate Avatar Retargeting", priority = 314)]
        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("Avatar retargeting regression — " + DateTime.UtcNow.ToString("u"));
            report.AppendLine("Unity " + Application.unityVersion);
            report.AppendLine("Imported humanoids in isolated preview scenes; no live-camera assessment.");
            int passed = 0, failed = 0;

            RunCase("Camera-space mapping and selfie-side correspondence", CheckMapping,
                report, ref passed, ref failed);
            RunCase("Imported script execution order", CheckExecutionOrder,
                report, ref passed, ref failed);
            RunCase("Camera sensor orientation, display reflection and metric depth", CheckCameraOrientation,
                report, ref passed, ref failed);
            foreach (string path in new[]
            {
                "Assets/Character/Main_man/main_man.fbx",
                "Assets/Character/Main_woman/main_woman.fbx",
            })
            {
                foreach (bool mirror in new[] { false, true })
                {
                    string label = Path.GetFileNameWithoutExtension(path) + (mirror ? " / selfie" : " / camera");
                    RunCase(label + " / horizontal placement", () =>
                    {
                        using (var fixture = new Fixture(path, mirror)) CheckPlacement(fixture);
                    }, report, ref passed, ref failed);
                    RunCase(label + " / frontal, profile and back turns", () =>
                    {
                        using (var fixture = new Fixture(path, mirror)) CheckTurns(fixture);
                    }, report, ref passed, ref failed);
                    RunCase(label + " / depth reach and partial occlusion", () =>
                    {
                        using (var fixture = new Fixture(path, mirror)) CheckOcclusion(fixture);
                    }, report, ref passed, ref failed);
                    RunCase(label + " / stale frames and invalid landmarks", () =>
                    {
                        using (var fixture = new Fixture(path, mirror)) CheckInvalidFrames(fixture);
                    }, report, ref passed, ref failed);
                    RunCase(label + " / animation handoff and return", () =>
                    {
                        using (var fixture = new Fixture(path, mirror)) CheckHandoff(fixture);
                    }, report, ref passed, ref failed);
                    RunCase(label + " / nearly straight elbow roll continuity", () =>
                    {
                        using (var fixture = new Fixture(path, mirror)) CheckElbowContinuity(fixture);
                    }, report, ref passed, ref failed);
                }
            }

            RunCase("Imported humanoid visual contact sheet", RenderContactSheet, report, ref passed, ref failed);
            report.AppendLine("Visual QA: Logs/retarget-poses.png. Top row: male. Bottom row: female.");
            report.AppendLine("Columns left to right: frontal; +90-degree profile; -90-degree profile; bent arms with one reaching toward the lens.");
            report.AppendLine("Selfie mode; isolated preview-scene camera and lights; each view fitted to the posed skin bounds.");

            report.AppendLine();
            report.AppendLine($"RESULT: {passed} passed, {failed} failed.");
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, report.ToString());
            if (failed == 0) Debug.Log($"[RetargetRegression] PASS — {passed} cases. {ReportPath}");
            else Debug.LogError($"[RetargetRegression] FAIL — {failed} of {passed + failed} cases. {ReportPath}");
        }

        private static void RunCase(string label, Action check, StringBuilder report,
            ref int passed, ref int failed)
        {
            try
            {
                check();
                report.AppendLine("PASS " + label);
                passed++;
            }
            catch (Exception exception)
            {
                while (exception is TargetInvocationException && exception.InnerException != null)
                    exception = exception.InnerException;
                report.AppendLine("FAIL " + label + ": " + exception.Message);
                report.AppendLine(exception.StackTrace);
                failed++;
            }
        }

        private static void CheckMapping()
        {
            // Neither a fixed world-axis sign nor a root-relative guess can pass a rolled,
            // pitched and yawed stage camera. Depth must keep its metric magnitude.
            Quaternion camera = Quaternion.Euler(13f, 157f, 9f);
            Vector3 source = new Vector3(0.3f, -0.4f, 0.8f);
            foreach (bool mirror in new[] { false, true })
            {
                Vector3 result = PoseRetargetMath.MapDirection(source, mirror, camera);
                Vector3 local = Quaternion.Inverse(camera) * result;
                Vector3 expected = new Vector3(mirror ? -source.x : source.x, -source.y, source.z);
                Require((local - expected).magnitude < 0.0001f,
                    "Direction changed camera-space metric X/Y/Z or applied the wrong mirror sign.");
            }
            Require(PoseRetargetMath.SwapSide(PoseLandmark.LeftShoulder) == PoseLandmark.RightShoulder,
                "Shoulders did not swap anatomical sides in selfie mode.");
            Require(PoseRetargetMath.SwapSide(PoseLandmark.RightAnkle) == PoseLandmark.LeftAnkle,
                "Ankles did not swap anatomical sides in selfie mode.");
            Require(PoseRetargetMath.SwapSide(PoseLandmark.Nose) == PoseLandmark.Nose,
                "A midline landmark was incorrectly swapped.");
            for (int i = 0; i < PoseLandmarks.Count; i++)
                Require(PoseRetargetMath.SwapSide(PoseRetargetMath.SwapSide((PoseLandmark)i)) == (PoseLandmark)i,
                    "Swapping a landmark twice did not restore it.");
        }

        private static void CheckExecutionOrder()
        {
            string[] paths =
            {
                "Assets/_Project/Scripts/CV/Avatar/PoseMirrorRetargeter.cs",
                "Assets/_Project/Scripts/CV/Avatar/PushupAvatarDriver.cs",
                "Assets/_Project/Scripts/CV/Avatar/AvatarMirrorAnchor.cs",
                "Assets/_Project/Scripts/Fight/FightAvatar.cs",
            };
            Type[] types = { typeof(PoseMirrorRetargeter), typeof(PushupAvatarDriver),
                typeof(AvatarMirrorAnchor), typeof(PushStars.Fight.FightAvatar) };
            int[] expected = { 100, 150, 200, 300 };
            for (int i = 0; i < paths.Length; i++)
            {
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(paths[i]);
                Require(script != null && script.GetClass() == types[i], "Imported runtime script is missing or has the wrong class: " + paths[i]);
                int order = MonoImporter.GetExecutionOrder(script);
                Require(order == expected[i],
                    $"Unity imported {types[i].Name} at order {order}; expected {expected[i]} so animation, bones, anchor and camera run in sequence.");
            }
        }

        private static void CheckCameraOrientation()
        {
            int[] turns = { 0, 90, 180, 270 };
            Vector2[] rotatedImage = { new Vector2(0.2f, 0.7f), new Vector2(0.3f, 0.2f),
                new Vector2(0.8f, 0.3f), new Vector2(0.7f, 0.8f) };
            Vector3[] rotatedWorld = { new Vector3(0.3f, -0.4f, 0.8f), new Vector3(0.4f, 0.3f, 0.8f),
                new Vector3(-0.3f, 0.4f, 0.8f), new Vector3(-0.4f, -0.3f, 0.8f) };
            Vector2 raw = new Vector2(0.2f, 0.7f);
            Vector3 metric = new Vector3(0.3f, -0.4f, 0.8f);
            for (int i = 0; i < turns.Length; i++)
            {
                foreach (bool sourceHorizontal in new[] { false, true })
                foreach (bool sourceVertical in new[] { false, true })
                foreach (bool sensorVertical in new[] { false, true })
                {
                    var orientation = new CameraFrameOrientation(turns[i], sourceHorizontal,
                        sourceVertical, sensorVertical, 1280, 720);
                    Require(Vector2.Distance(orientation.RotateImage(raw), rotatedImage[i]) < 0.0001f,
                        $"Image rotation {turns[i]} did not rotate clockwise in top-left coordinates.");
                    Vector3 result = orientation.RotateWorld(metric);
                    Require(Vector3.Distance(result, rotatedWorld[i]) < 0.0001f,
                        $"Metric world rotation {turns[i]} changed direction or relative depth.");
                    Require(Mathf.Abs(result.magnitude - metric.magnitude) < 0.0001f && Mathf.Abs(result.z - metric.z) < 0.0001f,
                        "Camera orientation scaled metric landmarks or altered Z depth.");
                    float expectedAspect = turns[i] == 90 || turns[i] == 270 ? 720f / 1280f : 1280f / 720f;
                    Require(Mathf.Abs(orientation.UprightAspect - expectedAspect) < 0.0001f,
                        "Upright camera aspect did not swap dimensions for a quarter turn.");
                    Require(orientation.ReadbackFlipHorizontally == sourceHorizontal
                        && orientation.ReadbackFlipVertically == (sourceVertical ^ sensorVertical),
                        "Camera sensor mirror was not composed exactly once with texture readback.");
                    Require(orientation.RawFlipHorizontally == sourceHorizontal
                        && orientation.RawFlipVertically == !(sourceVertical ^ sensorVertical),
                        "Top-left/raw-texture conversion used the wrong vertical reflection.");

                    Vector2 upright = orientation.RawToUpright(raw);
                    Require(Vector2.Distance(orientation.UprightToRaw(upright), raw) < 0.0001f,
                        "Raw camera pixel did not survive the upright inverse mapping.");
                    Vector2 detectorPoint = new Vector2(sourceHorizontal ? 1f - raw.x : raw.x,
                        orientation.RawFlipVertically ? 1f - raw.y : raw.y);
                    Require(Vector2.Distance(upright, orientation.RotateImage(detectorPoint)) < 0.0001f,
                        "Raw sensor flips were applied after the upright rotation.");

                    Vector2 a = new Vector2(0.2f, 0.3f), delta = new Vector2(0.1f, -0.15f);
                    Vector2 imageDelta = orientation.RotateImage(a + delta) - orientation.RotateImage(a);
                    Vector3 worldDelta = orientation.RotateWorld(new Vector3(delta.x, delta.y, 0.45f));
                    Require(Vector2.Distance(imageDelta, new Vector2(worldDelta.x, worldDelta.y)) < 0.0001f,
                        "Image and metric world landmarks no longer share the same upright X/Y axes.");

                    foreach (bool selfie in new[] { false, true })
                    {
                        Vector2 expectedDisplay = new Vector2(selfie ? 1f - upright.x : upright.x, upright.y);
                        Vector2 display = orientation.RawToDisplay(raw, selfie);
                        Require(Vector2.Distance(display, expectedDisplay) < 0.0001f
                            && Vector2.Distance(orientation.UprightToDisplay(upright, selfie), expectedDisplay) < 0.0001f,
                            "Selfie reflection was not applied horizontally after the sensor rotation.");
                        Require(Vector2.Distance(orientation.DisplayToRaw(display, selfie), raw) < 0.0001f,
                            "Displayed camera pixel did not map back to its raw sensor coordinate.");
                    }
                }
            }

            // Explicit asymmetric portrait case: sensor inversion and selfie reflection do not
            // commute with a 90-degree rotation. This catches a shared but wrong round-trip pair.
            var portrait = new CameraFrameOrientation(90, false, true, true, 1280, 720);
            Require(Vector2.Distance(portrait.RawToDisplay(raw, true), new Vector2(0.3f, 0.2f)) < 0.0001f,
                "Portrait sensor inversion + selfie reflection produced the wrong display corner.");
        }

        private static void CheckTurns(Fixture fixture)
        {
            float priorYaw = 0f;
            foreach (float yaw in new[] { 0f, 90f, 0f, -90f, -180f, 0f })
            {
                // Turn over half a second rather than fabricating an impossible one-frame
                // 180-degree rotation. Then allow temporal filtering to converge.
                for (int i = 1; i <= 30; i++)
                    fixture.Tick(Skeleton(Mathf.Lerp(priorYaw, yaw, i / 30f)));
                PoseFrame sample = fixture.Settle(Skeleton(yaw));
                Vector3 expectedRight = fixture.Direction(sample,
                    PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder);
                Vector3 actualRight = fixture.Bone(HumanBodyBones.RightUpperArm).position
                    - fixture.Bone(HumanBodyBones.LeftUpperArm).position;
                Require(Vector3.Angle(expectedRight, actualRight) < 15f,
                    $"Torso did not follow yaw {yaw:0}: shoulder-axis error {Vector3.Angle(expectedRight, actualRight):0.0} degrees.");
                fixture.CheckDirection(sample, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                    PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, 15f);
                fixture.CheckDirection(sample, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
                    PoseLandmark.RightHip, PoseLandmark.RightKnee, 15f);
                fixture.CheckFinite();
                priorYaw = yaw;
            }
        }

        private static void CheckPlacement(Fixture fixture)
        {
            var sampleMethod = typeof(AvatarMirrorAnchor).GetMethod("TrySample", BindingFlags.Instance | BindingFlags.NonPublic);
            var feedMethod = typeof(AvatarMirrorAnchor).GetMethod("FeedSample", BindingFlags.Instance | BindingFlags.NonPublic);
            var viewportField = typeof(AvatarMirrorAnchor).GetField("_filteredVp", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(sampleMethod != null && feedMethod != null && viewportField != null, "Anchor sample/filter seam is missing.");
            float timestamp = 1f;
            foreach (float x in new[] { 0.3f, 0.7f })
            {
                for (int n = 0; n < 120; n++)
                {
                    var sample = Frame(Skeleton(0f), timestamp);
                    for (int i = 0; i < sample.Landmarks.Length; i++)
                    {
                        var point = sample.Landmarks[i];
                        sample.Landmarks[i] = new Landmark(point.X + x - 0.5f, point.Y, point.Z, point.Visibility);
                    }
                    object[] arguments = { sample, Vector2.zero, 1f };
                    Require((bool)sampleMethod.Invoke(fixture.Anchor, arguments), "Valid standing placement sample was rejected.");
                    feedMethod.Invoke(fixture.Anchor, new[] { arguments[1], arguments[2], (object)timestamp });
                    timestamp += 1f / 30f;
                }
                Vector2 viewport = (Vector2)viewportField.GetValue(fixture.Anchor);
                float expected = fixture.Mirror ? 1f - x : x;
                Require(Mathf.Abs(viewport.x - expected) < 0.01f,
                    $"Anchor moved to viewport X {viewport.x:0.000}; expected {expected:0.000}.");
            }

            float frontalScale = 0f;
            foreach (float yaw in new[] { 0f, 60f, 90f })
            {
                var world = Skeleton(yaw);
                if (yaw != 0f)
                {
                    foreach (var id in new[] { PoseLandmark.RightShoulder, PoseLandmark.RightHip })
                    {
                        var point = world[(int)id];
                        world[(int)id] = new Landmark(point.X, point.Y, point.Z, 0.05f);
                    }
                }
                object[] arguments = { Frame(world, timestamp), Vector2.zero, 1f };
                Require((bool)sampleMethod.Invoke(fixture.Anchor, arguments), "Profile torso placement was rejected.");
                Vector2 center = (Vector2)arguments[1];
                float scale = (float)arguments[2];
                if (yaw == 0f) frontalScale = scale;
                Require(Vector2.Distance(center, new Vector2(0.5f, 0.48f)) < 0.002f,
                    "Hidden hip shifted the estimated body center to the visible side.");
                Require(Mathf.Abs(scale - frontalScale) < 0.02f,
                    "Turning into profile changed apparent body scale despite fixed camera distance.");
            }
        }

        private static void CheckOcclusion(Fixture fixture)
        {
            var standing = Skeleton(90f);
            fixture.Settle(standing);

            // Right arm reaches toward the lens. Its far-side counterpart disappears, including
            // its torso landmarks, as routinely happens in profile. The visible arm must still
            // move; global all-joints gates would fail this case.
            var reaching = Skeleton(90f);
            Set(reaching, PoseLandmark.RightElbow, new Vector3(0f, -0.5f, -0.12f));
            Set(reaching, PoseLandmark.RightWrist, new Vector3(0f, -0.5f, -0.42f));
            var hidden = new[] { PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow,
                PoseLandmark.LeftWrist, PoseLandmark.LeftHip, PoseLandmark.LeftKnee,
                PoseLandmark.LeftAnkle, PoseLandmark.LeftHeel, PoseLandmark.LeftFootIndex };
            foreach (var id in hidden)
            {
                var point = reaching[(int)id];
                reaching[(int)id] = new Landmark(point.X, point.Y, point.Z, 0.05f);
            }

            HumanBodyBones upper = fixture.Mirror ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
            HumanBodyBones lower = fixture.Mirror ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
            HumanBodyBones hand = fixture.Mirror ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            Vector3 before = fixture.Bone(hand).position - fixture.Bone(lower).position;
            PoseFrame sample = fixture.Settle(reaching, 120);
            Vector3 after = fixture.Bone(hand).position - fixture.Bone(lower).position;
            Require(Vector3.Angle(before, after) > 35f,
                "Visible forearm stopped responding when the far side was occluded.");
            PoseLandmark elbow = fixture.Mirror ? PoseLandmark.LeftElbow : PoseLandmark.RightElbow;
            PoseLandmark wrist = fixture.Mirror ? PoseLandmark.LeftWrist : PoseLandmark.RightWrist;
            fixture.CheckDirection(sample, lower, hand, elbow, wrist, 18f);
            Require(Mathf.Abs(Vector3.Dot(after.normalized, fixture.Camera.transform.forward)) > 0.8f,
                "A toward-camera reach was flattened instead of preserving depth.");
            Vector3 torsoUp = (fixture.Bone(HumanBodyBones.LeftUpperArm).position
                + fixture.Bone(HumanBodyBones.RightUpperArm).position) * 0.5f
                - fixture.Bone(HumanBodyBones.Hips).position;
            Require(Vector3.Angle(torsoUp, fixture.Camera.transform.up) < 3f,
                $"Single-side torso taper introduced {Vector3.Angle(torsoUp, fixture.Camera.transform.up):0.0} degrees of false lean.");
            Require(fixture.Bone(upper) != null, "Visible upper arm disappeared.");
            fixture.CheckFinite();

            // Exercise the real LateUpdate glue once, not only the deterministic Step seam.
            // PoseQuality deliberately calls this profile Lost for rep-counting purposes.
            // Fresh visible joints still have to reach the mirror's independent confidence gate.
            var sessionObject = new GameObject("Inactive session fixture (no source)");
            sessionObject.SetActive(false);
            sessionObject.transform.SetParent(fixture.Model.transform.parent, false);
            var session = sessionObject.AddComponent<PushupSession>();
            SetPrivate(session, "<LastFrame>k__BackingField", Frame(reaching, fixture.Now + 1f));
            SetPrivate(session, "<Quality>k__BackingField", TrackingQuality.Lost);
            SetPrivate(fixture.Solver, "_session", session);
            typeof(PoseMirrorRetargeter).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(fixture.Solver, null);
            Require(fixture.Solver.HasFreshPose, "Rep-counting Lost quality cut off a fresh, partially visible avatar pose.");
        }

        private static void CheckInvalidFrames(Fixture fixture)
        {
            PoseFrame last = fixture.Settle(Skeleton(0f));
            var raised = Skeleton(0f);
            Set(raised, PoseLandmark.LeftElbow, new Vector3(0.50f, -0.68f, -0.08f));
            Set(raised, PoseLandmark.LeftWrist, new Vector3(0.67f, -0.9f, -0.15f));
            HumanBodyBones checkedBone = fixture.Mirror ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;
            Quaternion before = fixture.Bone(checkedBone).rotation;
            PoseFrame stale = Frame(raised, last.TimestampSec);
            for (int i = 0; i < 6; i++) fixture.TickFrame(stale, true, false);
            Require(Quaternion.Angle(before, fixture.Bone(checkedBone).rotation) < 6f,
                "Repeated capture timestamp was treated as fresh motion.");

            var corrupt = Skeleton(0f);
            corrupt[(int)PoseLandmark.LeftWrist] = new Landmark(float.NaN, 0f, float.PositiveInfinity, 1f);
            corrupt[(int)PoseLandmark.RightKnee] = new Landmark(0f, float.NaN, 0f, 1f);
            for (int i = 0; i < 30; i++)
            {
                fixture.Tick(corrupt);
                fixture.CheckFinite();
            }
            for (int i = 0; i < 90; i++) fixture.TickFrame(default, false, false);
            fixture.CheckFinite();
            PoseFrame recovered = fixture.Settle(Skeleton(-90f));
            fixture.CheckDirection(recovered, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                PoseLandmark.RightElbow, PoseLandmark.RightWrist, 18f);
        }

        private static void CheckHandoff(Fixture fixture)
        {
            fixture.Settle(Skeleton(0f));
            PoseFrame lastArmed = default;
            for (int i = 0; i < 90; i++)
            {
                lastArmed = Frame(Skeleton(0f), fixture.Now);
                fixture.TickFrame(lastArmed, true, true);
            }
            Require(fixture.Solver.MirrorWeight <= 0.001f, "Mirror did not release animation ownership when armed.");

            // Supply a recognizably non-neutral animated pose and verify every local transform,
            // including hips translation, survives the zero-weight branch exactly.
            var transforms = fixture.Model.GetComponentsInChildren<Transform>(true);
            var rotations = new Quaternion[transforms.Length];
            var positions = new Vector3[transforms.Length];
            var animatedBones = new HashSet<Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = fixture.Animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null) animatedBones.Add(bone);
            }
            for (int i = 0; i < transforms.Length; i++)
            {
                if (animatedBones.Contains(transforms[i]))
                    transforms[i].localRotation *= Quaternion.Euler(3f, 5f, 7f);
                rotations[i] = transforms[i].localRotation;
                positions[i] = transforms[i].localPosition;
            }
            Transform hips = fixture.Bone(HumanBodyBones.Hips);
            hips.localPosition += new Vector3(0.01f, -0.02f, 0.03f);
            for (int i = 0; i < transforms.Length; i++) positions[i] = transforms[i].localPosition;

            fixture.TickFrame(default, false, true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Require(Quaternion.Angle(rotations[i], transforms[i].localRotation) < 0.05f,
                    "Zero-weight handoff overwrote animation rotation on " + transforms[i].name + ".");
                Require((positions[i] - transforms[i].localPosition).magnitude < 0.00001f,
                    "Zero-weight handoff overwrote animation position on " + transforms[i].name + ".");
            }

            // The source has stopped: disarming must not turn its last armed snapshot into
            // a new detection or reopen the calibration gate while waiting for another frame.
            for (int i = 0; i < 30; i++)
            {
                fixture.TickFrame(lastArmed, true, false);
                Require(fixture.Solver.MirrorWeight <= 0.001f,
                    "Disarm reacquired the mirror from an unchanged pre-disarm timestamp.");
            }

            PoseFrame returned = fixture.Settle(Skeleton(0f));
            fixture.CheckDirection(returned, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                PoseLandmark.LeftElbow, PoseLandmark.LeftWrist, 18f);
            fixture.CheckFinite();
        }

        private static void CheckElbowContinuity(Fixture fixture)
        {
            var straight = Skeleton(0f);
            Set(straight, PoseLandmark.LeftElbow, new Vector3(0.50f, -0.45f, 0f));
            Set(straight, PoseLandmark.LeftWrist, new Vector3(0.78f, -0.40f, 0f));
            fixture.Settle(straight, 120);
            var upper = fixture.Bone(fixture.Mirror ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            Quaternion previous = upper.rotation;
            for (int i = 0; i < 40; i++)
            {
                // This noise crosses both signs of the otherwise undefined straight-elbow bend
                // plane. A direction-only roll solver can suddenly spin the upper arm 180 degrees.
                Set(straight, PoseLandmark.LeftWrist,
                    new Vector3(0.78f, -0.40f, i % 2 == 0 ? 0.002f : -0.002f));
                fixture.Tick(straight);
                float change = Quaternion.Angle(previous, upper.rotation);
                Require(change < 12f, $"Nearly straight elbow made the upper arm jump {change:0.0} degrees in one frame.");
                previous = upper.rotation;
            }
            fixture.CheckFinite();
        }

        private static void RenderContactSheet()
        {
            const int width = 384, height = 576, columns = 4;
            var sheet = new Texture2D(width * columns, height * 2, TextureFormat.RGB24, false);
            try
            {
                string[] paths = { "Assets/Character/Main_man/main_man.fbx", "Assets/Character/Main_woman/main_woman.fbx" };
                for (int row = 0; row < paths.Length; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        using (var fixture = new Fixture(paths[row], true))
                        {
                            Landmark[] pose = Skeleton(column == 1 ? 90f : column == 2 ? -90f : 0f);
                            if (column == 3)
                            {
                                Set(pose, PoseLandmark.RightElbow, new Vector3(-0.43f, -0.43f, -0.22f));
                                Set(pose, PoseLandmark.RightWrist, new Vector3(-0.27f, -0.68f, -0.46f));
                                Set(pose, PoseLandmark.LeftElbow, new Vector3(0.46f, -0.18f, -0.02f));
                                Set(pose, PoseLandmark.LeftWrist, new Vector3(0.20f, -0.10f, -0.05f));
                            }
                            fixture.Settle(pose, 150);
                            Texture2D cell = fixture.Render(width, height);
                            try
                            {
                                var pixels = cell.GetPixels32();
                                Color32 background = pixels[0];
                                int foreground = 0;
                                foreach (Color32 pixel in pixels)
                                    if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g)
                                        + Math.Abs(pixel.b - background.b) > 35) foreground++;
                                Require(foreground > pixels.Length / 100,
                                    $"Contact-sheet cell {row + 1}/{column + 1} rendered no visible character.");
                                sheet.SetPixels32(column * width, (1 - row) * height, width, height, pixels);
                            }
                            finally { Object.DestroyImmediate(cell); }
                        }
                    }
                }
                sheet.Apply();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/retarget-poses.png", sheet.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(sheet); }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly Scene _scene;
            private readonly GameObject _owner;
            public readonly GameObject Model;
            public readonly Camera Camera;
            public readonly AvatarMirrorAnchor Anchor;
            public readonly Animator Animator;
            public readonly PoseMirrorRetargeter Solver;
            public readonly bool Mirror;
            public float Now { get; private set; } = 1f;

            public Fixture(string path, bool mirror)
            {
                Require(StepMethod != null, "Runtime Step(PoseFrame, bool, bool, float, float) method is missing.");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Require(prefab != null, "Imported model not found: " + path);
                _scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    _owner = new GameObject("Retarget regression (temporary)");
                    SceneManager.MoveGameObjectToScene(_owner, _scene);
                    _owner.hideFlags = HideFlags.HideAndDontSave;
                    var cameraObject = new GameObject("Pose camera (disabled)");
                    cameraObject.transform.SetParent(_owner.transform, false);
                    Camera = cameraObject.AddComponent<Camera>();
                    Camera.enabled = false;
                    Camera.transform.SetPositionAndRotation(new Vector3(0f, 1.2f, 4f), Quaternion.Euler(0f, 180f, 0f));
                    Anchor = _owner.AddComponent<AvatarMirrorAnchor>();
                    Anchor.enabled = false;
                    // Ordinary MonoBehaviour Awake is not guaranteed for edit-mode instances.
                    typeof(AvatarMirrorAnchor).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Anchor, null);
                    SetPrivate(Anchor, "_stageCamera", Camera);
                    SetPrivate(Anchor, "_mirrorX", mirror);
                    Model = Object.Instantiate(prefab, _owner.transform);
                    Model.transform.localRotation = Quaternion.Euler(0f, 23f, 0f);
                    Animator = Model.GetComponentInChildren<Animator>();
                    Require(Animator != null && Animator.isHuman, "Model does not have a valid Humanoid Animator: " + path);
                    Animator.applyRootMotion = false;
                    Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    Solver = _owner.AddComponent<PoseMirrorRetargeter>();
                    Solver.BindAnimator(Animator);
                    Anchor.BindAnimator(Animator);
                    Mirror = mirror;
                }
                catch
                {
                    EditorSceneManager.ClosePreviewScene(_scene);
                    throw;
                }
            }

            public Transform Bone(HumanBodyBones id)
            {
                var bone = Animator.GetBoneTransform(id);
                Require(bone != null, "Required humanoid bone missing: " + id);
                return bone;
            }

            public PoseFrame Settle(Landmark[] points, int frames = 90)
            {
                PoseFrame frame = default;
                for (int i = 0; i < frames; i++) frame = Tick(points);
                return frame;
            }

            public PoseFrame Tick(Landmark[] points)
            {
                var frame = Frame(points, Now);
                TickFrame(frame, true, false);
                return frame;
            }

            public void TickFrame(PoseFrame frame, bool tracked, bool armed)
            {
                Now += Dt;
                StepMethod.Invoke(Solver, new object[] { frame, tracked, armed, Now, Dt });
            }

            public Vector3 Direction(PoseFrame frame, PoseLandmark from, PoseLandmark to)
            {
                if (Mirror) { from = PoseRetargetMath.SwapSide(from); to = PoseRetargetMath.SwapSide(to); }
                var a = frame.GetWorld(from);
                var b = frame.GetWorld(to);
                return PoseRetargetMath.MapDirection(new Vector3(b.X - a.X, b.Y - a.Y, b.Z - a.Z),
                    Mirror, Camera.transform.rotation);
            }

            public void CheckDirection(PoseFrame frame, HumanBodyBones parent, HumanBodyBones child,
                PoseLandmark from, PoseLandmark to, float tolerance)
            {
                float angle = Vector3.Angle(Bone(child).position - Bone(parent).position, Direction(frame, from, to));
                Require(angle < tolerance, $"{parent} direction error {angle:0.0} degrees (limit {tolerance:0}).");
            }

            public void CheckFinite()
            {
                foreach (Transform bone in Model.GetComponentsInChildren<Transform>(true))
                {
                    Vector3 position = bone.position;
                    Quaternion rotation = bone.rotation;
                    Require(Finite(position.x) && Finite(position.y) && Finite(position.z)
                        && Finite(rotation.x) && Finite(rotation.y) && Finite(rotation.z) && Finite(rotation.w),
                        "Invalid landmark produced a non-finite transform on " + bone.name + ".");
                }
            }

            public Texture2D Render(int width, int height)
            {
                // A preview scene has its own culling bit. Restrict the camera explicitly so
                // Main/Fight geometry cannot enter this render even if the user has either open.
                Camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(_scene);
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = new Color(0.075f, 0.10f, 0.15f);
                Camera.orthographic = true;
                Camera.aspect = (float)width / height;
                Camera.nearClipPlane = 0.03f;
                Camera.farClipPlane = 30f;
                Camera.allowHDR = false;
                AddLight("Key", Quaternion.Euler(35f, 135f, 0f), 1.15f, new Color(1f, 0.96f, 0.90f));
                AddLight("Fill", Quaternion.Euler(10f, -135f, 0f), 0.75f, new Color(0.85f, 0.92f, 1f));
                AddLight("Rim", Quaternion.Euler(25f, 0f, 0f), 0.7f, Color.white);

                bool found = false;
                Bounds bounds = default;
                foreach (var skin in Model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!skin.enabled || skin.sharedMesh == null) continue;
                    skin.updateWhenOffscreen = true;
                    var baked = new Mesh();
                    try
                    {
                        skin.BakeMesh(baked);
                        foreach (Vector3 vertex in baked.vertices)
                        {
                            Vector3 world = skin.transform.TransformPoint(vertex);
                            if (!found) { bounds = new Bounds(world, Vector3.zero); found = true; }
                            else bounds.Encapsulate(world);
                        }
                    }
                    finally { Object.DestroyImmediate(baked); }
                }
                Require(found && bounds.size.y > 0.1f, "No skinned geometry available for visual QA.");
                Camera.orthographicSize = Mathf.Max(bounds.extents.y, bounds.extents.x / Camera.aspect) * 1.12f;
                Camera.transform.SetPositionAndRotation(bounds.center + Vector3.forward * (bounds.extents.z + 5f),
                    Quaternion.Euler(0f, 180f, 0f));

                var target = new RenderTexture(width, height, 24) { antiAliasing = 4 };
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Camera.targetTexture = target;
                    Camera.Render();
                    RenderTexture.active = target;
                    var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                    image.Apply();
                    return image;
                }
                finally
                {
                    RenderTexture.active = previous;
                    Camera.targetTexture = null;
                    target.Release();
                    Object.DestroyImmediate(target);
                }
            }

            private void AddLight(string name, Quaternion rotation, float intensity, Color color)
            {
                var lightObject = new GameObject("Retarget QA " + name);
                lightObject.transform.SetParent(_owner.transform, false);
                lightObject.transform.rotation = rotation;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = intensity;
                light.color = color;
                light.shadows = LightShadows.None;
            }

            public void Dispose() => EditorSceneManager.ClosePreviewScene(_scene);
        }

        private static Landmark[] Skeleton(float yaw)
        {
            var points = new Landmark[PoseLandmarks.Count];
            for (int i = 0; i < points.Length; i++) points[i] = new Landmark(0f, -0.65f, 0f, 1f);
            Set(points, PoseLandmark.Nose, new Vector3(0f, -0.76f, -0.10f));
            Set(points, PoseLandmark.LeftEar, new Vector3(0.08f, -0.73f, 0f));
            Set(points, PoseLandmark.RightEar, new Vector3(-0.08f, -0.73f, 0f));
            foreach (bool left in new[] { true, false })
            {
                float side = left ? 1f : -1f;
                Set(points, left ? PoseLandmark.LeftShoulder : PoseLandmark.RightShoulder, new Vector3(side * 0.22f, -0.50f, 0f));
                Set(points, left ? PoseLandmark.LeftElbow : PoseLandmark.RightElbow, new Vector3(side * 0.32f, -0.22f, -0.02f));
                Set(points, left ? PoseLandmark.LeftWrist : PoseLandmark.RightWrist, new Vector3(side * 0.36f, 0.04f, -0.06f));
                Set(points, left ? PoseLandmark.LeftHip : PoseLandmark.RightHip, new Vector3(side * 0.14f, 0f, 0f));
                Set(points, left ? PoseLandmark.LeftKnee : PoseLandmark.RightKnee, new Vector3(side * 0.15f, 0.43f, -0.025f));
                Set(points, left ? PoseLandmark.LeftAnkle : PoseLandmark.RightAnkle, new Vector3(side * 0.16f, 0.84f, 0f));
                Set(points, left ? PoseLandmark.LeftHeel : PoseLandmark.RightHeel, new Vector3(side * 0.16f, 0.88f, 0.04f));
                Set(points, left ? PoseLandmark.LeftFootIndex : PoseLandmark.RightFootIndex, new Vector3(side * 0.16f, 0.89f, -0.14f));
                Vector3 wrist = ToVector(points[(int)(left ? PoseLandmark.LeftWrist : PoseLandmark.RightWrist)]);
                Set(points, left ? PoseLandmark.LeftIndex : PoseLandmark.RightIndex, wrist + new Vector3(side * 0.02f, 0.09f, -0.02f));
                Set(points, left ? PoseLandmark.LeftPinky : PoseLandmark.RightPinky, wrist + new Vector3(-side * 0.02f, 0.09f, 0.02f));
                Set(points, left ? PoseLandmark.LeftThumb : PoseLandmark.RightThumb, wrist + new Vector3(-side * 0.04f, 0.04f, -0.02f));
            }
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < points.Length; i++) Set(points, (PoseLandmark)i, turn * ToVector(points[i]));
            return points;
        }

        private static PoseFrame Frame(Landmark[] world, float time)
        {
            var image = new Landmark[PoseLandmarks.Count];
            for (int i = 0; i < image.Length; i++)
            {
                var point = world[i];
                image[i] = new Landmark(0.5f + point.X * 0.4f, 0.48f + point.Y * 0.4f,
                    point.Z * 0.4f, point.Visibility);
            }
            return new PoseFrame(image, world, time, 1f);
        }

        private static void Set(Landmark[] points, PoseLandmark id, Vector3 position)
            => points[(int)id] = new Landmark(position.x, position.y, position.z, 1f);
        private static Vector3 ToVector(Landmark point) => new Vector3(point.X, point.Y, point.Z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void SetPrivate(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Require(field != null, "Fixture binding field missing: " + name);
            field.SetValue(target, value);
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
