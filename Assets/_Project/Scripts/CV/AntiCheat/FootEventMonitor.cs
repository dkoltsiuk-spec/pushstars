using UnityEngine;
using PushStars.CV.Util;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// S-KNEE-2 — corroborating frontal knee-cheat signal via the FEET. Two event types:
    ///
    /// <para><b>FootVanish</b>: ankle/foot visibility held ≥ 0.6 for at least 2s after arming, then
    /// dropped below 0.35 for ≥ 1s while the rest of the body kept tracking — the classic signature
    /// of shins lifting off the floor (knee push-up) taking the feet out of the pose model's
    /// confidence.</para>
    ///
    /// <para><b>FootDrift</b>: ankle RMS drift over a 12-frame window measured RELATIVE to the
    /// wrist midpoint (subtracts camera shake), normalized by shoulder width — planted feet don't
    /// wander.</para>
    ///
    /// <para>Feet never visible at all → the monitor never activates (correct fail-open for honest
    /// frontal users whose feet sit below the visibility floor the whole time). Verdicts are applied
    /// by <see cref="KneeCheatGate"/>: single event → SoftDock on subsequent reps; event + sub-hard
    /// KneeDropDelta → escalated HardVeto.</para>
    /// </summary>
    public sealed class FootEventMonitor
    {
        private readonly RingBuffer<Vector2> _ankleRelBuf = new RingBuffer<Vector2>(12);

        private float _visEma = float.NaN;
        private const float VisAlpha = 0.1f;

        private float _highVisHeldSec;
        private float _lowVisHeldSec;
        private bool _hadStableHighVis;
        private float _lastTickTime = -1f;

        /// <summary>Sticky: at least one foot event (vanish or drift) occurred since arming.</summary>
        public bool EventOccurred { get; private set; }

        /// <summary>Which fired last (HUD/telemetry): "vanish" / "drift" / "".</summary>
        public string LastEventKind { get; private set; } = "";

        public float VisEma => _visEma;

        public void Reset()
        {
            _ankleRelBuf.Clear();
            _visEma = float.NaN;
            _highVisHeldSec = 0f;
            _lowVisHeldSec = 0f;
            _hadStableHighVis = false;
            _lastTickTime = -1f;
            EventOccurred = false;
            LastEventKind = "";
        }

        public void Tick(in PoseFrame frame, bool trackingOk, bool isArmed, float timeSec)
        {
            float dt = _lastTickTime >= 0f ? Mathf.Clamp(timeSec - _lastTickTime, 0f, 0.2f) : 0f;
            _lastTickTime = timeSec;

            if (!isArmed || !trackingOk || !frame.IsValid)
            {
                _highVisHeldSec = 0f;
                _lowVisHeldSec = 0f;
                _hadStableHighVis = false;
                _ankleRelBuf.Clear();
                return;
            }

            // Best foot-end visibility this frame.
            float vis = Mathf.Max(
                Mathf.Max(frame.Visibility(PoseLandmark.LeftAnkle), frame.Visibility(PoseLandmark.RightAnkle)),
                Mathf.Max(frame.Visibility(PoseLandmark.LeftFootIndex), frame.Visibility(PoseLandmark.RightFootIndex)));

            _visEma = float.IsNaN(_visEma) ? vis : _visEma + VisAlpha * (vis - _visEma);

            // ── FootVanish ──
            if (_visEma >= CVConstants.FootVanishHighVis)
            {
                _highVisHeldSec += dt;
                _lowVisHeldSec = 0f;
                if (_highVisHeldSec >= CVConstants.FootVanishMinHeldSec) _hadStableHighVis = true;
            }
            else if (_visEma < CVConstants.FootVanishLowVis)
            {
                _lowVisHeldSec += dt;
                _highVisHeldSec = 0f;
                if (_hadStableHighVis && _lowVisHeldSec >= CVConstants.FootVanishMinLostSec)
                {
                    EventOccurred = true;
                    LastEventKind = "vanish";
                    _hadStableHighVis = false; // one event per stable-high episode
                }
            }
            else
            {
                _highVisHeldSec = 0f;
                _lowVisHeldSec = 0f;
            }

            // ── FootDrift (relative to wristMid — camera shake subtracted) ──
            bool la = frame.Visibility(PoseLandmark.LeftAnkle)  >= CVConstants.MinJointVisibility;
            bool ra = frame.Visibility(PoseLandmark.RightAnkle) >= CVConstants.MinJointVisibility;
            bool lw = frame.Visibility(PoseLandmark.LeftWrist)  >= CVConstants.MinJointVisibility;
            bool rw = frame.Visibility(PoseLandmark.RightWrist) >= CVConstants.MinJointVisibility;
            bool ls = frame.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = frame.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            if ((la || ra) && (lw || rw) && ls && rs)
            {
                float aspect = frame.Aspect;
                Vector2 ankle = (la && ra)
                    ? (PoseMath.ToSquare(frame.Get(PoseLandmark.LeftAnkle).Pos2D, aspect)
                     + PoseMath.ToSquare(frame.Get(PoseLandmark.RightAnkle).Pos2D, aspect)) * 0.5f
                    : PoseMath.ToSquare(frame.Get(la ? PoseLandmark.LeftAnkle : PoseLandmark.RightAnkle).Pos2D, aspect);
                Vector2 wrist = (lw && rw)
                    ? (PoseMath.ToSquare(frame.Get(PoseLandmark.LeftWrist).Pos2D, aspect)
                     + PoseMath.ToSquare(frame.Get(PoseLandmark.RightWrist).Pos2D, aspect)) * 0.5f
                    : PoseMath.ToSquare(frame.Get(lw ? PoseLandmark.LeftWrist : PoseLandmark.RightWrist).Pos2D, aspect);
                float sw = Vector2.Distance(
                    PoseMath.ToSquare(frame.Get(PoseLandmark.LeftShoulder).Pos2D, aspect),
                    PoseMath.ToSquare(frame.Get(PoseLandmark.RightShoulder).Pos2D, aspect));

                _ankleRelBuf.Push(ankle - wrist);
                if (_ankleRelBuf.IsFull && sw > 1e-3f)
                {
                    float drift = RmsFromMean(_ankleRelBuf) / sw;
                    if (drift >= CVConstants.FootDriftEventFrac)
                    {
                        EventOccurred = true;
                        LastEventKind = "drift";
                        _ankleRelBuf.Clear(); // one event per window fill
                    }
                }
            }
            else
            {
                _ankleRelBuf.Clear();
            }
        }

        private static float RmsFromMean(RingBuffer<Vector2> buf)
        {
            int n = buf.Count;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < n; i++) sum += buf[i];
            Vector2 mean = sum / n;
            float sq = 0f;
            for (int i = 0; i < n; i++) sq += (buf[i] - mean).sqrMagnitude;
            return Mathf.Sqrt(sq / n);
        }
    }
}
