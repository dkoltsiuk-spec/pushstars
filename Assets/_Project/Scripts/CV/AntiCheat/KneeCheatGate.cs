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

            float sum = 0f;
            int n = 0;
            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (s.Phase != PushupPhase.Top || float.IsNaN(s.Kappa)) continue;
                sum += s.Kappa;
                n++;
            }
            if (n < 2) return RepVote.Pass; // hips invisible — fail open, FullRom carries the audit

            float kappaMean = sum / n;
            if (kappaMean > CVConstants.AllFoursKappaHardVeto)
                return RepVote.HardVeto(RepRejectReason.KneeCheat);
            if (kappaMean > CVConstants.AllFoursKappaSoftDock)
                return RepVote.Dock(0.25f, RepRejectReason.KneeCheat);

            return RepVote.Pass;
        }
    }
}
