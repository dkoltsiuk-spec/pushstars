using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// FRONTAL knee-cheat mock — the S-KNEE-1 acceptance scenario with a CLEAN baseline:
    /// the user arms honestly (toes down, first <see cref="_honestSeconds"/> in a valid top plank),
    /// then drops to the knees and keeps "repping". Knees fall ~+0.045 normalized y relative to the
    /// hips (Δ ≈ +0.24·sw against the 0.12 disarm / 0.15 veto thresholds — 2× margin).
    ///
    /// Expected: arming succeeds during the honest phase; after the drop, KneeDropDetector triggers
    /// per-frame disarm (reason KneesBent) and/or KneeCheatGate hard-vetoes the rep. Reps must NOT
    /// grow after the drop. (The poisoned-baseline variant — arming already on knees with legs
    /// invisible — is accepted MVP risk #1 and intentionally not modeled here.)
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
            // Knees must be VISIBLE for the clean-baseline scenario (that's what we're testing).
            float hipVis  = visBase * 0.65f;
            float kneeVis = visBase * 0.60f;
            float ankleVis = visBase * (kneesDown ? 0.15f : 0.30f); // shins lift → feet fade (FootVanish)

            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Landmark(0.5f, 0.5f, 0f, 0.05f);

            // Same honest frontal table for the upper body; reduced ROM after the knee drop
            // (knee push-ups travel less) — s compresses toward the top half.
            float sBody = kneesDown ? 0.5f + 0.5f * s : s;

            SetLerp(PoseLandmark.Nose,          0.50f, 0.55f, 0.50f, 0.68f, sBody, visBase * 0.99f);
            SetLerp(PoseLandmark.LeftShoulder,  0.36f, 0.54f, 0.35f, 0.66f, sBody, visBase * 0.97f);
            SetLerp(PoseLandmark.RightShoulder, 0.64f, 0.54f, 0.65f, 0.66f, sBody, visBase * 0.97f);
            SetLerp(PoseLandmark.LeftElbow,     0.31f, 0.66f, 0.24f, 0.64f, s,     visBase * 0.92f);
            SetLerp(PoseLandmark.RightElbow,    0.69f, 0.66f, 0.76f, 0.64f, s,     visBase * 0.92f);
            SetLerp(PoseLandmark.LeftWrist,     0.25f, 0.77f, 0.25f, 0.77f, s,     visBase * 0.90f);
            SetLerp(PoseLandmark.RightWrist,    0.75f, 0.77f, 0.75f, 0.77f, s,     visBase * 0.90f);

            // Hips: kneeling tilts the torso up → hips sit lower in frame (κ grows toward ~0.36).
            float hipYTop = kneesDown ? 0.635f : 0.59f;
            float hipYBot = kneesDown ? 0.655f : 0.64f;
            SetLerp(PoseLandmark.LeftHip,  0.42f, hipYTop, 0.43f, hipYBot, sBody, hipVis);
            SetLerp(PoseLandmark.RightHip, 0.58f, hipYTop, 0.57f, hipYBot, sBody, hipVis);

            // Knees: honest ≈ hip_y + 0.05; kneeling → PLANTED low (+0.045 extra relative to hips
            // → Δ ≈ +0.24·sw against the arming baseline).
            float kneeY = kneesDown ? 0.755f : 0.64f;
            SetLerp(PoseLandmark.LeftKnee,  0.44f, kneeY, 0.44f, kneeY, 1f, kneeVis);
            SetLerp(PoseLandmark.RightKnee, 0.56f, kneeY, 0.56f, kneeY, 1f, kneeVis);

            float ankleY = kneesDown ? 0.62f : 0.66f; // shins lifted → ankles rise above the knees
            SetLerp(PoseLandmark.LeftAnkle,     0.46f, ankleY, 0.46f, ankleY, 1f, ankleVis);
            SetLerp(PoseLandmark.RightAnkle,    0.54f, ankleY, 0.54f, ankleY, 1f, ankleVis);
            SetLerp(PoseLandmark.LeftFootIndex, 0.465f, ankleY, 0.465f, ankleY, 1f, ankleVis);
            SetLerp(PoseLandmark.RightFootIndex,0.535f, ankleY, 0.535f, ankleY, 1f, ankleVis);

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
