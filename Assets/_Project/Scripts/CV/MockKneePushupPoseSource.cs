using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Synthetic <see cref="IPoseSource"/> that fakes a person doing KNEE push-ups — knees planted
    /// on the floor, shins lifted, hip-knee-ankle interior angle ~90°. Body-line (shoulder-hip-ankle)
    /// stays plausible because the ankles are lifted but still roughly along the body line, so the
    /// legacy <see cref="PoseMath.LooksLikePushup"/> gate would PASS this — only the new
    /// <see cref="AntiCheat.KneeBendDetector"/> catches it.
    ///
    /// Expected behaviour under the phase 08.1 anti-cheat: <c>PushupSession.Armer</c> never reaches
    /// <c>Armed</c> (reason = <see cref="AntiCheat.PlankRejectReason.KneesBent"/>) and
    /// <c>Counter.Reps</c> stays at 0.
    /// </summary>
    public sealed class MockKneePushupPoseSource : MonoBehaviour, IPoseSource
    {
        [Header("Motion")]
        [SerializeField] private float _tempoRpm = 40f;
        [SerializeField] private float _topElbowAngle = 172f;
        [SerializeField] private float _bottomElbowAngle = 72f;

        [Header("Debug")]
        [SerializeField] private bool simulateLost;

        public event Action<PoseFrame> OnFrame;
        public event Action<TrackingQuality> OnQualityChanged;

        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;
        public bool IsRunning { get; private set; }
        public string StatusMessage => IsRunning ? "mock kneepushup" : "mock stopped";

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

            float vis = simulateLost ? 0f : 0.95f;
            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Landmark(0.5f, 0.5f, 0f, vis);

            // Side-on, facing left. Same shoulder/hip layout as the perfect-plank mock, but the
            // knees are placed on the floor (same Y as wrists) and the ankles are LIFTED above
            // the knees — the cheat posture. Torso still descends with the rep so FullRom passes
            // — we want the rejection to be unambiguously KneesBent, not chest-travel.
            const float bodyY = 0.55f;
            const float chestDescentAtBottom = 0.06f; // smaller than full pushup — knee variant has less ROM
            float descent = chestDescentAtBottom * (1f - s);
            float torsoY  = bodyY + descent;
            Set(PoseLandmark.LeftShoulder,  0.45f, torsoY,         vis);
            Set(PoseLandmark.RightShoulder, 0.45f, torsoY + 0.01f, vis);
            Set(PoseLandmark.LeftHip,       0.70f, torsoY,         vis);
            Set(PoseLandmark.RightHip,      0.70f, torsoY + 0.01f, vis);

            const float wristY = bodyY + 0.18f;
            PlaceArm(PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist,
                     0.40f, wristY, elbow, vis);
            PlaceArm(PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist,
                     0.40f, wristY + 0.01f, elbow, vis);

            // KNEES on the floor (same Y as wrists). ANKLES lifted upward (smaller Y in top-left
            // origin = "higher in the image"). hip→knee horizontal-ish, knee→ankle nearly vertical
            // up → interior angle at knee ≈ 90°.
            Set(PoseLandmark.LeftKnee,        0.78f, wristY,         vis);
            Set(PoseLandmark.RightKnee,       0.78f, wristY + 0.01f, vis);
            Set(PoseLandmark.LeftAnkle,       0.82f, bodyY - 0.05f,  vis);
            Set(PoseLandmark.RightAnkle,      0.82f, bodyY - 0.04f,  vis);
            Set(PoseLandmark.LeftFootIndex,   0.83f, bodyY - 0.05f,  vis);
            Set(PoseLandmark.RightFootIndex,  0.83f, bodyY - 0.04f,  vis);

            // World landmarks — same shift+scale convention as MockPoseSource.
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

        // Same arm construction as MockPoseSource (interior angle at elbow == target).
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
