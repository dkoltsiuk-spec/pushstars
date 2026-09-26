using System.Text;
using PushStars.CV;
using PushStars.CV.AntiCheat;
using UnityEngine;

/// <summary>Editor trace tool (run via `unity command run_script`, see tools/cv/dump_pose.py):
/// replays a *.pose.csv through PushupSession and prints reps/vetoes/flights, plus a per-frame
/// trace between traceFrom and traceTo (recording seconds). keepEvery=2 simulates 15 fps.</summary>
public static class ReplayPose
{
    public static string Run(string path, float traceFrom = -1f, float traceTo = -1f, int keepEvery = 1, bool noAbsorb = false)
    {
        var frames = RecordedPoseSource.Read(RecordedPoseSource.ResolvePath(path), out float aspect);
        var go = new GameObject("ReplayPose") { hideFlags = HideFlags.HideAndDontSave };
        var sb = new StringBuilder();
        try
        {
            var s = go.AddComponent<PushupSession>();
            float t = 0f;
            s.EnsureBuilt();
            if (noAbsorb) s.Counter.ArcAbsorber = null; // A/B against the pre-clap counter
            s.OnRep += n => sb.AppendLine($"  REP {n} @ {t:0.00}");
            s.OnRepRejected += v => sb.AppendLine($"  VETO {v} @ {t:0.00}");
            s.Armer.OnArmed += () => sb.AppendLine($"  ARMED @ {t:0.00}");
            s.Armer.OnDisarmed += r => sb.AppendLine($"  DISARMED {r} @ {t:0.00}");
            s.Clap.OnFlightLanded += f => sb.AppendLine($"  {f} (rec {t:0.00})");
            s.OnClapRep += n => sb.AppendLine($"  >>> CLAP REP {n} @ {t:0.00}");
            s.Counter.OnArcAbsorbed += () => sb.AppendLine($"  absorbed landing arc @ {t:0.00}");
            const float clock0 = 100f; // session clock offset (Time.time is never 0 live)
            int idx = 0;
            foreach (var (ft, image, world) in frames)
            {
                if (idx++ % keepEvery != 0) continue; // simulate a slower pose rate
                t = ft;
                var frame = RecordedPoseSource.ToFrame(image, world, clock0 + ft, aspect);
                var q = image == null ? TrackingQuality.Lost : PoseQuality.Classify(frame);
                s.ProcessOffline(frame, q, clock0 + ft);
                if (ft >= traceFrom && ft <= traceTo)
                    sb.AppendLine($"    {ft:0.00} q={q} el={s.Tracker.MedianElbowDeg:0} arc={s.Tracker.ArcState} top={s.Tracker.InTopZone} bot={s.Tracker.InBottomZone} anchor={s.WristAnchor.LastVerdict} rise={s.Clap.RiseSw:0.00} fly={s.Clap.InFlight} armer={s.Armer.State} reps={s.Reps}");
            }
            sb.AppendLine($"TOTAL reps={s.Reps} claps={s.ClapReps} vetoed={s.Counter.VetoedReps}");
        }
        finally { Object.DestroyImmediate(go); }
        return sb.ToString();
    }
}
