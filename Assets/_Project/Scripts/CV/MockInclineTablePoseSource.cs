using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// TABLE / incline-lean cheat mock (frontal): the user leans onto a table edge toward the
    /// camera and bends the elbows — mechanically a "rep" but ~10× easier. Signature: wrists sit
    /// ABOVE the hip line in the image (support is elevated), shoulder width balloons through the
    /// rep (whole body approaches the camera, widthRatio ≈ 1.3) while shoulderMid barely moves
    /// vertically (travelFrac &lt; 0.15).
    ///
    /// Expected: PlankArmer F1 refuses to arm (wrists not below shoulders). If arming is somehow
    /// reached (Ambiguous OR-branch), SupportGeometryGate P2 hard-vetoes and FullRomGate's
    /// BodySwing rule backs it up. Reps must stay 0.
    /// </summary>
    public sealed class MockInclineTablePoseSource : MonoBehaviour, IPoseSource
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
        public string StatusMessage => IsRunning ? "mock table-lean" : "mock stopped";

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
            float s = (Mathf.Cos(_phase) + 1f) * 0.5f; // 1 = away (top), 0 = leaned in (bottom)
            float visBase = simulateLost ? 0f : 1f;

            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Landmark(0.5f, 0.5f, 0f, 0.05f);

            // Standing at a table: body upright-ish, whole silhouette approaches the camera on the
            // "descent" — shoulder width grows 0.28 → 0.365 (ratio ≈ 1.3), shoulderMid y almost
            // static (±0.02). Wrists on the table edge: ABOVE the hips in the image.
            float grow = Mathf.Lerp(0.30f, 0f, s);            // 0 at top, 0.30 leaned in
            float halfSw = 0.14f * (1f + grow);               // sw: 0.28 → 0.364
            float shoulderY = 0.50f + 0.02f * (1f - s);       // barely moves

            SetXY(PoseLandmark.Nose,          0.50f, shoulderY - 0.06f, visBase * 0.99f);
            SetXY(PoseLandmark.LeftShoulder,  0.50f - halfSw, shoulderY, visBase * 0.97f);
            SetXY(PoseLandmark.RightShoulder, 0.50f + halfSw, shoulderY, visBase * 0.97f);

            // Arms bend genuinely (the cheat's whole point): elbow angle sweeps ~170 → ~90.
            float bend = 1f - s;
            SetXY(PoseLandmark.LeftElbow,  0.50f - halfSw - 0.05f, shoulderY + 0.10f - 0.06f * bend, visBase * 0.93f);
            SetXY(PoseLandmark.RightElbow, 0.50f + halfSw + 0.05f, shoulderY + 0.10f - 0.06f * bend, visBase * 0.93f);
            // Wrists on the table edge — HIGH in the image (~0.45), above the hips (0.62).
            SetXY(PoseLandmark.LeftWrist,  0.50f - halfSw - 0.02f, 0.45f, visBase * 0.92f);
            SetXY(PoseLandmark.RightWrist, 0.50f + halfSw + 0.02f, 0.45f, visBase * 0.92f);

            SetXY(PoseLandmark.LeftHip,  0.44f, 0.62f, visBase * 0.80f);
            SetXY(PoseLandmark.RightHip, 0.56f, 0.62f, visBase * 0.80f);
            SetXY(PoseLandmark.LeftKnee,  0.45f, 0.78f, visBase * 0.75f);
            SetXY(PoseLandmark.RightKnee, 0.55f, 0.78f, visBase * 0.75f);
            SetXY(PoseLandmark.LeftAnkle,  0.46f, 0.92f, visBase * 0.70f);
            SetXY(PoseLandmark.RightAnkle, 0.54f, 0.92f, visBase * 0.70f);
            SetXY(PoseLandmark.LeftFootIndex,  0.465f, 0.94f, visBase * 0.65f);
            SetXY(PoseLandmark.RightFootIndex, 0.535f, 0.94f, visBase * 0.65f);

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
