using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// ALL-FOURS cheat audit (policy change 2026-07-10: knee push-ups now COUNT as full reps —
    /// the owner's call is that real knee push-ups are honest work and the master signal is the
    /// elbow bend. What must NOT count is the all-fours rock: knees directly under the body,
    /// torso near-vertical, no push-up effort at all).
    ///
    /// <para><b>Discriminator: absolute body incline κ</b> = (hipMid_y − shoulderMid_y)/sw over
    /// the rep's TOP frames. Real knee push-up (body extended from the knees at ~30–45°):
    /// κ ≈ 0.3–0.5. All-fours: κ ≈ 0.7–1.2. Plank: κ ≈ 0.1–0.3. Absolute κ replaces the old
    /// baseline-drift approach, which would have punished the now-legal plank→knees transition.</para>
    ///
    /// <para>FullRomGate independently backs this up: on all-fours the shoulders barely descend
    /// (travelFrac &lt; 0.25 → ChestNotLowered) and rocking trips BodySwing. Foot events and the
    /// knee-drop delta are no longer penalized — lifted shins are EXPECTED in a legal knee
    /// push-up; both stay as telemetry.</para>
    /// </summary>
    public sealed class KneeCheatGate : IRepValidator
    {
        public string Name => "AllFours";

        public RepVote Validate(in RepWindow window)
        {
            if (window.Count < 4) return RepVote.Pass;

            // Frontal-family signal: κ divides by shoulder width, degenerate side-on. Side-view
            // all-fours reads as a broken body line in the armer instead.
            if (window.View == ViewKind.Side) return RepVote.Pass;

            // ── channel 1 (primary): vertical-thigh signature kneeRel over TOP frames ──
            // On-device round 2 proved κ alone can't separate all-fours (κ≈0.6) from an honest
            // close-camera plank (κ≈0.52); the thigh's image length can — see CVConstants.
            float kneeRelSum = 0f;
            int kneeRelN = 0;
            float kappaSum = 0f;
            int kappaN = 0;
            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (s.Phase != PushupPhase.Top) continue;
                if (s.HasKneeMid && s.HasHipMid && s.ShoulderWidthSq > 1e-3f)
                {
                    kneeRelSum += (s.KneeMidY - s.HipMidSq.y) / s.ShoulderWidthSq;
                    kneeRelN++;
                }
                if (!float.IsNaN(s.Kappa))
                {
                    kappaSum += s.Kappa;
                    kappaN++;
                }
            }

            if (kneeRelN >= 2)
            {
                float kneeRelMean = kneeRelSum / kneeRelN;
                if (kneeRelMean >= CVConstants.KneeRelAllFoursHard)
                    return RepVote.HardVeto(RepRejectReason.KneeCheat);
                if (kneeRelMean >= CVConstants.KneeRelAllFoursSoft)
                    return RepVote.Dock(0.25f, RepRejectReason.KneeCheat);
            }

            // ── channel 2 (fallback, knees invisible): absolute κ ──
            if (kappaN >= 2)
            {
                float kappaMean = kappaSum / kappaN;
                if (kappaMean > CVConstants.AllFoursKappaHardVeto)
                    return RepVote.HardVeto(RepRejectReason.KneeCheat);
                if (kappaMean > CVConstants.AllFoursKappaSoftDock)
                    return RepVote.Dock(0.25f, RepRejectReason.KneeCheat);
            }

            return RepVote.Pass; // nothing computable — fail open, FullRom carries the audit
        }
    }
}
