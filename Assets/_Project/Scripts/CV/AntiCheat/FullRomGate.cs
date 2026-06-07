using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// "The chest must actually go down." Real push-ups move the shoulder-midpoint a significant
    /// distance along the body axis (away from the head, toward the floor) on the descent and back
    /// on the ascent. Fakes where the user just bends the elbows while the torso stays put (the
    /// wrist-snap cheat, the standing-arm-wave) fail this check.
    ///
    /// <para><b>Math.</b> Compute the body axis at the start of the rep
    /// (<see cref="RepWindow.TryComputeBodyAxis"/>, average shoulder→hip vector over the first
    /// few Top frames). Project shoulder-midpoint onto this axis for every sample. The travel
    /// (max − min of the projection) must reach <see cref="CVConstants.MinChestTravelFracBody"/>
    /// × torso scale. Body-relative threshold so camera zoom doesn't matter.</para>
    ///
    /// <para><b>Why body axis from Top frames specifically.</b> A real push-up rotates the torso
    /// slightly during the descent (shoulder rolls forward). Sampling the axis only from Top frames
    /// captures the "neutral" body orientation; projecting all frames onto that fixed axis gives
    /// the cleanest chest-travel signal.</para>
    /// </summary>
    public sealed class FullRomGate : IRepValidator
    {
        public string Name => "FullRom";

        public RepVote Validate(in RepWindow window)
        {
            if (window.Count < 4) return RepVote.Pass; // too short to judge; TempoSanity will handle

            if (!window.TryComputeBodyAxis(CVConstants.RepBodyAxisLeadFrames, out Vector2 spineDir, out float scale))
                return RepVote.Pass; // can't compute — fail open, don't punish honest users

            // Chest descent is perpendicular to the spine direction (in a plank: spine runs
            // head→feet horizontally, the chest moves vertically toward the floor). Take the 2D
            // perpendicular of the spine vector — direction doesn't matter (sign cancels in
            // max−min), magnitude does.
            Vector2 gravityProxy = new Vector2(-spineDir.y, spineDir.x);

            // Origin offset cancels out in (max − min), but use a stable reference for clarity.
            Vector2 origin = window[0].HasHipMid ? window[0].HipMidImg : Vector2.zero;

            float minProj =  float.PositiveInfinity;
            float maxProj = float.NegativeInfinity;
            int used = 0;
            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (!s.HasShoulderMid) continue;
                float p = RepWindow.Project(s.ShoulderMidImg, origin, gravityProxy);
                if (p < minProj) minProj = p;
                if (p > maxProj) maxProj = p;
                used++;
            }
            if (used < 4) return RepVote.Pass;

            float travel = maxProj - minProj;
            float travelFrac = travel / scale;

            if (travelFrac < CVConstants.MinChestTravelFracBody)
                return RepVote.HardVeto(RepRejectReason.ChestNotLowered);
            return RepVote.Pass;
        }
    }
}
