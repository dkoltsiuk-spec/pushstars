using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// "Both arms must do the same work." Catches one-arm fakes (left arm holds plank, right arm
    /// "waves through" a rep) and asymmetric flailing.
    ///
    /// <para><b>Two thresholds:</b></para>
    /// <list type="bullet">
    ///   <item>Hard veto if the weaker arm's ROM is less than half the stronger arm's — true
    ///   asymmetry.</item>
    ///   <item>Soft dock if the angles are usually offset by more than ~20° on average — both arms
    ///   working but sloppy.</item>
    /// </list>
    ///
    /// <para><b>Skip rule.</b> Side-camera framings legitimately occlude one arm. If either arm
    /// is visible &lt; <see cref="CVConstants.SymmetryArmVisibilityThreshold"/> of the window's
    /// frames, this validator returns Pass — we can't compare what we can't see.</para>
    /// </summary>
    public sealed class BilateralSymmetryGate : IRepValidator
    {
        public string Name => "Symmetry";

        public RepVote Validate(in RepWindow window)
        {
            if (window.Count < 4) return RepVote.Pass;

            // Both arms must be visible most of the rep for the comparison to be meaningful.
            float leftFrac  = window.LeftArmVisibilityFraction;
            float rightFrac = window.RightArmVisibilityFraction;
            if (leftFrac  < CVConstants.SymmetryArmVisibilityThreshold ||
                rightFrac < CVConstants.SymmetryArmVisibilityThreshold)
                return RepVote.Pass;

            // Per-side ROM = max − min over frames where that side was visible.
            float lMin = float.PositiveInfinity, lMax = float.NegativeInfinity;
            float rMin = float.PositiveInfinity, rMax = float.NegativeInfinity;
            float sumAbsDiff = 0f;
            int bothVisibleFrames = 0;

            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (s.LeftArmVisible && !float.IsNaN(s.LeftElbowDeg))
                {
                    if (s.LeftElbowDeg < lMin) lMin = s.LeftElbowDeg;
                    if (s.LeftElbowDeg > lMax) lMax = s.LeftElbowDeg;
                }
                if (s.RightArmVisible && !float.IsNaN(s.RightElbowDeg))
                {
                    if (s.RightElbowDeg < rMin) rMin = s.RightElbowDeg;
                    if (s.RightElbowDeg > rMax) rMax = s.RightElbowDeg;
                }
                if (s.LeftArmVisible && s.RightArmVisible
                    && !float.IsNaN(s.LeftElbowDeg) && !float.IsNaN(s.RightElbowDeg))
                {
                    sumAbsDiff += Mathf.Abs(s.LeftElbowDeg - s.RightElbowDeg);
                    bothVisibleFrames++;
                }
            }

            if (lMax == float.NegativeInfinity || rMax == float.NegativeInfinity)
                return RepVote.Pass; // sanity — visibility fractions said yes but no usable data

            float lRom = lMax - lMin;
            float rRom = rMax - rMin;
            float maxRom = Mathf.Max(lRom, rRom);
            if (maxRom < 1e-3f) return RepVote.Pass; // both arms basically static; FullRom will catch
            float ratio = Mathf.Min(lRom, rRom) / maxRom;

            if (ratio < CVConstants.MinBilateralAmplitudeRatio)
                return RepVote.HardVeto(RepRejectReason.Asymmetric);

            // Soft dock on per-frame angle difference.
            if (bothVisibleFrames > 0)
            {
                float meanAbsDiff = sumAbsDiff / bothVisibleFrames;
                if (meanAbsDiff > CVConstants.MaxBilateralMeanAbsDiffDeg)
                    return RepVote.Dock(CVConstants.SlightAsymmetryPenalty, RepRejectReason.SlightAsymmetry);
            }

            return RepVote.Pass;
        }
    }
}
