using System;

namespace PushStars.CV
{
    /// <summary>How well the skeleton is currently tracked — drives the calibration UI's
    /// "ОК / потеря скелета" indicator and gates the rep counter.</summary>
    public enum TrackingQuality
    {
        /// <summary>No frames yet / source not started.</summary>
        None = 0,
        /// <summary>Key joints visible and confident — counting is reliable.</summary>
        Good = 1,
        /// <summary>Person detected but key joints are low-confidence (bad light / angle / distance).</summary>
        LowVisibility = 2,
        /// <summary>No usable skeleton this frame (out of frame / occluded).</summary>
        Lost = 3,
    }

    /// <summary>
    /// Abstraction over a pose backend. The real source is <c>MediaPipePoseSource</c> (Homuler,
    /// gated behind a scripting define); <see cref="MockPoseSource"/> drives the pipeline in the
    /// editor without a camera or the plugin. Consumers subscribe to <see cref="OnFrame"/> and
    /// react to <see cref="OnQualityChanged"/>.
    /// </summary>
    public interface IPoseSource
    {
        /// <summary>Raised once per tracked frame on the main thread.</summary>
        event Action<PoseFrame> OnFrame;

        /// <summary>Raised when <see cref="Quality"/> transitions to a new state.</summary>
        event Action<TrackingQuality> OnQualityChanged;

        TrackingQuality Quality { get; }
        bool IsRunning { get; }

        /// <summary>Human-readable status / last error for on-screen diagnostics.</summary>
        string StatusMessage { get; }

        void StartTracking();
        void StopTracking();
    }
}
