namespace PushStars.CV
{
    /// <summary>Classifies tracking quality from a frame's key-joint visibilities. Shared by every
    /// <see cref="IPoseSource"/> so the calibration UI behaves the same regardless of backend.</summary>
    public static class PoseQuality
    {
        // Joints the pushup pipeline relies on (elbow angle + body line).
        static readonly PoseLandmark[] KeyJoints =
        {
            PoseLandmark.LeftShoulder, PoseLandmark.RightShoulder,
            PoseLandmark.LeftElbow,    PoseLandmark.RightElbow,
            PoseLandmark.LeftWrist,    PoseLandmark.RightWrist,
            PoseLandmark.LeftHip,      PoseLandmark.RightHip,
        };

        public static TrackingQuality Classify(in PoseFrame frame)
        {
            if (!frame.IsValid) return TrackingQuality.Lost;

            float sum = 0f;
            foreach (var j in KeyJoints) sum += frame.Visibility(j);
            float avg = sum / KeyJoints.Length;

            if (avg >= CVConstants.GoodVisibility) return TrackingQuality.Good;
            if (avg >= CVConstants.MinJointVisibility) return TrackingQuality.LowVisibility;
            return TrackingQuality.Lost;
        }
    }
}
