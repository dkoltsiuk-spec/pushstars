using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// S-AIR-1 — "the wrists must be the support". Pure ORDERING comparisons along image-Y
    /// (invariant to phone tilt), evaluated over the rep's TOP frames:
    ///
    /// <list type="bullet">
    /// <item><b>P1</b>: wristMid below shoulders by ≥ 0.15·sw — hands planted under the chest, not
    /// waving in front of it.</item>
    /// <item><b>P2</b>: wristMid below hips by ≥ 0.15·sw — kills TABLE and WALL push-ups (inclined
    /// support puts the hands above the hip line in the image).</item>
    /// <item><b>P3</b> (only when a knee/ankle is visible): wrists not meaningfully above the leg
    /// landmarks — soft evidence only.</item>
    /// </list>
    ///
    /// P1/P2 fail → HardVeto SupportGeometry; P3 fail → SoftDock. Together with the armer's κ check
    /// and FullRom's BodySwing rule this closes: standing bobs with a squat, table lean-ins, wall
    /// push-ups, and the "elbows on a desk, bob the head" fake.
    /// </summary>
    public sealed class SupportGeometryGate : IRepValidator
    {
        public string Name => "SupportGeometry";

        public RepVote Validate(in RepWindow window)
        {
            if (window.Count < 4) return RepVote.Pass;

            float wristBelowShoulderSum = 0f; int p1n = 0;
            float wristBelowHipSum = 0f;      int p2n = 0;
            float wristVsKneeSum = 0f;        int p3kn = 0;
            float wristVsAnkleSum = 0f;       int p3an = 0;
            float swSum = 0f;                 int swn = 0;

            for (int i = 0; i < window.Count; i++)
            {
                var s = window[i];
                if (s.Phase != PushupPhase.Top) continue;
                if (s.ShoulderWidthSq > 1e-3f) { swSum += s.ShoulderWidthSq; swn++; }

                if (s.HasWristMid && s.HasShoulderMid)
                {
                    wristBelowShoulderSum += s.WristMidY - s.ShoulderMidSq.y;
                    p1n++;
                }
                if (s.HasWristMid && s.HasHipMid)
                {
                    wristBelowHipSum += s.WristMidY - s.HipMidSq.y;
                    p2n++;
                }
                if (s.HasWristMid && s.HasKneeMid)  { wristVsKneeSum  += s.WristMidY - s.KneeMidY;  p3kn++; }
                if (s.HasWristMid && s.HasAnkleMid) { wristVsAnkleSum += s.WristMidY - s.AnkleMidY; p3an++; }
            }

            if (swn == 0) return RepVote.Pass; // no scale — fail open
            float sw = swSum / swn;

            // P1 — hard. Y grows downward: wrist BELOW shoulder ⇒ positive difference.
            if (p1n >= 2 && wristBelowShoulderSum / p1n < CVConstants.SupportWristBelowShoulderFrac * sw)
                return RepVote.HardVeto(RepRejectReason.SupportGeometry);

            // P2 — hard (table/wall killer).
            if (p2n >= 2 && wristBelowHipSum / p2n < CVConstants.SupportWristBelowHipFrac * sw)
                return RepVote.HardVeto(RepRejectReason.SupportGeometry);

            // P3 — soft: wrists meaningfully ABOVE visible leg landmarks is suspicious but leg
            // landmarks are too flaky frontally for a hard call.
            bool p3Fail =
                (p3kn >= 2 && wristVsKneeSum  / p3kn < -CVConstants.SupportWristVsLegMarginFrac * sw) ||
                (p3an >= 2 && wristVsAnkleSum / p3an < -CVConstants.SupportWristVsLegMarginFrac * sw);
            if (p3Fail)
                return RepVote.Dock(0.25f, RepRejectReason.SupportGeometry);

            return RepVote.Pass;
        }
    }
}
