using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// A pose source whose start-up (camera open, model load, first inference) can run before its
    /// own GameObject is active. The onboarding uses it to spend those long main-thread frames on
    /// a still screen instead of in the middle of the hand-off animation. Split from
    /// <see cref="IPoseSource"/> for the same reason as <see cref="ICameraFeed"/>: callers must not
    /// reference the define-gated MediaPipe assembly.
    /// </summary>
    public interface IPoseSourceWarmup
    {
        /// <summary>Starts tracking with <paramref name="host"/> running the start-up. When the
        /// source's own object is activated later, it adopts the running camera and model.</summary>
        void Warmup(MonoBehaviour host);

        /// <summary>Releases a warm-up that the source's own object never took over.</summary>
        void CancelWarmup();

        /// <summary>The camera and model are up (or failed) — no heavy start-up frames remain.</summary>
        bool IsWarm { get; }
    }
}
