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
            float meanVis = window.MeanKeyJointVisibility;
            if (meanVis < CVConstants.RepWindowMinVisibilityAvg)
                return RepVote.HardVeto(RepRejectReason.LowVisibility);
            if (meanVis < CVConstants.RepWindowSoftDockVisibilityAvg)
                return RepVote.Dock(CVConstants.PoorTrackingPenalty, RepRejectReason.PoorTracking);
            return RepVote.Pass;
        }
    }
}
