namespace PushStars.CV
{
    /// <summary>Classifies tracking quality from a frame's key-joint visibilities. Shared by every
    /// <see cref="IPoseSource"/> so the calibration UI behaves the same regardless of backend.
    ///
    /// <para>Frontal rework (adopted from the owner's previous app, which tracked reliably in the
    /// same setup): quality comes from the UPPER 6 joints only — the rep signal lives in the arms
    /// and frontal hips/legs legitimately flap at the bottom of a rep — and uses the SECOND-lowest
    /// visibility, so a single flaky joint (one wrist momentarily occluded) can't flip the whole
    /// session to Lost mid-rep.</para></summary>
    public static class PoseQuality
    {
        static readonly PoseLandmark[] KeyJoints =
        {
            PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder,
            PoseLandmark.LeftElbow,    PoseLandmark.RightElbow,
            PoseLandmark.LeftWrist,    PoseLandmark.RightWrist,
        };

        /// <summary>Second-lowest visibility floor for LowVisibility (below → Lost).</summary>
        const float LowVisibilityFloor = 0.35f;

        public static TrackingQuality Classify(in PoseFrame frame)
        {
            if (!frame.IsValid) return TrackingQuality.Lost;

            // Track the two lowest visibilities in one pass — no sort, no alloc.
            float min1 = float.PositiveInfinity, min2 = float.PositiveInfinity;
            foreach (var j in KeyJoints)
            {
                float v = frame.Visibility(j);
                if (v < min1) { min2 = min1; min1 = v; }
                else if (v < min2) { min2 = v; }
            }

            if (min2 >= CVConstants.MinJointVisibility) return TrackingQuality.Good;
            if (min2 >= LowVisibilityFloor) return TrackingQuality.LowVisibility;
            return TrackingQuality.Lost;
        }
    }
}
