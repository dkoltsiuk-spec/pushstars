using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// A pose source that can also expose its live camera texture for an on-screen preview.
    /// Split from <see cref="IPoseSource"/> so UI code (the fight screen's camera background)
    /// can render the feed without referencing the define-gated MediaPipe assembly. Mocks
    /// simply don't implement it — the preview then stays hidden.
    /// </summary>
    public interface ICameraFeed
    {
        /// <summary>The raw camera texture, or null until the camera is up.</summary>
        WebCamTexture CameraTexture { get; }
    }
}
