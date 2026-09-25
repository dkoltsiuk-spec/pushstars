using System;
using System.Collections.Generic;
using PushStars.CV;
using PushStars.CV.AntiCheat;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    public static class PushupDepthRegression
    {
        [MenuItem("Push Stars/Validation/Pushup Depth")]
        public static void Run()
        {
            var failures = new List<string>();
            int checks = 0;
            foreach (int fps in new[] { 15, 30, 60 })
            foreach (float duration in new[] { 0.6f, 1.2f, 2.4f })
            foreach (float bottom in new[] { 135f, 118f, 110f, 100f, 96f, 95f, 90f })
            {
                int reps = CountTrace(fps, duration, bottom, false);
                Check(reps == (bottom <= 95f ? 1 : 0),
                    $"{fps}fps/{duration}s/{bottom}deg counted {reps}", failures, ref checks);
            }

            // A short dropout near a shallow trough must not invent a deeper bottom.
            Check(CountTrace(30, 1.2f, 98f, true) == 0,
                "Tracking-gap recovery credited an unobserved bottom", failures, ref checks);

            foreach (ViewKind view in new[] { ViewKind.Frontal, ViewKind.Side, ViewKind.Unknown })
            {
                CheckRom(view, 0.20f, true, RepVoteKind.HardVeto, failures, ref checks);
                CheckRom(view, 0.50f, true, RepVoteKind.Pass, failures, ref checks);
            }
            // Frontal hips can disappear behind the chest; shoulders still give a valid scale.
            CheckRom(ViewKind.Frontal, 0.20f, false, RepVoteKind.HardVeto, failures, ref checks);
            CheckRom(ViewKind.Frontal, 0.50f, false, RepVoteKind.Pass, failures, ref checks);
            var gate = new FullRomGate();
            Check(gate.Validate(new RepWindow(Array.Empty<RepSample>(), 0, ViewKind.Frontal)).Kind
                == RepVoteKind.HardVeto, "Empty depth evidence passed", failures, ref checks);
            Check(gate.Validate(new RepWindow(new RepSample[8], 8, ViewKind.Frontal)).Kind
                == RepVoteKind.HardVeto, "Invisible shoulders passed", failures, ref checks);

            // Exercise the audit seam as well as the latch: noisy elbow estimates must not
            // turn a 40%-of-full chest dip (0.20 vs 0.50 body scale) into a credited rep.
            Check(CountAuditedTrace(0.20f, false) == 0,
                "40% chest dip counted when hips were hidden", failures, ref checks);
            Check(CountAuditedTrace(0.50f, false) == 1,
                "Full frontal rep with hidden hips was lost", failures, ref checks);

            if (failures.Count > 0)
                throw new InvalidOperationException($"Pushup depth: {failures.Count}/{checks} failed:\n"
                    + string.Join("\n", failures));
            Debug.Log($"PASS: {checks} pushup depth checks (partial/full, fast/slow, 15/30/60fps, tracking gap, hidden hips, audit seam).");
        }

        private static int CountTrace(int fps, float duration, float bottom, bool gapAtBottom)
        {
            var tracker = new AmplitudeTracker();
            var counter = new PushupRepCounter(tracker);
            int steps = Mathf.Max(4, Mathf.RoundToInt(duration * fps / 2f));
            float time = 0f;
            for (int i = -5; i <= 2 * steps + 5; i++)
            {
                float depth = i < 0 || i > 2 * steps ? 0f
                    : 1f - Mathf.Abs(i - steps) / (float)steps;
                float angle = Mathf.Lerp(165f, bottom, depth);
                bool valid = !gapAtBottom || i != steps;
                var frame = Frame(time, 0.1f * depth, true);
                tracker.Tick(frame, valid, true, time, valid, angle, true);
                counter.Process(frame, valid, time);
                time += 1f / fps;
            }
            return counter.Reps;
        }

        private static void CheckRom(ViewKind view, float travel, bool hips, RepVoteKind expected,
            List<string> failures, ref int checks)
        {
            var samples = new RepSample[21];
            for (int i = 0; i < samples.Length; i++)
            {
                float depth = i < 5 ? 0f : 1f - Mathf.Abs(i - 12) / 8f;
                var frame = Frame(i / 30f, travel * 0.2f * depth, hips, view);
                samples[i] = RepSample.From(frame, i < 5 ? PushupPhase.Top : PushupPhase.Bottom,
                    90f, 90f, 90f, 90f);
            }
            var vote = new FullRomGate().Validate(new RepWindow(samples, samples.Length, view));
            Check(vote.Kind == expected, $"ROM {view}/{travel}/hips={hips}: {vote}", failures, ref checks);
        }

        private static int CountAuditedTrace(float travel, bool hips)
        {
            var tracker = new AmplitudeTracker();
            var counter = new PushupRepCounter(tracker);
            var samples = new List<RepSample>();
            counter.RepAuditor = () => new FullRomGate().Validate(
                new RepWindow(samples.ToArray(), samples.Count, ViewKind.Frontal));
            for (int i = -5; i <= 41; i++)
            {
                float depth = i < 0 || i > 36 ? 0f : 1f - Mathf.Abs(i - 18) / 18f;
                float angle = Mathf.Lerp(165f, 90f, depth);
                float time = (i + 5) / 30f;
                var frame = Frame(time, travel * 0.2f * depth, hips);
                tracker.Tick(frame, true, true, time, true, angle, true);
                samples.Add(RepSample.From(frame, counter.Phase, angle, angle,
                    tracker.SmoothedElbowDeg, tracker.MedianElbowDeg));
                counter.Process(frame, true, time);
            }
            return counter.Reps;
        }

        private static PoseFrame Frame(float time, float drop, bool hips, ViewKind view = ViewKind.Frontal)
        {
            var points = new Landmark[PoseLandmarks.Count];
            points[(int)PoseLandmark.LeftShoulder] = new Landmark(.4f, .4f + drop, 0f, 1f);
            points[(int)PoseLandmark.RightShoulder] = new Landmark(.6f, .4f + drop, 0f, 1f);
            float hipX = view == ViewKind.Side ? .7f : .5f;
            float hipY = view == ViewKind.Side ? .4f : .35f;
            points[(int)PoseLandmark.LeftHip] = new Landmark(hipX - .05f, hipY + drop, 0f, hips ? 1f : 0f);
            points[(int)PoseLandmark.RightHip] = new Landmark(hipX + .05f, hipY + drop, 0f, hips ? 1f : 0f);
            return new PoseFrame(points, time);
        }

        private static void Check(bool passed, string message, List<string> failures, ref int checks)
        {
            checks++;
            if (!passed) failures.Add(message);
        }
    }
}
