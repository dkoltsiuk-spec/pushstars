using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// STANDING-BOB cheat mock (frontal): the user stands facing the camera, squats a little on
    /// each "rep" (real vertical shoulder travel ≈ 0.2!) and pumps the elbows. After the FullRom
    /// v2 fix this cheat would pass the travel check by accident — which is exactly why
    /// SupportGeometryGate ships in the same phase.
    ///
    /// Signature: κ ≈ 1.2 (upright body — hips FAR below shoulders relative to shoulder width),
    /// legs fully visible, wrists sweeping through the air with the squat.
    ///
    /// Expected: PlankArmer F3 (BodyIncline) refuses to arm; WristAnchor reads Airborne; even in
    /// the Ambiguous OR-branch SupportGeometryGate P1/P2 hard-vetoes. Reps must stay 0.
    /// </summary>
    public sealed class MockStandingBobPoseSource : MonoBehaviour, IPoseSource
    {
        [Header("Motion")]
        [SerializeField] private float _tempoRpm = 40f;
        [SerializeField] private float _jitterSigma = 0.005f;

        [Header("Debug")]
        [SerializeField] private bool simulateLost;

        public event Action<PoseFrame> OnFrame;
        public event Action<TrackingQuality> OnQualityChanged;

        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;
        public bool IsRunning { get; private set; }
        public string StatusMessage => IsRunning ? "mock standing-bob" : "mock stopped";

        private float _phase;
        private readonly Landmark[] _buf = new Landmark[PoseLandmarks.Count];

        public void StartTracking() => IsRunning = true;
        public void StopTracking()  => IsRunning = false;
        private void OnEnable()  => StartTracking();
        private void OnDisable() => StopTracking();

        private void Update()
        {
            if (!IsRunning) return;
            _phase += (_tempoRpm / 60f) * 2f * Mathf.PI * Time.deltaTime;
            var frame = BuildFrame(Time.time);
            OnFrame?.Invoke(frame);
            var q = simulateLost ? TrackingQuality.Lost : PoseQuality.Classify(frame);
            if (q != Quality) { Quality = q; OnQualityChanged?.Invoke(q); }
        }

        private PoseFrame BuildFrame(float timeSec)
        {
            float s = (Mathf.Cos(_phase) + 1f) * 0.5f;  // 1 = standing tall, 0 = squat bottom
            float visBase = simulateLost ? 0f : 1f;
            float squat = 0.10f * (1f - s);              // whole body bobs down 0.10 in y

            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Landmark(0.5f, 0.5f, 0f, 0.05f);

            const float halfSw = 0.14f;                  // sw 0.28 — plausible standing distance
            float shoulderY = 0.30f + squat;

            SetXY(PoseLandmark.Nose,          0.50f, shoulderY - 0.08f, visBase * 0.99f);
            SetXY(PoseLandmark.LeftShoulder,  0.50f - halfSw, shoulderY, visBase * 0.97f);
            SetXY(PoseLandmark.RightShoulder, 0.50f + halfSw, shoulderY, visBase * 0.97f);

            // Arms pump: elbows bend, wrists sweep through the air in front of the chest.
            float bend = 1f - s;
            float wristSwing = 0.06f * Mathf.Sin(_phase);
            SetXY(PoseLandmark.LeftElbow,  0.50f - halfSw - 0.04f, shoulderY + 0.12f - 0.05f * bend, visBase * 0.93f);
            SetXY(PoseLandmark.RightElbow, 0.50f + halfSw + 0.04f, shoulderY + 0.12f - 0.05f * bend, visBase * 0.93f);
            SetXY(PoseLandmark.LeftWrist,  0.50f - halfSw + wristSwing, shoulderY + 0.16f - 0.10f * bend, visBase * 0.92f);
            SetXY(PoseLandmark.RightWrist, 0.50f + halfSw + wristSwing, shoulderY + 0.16f - 0.10f * bend, visBase * 0.92f);

            // Upright column: hips well below shoulders → κ = (0.64−0.30)/0.28 ≈ 1.2.
            SetXY(PoseLandmark.LeftHip,  0.45f, 0.64f + squat, visBase * 0.85f);
            SetXY(PoseLandmark.RightHip, 0.55f, 0.64f + squat, visBase * 0.85f);
            SetXY(PoseLandmark.LeftKnee,  0.45f, 0.78f + squat * 0.5f, visBase * 0.80f);
            SetXY(PoseLandmark.RightKnee, 0.55f, 0.78f + squat * 0.5f, visBase * 0.80f);
            SetXY(PoseLandmark.LeftAnkle,  0.46f, 0.92f, visBase * 0.75f);
            SetXY(PoseLandmark.RightAnkle, 0.54f, 0.92f, visBase * 0.75f);
            SetXY(PoseLandmark.LeftFootIndex,  0.465f, 0.94f, visBase * 0.70f);
            SetXY(PoseLandmark.RightFootIndex, 0.535f, 0.94f, visBase * 0.70f);

            ApplyJitter();
            return new PoseFrame((Landmark[])_buf.Clone(), null, timeSec, 1f);
        }

        private void SetXY(PoseLandmark id, float x, float y, float vis)
            => _buf[(int)id] = new Landmark(x, y, 0f, vis);

        private void ApplyJitter()
        {
            if (_jitterSigma <= 0f) return;
            for (int i = 0; i < _buf.Length; i++)
            {
                var lm = _buf[i];
                _buf[i] = new Landmark(lm.X + Gauss() * _jitterSigma, lm.Y + Gauss() * _jitterSigma, lm.Z, lm.Visibility);
            }
        }

        private static float Gauss()
        {
            float u1 = Mathf.Max(UnityEngine.Random.value, 1e-6f);
            float u2 = UnityEngine.Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }
    }
}
