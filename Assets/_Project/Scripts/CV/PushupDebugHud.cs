using UnityEngine;
using PushStars.CV.AntiCheat;

namespace PushStars.CV
{
    /// <summary>
    /// On-screen readout for testing the CV pipeline (phase 08 + 08.1 frontal addendum). Shows a
    /// large REPS counter, the diagnostic block, and the AMPLITUDE GAUGE — a vertical depth scale
    /// with the top/bottom latch zones, a floating depth marker, and per-rep watermarks. Plays:
    /// a beep per counted rep, a short high tick when the bottom latches (depth registered — the
    /// tuning-critical feedback), and a buzz when a rep is vetoed.
    ///
    /// Throwaway debug UI — the real duel HUD is phase 14; this is the under-the-hood tuning tool.
    /// </summary>
    public sealed class PushupDebugHud : MonoBehaviour
    {
        [SerializeField] private PushupSession _session;
        [Tooltip("Play a beep each time a rep is counted.")]
        [SerializeField] private bool _repSound = true;
        [Tooltip("Play a short tick when the bottom of a rep is latched.")]
        [SerializeField] private bool _bottomTickSound = true;
        [Tooltip("Play a buzz when a rep candidate is vetoed by the anti-cheat auditor.")]
        [SerializeField] private bool _rejectBuzzSound = true;

        private GUIStyle _repsStyle;
        private GUIStyle _infoStyle;
        private GUIStyle _gaugeLabelStyle;
        private AudioSource _audio;
        private AudioClip _beep;
        private AudioClip _tick;
        private AudioClip _buzz;

        private float _lastTickPlayTime = -10f;
        private float _lastBuzzPlayTime = -10f;
        private float _lastBottomFlashTime = -10f;
        private float _lastTopFlashTime = -10f;
        private float _lastRepFlashTime = -10f;
        private float _lastVetoFlashTime = -10f;

        private void Awake()
        {
            _audio = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _beep = MakeBeep();
            _tick = MakeTick();
            _buzz = MakeBuzz();
        }

        private void OnEnable()
        {
            if (_session == null) return;
            _session.OnRep += HandleRep;
            _session.OnRepRejected += HandleRepRejected;
            _session.Tracker.OnBottomLatched += HandleBottomLatched;
            _session.Tracker.OnTopLatched += HandleTopLatched;
        }

        private void OnDisable()
        {
            if (_session == null) return;
            _session.OnRep -= HandleRep;
            _session.OnRepRejected -= HandleRepRejected;
            _session.Tracker.OnBottomLatched -= HandleBottomLatched;
            _session.Tracker.OnTopLatched -= HandleTopLatched;
        }

        private void HandleRep(int reps)
        {
            _lastRepFlashTime = Time.time;
            if (_repSound && _audio != null && _beep != null)
                _audio.PlayOneShot(_beep);
        }

        private void HandleRepRejected(RepVote vote)
        {
            _lastVetoFlashTime = Time.time;
            if (!_rejectBuzzSound || _audio == null || _buzz == null) return;
            if (Time.time - _lastBuzzPlayTime < 0.5f) return; // debounce
            _lastBuzzPlayTime = Time.time;
            _audio.PlayOneShot(_buzz);
        }

        private void HandleBottomLatched()
        {
            _lastBottomFlashTime = Time.time;
            if (!_bottomTickSound || _audio == null || _tick == null) return;
            if (Time.time - _lastTickPlayTime < 0.15f) return; // debounce (arc guarantees 1/rep)
            _lastTickPlayTime = Time.time;
            _audio.PlayOneShot(_tick);
        }

        private void HandleTopLatched() => _lastTopFlashTime = Time.time;

        private void OnGUI()
        {
            if (_session == null) return;

            GUI.depth = -10; // draw on top of the camera/skeleton (lower depth = on top)

            float sh = Screen.height;
            int repsFont = Mathf.RoundToInt(Mathf.Clamp(sh * 0.085f, 48f, 160f));
            int infoFont = Mathf.RoundToInt(Mathf.Clamp(sh * 0.020f, 16f, 32f));
            float top = sh * 0.06f; // clear the notch / dynamic island

            _repsStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            _infoStyle ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
            _repsStyle.fontSize = repsFont;
            _infoStyle.fontSize = infoFont;

            // ── Big REPS counter ──
            string repsLabel = $"{_session.Reps}  REPS";
            DrawWithShadow(new Rect(22, top, Screen.width - 40, repsFont * 1.4f), repsLabel, _repsStyle, new Color(0.3f, 1f, 0.45f));

            // ── Diagnostics block ──
            var f = _session.LastForm;
            var t = _session.Tracker;
            string info =
                $"PHASE: {_session.Phase}   ARC: {t.ArcState}\n" +
                $"θs/θm: {t.SmoothedElbowDeg:0.0} / {t.MedianElbowDeg:0.0}°   FORM: {f.Form:0}\n" +
                $"TEMPO: {_session.TempoRpm:0} rpm   TRACK: {_session.Quality}   FPS: {(1f / Mathf.Max(Time.smoothDeltaTime, 0.0001f)):0}\n" +
                $"VIEW:  {_session.View.View} (R={_session.View.RMedian:0.00})   κ={KappaText()}   Δknee={KneeDropText()}\n" +
                $"SET:   {BuildSetLine()}\n" +
                $"ARMER: {BuildArmerLine()}\n" +
                $"AC:    {BuildAntiCheatLine()}\n" +
                $"VOTE:  {BuildLastVoteLine()}\n" +
                $"STATUS: {_session.SourceStatus}\n" +
                $"WORLD: {BuildWorldProbeLine()}";

            float infoTop = top + repsFont * 1.5f;
            DrawWithShadow(new Rect(22, infoTop, Screen.width - 40, sh), info, _infoStyle, Color.white);

            DrawAmplitudeGauge();
        }

        // ── Amplitude gauge (right edge, per the frontal-addendum spec) ─────────────────────────

        private void DrawAmplitudeGauge()
        {
            var t = _session.Tracker;
            float s = Screen.height / 100f;
            float barW = 3f * s;
            float barH = 55f * s;
            float barX = Screen.width - barW - 2f * s;
            float barY = (Screen.height - barH) * 0.5f;

            bool armed = _session.Armer != null && _session.Armer.IsArmed;
            float dim = armed ? 1f : 0.45f; // whole gauge dims when disarmed

            // Rep-credited / vetoed full-gauge pulse.
            float repPulse = Mathf.Clamp01(1f - (Time.time - _lastRepFlashTime) / 0.3f);
            float vetoPulse = Mathf.Clamp01(1f - (Time.time - _lastVetoFlashTime) / 0.3f);

            // Background + border.
            GUI.color = new Color(0.1f + 0.4f * vetoPulse, 0.1f + 0.4f * repPulse, 0.1f, 0.75f);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.9f);
            DrawRectOutline(new Rect(barX, barY, barW, barH), 1f);

            // Zone bands. d01: 0 = top of track (lockout), 1 = deepest.
            float topZoneEnd = D01(CVConstants.TopElbowAngle);        // 0.15
            float bottomZoneStart = D01(CVConstants.BottomElbowAngle); // 0.80

            bool inTop = t.InTopZone;
            bool inBottom = t.InBottomZone;
            float topFlash = Mathf.Clamp01(1f - (Time.time - _lastTopFlashTime) / 0.15f);
            float bottomFlash = Mathf.Clamp01(1f - (Time.time - _lastBottomFlashTime) / 0.15f);

            DrawZone(barX, barY, barW, barH, 0f, topZoneEnd, inTop, topFlash, dim);
            DrawZone(barX, barY, barW, barH, bottomZoneStart, 1f, inBottom, bottomFlash, dim);

            // HUD-adaptive inner edges (display only — latches stay absolute).
            GUI.color = new Color(0.4f, 1f, 0.6f, 0.9f * dim);
            float hudTopY = barY + D01(t.HudTopEnterDeg) * barH;
            float hudBotY = barY + D01(t.HudBottomEnterDeg) * barH;
            GUI.DrawTexture(new Rect(barX, hudTopY - 1f, barW, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(barX, hudBotY - 1f, barW, 2f), Texture2D.whiteTexture);

            // Arc watermarks (yellow, left of the bar) + last-rep watermarks (gray, right).
            GUI.color = new Color(1f, 0.9f, 0.2f, 0.95f * dim);
            DrawTickLeft(barX, barY, barH, t.RepMinDepth01, s);
            DrawTickLeft(barX, barY, barH, t.RepMaxDepth01, s);
            GUI.color = new Color(0.7f, 0.7f, 0.7f, 0.8f * dim);
            DrawTickRight(barX + barW, barY, barH, t.LastRepMinDepth01, s);
            DrawTickRight(barX + barW, barY, barH, t.LastRepMaxDepth01, s);

            // Floating depth marker — red when the signal is frozen.
            GUI.color = t.SignalValid
                ? new Color(1f, 1f, 1f, 1f * dim)
                : new Color(1f, 0.25f, 0.2f, 0.95f);
            float markerY = barY + t.CurrentDepth01 * barH;
            GUI.DrawTexture(new Rect(barX - 1.2f * s, markerY - 0.25f * s, barW + 2.4f * s, 0.5f * s),
                Texture2D.whiteTexture);

            // Compact labels under the bar.
            _gaugeLabelStyle ??= new GUIStyle(GUI.skin.label)
            { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
            _gaugeLabelStyle.fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.016f, 12f, 24f));
            string hint = t.BottomAltHintActive ? "\nЛОКТИ ШИРЕ!" : "";
            string label = $"{t.MedianElbowDeg:0}°{hint}";
            GUI.color = t.BottomAltHintActive ? new Color(1f, 0.7f, 0.2f) : Color.white;
            GUI.Label(new Rect(barX - 30f * s, barY + barH + 0.5f * s, barW + 30f * s, 6f * s), label, _gaugeLabelStyle);

            GUI.color = Color.white;
        }

        private static float D01(float angleDeg)
            => Mathf.Clamp01((CVConstants.AmplitudeGaugeTopDeg - angleDeg)
                / (CVConstants.AmplitudeGaugeTopDeg - CVConstants.AmplitudeGaugeBottomDeg));

        private static void DrawZone(float barX, float barY, float barW, float barH,
                                     float d0, float d1, bool active, float latchFlash, float dim)
        {
            Color zone = active
                ? new Color(0.2f, 0.9f, 0.2f, 0.8f * dim)
                : new Color(0.2f, 0.6f, 0.2f, 0.35f * dim);
            zone = Color.Lerp(zone, Color.white, latchFlash);
            GUI.color = zone;
            GUI.DrawTexture(new Rect(barX, barY + d0 * barH, barW, (d1 - d0) * barH), Texture2D.whiteTexture);
        }

        private static void DrawTickLeft(float barX, float barY, float barH, float d01, float s)
            => GUI.DrawTexture(new Rect(barX - 1.5f * s, barY + d01 * barH - 1f, 1.3f * s, 2f), Texture2D.whiteTexture);

        private static void DrawTickRight(float barRight, float barY, float barH, float d01, float s)
            => GUI.DrawTexture(new Rect(barRight + 0.2f * s, barY + d01 * barH - 1f, 1.3f * s, 2f), Texture2D.whiteTexture);

        private static void DrawRectOutline(Rect r, float w)
        {
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, w), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - w, r.width, w), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, w, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - w, r.y, w, r.height), Texture2D.whiteTexture);
        }

        // ── Readout lines ────────────────────────────────────────────────────────────────────────

        private string KappaText()
        {
            var d = _session.KneeDrop;
            return float.IsNaN(d.KappaBaseline) ? "--" : $"{d.KappaBaseline:0.00}b";
        }

        private string KneeDropText()
        {
            var d = _session.KneeDrop;
            return float.IsNaN(d.Delta) ? "--" : $"{d.Delta:+0.00;-0.00}";
        }

        private string BuildSetLine()
        {
            var st = _session.SetTracker;
            return st.State switch
            {
                WorkoutSetState.Idle        => "Idle (встань в планку)",
                WorkoutSetState.Active      => $"Active  #{st.SetIndex}  reps={st.RepsInSet}",
                WorkoutSetState.Resting     => $"ОТДЫХ  #{st.SetIndex}  reps={st.RepsInSet}  {st.RestingForSec:0.0}s/{CVConstants.RestToSetCompleteSec:0}s",
                WorkoutSetState.SetComplete => $"ПОДХОД ЗАВЕРШЁН  #{st.SetIndex}  reps={st.RepsInSet}",
                _ => st.State.ToString(),
            };
        }

        private string BuildArmerLine()
        {
            var armer = _session.Armer;
            if (armer == null) return "(not initialized)";
            string tail = armer.State switch
            {
                PlankArmerState.Arming  => $"  prog={armer.ArmingProgress01:0.00}",
                PlankArmerState.Cooling => $"  cool={armer.CoolingTimeLeftSec:0.0}s",
                _ => "",
            };
            return $"{armer.State}{tail}  reason={armer.LastRejectReason}";
        }

        private string BuildAntiCheatLine()
        {
            var anchor = _session.WristAnchor;
            var knee   = _session.KneeBend;
            string kneeAngle = float.IsNaN(knee.LastMinKneeAngleDeg) ? "--" : $"{knee.LastMinKneeAngleDeg:0}°";
            string foot = _session.FootMonitor.EventOccurred ? $"  FOOT:{_session.FootMonitor.LastEventKind}" : "";
            return $"anchor={anchor.LastVerdict} L{anchor.LastLeftDriftFrac:0.00}/R{anchor.LastRightDriftFrac:0.00}  " +
                   $"knee={knee.Classification} ({kneeAngle}){foot}";
        }

        private string BuildLastVoteLine()
        {
            if (_session.Auditor == null || _session.Counter == null) return "(no auditor)";
            var v = _session.Counter.LastRepVote;
            string src = string.IsNullOrEmpty(_session.Auditor.LastVoteSource) ? "" : $"  src={_session.Auditor.LastVoteSource}";
            return $"{v}{src}  vetoed={_session.Counter.VetoedReps}  win={_session.Auditor.LastWindowFrameCount}f/{_session.Auditor.LastWindowDurationSec:0.0}s";
        }

        /// <summary>Stage 0 axis-convention probe (docs/plan/phase-08.1-pushup-anticheat.md §4).</summary>
        private string BuildWorldProbeLine()
        {
            var src = _session.LastFrame;
            if (!src.IsValid) return "(no frame)";
            if (!src.HasWorldLandmarks) return "(no world landmarks)";

            var lw = src.GetWorld(PoseLandmark.LeftWrist);
            var ls = src.GetWorld(PoseLandmark.LeftShoulder);
            var lh = src.GetWorld(PoseLandmark.LeftHip);
            return $"LW({lw.X:0.00},{lw.Y:0.00},{lw.Z:0.00})  " +
                   $"LS({ls.X:0.00},{ls.Y:0.00},{ls.Z:0.00})  " +
                   $"LH({lh.X:0.00},{lh.Y:0.00},{lh.Z:0.00})";
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

        // ── Procedural audio clips (no assets) ───────────────────────────────────────────────────

        /// <summary>880Hz rep-counted beep (A5, 100ms).</summary>
        private static AudioClip MakeBeep()
        {
            const int rate = 44100;
            const float dur = 0.10f;
            const float freq = 880f;
            int n = (int)(rate * dur);
            var samples = new float[n];
            for (int i = 0; i < n; i++)
            {
                float time = (float)i / rate;
                float attack = Mathf.Min(1f, i / (rate * 0.004f));
                float decay  = Mathf.Min(1f, (n - i) / (rate * 0.04f));
                float env = Mathf.Clamp01(attack) * Mathf.Clamp01(decay);
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * time) * 0.5f * env;
            }
            var clip = AudioClip.Create("repBeep", n, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>1320Hz bottom-latch tick (E6, 40ms, 2ms attack, exp decay τ=15ms) — fires when
        /// the depth registers; short and high so it never masks the 880Hz rep beep.</summary>
        private static AudioClip MakeTick()
        {
            const int rate = 44100;
            float dur = CVConstants.BottomTickDurSec;
            float freq = CVConstants.BottomTickFreqHz;
            int n = (int)(rate * dur);
            var samples = new float[n];
            const float tau = 0.015f;
            for (int i = 0; i < n; i++)
            {
                float time = (float)i / rate;
                float attack = Mathf.Clamp01(i / (rate * 0.002f));
                float decay = Mathf.Exp(-time / tau);
                samples[i] = Mathf.Sin(2f * Mathf.PI * freq * time) * 0.45f * attack * decay;
            }
            var clip = AudioClip.Create("bottomTick", n, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>220Hz veto buzz (250ms, 5ms attack, linear fade over the last 100ms).</summary>
        private static AudioClip MakeBuzz()
        {
            const int rate = 44100;
            float dur = CVConstants.RejectBuzzDurSec;
            float freq = CVConstants.RejectBuzzFreqHz;
            int n = (int)(rate * dur);
            var samples = new float[n];
            int fadeStart = n - (int)(rate * 0.1f);
            for (int i = 0; i < n; i++)
            {
                float time = (float)i / rate;
                float attack = Mathf.Clamp01(i / (rate * 0.005f));
                float fade = i >= fadeStart ? 1f - (float)(i - fadeStart) / (n - fadeStart) : 1f;
                // Slightly square-ish (adds the 3rd harmonic) so it reads as a "wrong" sound.
                float wave = Mathf.Sin(2f * Mathf.PI * freq * time)
                           + 0.35f * Mathf.Sin(2f * Mathf.PI * freq * 3f * time);
                samples[i] = wave * 0.35f * attack * fade;
            }
            var clip = AudioClip.Create("rejectBuzz", n, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
