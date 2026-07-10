namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Per-rep average key-joint visibility — catches reps where the skeleton was hallucinated
    /// throughout (background clutter, brief person-out-of-frame, severe occlusion). The per-frame
    /// <see cref="CVConstants.MinJointVisibility"/> tolerates single bad frames; a whole rep at
    /// that level isn't trustworthy.
    /// </summary>
    public sealed class RepVisibilityGate : IRepValidator
    {
        public string Name => "RepVisibility";

        public RepVote Validate(in RepWindow window)
        {
            // Side view: full 8-joint set (hips carry signal there). Frontal/Ambiguous/Unknown:
            // upper 6 joints only — frontal hips/legs legitimately flap at the bottom of a rep
            // and must not fail an honest rep; the rep signal lives in the arms.
            float meanVis = window.View == ViewKind.Side
                ? window.MeanKeyJointVisibility
                : window.MeanUpperBodyVisibility;
            if (meanVis < CVConstants.RepWindowMinVisibilityAvg)
                return RepVote.HardVeto(RepRejectReason.LowVisibility);
            if (meanVis < CVConstants.RepWindowSoftDockVisibilityAvg)
                return RepVote.Dock(CVConstants.PoorTrackingPenalty, RepRejectReason.PoorTracking);
            return RepVote.Pass;
        }
    }
}
