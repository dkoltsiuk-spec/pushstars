using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using PushStars.CV;
using PushStars.CV.AntiCheat;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// A player who stops mid-set — sits back, kneels up, stands — must lose the arm within half
    /// a second, so the avatar goes back to mirroring them instead of scrubbing its push-up clip
    /// off a resting pair of arms. Replays the real recordings (CVRecordings/clap) through the
    /// production <see cref="PushupSession"/> and <see cref="PushupAvatarDriver"/>; the rests
    /// that no recording holds yet are posed onto a real plank frame of the same recording.
    /// </summary>
    public static class RestPostureRegression
    {
        private const string Fixture = "CVRecordings/clap/1_clap_IMG_1158.pose.csv";
        private const string LiftFixture = "CVRecordings/clap/3_lift_no_clap_IMG_1159.pose.csv";
        private const string ControllerPath = "Assets/_Project/Art/Characters/Mixamo/AvatarOverlayTest.controller";
        /// <summary>Recording 1158: armed since 7.9 s and holding the plank; the first rep starts ≈ 10.8 s.</summary>
        private const float PlankSec = 10.5f;
        /// <summary>Recording 1158: the hands leave the floor for good (the owner stands up).</summary>
        private const float StandUpSec = 28.83f;
        private const float RestSeconds = 6f;
        private const float Clock0 = 100f; // live Time.time is never 0
        /// <summary>The monitor's hold plus a frame or two of detector rate.</summary>
        private const float DisarmWithinSec = CVConstants.PostureLeftHoldSec + 0.1f;

        [MenuItem("Tools/Push Stars/CV/Validate Rest Detection", priority = 361)]
        public static void Run()
        {
            var report = new StringBuilder("Rest posture regression — " + DateTime.UtcNow.ToString("u") + "\n");
            int passed = 0, failed = 0;
            Check("Working sets (5 claps; 4 lifts) are never taken for a rest", WorkingSetsKeepTheArm, report, ref passed, ref failed);
            Check("Standing up after the set disarms within the hold", StandingUpDisarms, report, ref passed, ref failed);
            Check("Sitting back, hands on the knees, arms flexing → disarmed, no reps, no flights", SittingBackDisarms, report, ref passed, ref failed);
            Check("Same rest at 15 fps", SittingBackDisarms15Fps, report, ref passed, ref failed);
            Check("Kneeling up with the hands still low → disarmed by the upright torso", KneelingUpDisarms, report, ref passed, ref failed);
            Check("Avatar holds its depth through the rest, plays no flight, then leaves the push-up clip", AvatarStopsPushingUp, report, ref passed, ref failed);
            report.AppendLine($"RESULT: {passed} passed, {failed} failed.");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/rest-posture-regression.txt", report.ToString());
            if (failed != 0) throw new InvalidOperationException($"Rest posture regression: {failed} failed. See Logs/rest-posture-regression.txt");
            Debug.Log($"[RestPostureRegression] PASS — {passed} cases. Logs/rest-posture-regression.txt");
        }

        private static void Check(string name, Func<string> test, StringBuilder report, ref int passed, ref int failed)
        {
            string error;
            try { error = test(); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
            if (error == null) { passed++; report.AppendLine("PASS  " + name); }
            else { failed++; report.AppendLine("FAIL  " + name + "\n      " + error); }
        }

        // ── Replay ───────────────────────────────────────────────────────────────────────────────

        private sealed class Replay
        {
            public int Reps, RepsAfterCut, FlightsAfterCut;
            /// <summary>Recording time of each disarm, with its reason.</summary>
            public readonly List<(float t, PlankRejectReason reason)> Disarms = new();
            public bool ArmedAtCut, ArmedAtEnd;
            public float LastRepSec = float.NegativeInfinity;
            // Avatar driver, when one was stepped.
            public float DepthAtCut, MaxDepthDriftWhileArmed, LeftPushupClipSec = float.NaN;
            public int AvatarFlightsAfterCut;

            public float PostureDisarmSec
            {
                get
                {
                    foreach (var (t, reason) in Disarms)
                        if (reason == PlankRejectReason.LeftPushupPose) return t;
                    return float.NaN;
                }
            }

            public override string ToString()
                => $"reps={Reps} afterCut(reps={RepsAfterCut} flights={FlightsAfterCut} avatarFlights={AvatarFlightsAfterCut}) " +
                   $"armedAtCut={ArmedAtCut} armedAtEnd={ArmedAtEnd} depthDrift={MaxDepthDriftWhileArmed:0.00} " +
                   $"leftClip={LeftPushupClipSec:0.00} disarms=[{string.Join("; ", Disarms.ConvertAll(d => $"{d.reason}@{d.t:0.00}"))}]";
        }

        /// <summary>Plays <paramref name="fixture"/> up to <paramref name="cutSec"/>, then — when
        /// <paramref name="rest"/> is given — <see cref="RestSeconds"/> of that pose built on the
        /// last real frame. Times in the result are recording seconds.</summary>
        private static Replay Play(string fixture = Fixture, float cutSec = float.PositiveInfinity,
            Func<Landmark[], float, float, Landmark[]> rest = null, int keepEvery = 1, bool withAvatar = false)
        {
            string path = RecordedPoseSource.ResolvePath(fixture);
            if (!File.Exists(path)) throw new FileNotFoundException("fixture missing: " + fixture);
            var frames = RecordedPoseSource.Read(path, out float aspect);

            // The recording up to the cut, then the posed rest at the recording's own rate.
            var timeline = new List<(float t, Landmark[] image, Landmark[] world)>();
            Landmark[] plank = null, plankWorld = null;
            foreach (var f in frames)
            {
                if (f.t > cutSec) break;
                timeline.Add(f);
                if (f.image != null) { plank = f.image; plankWorld = f.world; }
            }
            float cut = timeline[timeline.Count - 1].t;
            if (rest != null)
                for (float dt = 1f / 30f; dt <= RestSeconds; dt += 1f / 30f)
                    timeline.Add((cut + dt, rest(plank, aspect, dt), plankWorld));

            var go = new GameObject("RestRegression") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var r = new Replay();
                var s = go.AddComponent<PushupSession>();
                s.EnsureBuilt();
                float t = 0f;
                s.OnRep += _ => { r.Reps++; r.LastRepSec = t; if (t > cut) r.RepsAfterCut++; };
                s.Clap.OnFlightLanded += _ => { if (t > cut) r.FlightsAfterCut++; };
                s.Armer.OnDisarmed += reason => r.Disarms.Add((t, reason));

                PushupAvatarDriver driver = null;
                MethodInfo step = null;
                FieldInfo flightPhase = null;
                if (withAvatar)
                {
                    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
                    var animator = go.AddComponent<Animator>();
                    animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
                    driver = go.AddComponent<PushupAvatarDriver>();
                    driver.Configure(s, animator);
                    step = typeof(PushupAvatarDriver).GetMethod("Step", Private);
                    flightPhase = typeof(PushupAvatarDriver).GetField("_flightPhase", Private);
                }

                float previous = timeline[0].t, lastPhase = -1f;
                for (int i = 0; i < timeline.Count; i += keepEvery)
                {
                    var (ft, image, world) = timeline[i];
                    t = ft;
                    var frame = RecordedPoseSource.ToFrame(image, world, Clock0 + ft, aspect);
                    s.ProcessOffline(frame, image == null ? TrackingQuality.Lost : PoseQuality.Classify(frame), Clock0 + ft);
                    if (ft <= cut) r.ArmedAtCut = s.Armer.IsArmed;

                    if (driver == null) continue;
                    // Two display frames per pose frame: a 60 fps screen over a 30 fps detector.
                    float dt = Mathf.Max(0f, ft - previous) * .5f; previous = ft;
                    for (int sub = 0; sub < 2; sub++)
                    {
                        step.Invoke(driver, new object[] { dt });
                        float phase = (float)flightPhase.GetValue(driver);
                        if (ft > cut && phase >= 0f && lastPhase < 0f) r.AvatarFlightsAfterCut++;
                        lastPhase = phase;
                    }
                    if (ft <= cut) r.DepthAtCut = driver.SmoothedDepth;
                    else if (driver.Mode == PushupAvatarDriver.AvatarMode.Pushup)
                        r.MaxDepthDriftWhileArmed = Mathf.Max(r.MaxDepthDriftWhileArmed,
                            Mathf.Abs(driver.SmoothedDepth - r.DepthAtCut));
                    else if (float.IsNaN(r.LeftPushupClipSec)) r.LeftPushupClipSec = ft;
                }
                r.ArmedAtEnd = s.Armer.IsArmed;
                return r;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // ── Posed rests ──────────────────────────────────────────────────────────────────────────

        private static Vector2 Sq(Landmark[] lm, PoseLandmark p, float aspect)
            => PoseMath.ToSquare(lm[(int)p].Pos2D, aspect);

        private static void Put(Landmark[] lm, PoseLandmark p, Vector2 square, float aspect)
        {
            var o = lm[(int)p];
            lm[(int)p] = new Landmark(square.x / aspect, square.y, o.Z, o.Visibility);
        }

        private static void Move(Landmark[] lm, Vector2 square, float aspect, params PoseLandmark[] points)
        {
            foreach (var p in points) Put(lm, p, Sq(lm, p, aspect) + square, aspect);
        }

        private static float ShoulderWidth(Landmark[] lm, float aspect)
            => Vector2.Distance(Sq(lm, PoseLandmark.LeftShoulder, aspect), Sq(lm, PoseLandmark.RightShoulder, aspect));

        /// <summary>Sat back on the heels with the hands on the thighs, bending and straightening
        /// the arms every two seconds (shaking them out, wiping the face). The torso is left as
        /// the plank had it — the hands alone have to give this one away. Image Y points down.</summary>
        private static Landmark[] HandsOnKnees(Landmark[] plank, float aspect, float sinceCut)
        {
            var lm = (Landmark[])plank.Clone();
            float sw = ShoulderWidth(lm, aspect);
            float bend = Mathf.Lerp(170f, 75f, .5f - .5f * Mathf.Cos(sinceCut * Mathf.PI)); // elbow angle
            PoseArm(lm, aspect, sw, bend, PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist,
                PoseLandmark.LeftPinky, PoseLandmark.LeftIndex, PoseLandmark.LeftThumb);
            PoseArm(lm, aspect, sw, bend, PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist,
                PoseLandmark.RightPinky, PoseLandmark.RightIndex, PoseLandmark.RightThumb);
            return lm;
        }

        /// <summary>Upper arm hanging 0.55·sw down and a little out from the shoulder; the forearm,
        /// as long again, folds inward to the given elbow angle. Straight, the wrist ends up
        /// ≈ 0.55·sw above where the plank planted it; folded, more than a shoulder width.</summary>
        private static void PoseArm(Landmark[] lm, float aspect, float sw, float elbowDeg,
            PoseLandmark shoulder, PoseLandmark elbow, PoseLandmark wrist, params PoseLandmark[] hand)
        {
            Vector2 s = Sq(lm, shoulder, aspect), oldWrist = Sq(lm, wrist, aspect);
            float side = Mathf.Sign(oldWrist.x - s.x);
            Vector2 e = s + new Vector2(side * .2f, .52f) * sw;
            Vector2 toShoulder = (s - e).normalized;
            // Rotate the elbow→shoulder direction by the interior angle, toward the body's midline.
            float a = elbowDeg * Mathf.Deg2Rad * side;
            Vector2 forearm = new Vector2(toShoulder.x * Mathf.Cos(a) - toShoulder.y * Mathf.Sin(a),
                                          toShoulder.x * Mathf.Sin(a) + toShoulder.y * Mathf.Cos(a));
            Vector2 w = e + forearm * (.55f * sw);
            Put(lm, elbow, e, aspect);
            Move(lm, w - oldWrist, aspect, hand);
            Put(lm, wrist, w, aspect);
        }

        /// <summary>Knelt up: the torso comes upright (κ ≈ 1.45) while the arms still hang to
        /// about where the palms were planted. Nothing here lifts the wrists.</summary>
        private static Landmark[] KneelingUp(Landmark[] plank, float aspect, float sinceCut)
        {
            var lm = (Landmark[])plank.Clone();
            float sw = ShoulderWidth(lm, aspect);
            float kappa = (.5f * (lm[(int)PoseLandmark.LeftHip].Y + lm[(int)PoseLandmark.RightHip].Y)
                           - .5f * (lm[(int)PoseLandmark.LeftShoulder].Y + lm[(int)PoseLandmark.RightShoulder].Y)) / sw;
            var up = new Vector2(0f, -(1.45f - kappa) * sw);
            for (var p = PoseLandmark.Nose; p <= PoseLandmark.RightShoulder; p++) Move(lm, up, aspect, p);
            Move(lm, up * .5f, aspect, PoseLandmark.LeftElbow, PoseLandmark.RightElbow);
            return lm;
        }

        // ── Cases ────────────────────────────────────────────────────────────────────────────────

        private static string WorkingSetsKeepTheArm()
        {
            var claps = Play();
            float disarm = claps.PostureDisarmSec;
            // Flights are 0.2 s of lifted hands each; the only rest in the recording is its end.
            if (claps.Reps != 5 || (!float.IsNaN(disarm) && disarm < StandUpSec)) return "claps: " + claps;

            var lifts = Play(LiftFixture);
            disarm = lifts.PostureDisarmSec;
            return lifts.Reps == 4 && (float.IsNaN(disarm) || disarm > lifts.LastRepSec + 1f) ? null : "lifts: " + lifts;
        }

        private static string StandingUpDisarms()
        {
            var r = Play();
            float disarm = r.PostureDisarmSec;
            return disarm >= StandUpSec && disarm <= StandUpSec + DisarmWithinSec && !r.ArmedAtEnd ? null : r.ToString();
        }

        private static string Rested(Replay r, float cut)
        {
            float disarm = r.PostureDisarmSec;
            return r.ArmedAtCut && disarm > cut && disarm <= cut + DisarmWithinSec
                   && !r.ArmedAtEnd && r.RepsAfterCut == 0 && r.FlightsAfterCut == 0 ? null : r.ToString();
        }

        private static string SittingBackDisarms() => Rested(Play(cutSec: PlankSec, rest: HandsOnKnees), PlankSec);

        private static string SittingBackDisarms15Fps()
        {
            var r = Play(cutSec: PlankSec, rest: HandsOnKnees, keepEvery: 2);
            float disarm = r.PostureDisarmSec;
            // One extra detector frame of latency at half the rate.
            return r.ArmedAtCut && disarm > PlankSec && disarm <= PlankSec + DisarmWithinSec + 1f / 15f
                   && !r.ArmedAtEnd && r.RepsAfterCut == 0 && r.FlightsAfterCut == 0 ? null : r.ToString();
        }

        private static string KneelingUpDisarms() => Rested(Play(cutSec: PlankSec, rest: KneelingUp), PlankSec);

        private static string AvatarStopsPushingUp()
        {
            var r = Play(cutSec: PlankSec, rest: HandsOnKnees, withAvatar: true);
            // The arms swing the elbow angle through the whole rep range; none of it may reach
            // the clip. 0.05 of the depth scale is the smoothing settling on the held value.
            return r.MaxDepthDriftWhileArmed <= .05f && r.AvatarFlightsAfterCut == 0
                   && r.LeftPushupClipSec <= PlankSec + DisarmWithinSec + 1f / 30f ? null : r.ToString();
        }
    }
}
