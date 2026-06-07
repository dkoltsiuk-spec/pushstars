using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Per-frame classifier for the user's lower-body posture — toes-down (Straight) vs knees-down
    /// (Bent). The signal is the minimum of the visible left/right hip-knee-ankle interior angles:
    /// a real (toe) push-up holds it near 180°, a knee push-up has it near 90°.
    ///
    /// <para><b>Hysteresis dead zone</b> [<see cref="CVConstants.KneeBentMaxAngle"/>,
    /// <see cref="CVConstants.KneeStraightMinAngle"/>] suppresses flicker mid-pose. A classification
    /// change requires <see cref="CVConstants.KneeClassificationRibbonFrames"/> consecutive raw
    /// observations on the same side — single noisy frames (knee occluded by shorts, perspective
    /// jitter) won't flip the state.</para>
    ///
    /// <para>Angle is rotation-invariant (interior angle in image space), so the classification
    /// works regardless of camera orientation. World-coords path can be added later for better
    /// perspective handling but image space is the safe default.</para>
    /// </summary>
    public sealed class KneeBendDetector
    {
        private int _bentStreak;
        private int _straightStreak;

        /// <summary>Smoothed classification after the ribbon — Unknown until the first stable run.</summary>
        public KneeClassification Classification { get; private set; } = KneeClassification.Unknown;

        /// <summary>The minimum knee angle observed on the most recent frame (NaN if neither knee
        /// was computable). For HUD and telemetry — NOT the classification, just the raw number.</summary>
        public float LastMinKneeAngleDeg { get; private set; } = float.NaN;

        public void Reset()
        {
            _bentStreak = 0;
            _straightStreak = 0;
            Classification = KneeClassification.Unknown;
            LastMinKneeAngleDeg = float.NaN;
        }

        public void Tick(in PoseFrame frame)
        {
            if (!frame.IsValid) return;

            float angle = ComputeMinKneeAngle(frame);
            LastMinKneeAngleDeg = angle;

            if (float.IsNaN(angle))
            {
                // No knee visible this frame — break any in-progress streak (a stable
                // classification needs continuous evidence) but DON'T flip Classification.
                _bentStreak = 0;
                _straightStreak = 0;
                return;
            }

            if (angle <= CVConstants.KneeBentMaxAngle)
            {
                _bentStreak++;
                _straightStreak = 0;
                if (_bentStreak >= CVConstants.KneeClassificationRibbonFrames)
                    Classification = KneeClassification.Bent;
            }
            else if (angle >= CVConstants.KneeStraightMinAngle)
            {
                _straightStreak++;
                _bentStreak = 0;
                if (_straightStreak >= CVConstants.KneeClassificationRibbonFrames)
                    Classification = KneeClassification.Straight;
            }
            // Else: hysteresis dead zone — leave streaks and Classification untouched.
        }

        static float ComputeMinKneeAngle(in PoseFrame f)
        {
            float l = SideKneeAngle(f, PoseLandmark.LeftHip,  PoseLandmark.LeftKnee,  PoseLandmark.LeftAnkle);
            float r = SideKneeAngle(f, PoseLandmark.RightHip, PoseLandmark.RightKnee, PoseLandmark.RightAnkle);
            if (float.IsNaN(l) && float.IsNaN(r)) return float.NaN;
            if (float.IsNaN(l)) return r;
            if (float.IsNaN(r)) return l;
            return Mathf.Min(l, r);
        }

        static float SideKneeAngle(in PoseFrame f, PoseLandmark hip, PoseLandmark knee, PoseLandmark ankle)
        {
            if (f.Visibility(hip)   < CVConstants.MinJointVisibility) return float.NaN;
            if (f.Visibility(knee)  < CVConstants.MinJointVisibility) return float.NaN;
            if (f.Visibility(ankle) < CVConstants.MinJointVisibility) return float.NaN;
            return PoseMath.AngleDeg(f, hip, knee, ankle);
        }
    }
}
