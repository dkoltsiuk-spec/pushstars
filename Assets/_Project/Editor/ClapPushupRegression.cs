using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PushStars.Core;
using PushStars.CV;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Replays real clap push-up recordings (tools/cv/dump_pose.py → CVRecordings/clap/*.pose.csv)
    /// through the production <see cref="PushupSession"/> chain, frame by frame with the recording
    /// clock, and checks rep counting, clap tagging and the boss's x2 damage.
    /// </summary>
    public static class ClapPushupRegression
    {
        private const string Fixture = "CVRecordings/clap/1_clap_IMG_1158.pose.csv";
        /// <summary>Four explosive lifts, hands off the floor, no clap.</summary>
        private const string LiftFixture = "CVRecordings/clap/3_lift_no_clap_IMG_1159.pose.csv";
        /// <summary>Cheats: slapping a forearm in plank, claps and floor slaps while kneeling.</summary>
        private const string FakesFixture = "CVRecordings/clap/5_fakes_IMG_1160.pose.csv";
        private const int FixtureClaps = 5;

        [MenuItem("Tools/Push Stars/CV/Validate Clap Push-ups", priority = 360)]
        public static void Run()
        {
            var report = new StringBuilder("Clap push-up regression — " + DateTime.UtcNow.ToString("u") + "\n");
            int passed = 0, failed = 0;
            Check("5 clap push-ups at 30 fps → 5 reps, 5 claps, landing dips absorbed, no vetoes", Recorded30Fps, report, ref passed, ref failed);
            Check("At 15 fps (degraded pose rate) ≥ 4 of 5 claps are still recognised", Recorded15Fps, report, ref passed, ref failed);
            Check("Same flights with the hands kept apart → reps count, no clap", LiftWithoutClap, report, ref passed, ref failed);
            Check("Recorded explosive lifts without a clap → flights, no clap", RecordedLiftsNoClap, report, ref passed, ref failed);
            Check("Recorded fakes (forearm slaps in plank, claps on knees) → no reps, no claps", RecordedFakes, report, ref passed, ref failed);
            Check("Standing up after the set is not a push-up flight", StandUpIsNotAFlight, report, ref passed, ref failed);
            Check("Clap strike deals the rep's damage again and is reported separately", BossDoubleDamage, report, ref passed, ref failed);
            report.AppendLine($"RESULT: {passed} passed, {failed} failed.");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/clap-pushup-regression.txt", report.ToString());
            if (failed != 0) throw new InvalidOperationException($"Clap push-up regression: {failed} failed. See Logs/clap-pushup-regression.txt");
            Debug.Log($"[ClapPushupRegression] PASS — {passed} cases. Logs/clap-pushup-regression.txt");
        }

        private static void Check(string name, Func<string> test, StringBuilder report, ref int passed, ref int failed)
        {
            string error;
            try { error = test(); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
            if (error == null) { passed++; report.AppendLine("PASS  " + name); }
            else { failed++; report.AppendLine("FAIL  " + name + "\n      " + error); }
        }

        private sealed class Replay
        {
            public int Reps, Claps, Vetoes, Absorbed;
            public readonly List<ClapFlight> Flights = new();
            public string Log = "";
            public override string ToString()
                => $"reps={Reps} claps={Claps} vetoes={Vetoes} absorbed={Absorbed} flights=[{string.Join("; ", Flights)}]";
        }

        private static Replay Play(int keepEvery = 1,
            Func<float, Landmark[], Landmark[]> mutate = null, string fixture = Fixture)
        {
            string path = RecordedPoseSource.ResolvePath(fixture);
            if (!File.Exists(path)) throw new FileNotFoundException("fixture missing: " + fixture);
            var frames = RecordedPoseSource.Read(path, out float aspect);
            var go = new GameObject("ClapRegression") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var r = new Replay();
                var s = go.AddComponent<PushupSession>();
                s.EnsureBuilt();
                s.OnRep += _ => r.Reps++;
                s.OnClapRep += _ => r.Claps++;
                s.OnRepRejected += v => { r.Vetoes++; r.Log += v + " "; };
                s.Counter.OnArcAbsorbed += () => r.Absorbed++;
                s.Clap.OnFlightLanded += f => r.Flights.Add(f);
                const float clock0 = 100f; // live Time.time is never 0
                for (int i = 0; i < frames.Count; i += keepEvery)
                {
                    var (t, image, world) = frames[i];
                    if (image != null && mutate != null) image = mutate(t, image);
                    var frame = RecordedPoseSource.ToFrame(image, world, clock0 + t, aspect);
                    var q = image == null ? TrackingQuality.Lost : PoseQuality.Classify(frame);
                    s.ProcessOffline(frame, q, clock0 + t);
                }
                return r;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static string Recorded30Fps()
        {
            var r = Play();
            return r.Reps == FixtureClaps && r.Claps == FixtureClaps && r.Vetoes == 0
                   && r.Absorbed == FixtureClaps ? null : r + " " + r.Log;
        }

        private static string Recorded15Fps()
        {
            var r = Play(keepEvery: 2);
            int claps = r.Flights.FindAll(f => f.IsClap).Count;
            // At 15 fps the frame where the palms touch can fall between samples; the closest
            // sampled gap (≈0.19) is then indistinguishable from a lift without a clap (≥0.21),
            // so one miss in five is the honest floor. The device runs 30 fps (5/5 above).
            return claps >= FixtureClaps - 1 && r.Flights.Count == FixtureClaps ? null : r.ToString();
        }

        private static string LiftWithoutClap()
        {
            // Slide each whole hand sideways so its wrist keeps its planted X: same lift, same
            // timing, the hands never meet.
            float lx = float.NaN, rx = float.NaN;
            var r = Play(mutate: (t, lm) =>
            {
                var copy = (Landmark[])lm.Clone();
                int l = (int)PoseLandmark.LeftWrist, rr = (int)PoseLandmark.RightWrist;
                if (t < 11f) { lx = lm[l].X; rx = lm[rr].X; return copy; }
                Shift(copy, lx - lm[l].X, PoseLandmark.LeftWrist, PoseLandmark.LeftPinky,
                    PoseLandmark.LeftIndex, PoseLandmark.LeftThumb);
                Shift(copy, rx - lm[rr].X, PoseLandmark.RightWrist, PoseLandmark.RightPinky,
                    PoseLandmark.RightIndex, PoseLandmark.RightThumb);
                return copy;
            });
            int flights = r.Flights.FindAll(f => f.TakeoffSec < 128f).Count;
            return r.Reps == FixtureClaps && r.Claps == 0 && flights == FixtureClaps
                   && r.Flights.TrueForAll(f => !f.IsClap) ? null : r.ToString();
        }

        private static void Shift(Landmark[] lm, float dx, params PoseLandmark[] points)
        {
            foreach (var p in points)
            {
                var o = lm[(int)p];
                lm[(int)p] = new Landmark(o.X + dx, o.Y, o.Z, o.Visibility);
            }
        }

        private static string RecordedLiftsNoClap()
        {
            var r = Play(fixture: LiftFixture);
            // Rep crediting is the counter's business (its knee gate vetoes one borderline rep
            // here, with or without the clap logic); the detector must see flights and no clap.
            return r.Claps == 0 && r.Flights.Count >= 4 && r.Flights.TrueForAll(f => !f.IsClap)
                   && r.Reps >= 3 ? null : r.ToString();
        }

        private static string RecordedFakes()
        {
            var r = Play(fixture: FakesFixture);
            return r.Reps == 0 && r.Claps == 0 ? null : r.ToString();
        }

        private static string StandUpIsNotAFlight()
        {
            var r = Play();
            // The set ends ~27.5 s; after it the owner stands and walks to the phone.
            return r.Flights.TrueForAll(f => f.TakeoffSec < 100f + 28f) ? null : r.ToString();
        }

        private static string BossDoubleDamage()
        {
            var boss = new BossCombatState(BossCatalog.Current.Id);
            int hp0 = boss.BossHp, strikes = 0, damaged = 0;
            boss.Damaged += (toBoss, _) => { if (toBoss) damaged++; };
            boss.ClapStrike += _ => strikes++;
            boss.PlayerRep(100f);
            boss.PlayerClapStrike(100f);
            int expected = 2 * BossCombatState.RepDamage(100f);
            return hp0 - boss.BossHp == Mathf.Min(expected, hp0) && strikes == 1 && damaged == 2
                ? null : $"hp {hp0}→{boss.BossHp} strikes={strikes} damaged={damaged}";
        }
    }
}
