namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Caps the upper duration of a single rep. The existing <see cref="CVConstants.MinRepSeconds"/>
    /// in <see cref="PushupRepCounter"/> handles the lower bound (arm-flapping). This handles the
    /// upper bound: a rep candidate that took more than <see cref="CVConstants.MaxRepSeconds"/>
    /// almost certainly involved a mid-rep rest — not a real rep.
    /// </summary>
    public sealed class TempoSanityGate : IRepValidator
    {
        public string Name => "TempoSanity";

        public RepVote Validate(in RepWindow window)
        {
            if (window.DurationSec > CVConstants.MaxRepSeconds)
                return RepVote.HardVeto(RepRejectReason.TooSlow);
            return RepVote.Pass;
        }
    }
}
