using UnityEngine;
using PushStars.CV.AntiCheat;

namespace PushStars.CV
{
    /// <summary>
    /// On-screen readout for testing the CV pipeline (phase 08). Shows a large REPS counter plus the
    /// diagnostic block (PHASE / FORM / ELBOW / TEMPO / TRACK / FPS / STATUS) from a
    /// <see cref="PushupSession"/>, and plays a beep on each counted rep. Works the same whether the
    /// session is fed by MockPoseSource (no camera) or MediaPipePoseSource (real webcam). Throwaway
    /// debug UI — the real duel HUD is phase 14.
    /// </summary>
    public sealed class PushupDebugHud : MonoBehaviour
    {
        [SerializeField] private PushupSession _session;
        [Tooltip("Play a beep each time a rep is counted.")]
        [SerializeField] private bool _repSound = true;

        private GUIStyle _repsStyle;
        private GUIStyle _infoStyle;
        private AudioSource _audio;
        private AudioClip _beep;

        private void Awake()
        {
            _audio = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _beep = MakeBeep();
        }

        private void OnEnable()
        {
            if (_session != null) _session.OnRep += HandleRep;
        }

        private void OnDisable()
        {
            if (_session != null) _session.OnRep -= HandleRep;
        }

        private void HandleRep(int reps)
        {
            if (_repSound && _audio != null && _beep != null)
                _audio.PlayOneShot(_beep);
        }

        /// <summary>Plank-armer state + arming progress / cooling timeleft + last reject reason.
        /// Lets the dev watch the FSM transitions live during anti-cheat tuning.</summary>
        private string BuildArmerLine()
        {
            var armer = _session != null ? _session.Armer : null;
            if (armer == null) return "(not initialized)";
            string tail = armer.State switch
            {
                PlankArmerState.Arming  => $"  prog={armer.ArmingProgress01:0.00}",
                PlankArmerState.Cooling => $"  cool={armer.CoolingTimeLeftSec:0.0}s",
                _ => "",
            };
            return $"{armer.State}{tail}  reason={armer.LastRejectReason}";
        }

        /// <summary>Most recent per-rep audit vote + source validator + rejected-rep count for the
        /// session. Set on EVERY rep arc (passed or vetoed) by the auditor.</summary>
        private string BuildLastVoteLine()
        {
            if (_session == null || _session.Auditor == null) return "(no auditor)";
            var v = _session.Counter.LastRepVote;
            string src = string.IsNullOrEmpty(_session.Auditor.LastVoteSource)
                ? ""
                : $"  src={_session.Auditor.LastVoteSource}";
            return $"{v}{src}  vetoed={_session.Counter.VetoedReps}  win={_session.Auditor.LastWindowFrameCount}f/{_session.Auditor.LastWindowDurationSec:0.0}s";
        }

        /// <summary>Wrist-anchor verdict + per-side drift, and the knee classification + raw angle.
        /// These are the inputs to the plank-armer predicate — surfacing them makes failed armings
        /// debuggable.</summary>
        private string BuildAntiCheatLine()
        {
            if (_session == null) return "(no session)";
            var anchor = _session.WristAnchor;
            var knee   = _session.KneeBend;
            string kneeAngle = float.IsNaN(knee.LastMinKneeAngleDeg)
                ? "--"
                : $"{knee.LastMinKneeAngleDeg:0}°";
            return $"anchor={anchor.LastVerdict} L{anchor.LastLeftDriftFrac:0.00}/R{anchor.LastRightDriftFrac:0.00}  " +
                   $"knee={knee.Classification} ({kneeAngle})";
        }

        /// <summary>Stage 0 axis-convention probe (see docs/plan/phase-08.1-pushup-anticheat.md §4).
        /// On a real device, stand in plank and read these numbers — they tell us whether the
        /// MediaPipe world frame is body-aligned (wrist.y differs from shoulder.y by ~30-50cm) or
        /// image-aligned (axes vary with phone orientation). Numbers go into the design doc.</summary>
        private string BuildWorldProbeLine()
        {
            var src = _session != null ? _session.LastFrame : default;
            if (!src.IsValid) return "(no frame)";
            if (!src.HasWorldLandmarks) return "(no world landmarks)";

            var lw = src.GetWorld(PoseLandmark.LeftWrist);
            var ls = src.GetWorld(PoseLandmark.LeftShoulder);
            var lh = src.GetWorld(PoseLandmark.LeftHip);
            return $"LW({lw.X:0.00},{lw.Y:0.00},{lw.Z:0.00})  " +
                   $"LS({ls.X:0.00},{ls.Y:0.00},{ls.Z:0.00})  " +
                   $"LH({lh.X:0.00},{lh.Y:0.00},{lh.Z:0.00})";
        }

        private void OnGUI()
        {
            if (_session == null) return;

            GUI.depth = -10; // draw on top of the camera/skeleton (lower depth = on top)

            float sh = Screen.height;
            int repsFont = Mathf.RoundToInt(Mathf.Clamp(sh * 0.085f, 48f, 160f));
            int infoFont = Mathf.RoundToInt(Mathf.Clamp(sh * 0.022f, 18f, 36f));
            float top = sh * 0.06f; // clear the notch / dynamic island

            _repsStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            _infoStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            _repsStyle.fontSize = repsFont;
            _infoStyle.fontSize = infoFont;

            // ── Big REPS counter ──
            string reps = $"{_session.Reps}";
            string repsLabel = $"{reps}  REPS";
            DrawWithShadow(new Rect(22, top, Screen.width - 40, repsFont * 1.4f), repsLabel, _repsStyle, new Color(0.3f, 1f, 0.45f));

            // ── Diagnostics block ──
            var f = _session.LastForm;
            string worldLine = BuildWorldProbeLine();
            string armerLine = BuildArmerLine();
            string acLine    = BuildAntiCheatLine();
            string voteLine  = BuildLastVoteLine();
            string info =
                $"PHASE: {_session.Phase}\n" +
                $"FORM:  {f.Form:0}   (depth {f.DepthScore:0}, line {f.BodyLineScore:0})\n" +
                $"ELBOW: {f.ElbowAngleDeg:0}°\n" +
                $"TEMPO: {_session.TempoRpm:0} rpm\n" +
                $"TRACK: {_session.Quality}\n" +
                $"FPS:   {(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f)):0}\n" +
                $"STATUS: {_session.SourceStatus}\n" +
                $"ARMER: {armerLine}\n" +
                $"AC:    {acLine}\n" +
                $"VOTE:  {voteLine}\n" +
                $"WORLD: {worldLine}";

            float infoTop = top + repsFont * 1.5f;
            DrawWithShadow(new Rect(22, infoTop, Screen.width - 40, sh), info, _infoStyle, Color.white);
        }

        private static void DrawWithShadow(Rect r, string text, GUIStyle style, Color color)
        {
            var prev = style.normal.textColor;
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
            style.normal.textColor = prev;
        }

        /// <summary>Builds a short sine "beep" AudioClip procedurally so no audio asset is needed.</summary>
        private static AudioClip MakeBeep()
        {
            const int rate = 44100;
            const float dur = 0.10f;
            const float freq = 880f; // A5
            int n = (int)(rate * dur);
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float attack = Mathf.Min(1f, i / (rate * 0.004f));     // ~4ms fade-in
                float decay  = Mathf.Min(1f, (n - i) / (rate * 0.04f)); // ~40ms fade-out
                float env = Mathf.Clamp01(attack) * Mathf.Clamp01(decay);
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.5f * env;
            }
            var clip = AudioClip.Create("repBeep", n, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
