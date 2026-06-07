using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Per-frame snapshot captured by <see cref="AntiCheatAuditor.RecordSample"/> while the user
    /// is armed. Stored in a ring buffer and passed to per-rep validators via <see cref="RepWindow"/>.
    ///
    /// <para>Keeps only the values validators actually need — full PoseFrame is too expensive to
    /// retain at 30fps × 12s = 360 frames per rep. Image-space body frame
    /// (<see cref="ShoulderMidImg"/> / <see cref="HipMidImg"/>) is sufficient for Stage 2 signals;
    /// world coordinates can be added later for absolute meters if needed.</para>
    /// </summary>
    public readonly struct RepSample
    {
        public readonly float TimestampSec;
        public readonly PushupPhase Phase;

        /// <summary>Left shoulder-elbow-wrist interior angle (deg). NaN if not all visible.</summary>
        public readonly float LeftElbowDeg;
        /// <summary>Right shoulder-elbow-wrist interior angle (deg). NaN if not all visible.</summary>
        public readonly float RightElbowDeg;

        /// <summary>Midpoint between left/right shoulders in normalized image coords. Zero if the
        /// shoulders weren't both visible — <see cref="HasShoulderMid"/> guards.</summary>
        public readonly Vector2 ShoulderMidImg;
        public readonly bool HasShoulderMid;

        /// <summary>Midpoint between left/right hips in normalized image coords.</summary>
        public readonly Vector2 HipMidImg;
        public readonly bool HasHipMid;

        /// <summary>Average visibility of the 8 key joints (shoulders, elbows, wrists, hips) on
        /// this frame. Used by <see cref="RepVisibilityGate"/>.</summary>
        public readonly float KeyJointVisAvg;

        /// <summary>Whether the left full arm chain (shoulder + elbow + wrist) cleared
        /// <see cref="CVConstants.MinJointVisibility"/>. Used by <see cref="BilateralSymmetryGate"/>
        /// to decide whether to apply the symmetry check at all.</summary>
        public readonly bool LeftArmVisible;
        public readonly bool RightArmVisible;

        public RepSample(
            float t, PushupPhase phase,
            float leftElbow, float rightElbow,
            Vector2 shoulderMid, bool hasShoulderMid,
            Vector2 hipMid, bool hasHipMid,
            float keyJointVisAvg,
            bool leftArmVisible, bool rightArmVisible)
        {
            TimestampSec = t;
            Phase = phase;
            LeftElbowDeg = leftElbow;
            RightElbowDeg = rightElbow;
            ShoulderMidImg = shoulderMid;
            HasShoulderMid = hasShoulderMid;
            HipMidImg = hipMid;
            HasHipMid = hasHipMid;
            KeyJointVisAvg = keyJointVisAvg;
            LeftArmVisible = leftArmVisible;
            RightArmVisible = rightArmVisible;
        }

        /// <summary>Build a sample from a frame + the per-side elbow angles the caller computed
        /// once (avoids re-computing them inside the auditor).</summary>
        public static RepSample From(in PoseFrame f, PushupPhase phase, float leftElbow, float rightElbow)
        {
            bool ls = f.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = f.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            bool lh = f.Visibility(PoseLandmark.LeftHip)       >= CVConstants.MinJointVisibility;
            bool rh = f.Visibility(PoseLandmark.RightHip)      >= CVConstants.MinJointVisibility;

            Vector2 shMid = Vector2.zero;
            bool hasShMid = ls && rs;
            if (hasShMid)
                shMid = (f.Get(PoseLandmark.LeftShoulder).Pos2D + f.Get(PoseLandmark.RightShoulder).Pos2D) * 0.5f;

            Vector2 hpMid = Vector2.zero;
            bool hasHpMid = lh && rh;
            if (hasHpMid)
                hpMid = (f.Get(PoseLandmark.LeftHip).Pos2D + f.Get(PoseLandmark.RightHip).Pos2D) * 0.5f;

            // 8 key joints — same set as PoseQuality.Classify.
            float visSum =
                f.Visibility(PoseLandmark.LeftShoulder)  + f.Visibility(PoseLandmark.RightShoulder) +
                f.Visibility(PoseLandmark.LeftElbow)     + f.Visibility(PoseLandmark.RightElbow) +
                f.Visibility(PoseLandmark.LeftWrist)     + f.Visibility(PoseLandmark.RightWrist) +
                f.Visibility(PoseLandmark.LeftHip)       + f.Visibility(PoseLandmark.RightHip);
            float visAvg = visSum / 8f;

            bool leftArm =
                f.Visibility(PoseLandmark.LeftShoulder) >= CVConstants.MinJointVisibility &&
                f.Visibility(PoseLandmark.LeftElbow)    >= CVConstants.MinJointVisibility &&
                f.Visibility(PoseLandmark.LeftWrist)    >= CVConstants.MinJointVisibility;
            bool rightArm =
                f.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility &&
                f.Visibility(PoseLandmark.RightElbow)    >= CVConstants.MinJointVisibility &&
                f.Visibility(PoseLandmark.RightWrist)    >= CVConstants.MinJointVisibility;

            return new RepSample(
                f.TimestampSec, phase,
                leftElbow, rightElbow,
                shMid, hasShMid,
                hpMid, hasHpMid,
                visAvg,
                leftArm, rightArm);
        }
    }
}
