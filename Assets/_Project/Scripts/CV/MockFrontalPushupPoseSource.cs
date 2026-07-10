using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Honest FRONTAL push-up mock (frontal addendum reference geometry): camera on the floor
    /// ~1.5–2m in front, head/wrists large and low, torso receding (hips above shoulders in image,
    /// torso length collapsed), legs barely visible with flapping visibility.
    ///
    /// Landmark keyframes from docs/plan/phase-08.1-frontal-addendum.md (§мок-таблица). Expected
    /// derived values: sw 0.28→0.30, shoulderMid Δy ≈ 0.12, torsoLen ≈ 0.05→0.02, κ_arm ≈ 0.18.
    /// Under the frontal pipeline: ViewClassifier → Frontal ≤ 1.5s, PlankArmer arms ≤ 2.5s, reps
    /// count cleanly through all gates.
    /// </summary>
    public sealed class MockFrontalPushupPoseSource : MonoBehaviour, IPoseSource
    {
        [Header("Motion")]
        [SerializeField] private float _tempoRpm = 40f;
        [Tooltip("Gaussian positional jitter sigma (normalized units) applied to every landmark.")]
        [SerializeField] private float _jitterSigma = 0.006f;

        [Header("Debug")]
        [SerializeField] private bool simulateLost;

        public event Action<PoseFrame> OnFrame;
        public event Action<TrackingQuality> OnQualityChanged;

        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;
        public bool IsRunning { get; private set; }
        public string StatusMessage => IsRunning ? "mock frontal" : "mock stopped";

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
            // s: 1 = TOP (arms locked), 0 = BOTTOM.
            float s = (Mathf.Cos(_phase) + 1f) * 0.5f;
            float visBase = simulateLost ? 0f : 1f;

            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Landmark(0.5f, 0.5f, 0f, 0.05f);

            // Flapping visibility on hips/legs (sin at unrelated frequencies, per the spec).
            float hipVis   = visBase * Mathf.Clamp01(0.60f + 0.12f * Mathf.Sin(timeSec * 1.7f));
            float kneeVis  = visBase * Mathf.Clamp01(0.40f + 0.15f * Mathf.Sin(timeSec * 2.3f + 1f));
            float ankleVis = visBase * Mathf.Clamp01(0.25f + 0.15f * Mathf.Sin(timeSec * 1.1f + 2f));

            SetLerp(PoseLandmark.Nose,          0.50f, 0.55f, 0.50f, 0.68f, s, visBase * 0.99f);
            SetLerp(PoseLandmark.LeftShoulder,  0.36f, 0.54f, 0.35f, 0.66f, s, visBase * 0.97f);
            SetLerp(PoseLandmark.RightShoulder, 0.64f, 0.54f, 0.65f, 0.66f, s, visBase * 0.97f);
            SetLerp(PoseLandmark.LeftElbow,     0.31f, 0.66f, 0.24f, 0.64f, s, visBase * 0.92f);
            SetLerp(PoseLandmark.RightElbow,    0.69f, 0.66f, 0.76f, 0.64f, s, visBase * 0.92f);
            // Wrists PLANTED — identical at both keyframes.
            SetLerp(PoseLandmark.LeftWrist,     0.25f, 0.77f, 0.25f, 0.77f, s, visBase * 0.90f);
            SetLerp(PoseLandmark.RightWrist,    0.75f, 0.77f, 0.75f, 0.77f, s, visBase * 0.90f);
            SetLerp(PoseLandmark.LeftHip,       0.41f, 0.59f, 0.42f, 0.64f, s, hipVis);
            SetLerp(PoseLandmark.RightHip,      0.59f, 0.59f, 0.58f, 0.64f, s, hipVis);
            SetLerp(PoseLandmark.LeftKnee,      0.44f, 0.64f, 0.44f, 0.65f, s, kneeVis);
            SetLerp(PoseLandmark.RightKnee,     0.56f, 0.64f, 0.56f, 0.65f, s, kneeVis);
            SetLerp(PoseLandmark.LeftAnkle,     0.46f, 0.66f, 0.46f, 0.66f, s, ankleVis);
            SetLerp(PoseLandmark.RightAnkle,    0.54f, 0.66f, 0.54f, 0.66f, s, ankleVis);
            SetLerp(PoseLandmark.LeftFootIndex, 0.465f, 0.665f, 0.465f, 0.665f, s, ankleVis);
            SetLerp(PoseLandmark.RightFootIndex,0.535f, 0.665f, 0.535f, 0.665f, s, ankleVis);

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
                _buf[i] = new Landmark(
                    lm.X + Gauss() * _jitterSigma,
                    lm.Y + Gauss() * _jitterSigma,
                    lm.Z, lm.Visibility);
            }
        }

        // Box-Muller — cheap gaussian from two uniforms.
        private static float Gauss()
        {
            float u1 = Mathf.Max(UnityEngine.Random.value, 1e-6f);
            float u2 = UnityEngine.Random.value;
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }
    }
}
