using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// FRONTAL ALL-FOURS cheat mock (policy 2026-07-10: real knee push-ups COUNT — what must be
    /// vetoed is the all-fours rock): the user arms honestly (plank, first
    /// <see cref="_honestSeconds"/>), then sits back onto all fours — torso near-vertical
    /// (κ ≈ 0.63), knees directly under the hips — and pumps the elbows through the full envelope
    /// while the shoulders barely descend (Δy ≈ 0.03 → travelFrac ≈ 0.1).
    ///
    /// Expected: within the 2.5s Cooling grace at most ONE rep candidate forms and is HardVetoed
    /// (KneeCheatGate κ_mean &gt; 0.60 and/or FullRom ChestNotLowered); then the armer disarms via
    /// F3 BodyIncline. Reps must NOT grow after the transition.
    /// </summary>
    public sealed class MockFrontalKneePoseSource : MonoBehaviour, IPoseSource
    {
        [Header("Scenario")]
        [Tooltip("Seconds of honest top-plank hold before dropping to the knees.")]
        [SerializeField] private float _honestSeconds = 3f;
        [SerializeField] private float _tempoRpm = 40f;
        [SerializeField] private float _jitterSigma = 0.005f;

        [Header("Debug")]
        [SerializeField] private bool simulateLost;

        public event Action<PoseFrame> OnFrame;
        public event Action<TrackingQuality> OnQualityChanged;

        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;
        public bool IsRunning { get; private set; }
        public string StatusMessage => IsRunning ? "mock frontal-knee" : "mock stopped";

        private float _phase;
        private float _elapsed;
        private readonly Landmark[] _buf = new Landmark[PoseLandmarks.Count];

        public void StartTracking() { IsRunning = true; _elapsed = 0f; }
        public void StopTracking()  => IsRunning = false;
        private void OnEnable()  => StartTracking();
        private void OnDisable() => StopTracking();

        private void Update()
        {
            if (!IsRunning) return;
            _elapsed += Time.deltaTime;
            bool kneesDown = _elapsed >= _honestSeconds;
            if (kneesDown)
                _phase += (_tempoRpm / 60f) * 2f * Mathf.PI * Time.deltaTime;

            var frame = BuildFrame(Time.time, kneesDown);
            OnFrame?.Invoke(frame);
            var q = simulateLost ? TrackingQuality.Lost : PoseQuality.Classify(frame);
            if (q != Quality) { Quality = q; OnQualityChanged?.Invoke(q); }
        }

        private PoseFrame BuildFrame(float timeSec, bool kneesDown)
        {
            float s = kneesDown ? (Mathf.Cos(_phase) + 1f) * 0.5f : 1f; // hold at top until the drop
            float visBase = simulateLost ? 0f : 1f;
            float hipVis  = visBase * 0.70f;
            float kneeVis = visBase * 0.65f;
            float ankleVis = visBase * 0.30f;

            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Landmark(0.5f, 0.5f, 0f, 0.05f);

            if (!kneesDown)
            {
                // Honest plank at the top — same reference geometry as MockFrontalPushupPoseSource.
                SetLerp(PoseLandmark.Nose,          0.50f, 0.55f, 0.50f, 0.68f, 1f, visBase * 0.99f);
                SetLerp(PoseLandmark.LeftShoulder,  0.36f, 0.54f, 0.35f, 0.66f, 1f, visBase * 0.97f);
                SetLerp(PoseLandmark.RightShoulder, 0.64f, 0.54f, 0.65f, 0.66f, 1f, visBase * 0.97f);
                SetLerp(PoseLandmark.LeftElbow,     0.31f, 0.66f, 0.24f, 0.64f, 1f, visBase * 0.92f);
                SetLerp(PoseLandmark.RightElbow,    0.69f, 0.66f, 0.76f, 0.64f, 1f, visBase * 0.92f);
                SetLerp(PoseLandmark.LeftWrist,     0.25f, 0.77f, 0.25f, 0.77f, 1f, visBase * 0.90f);
                SetLerp(PoseLandmark.RightWrist,    0.75f, 0.77f, 0.75f, 0.77f, 1f, visBase * 0.90f);
                SetLerp(PoseLandmark.LeftHip,       0.41f, 0.59f, 0.41f, 0.59f, 1f, hipVis);
                SetLerp(PoseLandmark.RightHip,      0.59f, 0.59f, 0.59f, 0.59f, 1f, hipVis);
                SetLerp(PoseLandmark.LeftKnee,      0.44f, 0.64f, 0.44f, 0.64f, 1f, kneeVis);
                SetLerp(PoseLandmark.RightKnee,     0.56f, 0.64f, 0.56f, 0.64f, 1f, kneeVis);
                SetLerp(PoseLandmark.LeftAnkle,     0.46f, 0.66f, 0.46f, 0.66f, 1f, ankleVis);
                SetLerp(PoseLandmark.RightAnkle,    0.54f, 0.66f, 0.54f, 0.66f, 1f, ankleVis);
                SetLerp(PoseLandmark.LeftFootIndex, 0.465f, 0.665f, 0.465f, 0.665f, 1f, ankleVis);
                SetLerp(PoseLandmark.RightFootIndex,0.535f, 0.665f, 0.535f, 0.665f, 1f, ankleVis);
            }
            else
            {
                // ALL-FOURS: torso near-vertical — hips deep below the shoulders in the image
                // (κ = (0.72 − 0.545)/0.28 ≈ 0.63), knees planted under the hips. The elbows pump
                // the FULL envelope (~172° → ~75°, the cheater flaps hard) but the shoulders only
                // dip 0.03 → travelFrac ≈ 0.1, far under the 0.25 ChestNotLowered floor.
                float shY = 0.54f + 0.03f * (1f - s);
                SetLerp(PoseLandmark.Nose,          0.50f, shY - 0.06f, 0.50f, shY - 0.06f, 1f, visBase * 0.99f);
                SetLerp(PoseLandmark.LeftShoulder,  0.36f, shY, 0.36f, shY, 1f, visBase * 0.97f);
                SetLerp(PoseLandmark.RightShoulder, 0.64f, shY, 0.64f, shY, 1f, visBase * 0.97f);
                // Same elbow keyframes as the honest mock — full genuine bend.
                SetLerp(PoseLandmark.LeftElbow,     0.31f, 0.66f, 0.24f, 0.64f, s, visBase * 0.92f);
                SetLerp(PoseLandmark.RightElbow,    0.69f, 0.66f, 0.76f, 0.64f, s, visBase * 0.92f);
                SetLerp(PoseLandmark.LeftWrist,     0.25f, 0.77f, 0.25f, 0.77f, s, visBase * 0.90f);
                SetLerp(PoseLandmark.RightWrist,    0.75f, 0.77f, 0.75f, 0.77f, s, visBase * 0.90f);
                // Hips static and LOW in the image, knees DIRECTLY UNDER them — the vertical-thigh
                // signature: kneeRel = (0.88 − 0.72)/0.28 ≈ 0.57 ≥ KneeRelAllFoursHard (0.50).
                SetLerp(PoseLandmark.LeftHip,  0.43f, 0.72f, 0.43f, 0.72f, 1f, hipVis);
                SetLerp(PoseLandmark.RightHip, 0.57f, 0.72f, 0.57f, 0.72f, 1f, hipVis);
                SetLerp(PoseLandmark.LeftKnee,  0.44f, 0.88f, 0.44f, 0.88f, 1f, kneeVis);
                SetLerp(PoseLandmark.RightKnee, 0.56f, 0.88f, 0.56f, 0.88f, 1f, kneeVis);
                SetLerp(PoseLandmark.LeftAnkle,     0.46f, 0.90f, 0.46f, 0.90f, 1f, ankleVis);
                SetLerp(PoseLandmark.RightAnkle,    0.54f, 0.90f, 0.54f, 0.90f, 1f, ankleVis);
                SetLerp(PoseLandmark.LeftFootIndex, 0.465f, 0.91f, 0.465f, 0.91f, 1f, ankleVis);
                SetLerp(PoseLandmark.RightFootIndex,0.535f, 0.91f, 0.535f, 0.91f, 1f, ankleVis);
            }

            ApplyJitter();
            return new PoseFrame((Landmark[])_buf.Clone(), null, timeSec, 1f);
        }

        private void SetLerp(PoseLandmark id, float xTop, float yTop, float xBot, float yBot, float s, float vis)
            => _buf[(int)id] = new Landmark(Mathf.Lerp(xBot, xTop, s), Mathf.Lerp(yBot, yTop, s), 0f, vis);

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
