using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// S-KNEE-1 — the primary frontal knee-cheat signal. The hip-knee-ankle ANGLE is uninformative
    /// frontally (the bend lies in the depth plane and projects collinear), but the knees' image
    /// POSITION relative to the hips is not: dropping to the knees lowers the knees ~13cm →
    /// ~0.24·sw in the image, against a jitter floor of 0.02–0.04.
    ///
    /// <para><c>kneeRel = (kneeMid_y − hipMid_y) / sw</c>. Baseline = mean over the arming window
    /// (captured on OnArmed); "now" = EMA over TOP-phase frames only (elbow extended — otherwise
    /// we'd measure descent kinematics). Δ = now − baseline. Also keeps the arming κ baseline for
    /// the κ-drift half of <see cref="KneeCheatGate"/>, which works even with knees invisible.</para>
    ///
    /// <para>Knees never visible → detector stays silent (fail-open, honest frontal users often
    /// have knee vis 0.2–0.6) + telemetry flag. The poisoned-baseline case (user armed ALREADY on
    /// knees with legs out of frame) is accepted MVP risk #1.</para>
    /// </summary>
    public sealed class KneeDropDetector
    {
        // rolling pre-arm accumulator (becomes the baseline at OnArmed)
        private float _preArmEma = float.NaN;
        private const float PreArmAlpha = 0.1f;

        private float _baselineKneeRel = float.NaN;
        private float _nowEma = float.NaN;
        private const float NowAlpha = 0.2f;

        private int _disarmRibbon;

        /// <summary>κ at arming (baseline for the per-rep κ-drift check). NaN until armed.</summary>
        public float KappaBaseline { get; private set; } = float.NaN;

        /// <summary>kneeRel baseline frozen at arming — consumed by <see cref="KneeCheatGate"/>'s
        /// per-rep re-derivation. NaN when knees were never visible before arming.</summary>
        public float BaselineKneeRel => _baselineKneeRel;

        /// <summary>Current Δ = kneeRel_now − kneeRel_baseline. NaN while unavailable.</summary>
        public float Delta { get; private set; } = float.NaN;

        /// <summary>Per-frame disarm signal: Δ ≥ KneeDropDeltaDisarm held for the full ribbon.
        /// PlankArmer's frontal branch consumes this as its "knees dropped" condition.</summary>
        public bool DisarmTriggered { get; private set; }

        /// <summary>True if a knee midpoint was ever visible since Reset (telemetry: risk #1 flag
        /// is the inverse — armed sessions where this never went true).</summary>
        public bool KneeEverVisible { get; private set; }

        public void Reset()
        {
            _preArmEma = float.NaN;
            _baselineKneeRel = float.NaN;
            _nowEma = float.NaN;
            _disarmRibbon = 0;
            KappaBaseline = float.NaN;
            Delta = float.NaN;
            DisarmTriggered = false;
            KneeEverVisible = false;
        }

        /// <summary>Freeze the baselines at the moment the armer fires OnArmed. κ comes from the
        /// session's per-frame computation (single-source rule).</summary>
        public void CaptureBaseline(float kappaAtArming)
        {
            _baselineKneeRel = _preArmEma; // NaN if knees were never visible pre-arm — stays silent
            _nowEma = float.NaN;
            KappaBaseline = kappaAtArming;
            Delta = float.NaN;
            DisarmTriggered = false;
            _disarmRibbon = 0;
        }

        /// <summary>Advance one frame. <paramref name="kneeRel"/> is NaN when knees/hips/sw are not
        /// computable this frame. <paramref name="elbowExtended"/> = raw elbow ≥ ArmingElbowTopAngle
        /// (Top-phase gating for the "now" EMA). <paramref name="isArmed"/> switches between
        /// baseline-accumulation and delta-tracking modes.</summary>
        public void Tick(float kneeRel, bool elbowExtended, bool isArmed)
        {
            if (!float.IsNaN(kneeRel)) KneeEverVisible = true;

            if (!isArmed)
            {
                if (!float.IsNaN(kneeRel))
                    _preArmEma = float.IsNaN(_preArmEma)
                        ? kneeRel
                        : _preArmEma + PreArmAlpha * (kneeRel - _preArmEma);
                DisarmTriggered = false;
                _disarmRibbon = 0;
                return;
            }

            if (float.IsNaN(_baselineKneeRel) || float.IsNaN(kneeRel) || !elbowExtended)
            {
                // No baseline, no knees this frame, or mid-descent — hold state, decay the ribbon.
                if (_disarmRibbon > 0) _disarmRibbon--;
                return;
            }

            _nowEma = float.IsNaN(_nowEma) ? kneeRel : _nowEma + NowAlpha * (kneeRel - _nowEma);
            Delta = _nowEma - _baselineKneeRel;

            if (Delta >= CVConstants.KneeDropDeltaDisarm)
            {
                _disarmRibbon++;
                if (_disarmRibbon >= CVConstants.KneeDropDisarmRibbonFrames)
                    DisarmTriggered = true;
            }
            else if (Delta <= CVConstants.KneeDropDeltaRelease)
            {
                _disarmRibbon = 0;
                DisarmTriggered = false;
            }
        }
    }
}
