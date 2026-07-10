using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// "The chest must actually go down" — v2 with a view-adaptive projection axis (frontal
    /// addendum, fix for hypothesis H1).
    ///
    /// <para><b>Why v1 was broken frontally:</b> v1 projected shoulder motion onto the
    /// perpendicular of the image-space body axis. Frontally the body axis projects nearly
    /// VERTICAL (hips above shoulders in frame) while the chest also descends VERTICALLY — the
    /// perpendicular saw ~zero travel. Worse: with the frontal torso vector collapsed to ~0.05 of
    /// frame and ~0.01 jitter, the axis direction wobbled ±15°, making travelFrac a noise roulette
    /// (0.2–0.7) — honest reps randomly vetoed, cheats randomly passed.</para>
    ///
    /// <para><b>Axis by view:</b> Frontal → frame vertical (0,1) (also kills standing wall
    /// push-ups: shoulders don't move in y). Side → perpendicular of the body axis (v1 logic,
    /// still correct there). Unknown/Ambiguous → first principal component of the shoulderMid
    /// trajectory (closed-form 2×2 covariance eigenvector — view-agnostic, degrades gracefully).</para>
    ///
    /// <para><b>BodySwing rule</b> (mandatory companion): shoulder-width growth ≥ 1.15 with
    /// travelFrac &lt; 0.30 → HardVeto. The lean-toward-camera cheat and knee-rocking have the
    /// signature "width grows, y stays" — the OPPOSITE of an honest frontal rep (Δy 0.10–0.14 with
    /// width +4–8%). Width growth NEVER rescues a rep by itself — the earlier idea of "widthRatio
    /// confirms ROM" was rejected in review (it whitelisted exactly the lean-in cheat).</para>
    /// </summary>
    public sealed class FullRomGate : IRepValidator
    {
        public string Name => "FullRom";

        public RepVote Validate(in RepWindow window)
        {
            if (window.Count < 4) return RepVote.Pass;

            if (!window.TryComputeBodyAxis(CVConstants.RepBodyAxisLeadFrames, out Vector2 bodyAxis, out float scale))
                return RepVote.Pass; // no geometry — fail open

            Vector2 axis;
            switch (window.View)
            {
                case ViewKind.Frontal:
                    axis = Vector2.up; // frame vertical; sign irrelevant (max−min)
                    break;
                case ViewKind.Side:
                    axis = new Vector2(-bodyAxis.y, bodyAxis.x); // perpendicular of spine — v1 logic
                    break;
                default:
                    axis = PrincipalAxis(in window);
                    break;
            }

            float minProj = float.PositiveInfinity;
            float maxProj = float.NegativeInfinity;
            int used = 0;
            Vector2 origin = window[0].HasHipMid ? window[0].HipMidSq : Vector2.zero;
            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (!s.HasShoulderMid) continue;
                float p = RepWindow.Project(s.ShoulderMidSq, origin, axis);
                if (p < minProj) minProj = p;
                if (p > maxProj) maxProj = p;
                used++;
            }
            if (used < 4) return RepVote.Pass;

            float travelFrac = (maxProj - minProj) / scale;

            // BodySwing: approaching the camera without descending.
            window.ShoulderWidthRange(out float swMin, out float swMax);
            if (swMin > 1e-4f)
            {
                float widthRatio = swMax / swMin;
                if (widthRatio >= CVConstants.BodySwingWidthRatioMin
                    && travelFrac < CVConstants.BodySwingMaxTravelFrac)
                    return RepVote.HardVeto(RepRejectReason.BodySwing);
            }

            if (travelFrac < CVConstants.MinChestTravelFracHard)
                return RepVote.HardVeto(RepRejectReason.ChestNotLowered);
            if (travelFrac < CVConstants.MinChestTravelFracSoft)
                return RepVote.Dock(0.25f, RepRejectReason.ShallowTravel);
            return RepVote.Pass;
        }

        /// <summary>First principal component of the shoulderMid trajectory — closed-form 2×2
        /// covariance eigenvector: θ = ½·atan2(2·Sxy, Sxx − Syy). Single pass, zero alloc.</summary>
        private static Vector2 PrincipalAxis(in RepWindow window)
        {
            float sumX = 0f, sumY = 0f;
            int n = 0;
            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (!s.HasShoulderMid) continue;
                sumX += s.ShoulderMidSq.x;
                sumY += s.ShoulderMidSq.y;
                n++;
            }
            if (n < 2) return Vector2.up;
            float mx = sumX / n, my = sumY / n;

            float sxx = 0f, syy = 0f, sxy = 0f;
            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (!s.HasShoulderMid) continue;
                float dx = s.ShoulderMidSq.x - mx;
                float dy = s.ShoulderMidSq.y - my;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }
            float theta = 0.5f * Mathf.Atan2(2f * sxy, sxx - syy);
            return new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
        }
    }
}
