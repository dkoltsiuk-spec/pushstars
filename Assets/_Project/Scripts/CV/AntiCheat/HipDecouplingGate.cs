using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// "The torso is a rigid body during a push-up." In a real rep, hips and shoulders move
    /// together along the body axis (the whole upper body descends and rises). The "worm" cheat
    /// — hips going up and down while shoulders stay put, or vice versa — produces a low
    /// correlation between the two projections.
    ///
    /// <para>Note: the harshest version of the worm cheat (shoulder doesn't move at all) is
    /// already caught by <see cref="FullRomGate"/>. This validator handles the gentler version
    /// where both move but out of sync, and applies a SoftDock rather than a HardVeto. Aggressive
    /// HardVeto here would catch too many legitimate variations (slight pelvic rotation during
    /// fatigue, lifters with a strong glute squeeze).</para>
    /// </summary>
    public sealed class HipDecouplingGate : IRepValidator
    {
        public string Name => "HipDecoupling";

        public RepVote Validate(in RepWindow window)
        {
            if (window.Count < 6) return RepVote.Pass;

            if (!window.TryComputeBodyAxis(CVConstants.RepBodyAxisLeadFrames, out Vector2 spineDir, out _))
                return RepVote.Pass;

            // Same gravity proxy as FullRomGate: perpendicular to the spine direction (chest/hip
            // motion in a real pushup happens perpendicular to the spine, not along it).
            Vector2 gravityProxy = new Vector2(-spineDir.y, spineDir.x);

            // Build the two time-series in one pass — Pearson correlation only needs
            // sums, sums-of-squares, sum-of-products. Reject samples where either midpoint
            // was missing; need at least 6 paired points to be meaningful.
            float sumX = 0f, sumY = 0f, sumXX = 0f, sumYY = 0f, sumXY = 0f;
            int n = 0;
            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (!s.HasShoulderMid || !s.HasHipMid) continue;
                float x = Vector2.Dot(s.ShoulderMidImg, gravityProxy);
                float y = Vector2.Dot(s.HipMidImg,      gravityProxy);
                sumX += x; sumY += y;
                sumXX += x * x; sumYY += y * y;
                sumXY += x * y;
                n++;
            }
            if (n < 6) return RepVote.Pass;

            float meanX = sumX / n;
            float meanY = sumY / n;
            float varX = sumXX / n - meanX * meanX;
            float varY = sumYY / n - meanY * meanY;
            float cov  = sumXY / n - meanX * meanY;

            // Degenerate when one series is constant — can't correlate; fail open.
            if (varX < 1e-8f || varY < 1e-8f) return RepVote.Pass;

            float corr = cov / Mathf.Sqrt(varX * varY);

            if (corr < CVConstants.MinHipShoulderCorrelation)
                return RepVote.Dock(CVConstants.HipDecouplingPenalty, RepRejectReason.HipDecoupled);
            return RepVote.Pass;
        }
    }
}
