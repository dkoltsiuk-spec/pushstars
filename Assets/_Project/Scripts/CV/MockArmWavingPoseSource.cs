using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Synthetic <see cref="IPoseSource"/> for the canonical "lying on back, waving arms" cheat
    /// (and equivalents — standing while bending elbows, sitting). The torso is upright-ish, NOT
    /// horizontal, so body-line angle (shoulder-hip-ankle) is small. Wrists oscillate left-right
    /// with the elbow swing — they are NOT planted on a fixed support.
    ///
    /// Expected behaviour under phase 08.1 anti-cheat: <c>PushupSession.Armer</c> never arms
    /// (reason cycles between <see cref="AntiCheat.PlankRejectReason.BodySagging"/> and
    /// <see cref="AntiCheat.PlankRejectReason.WristsAirborne"/>) and <c>Counter.Reps</c> stays at 0
    /// even though the elbow angle sweeps the same Top→Bottom→Top arc the real FSM would credit.
    /// </summary>
    public sealed class MockArmWavingPoseSource : MonoBehaviour, IPoseSource
    {
        [Header("Motion")]
        [SerializeField] private float _tempoRpm = 40f;
        [SerializeField] private float _topElbowAngle = 172f;
        [SerializeField] private float _bottomElbowAngle = 72f;
        [SerializeField, Tooltip("How far the wrists swing horizontally per cycle (normalized img units).")]
        private float _wristSwingAmplitude = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool simulateLost;

        public event Action<PoseFrame> OnFrame;
        public event Action<TrackingQuality> OnQualityChanged;

        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;
        public bool IsRunning { get; private set; }
        public string StatusMessage => IsRunning ? "mock armwaving" : "mock stopped";

        private float _phase;
        private readonly Landmark[] _buf = new Landmark[PoseLandmarks.Count];
        private readonly Landmark[] _worldBuf = new Landmark[PoseLandmarks.Count];
        private const float MockImageToMeters = 2.0f;

        public void StartTracking() => IsRunning = true;
        public void StopTracking()  => IsRunning = false;
        private void OnEnable()  => StartTracking();
        private void OnDisable() => StopTracking();

        private void Update()
        {
            if (!IsRunning) return;

            float cyclesPerSec = _tempoRpm / 60f;
            _phase += cyclesPerSec * 2f * Mathf.PI * Time.deltaTime;

            var frame = BuildFrame(Time.time);
            OnFrame?.Invoke(frame);

            var q = simulateLost ? TrackingQuality.Lost : PoseQuality.Classify(frame);
            if (q != Quality)
            {
                Quality = q;
                OnQualityChanged?.Invoke(q);
            }
        }

        private PoseFrame BuildFrame(float timeSec)
        {
            float s = (Mathf.Cos(_phase) + 1f) * 0.5f;
            float elbow = Mathf.Lerp(_bottomElbowAngle, _topElbowAngle, s);
            // Wrists sweep horizontally with the elbow — anchored wrists wouldn't move at all.
            float wristOffsetX = Mathf.Sin(_phase) * _wristSwingAmplitude;

            float vis = simulateLost ? 0f : 0.95f;
            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Landmark(0.5f, 0.5f, 0f, vis);

            // Upright torso (camera looks at person from the side; person SITTING). Shoulders up,
            // hips down, ankles further down — body-line angle ≈ 90-110°, far below ArmingBodyLineAngle.
            Set(PoseLandmark.LeftShoulder,  0.45f, 0.30f, vis);
            Set(PoseLandmark.RightShoulder, 0.45f, 0.31f, vis);
            Set(PoseLandmark.LeftHip,       0.45f, 0.55f, vis);
            Set(PoseLandmark.RightHip,      0.45f, 0.56f, vis);
            Set(PoseLandmark.LeftKnee,      0.55f, 0.65f, vis);
            Set(PoseLandmark.RightKnee,     0.55f, 0.66f, vis);
            Set(PoseLandmark.LeftAnkle,     0.65f, 0.75f, vis);
            Set(PoseLandmark.RightAnkle,    0.65f, 0.76f, vis);
            Set(PoseLandmark.LeftFootIndex, 0.67f, 0.76f, vis);
            Set(PoseLandmark.RightFootIndex,0.67f, 0.77f, vis);

            // Arms swing in front. Wrist X sweeps left-right with the cycle — drift detector should
            // see this as Airborne.
            float wristY = 0.40f;
            PlaceArm(PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist,
                     0.60f + wristOffsetX, wristY,         elbow, vis);
            PlaceArm(PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist,
                     0.60f + wristOffsetX, wristY + 0.01f, elbow, vis);

            Vector2 imgMidHip = (_buf[(int)PoseLandmark.LeftHip].Pos2D
                               + _buf[(int)PoseLandmark.RightHip].Pos2D) * 0.5f;
            for (int i = 0; i < _buf.Length; i++)
            {
                var lm = _buf[i];
                _worldBuf[i] = new Landmark(
                    (lm.X - imgMidHip.x) * MockImageToMeters,
                    (lm.Y - imgMidHip.y) * MockImageToMeters,
                    lm.Z * MockImageToMeters,
                    lm.Visibility);
            }

            return new PoseFrame((Landmark[])_buf.Clone(), (Landmark[])_worldBuf.Clone(), timeSec);
        }

        private void PlaceArm(PoseLandmark shoulder, PoseLandmark elbow, PoseLandmark wrist,
                              float wristX, float wristY, float targetAngle, float vis)
        {
            Vector2 sh = _buf[(int)shoulder].Pos2D;
            Vector2 wr = new Vector2(wristX, wristY);
            Vector2 mid = (sh + wr) * 0.5f;
            float half = Vector2.Distance(sh, wr) * 0.5f;
            float bend = Mathf.Tan(Mathf.Deg2Rad * (180f - targetAngle) * 0.5f) * half;
            Vector2 dir = (wr - sh).normalized;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            Vector2 el = mid + normal * bend;
            Set(wrist, wristX, wristY, vis);
            Set(elbow, el.x, el.y, vis);
        }

        private void Set(PoseLandmark id, float x, float y, float vis)
            => _buf[(int)id] = new Landmark(x, y, 0f, vis);
    }
}
