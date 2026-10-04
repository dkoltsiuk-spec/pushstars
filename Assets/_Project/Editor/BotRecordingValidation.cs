using System;
using System.IO;
using PushStars.Core;
using PushStars.Fight;
using PushStars.CV;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class BotRecordingValidation
    {
        [MenuItem("Tools/Push Stars/Bots/Validate Recording")]
        public static void Run()
        {
            var clip = new GhostMotionClip();
            Add(0, 0, .1f); Add(0, 2, .2f);
            Add(2, 0, 0); Add(2, 2, 1); Add(2, 3, 0); Add(2, 17, 0); Add(2, 18, 1); Add(2, 19, 0); Add(2, 60, 0);
            var decoded = GhostMotionClip.Decode(clip.Encode());
            Require(decoded.Frames.Count == clip.Frames.Count, "Frame count roundtrip");
            HumanPose pose = default;
            Require(decoded.Sample(2, 10, ref pose, out _, out _) && pose.muscles[0] == 0, "Long pause is not stretched into a rep");
            Require(decoded.Sample(2, 1, ref pose, out _, out _) && Mathf.Abs(pose.muscles[0] - .5f) < .001f, "Interpolation uses original seconds");
            Require(decoded.Sample(0, 1, ref pose, out _, out _) && Mathf.Abs(pose.muscles[0] - .15f) < .001f, "Preparation has an independent clock");
            var go = new GameObject("GhostValidation");
            try
            {
                var ghost = go.AddComponent<GhostOpponent>();
                var record = GhostRecord.From(new[] { 2f, 3f, 17f, 19f }, 90, "validation"); record.motionBase64 = clip.Encode();
                Require(ghost.Configure(record), "Recorded opponent loads"); ghost.Begin();
                ghost.Tick(3.1f); Require(ghost.Reps == 2, "First burst scores on time");
                ghost.Tick(16.9f); Require(ghost.Reps == 2, "Pause has no fabricated reps");
                ghost.Tick(19.1f); Require(ghost.Reps == 4, "Second burst scores on time");
                ghost.Tick(60); Require(ghost.Reps == 4, "No reps spread into remaining seconds");
                record.motionBase64 = ""; Require(ghost.Configure(record), "Legacy ghost still loads");
                ghost.Begin(); ghost.Tick(10); Require(ghost.Depth01 == 0, "Legacy pause stays at top");
                FightRequest.ReplayBot(new BotRecording { id = Guid.NewGuid().ToString("N"), fight = new GhostRecord { reps = 1, repTimes = new[] { 2f }, motionBase64 = clip.Encode() } });
                Require(FightRequest.IsBotTest, "Test is isolated"); FightRequest.Ghost(); Require(!FightRequest.IsBotTest, "Next normal match clears test state");
            }
            finally { Object.DestroyImmediate(go); FightRequest.Clear(); }
            ValidateHumanoid();
            Directory.CreateDirectory("output/bot-recording");
            File.WriteAllText("output/bot-recording/validation.txt", "PASS: pose codec, irregular timing, pauses, phase clocks, legacy replay, isolated test request, Humanoid cross-stage roundtrip and recorded-pose framing. Device recording still requires phone verification.\n");
            Debug.Log("[BotRecordingValidation] PASS");
            void Add(byte phase, float time, float value)
            {
                var frame = new GhostMotionClip.Frame { phase = phase, time = time, bodyRotation = Quaternion.identity,
                    rootRotation = Quaternion.identity, muscles = new float[GhostMotionClip.MuscleCount] };
                frame.muscles[0] = value; clip.Frames.Add(frame);
            }
        }
        private static void ValidateHumanoid()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Main_man/MainMan.prefab");
            Require(prefab != null, "Humanoid test fixture exists");
            var sourceStage = new GameObject("SourceStage"); var replayStage = new GameObject("ReplayStage");
            try
            {
                sourceStage.transform.position = new Vector3(100, 0, 0);
                replayStage.transform.position = new Vector3(300, 0, 0);
                sourceStage.transform.rotation = Quaternion.Euler(0, 30, 0);
                replayStage.transform.rotation = Quaternion.Euler(0, -20, 0);
                var source = Object.Instantiate(prefab, sourceStage.transform).GetComponentInChildren<Animator>();
                var replay = Object.Instantiate(prefab, replayStage.transform).GetComponentInChildren<Animator>();
                using var reader = new HumanPoseHandler(source.avatar, source.transform);
                using var writer = new HumanPoseHandler(replay.avatar, replay.transform);
                var correction = PushupPoseCorrection.Bind(source);
                var ghost = replayStage.AddComponent<GhostOpponent>();
                GhostPosePlayback.Attach(replay, ghost);
                var playback = replay.GetComponent<GhostPosePlayback>();
                foreach (float depth in new[] { 0f, .5f, 1f })
                {
                    correction.Apply(depth);
                    HumanPose input = default, output = default;
                    var clip = new GhostMotionClip(); clip.Frames.Add(FightPoseRecorder.CaptureFrame(source, reader, ref input, 2, 0));
                    clip = GhostMotionClip.Decode(clip.Encode());
                    Require(clip.Sample(2, 0, ref output, out var p, out var q), "Humanoid clip sample");
                    replay.transform.localPosition = p; replay.transform.localRotation = q; writer.SetHumanPose(ref output);
                    var record = GhostRecord.From(new[] { 1f }, 90, "validation"); record.motionBase64 = clip.Encode();
                    ghost.Configure(record); ghost.Begin();
                    typeof(GhostPosePlayback).GetMethod("LateUpdate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(playback, null);
                    Require(playback.UsesPushupFraming, "Recorded pushup uses fixed framing");
                    foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.LeftFoot })
                    {
                        var expected = source.transform.InverseTransformPoint(source.GetBoneTransform(bone).position);
                        var actual = replay.transform.InverseTransformPoint(replay.GetBoneTransform(bone).position);
                        float error = Vector3.Distance(expected, actual) * source.transform.lossyScale.x;
                        Require(error < .04f, $"Humanoid {bone} at depth {depth}: error {error:0.000} m");
                    }
                }
            }
            finally { Object.DestroyImmediate(sourceStage); Object.DestroyImmediate(replayStage); }
        }
        private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    }
}
