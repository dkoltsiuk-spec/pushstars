using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// "The torso is a rigid body during a push-up" — hips and shoulders move together. The gentler
    /// "worm" (both move, out of sync) gets a SoftDock; the harsh worm (shoulders static) is already
    /// killed by <see cref="FullRomGate"/>.
    ///
    /// <para><b>Side view</b>: v1 logic — Pearson correlation of shoulder/hip projections on the
    /// gravity proxy (perpendicular of the body axis), floor 0.6.</para>
    ///
    /// <para><b>Frontal / Ambiguous / Unknown</b>: correlate raw image-Y series (the frontal motion
    /// axis), floor LOWERED to 0.45 — perspective squeezes hip travel to Δy ≈ 0.05 and the noise
    /// floor eats correlation. Adds the hipDropRatio band [0.15, 1.1]: hips must travel between 15%
    /// and 110% of the shoulder excursion. hipDropRatio &lt; 0.15 doubles as the only (soft) mid-set
    /// dropped-to-knees hint when knees are invisible (accepted MVP risk #2).</para>
    /// </summary>
    public sealed class HipDecouplingGate : IRepValidator
    {
        public string Name => "HipDecoupling";

        public RepVote Validate(in RepWindow window)
        {
            if (window.Count < 6) return RepVote.Pass;

            bool sideBranch = window.View == ViewKind.Side;

            Vector2 axis;
            if (sideBranch)
            {
                if (!window.TryComputeBodyAxis(CVConstants.RepBodyAxisLeadFrames, out Vector2 spineDir, out _))
                    return RepVote.Pass;
                axis = new Vector2(-spineDir.y, spineDir.x);
            }
            else
            {
                axis = Vector2.up; // frontal motion axis = image vertical
            }

            float sumX = 0f, sumY = 0f, sumXX = 0f, sumYY = 0f, sumXY = 0f;
            float shMin = float.PositiveInfinity, shMax = float.NegativeInfinity;
            float hpMin = float.PositiveInfinity, hpMax = float.NegativeInfinity;
            int n = 0;
            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (!s.HasShoulderMid || !s.HasHipMid) continue;
                float x = Vector2.Dot(s.ShoulderMidSq, axis);
                float y = Vector2.Dot(s.HipMidSq, axis);
                sumX += x; sumY += y;
                sumXX += x * x; sumYY += y * y;
                sumXY += x * y;
                if (x < shMin) shMin = x;
                if (x > shMax) shMax = x;
                if (y < hpMin) hpMin = y;
                if (y > hpMax) hpMax = y;
                n++;
            }
            if (n < 6) return RepVote.Pass;

            float meanX = sumX / n;
            float meanY = sumY / n;
            float varX = sumXX / n - meanX * meanX;
            float varY = sumYY / n - meanY * meanY;
            float cov  = sumXY / n - meanX * meanY;

            if (varX < 1e-8f || varY < 1e-8f) return RepVote.Pass; // constant series — fail open

            float corr = cov / Mathf.Sqrt(varX * varY);
            float corrFloor = sideBranch ? CVConstants.MinHipShoulderCorrelation
                                         : CVConstants.FrontalMinHipShoulderCorr;
            if (corr < corrFloor)
                return RepVote.Dock(CVConstants.HipDecouplingPenalty, RepRejectReason.HipDecoupled);

            if (!sideBranch)
            {
                float shoulderTravel = shMax - shMin;
                if (shoulderTravel > 1e-4f)
                {
                    float hipDropRatio = (hpMax - hpMin) / shoulderTravel;
                    if (hipDropRatio < CVConstants.HipDropRatioMin || hipDropRatio > CVConstants.HipDropRatioMax)
                        return RepVote.Dock(CVConstants.HipDecouplingPenalty, RepRejectReason.HipDecoupled);
                }
            }

            return RepVote.Pass;
        }
    }
}
