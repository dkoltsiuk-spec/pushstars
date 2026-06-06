using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Synthetic <see cref="IPoseSource"/> that fakes a person doing pushups — drives the whole CV
    /// pipeline (rep counter, form, HUD, calibration UI) in the editor with no camera and no
    /// MediaPipe plugin. The elbow angle oscillates between a top and bottom angle at a configurable
    /// tempo; a side-on skeleton is synthesized so <see cref="PoseMath"/> produces sensible elbow and
    /// body-line angles.
    ///
    /// Toggle <see cref="simulateLost"/> at runtime to test the "skeleton lost" UI path.
    /// </summary>
    public sealed class MockPoseSource : MonoBehaviour, IPoseSource
    {
        [Header("Motion")]
        [Tooltip("Reps per minute the fake lifter performs.")]
        [SerializeField] private float _tempoRpm = 40f;
        [SerializeField] private float _topElbowAngle = 172f;
        [SerializeField] private float _bottomElbowAngle = 72f;

        [Header("Debug")]
        [Tooltip("When on, frames report no visible skeleton (TrackingQuality.Lost).")]
        [SerializeField] private bool simulateLost;

        public event Action<PoseFrame> OnFrame;
        public event Action<TrackingQuality> OnQualityChanged;

        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;
        public bool IsRunning { get; private set; }

        private float _phase; // radians through the pushup cycle
        private readonly Landmark[] _buf = new Landmark[PoseLandmarks.Count];

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
            // Elbow angle as a cosine sweep: top at cos=+1, bottom at cos=-1.
            float s = (Mathf.Cos(_phase) + 1f) * 0.5f; // 1 at top, 0 at bottom
            float elbow = Mathf.Lerp(_bottomElbowAngle, _topElbowAngle, s);

            float vis = simulateLost ? 0f : 0.95f;
            for (int i = 0; i < _buf.Length; i++) _buf[i] = new Landmark(0.5f, 0.5f, 0f, vis);

            // Side-on plank, person facing left. Coords normalized (origin top-left, y down).
            // Body line is a straight horizontal plank; arms bend by `elbow`.
            const float bodyY = 0.55f;
            Set(PoseLandmark.LeftShoulder,  0.45f, bodyY,        vis);
            Set(PoseLandmark.RightShoulder, 0.45f, bodyY + 0.01f, vis);
            Set(PoseLandmark.LeftHip,       0.70f, bodyY,        vis);
            Set(PoseLandmark.RightHip,      0.70f, bodyY + 0.01f, vis);
            Set(PoseLandmark.LeftAnkle,     0.92f, bodyY,        vis);
            Set(PoseLandmark.RightAnkle,    0.92f, bodyY + 0.01f, vis);

            // Wrists planted on the ground; elbows rise/fall so the shoulder–elbow–wrist angle
            // equals `elbow`. Place the elbow by rotating the forearm from the wrist.
            float wristY = bodyY + 0.18f;
            PlaceArm(PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist,
                     0.40f, wristY, elbow, vis);
            PlaceArm(PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist,
                     0.40f, wristY + 0.01f, elbow, vis);

            return new PoseFrame((Landmark[])_buf.Clone(), timeSec);
        }

        // Builds a shoulder–elbow–wrist triangle whose interior angle at the elbow ≈ targetAngle.
        private void PlaceArm(PoseLandmark shoulder, PoseLandmark elbow, PoseLandmark wrist,
                              float wristX, float wristY, float targetAngle, float vis)
        {
            Vector2 sh = _buf[(int)shoulder].Pos2D;
            Vector2 wr = new Vector2(wristX, wristY);

            // Elbow sits between shoulder and wrist, pushed outward so the bend matches targetAngle.
            Vector2 mid = (sh + wr) * 0.5f;
            float half = Vector2.Distance(sh, wr) * 0.5f;
            // Larger bend (smaller angle) → elbow further from the shoulder–wrist line.
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
