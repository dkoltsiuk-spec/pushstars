using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PushStars.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Removes "false push-ups" from a recorded bot: stretches of the live phase where the avatar
    /// goes down, comes up or claps but no repetition was counted (a rejected rep, a clap the
    /// counter did not accept, a half-rep). A bot that visibly works without its score moving reads
    /// as a broken counter, so those stretches are replaced by a held plank.
    ///
    /// <para>The clock is untouched — same frames, same timestamps, same rep times — only the poses
    /// inside a false stretch change. Kept as recorded: every cycle that ends in a counted rep
    /// (from the top before its descent to the rep), a clap flight that follows a counted rep, and
    /// rest (hands off the floor for seconds: the owner sat or stood up).</para>
    ///
    /// <para>What a frame is doing is measured on the man's rig, not guessed from muscle numbers:
    /// elbow straightness, and how far the hands are off the floor relative to the feet.</para>
    /// </summary>
    public static class BotRecordingCleaner
    {
        private const string RigPrefab = "Assets/Character/Main_man/MainMan.prefab";
        public const string Folder = "tools/BotRecordings";

        private const float TopElbow = .85f;          // forearm stretch at the top of the plank
        private const float AirborneLift = .05f;      // metres the hands are above their planted height
        private const float RestSeconds = 2f;         // hands up this long = resting, not a clap
        private const float Blend = .3f;              // seconds to ease into and out of the held plank

        [MenuItem("Tools/Push Stars/Bots/Clean Recordings (tools/BotRecordings/originals → cleaned)")]
        public static void CleanAll() => Debug.Log("[BotCleaner]\n" + CleanFolder());

        public static string CleanFolder()
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(Folder + "/cleaned");
            foreach (string file in Directory.GetFiles(Folder + "/originals", "*.json"))
                log.AppendLine(CleanFile(file, Folder + "/cleaned/" + Path.GetFileName(file)));
            return log.ToString();
        }

        public static string CleanFile(string input, string output)
        {
            var recording = JsonUtility.FromJson<BotRecording>(File.ReadAllText(input));
            var clip = GhostMotionClip.Decode(recording.fight.motionBase64);
            string report = Clean(clip, recording.fight.repTimes);
            recording.fight.motionBase64 = clip.Encode();
            recording.cloudPath = "";
            File.WriteAllText(output, JsonUtility.ToJson(recording));
            return $"{recording.id.Substring(0, 8)} ({recording.fight.reps} reps): {report}";
        }

        /// <summary>Edits the clip in place; returns what was replaced.</summary>
        public static string Clean(GhostMotionClip clip, IReadOnlyList<float> repTimes)
        {
            var frames = clip.Frames;
            int first = frames.FindIndex(f => f.phase == GhostMotionClip.Live);
            if (first < 0) return "no live phase";
            int last = frames.FindLastIndex(f => f.phase == GhostMotionClip.Live);
            int n = last - first + 1;
            Measure(frames, first, n, out var elbow, out var lift);
            float[] time = Enumerable.Range(first, n).Select(i => frames[i].time).ToArray();

            // Planted hand height = where the hands spend most of the take.
            var sorted = (float[])lift.Clone(); Array.Sort(sorted);
            float planted = sorted[n / 2];
            var airborne = lift.Select(l => l - planted > AirborneLift).ToArray();

            // Rest: the hands stay up for seconds (sat or stood up).
            var rest = new bool[n];
            for (int i = 0; i < n;)
            {
                if (!airborne[i]) { i++; continue; }
                int j = i; while (j + 1 < n && airborne[j + 1]) j++;
                if (time[j] - time[i] >= RestSeconds) for (int k = i; k <= j; k++) rest[k] = true;
                i = j + 1;
            }
            bool Top(int i) => elbow[i] >= TopElbow && !airborne[i] && !rest[i];

            // Keep every cycle that ends in a counted rep, and a clap flight right after it.
            var keep = new bool[n];
            foreach (float rep in repTimes)
            {
                int r = Array.FindLastIndex(time, t => t <= rep);
                if (r < 0) continue;
                int s = r;
                while (s > 0 && Top(s) && rep - time[s] < .5f) s--;      // back off the top the rep latched on
                if (Top(s)) continue;                                     // counted with no visible dip
                while (s > 0 && !Top(s) && !rest[s]) s--;                 // through the dip to the top before it
                int e = r;
                while (e + 1 < n && !Top(e) && !rest[e + 1] && time[e] - rep < .8f) e++;   // finish the ascent
                for (int k = e; k < n && time[k] - time[e] < .6f; k++)                      // a clap flight right after
                    if (airborne[k] && !rest[k])
                    {
                        e = k;
                        while (e + 1 < n && !Top(e) && !rest[e + 1] && time[e] - rep < 2f) e++;
                        break;
                    }
                for (int k = s; k <= e; k++) keep[k] = true;
            }

            // False = moving, not kept, not resting. Runs split only by a breath of plank are one run.
            var bad = new bool[n];
            for (int i = 0; i < n; i++) bad[i] = !keep[i] && !rest[i] && !Top(i);
            for (int i = 0; i < n;)
            {
                if (bad[i] || keep[i] || rest[i]) { i++; continue; }
                int j = i; while (j + 1 < n && !bad[j + 1] && !keep[j + 1] && !rest[j + 1]) j++;
                if (i > 0 && j + 1 < n && bad[i - 1] && bad[j + 1] && time[j] - time[i] < .6f)
                    for (int k = i; k <= j; k++) bad[k] = true;
                i = j + 1;
            }

            int referenceTop = Enumerable.Range(0, n).FirstOrDefault(Top);
            var report = new List<string>();
            float removed = 0f;
            for (int i = 0; i < n;)
            {
                if (!bad[i]) { i++; continue; }
                int j = i; while (j + 1 < n && bad[j + 1]) j++;
                if (time[j] - time[i] >= .15f)
                {
                    int a = Mathf.Max(0, i - 1), b = Mathf.Min(n - 1, j + 1);
                    // Hold the nearest real plank pose; ease from the frame before and into the frame after.
                    int hold = Top(a) ? a : Top(b) ? b : referenceTop;
                    var from = Copy(frames[first + a]); var to = Copy(frames[first + b]); var held = Copy(frames[first + hold]);
                    for (int k = i; k <= j; k++)
                    {
                        // A stretch that runs to the edge of the take has nothing to ease into there.
                        float inU = i == 0 ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time[k] - time[a]) / Blend));
                        float outU = j == n - 1 ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((time[b] - time[k]) / Blend));
                        var f = frames[first + k];
                        Mix(f, from, held, inU);
                        if (outU < 1f) Mix(f, to, Snapshot(f), outU);
                    }
                    report.Add($"{time[i]:0.0}–{time[j]:0.0} s");
                    removed += time[j] - time[i];
                }
                i = j + 1;
            }
            return report.Count == 0 ? "nothing to remove"
                : $"replaced {report.Count} stretch(es), {removed:0.0} s in all, with a held plank: {string.Join(", ", report)}";
        }

        private sealed class Pose
        {
            public Vector3 body, root; public Quaternion bodyRot, rootRot; public float[] muscles;
        }

        private static Pose Copy(GhostMotionClip.Frame f) => new Pose
        { body = f.bodyPosition, root = f.rootPosition, bodyRot = f.bodyRotation, rootRot = f.rootRotation, muscles = (float[])f.muscles.Clone() };

        private static Pose Snapshot(GhostMotionClip.Frame f) => Copy(f);

        /// <summary>Writes <c>lerp(a, b, u)</c> into the frame.</summary>
        private static void Mix(GhostMotionClip.Frame f, Pose a, Pose b, float u)
        {
            f.bodyPosition = Vector3.Lerp(a.body, b.body, u);
            f.rootPosition = Vector3.Lerp(a.root, b.root, u);
            f.bodyRotation = Quaternion.Slerp(a.bodyRot, b.bodyRot, u);
            f.rootRotation = Quaternion.Slerp(a.rootRot, b.rootRot, u);
            var muscles = new float[a.muscles.Length];
            for (int m = 0; m < muscles.Length; m++) muscles[m] = Mathf.Lerp(a.muscles[m], b.muscles[m], u);
            f.muscles = muscles;
        }

        private static void Measure(List<GhostMotionClip.Frame> frames, int first, int count, out float[] elbow, out float[] lift)
        {
            elbow = new float[count]; lift = new float[count];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefab));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var animator = go.GetComponentInChildren<Animator>();
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            try
            {
                int leftElbow = Array.IndexOf(HumanTrait.MuscleName, "Left Forearm Stretch");
                int rightElbow = Array.IndexOf(HumanTrait.MuscleName, "Right Forearm Stretch");
                var pose = new HumanPose();
                for (int i = 0; i < count; i++)
                {
                    var f = frames[first + i];
                    pose.muscles = f.muscles; pose.bodyPosition = f.bodyPosition; pose.bodyRotation = f.bodyRotation;
                    handler.SetHumanPose(ref pose);
                    elbow[i] = (f.muscles[leftElbow] + f.muscles[rightElbow]) * .5f;
                    float hands = (animator.GetBoneTransform(HumanBodyBones.LeftHand).position.y
                                   + animator.GetBoneTransform(HumanBodyBones.RightHand).position.y) * .5f;
                    float feet = (animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y
                                  + animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y) * .5f;
                    lift[i] = hands - feet;
                }
            }
            finally
            {
                handler.Dispose();
                Object.DestroyImmediate(go);
            }
        }
    }
}
