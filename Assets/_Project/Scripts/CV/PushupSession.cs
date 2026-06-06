using System;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// Runtime glue between a pose backend and the pushup analytics. Subscribes to an
    /// <see cref="IPoseSource"/>, feeds each frame into a <see cref="PushupRepCounter"/> and computes
    /// the live <see cref="FormReading"/>. This is the single component the duel HUD (phase 14) and
    /// training screen bind to for REPS / FORM / TEMPO.
    ///
    /// Drop a <c>MockPoseSource</c> in <see cref="_poseSourceBehaviour"/> to validate counting in the
    /// editor with no camera; swap to <c>MediaPipePoseSource</c> once the plugin is installed. Counting
    /// is gated on tracking quality so a lost skeleton never produces phantom reps.
    /// </summary>
    public sealed class PushupSession : MonoBehaviour
    {
        [Header("Pose source (must implement IPoseSource)")]
        [SerializeField] private MonoBehaviour _poseSourceBehaviour;

        [Header("Debug")]
        [SerializeField] private bool _logReps;

        public PushupRepCounter Counter { get; } = new PushupRepCounter();
        public FormReading LastForm { get; private set; }
        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;

        public int Reps => Counter.Reps;
        public PushupPhase Phase => Counter.Phase;
        public float TempoRpm => Counter.TempoRpm;
        public float Form => LastForm.Form;

        /// <summary>Status/error from the pose source (for the on-screen debug HUD).</summary>
        public string SourceStatus => _source != null ? _source.StatusMessage : "(no source)";

        /// <summary>Forwarded from the rep counter (new total each completed rep).</summary>
        public event Action<int> OnRep;
        /// <summary>Raised every processed frame with the latest form reading.</summary>
        public event Action<FormReading> OnFormUpdated;

        private IPoseSource _source;

        private void Awake()
        {
            _source = _poseSourceBehaviour as IPoseSource;
            if (_source == null)
            {
                Debug.LogError("[PushupSession] Pose source is missing or does not implement IPoseSource.");
                return;
            }
            Counter.OnRep += HandleRep;
        }

        private void OnEnable()
        {
            if (_source == null) return;
            _source.OnFrame += HandleFrame;
            _source.OnQualityChanged += HandleQuality;
            Quality = _source.Quality;
        }

        private void OnDisable()
        {
            if (_source == null) return;
            _source.OnFrame -= HandleFrame;
            _source.OnQualityChanged -= HandleQuality;
        }

        private void HandleQuality(TrackingQuality q) => Quality = q;

        private void OnDestroy()
        {
            Counter.OnRep -= HandleRep;
        }

        public void ResetSession() => Counter.Reset();

        private void HandleFrame(PoseFrame frame)
        {
            bool ok = Quality != TrackingQuality.Lost;
            Counter.Process(frame, ok);

            if (ok)
            {
                LastForm = FormScoreCalculator.Evaluate(frame);
                OnFormUpdated?.Invoke(LastForm);
            }
        }

        private void HandleRep(int reps)
        {
            if (_logReps)
                Debug.Log($"[PushupSession] Rep {reps} | phase={Phase} | form={Form:0} | tempo={TempoRpm:0} rpm");
            OnRep?.Invoke(reps);
        }
    }
}
