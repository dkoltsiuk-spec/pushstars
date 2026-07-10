using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Per-rep knee-cheat audit — two independent evidence channels aggregated:
    ///
    /// <para><b>KneeDropDelta</b> (needs knees visible): mean (kneeMid_y − hipMid_y)/sw over the
    /// rep's TOP frames vs the arming baseline. ≥ 0.15 → HardVeto, 0.10–0.15 → SoftDock.</para>
    ///
    /// <para><b>κ-drift</b> (works WITHOUT knee landmarks): mean body-incline κ over TOP frames vs
    /// the arming κ. Dropping to knees tilts the torso up → κ grows. ≥ 0.15 → HardVeto,
    /// ≥ 0.08 → SoftDock. Catches "armed honestly, then knelt" even with knees out of frame.</para>
    ///
    /// <para><b>Foot-event escalation</b>: a FootVanish/FootDrift event plus a sub-hard
    /// KneeDropDelta ≥ SoftDock threshold → escalated HardVeto; an event alone → SoftDock.</para>
    /// </summary>
    public sealed class KneeCheatGate : IRepValidator
    {
        private readonly KneeDropDetector _kneeDrop;
        private readonly FootEventMonitor _footMonitor;

        public string Name => "KneeCheat";

        public KneeCheatGate(KneeDropDetector kneeDrop, FootEventMonitor footMonitor)
        {
            _kneeDrop = kneeDrop;
            _footMonitor = footMonitor;
        }

        public RepVote Validate(in RepWindow window)
        {
            if (window.Count < 4) return RepVote.Pass;

            // Frontal-family signal only: kneeRel and κ divide by shoulder width, which collapses
            // to near-zero in the side view and turns both metrics into amplified jitter. Side-view
            // knee cheats are covered by KneeBendDetector in the armer.
            if (window.View == ViewKind.Side) return RepVote.Pass;

            // ── channel 1: mean knee-drop over TOP frames ──
            float kneeBaseline = _kneeDrop != null ? _kneeDrop.BaselineKneeRel : float.NaN;
            float meanDelta = float.NaN;
            if (!float.IsNaN(kneeBaseline))
            {
                float sum = 0f;
                int n = 0;
                for (int i = 0; i < window.Count; i++)
                {
                    var s = window[i];
                    if (s.Phase != PushupPhase.Top) continue;
                    if (!s.HasKneeMid || !s.HasHipMid || s.ShoulderWidthSq < 1e-3f) continue;
                    float kneeRel = (s.KneeMidY - s.HipMidSq.y) / s.ShoulderWidthSq;
                    sum += kneeRel - kneeBaseline;
                    n++;
                }
                if (n >= 2) meanDelta = sum / n;
            }

            if (!float.IsNaN(meanDelta))
            {
                if (meanDelta >= CVConstants.KneeDropDeltaHardVeto)
                    return RepVote.HardVeto(RepRejectReason.KneeCheat);
            }

            // ── channel 2: κ-drift from arming baseline ──
            float kappaArm = _kneeDrop != null ? _kneeDrop.KappaBaseline : float.NaN;
            float kappaDrift = float.NaN;
            if (!float.IsNaN(kappaArm))
            {
                float sum = 0f;
                int n = 0;
                for (int i = 0; i < window.Count; i++)
                {
                    var s = window[i];
                    if (s.Phase != PushupPhase.Top || float.IsNaN(s.Kappa)) continue;
                    sum += s.Kappa;
                    n++;
                }
                if (n >= 2) kappaDrift = sum / n - kappaArm;
            }

            if (!float.IsNaN(kappaDrift) && kappaDrift >= CVConstants.KappaDriftHardVeto)
                return RepVote.HardVeto(RepRejectReason.KneeCheat);

            // ── foot-event escalation ──
            bool footEvent = _footMonitor != null && _footMonitor.EventOccurred;
            bool subHardKnee = !float.IsNaN(meanDelta) && meanDelta >= CVConstants.KneeDropDeltaSoftDock;
            if (footEvent && subHardKnee)
                return RepVote.HardVeto(RepRejectReason.KneeCheat);

            // ── soft verdicts ──
            if (subHardKnee
                || (!float.IsNaN(kappaDrift) && kappaDrift >= CVConstants.KappaDriftSoftDock)
                || footEvent)
                return RepVote.Dock(0.25f, RepRejectReason.KneeCheat);

            return RepVote.Pass;
        }
    }
}
