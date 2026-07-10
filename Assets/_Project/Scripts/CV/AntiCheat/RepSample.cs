using UnityEngine;

namespace PushStars.CV.AntiCheat
{
    /// <summary>
    /// Per-frame snapshot captured by <see cref="AntiCheatAuditor.RecordSample"/> while the user
    /// is armed. Stored in a ring buffer and passed to per-rep validators via <see cref="RepWindow"/>.
    ///
    /// <para>All 2D positions/widths are in the aspect-corrected SQUARE space (frontal addendum) so
    /// distances along x and y are metrically comparable. Keeps only what validators need — a full
    /// PoseFrame at 30fps × 12s is too expensive to retain.</para>
    /// </summary>
    public readonly struct RepSample
    {
        public readonly float TimestampSec;
        public readonly PushupPhase Phase;

        /// <summary>Left shoulder-elbow-wrist interior angle (deg). NaN if not all visible.</summary>
        public readonly float LeftElbowDeg;
        /// <summary>Right shoulder-elbow-wrist interior angle (deg). NaN if not all visible.</summary>
        public readonly float RightElbowDeg;

        /// <summary>θs / θm from the AmplitudeTracker at this frame (NaN when tracker absent).</summary>
        public readonly float SmoothedElbowDeg;
        public readonly float MedianElbowDeg;

        /// <summary>Midpoint between shoulders (square space). Guarded by <see cref="HasShoulderMid"/>.</summary>
        public readonly Vector2 ShoulderMidSq;
        public readonly bool HasShoulderMid;

        public readonly Vector2 HipMidSq;
        public readonly bool HasHipMid;

        /// <summary>|LeftShoulder − RightShoulder| in square space; 0 when shoulders not visible.</summary>
        public readonly float ShoulderWidthSq;

        /// <summary>Body incline κ = (hipMid_y − shoulderMid_y) / sw. NaN when inputs missing.</summary>
        public readonly float Kappa;

        /// <summary>Knee midpoint Y (square space, visible knees averaged). Guarded by HasKneeMid.</summary>
        public readonly float KneeMidY;
        public readonly bool HasKneeMid;

        /// <summary>Wrist midpoint Y (square space). Guarded by HasWristMid.</summary>
        public readonly float WristMidY;
        public readonly bool HasWristMid;

        /// <summary>Ankle midpoint Y (square space). Guarded by HasAnkleMid.</summary>
        public readonly float AnkleMidY;
        public readonly bool HasAnkleMid;

        /// <summary>Average visibility of the 8 key joints on this frame (RepVisibilityGate).</summary>
        public readonly float KeyJointVisAvg;

        public readonly bool LeftArmVisible;
        public readonly bool RightArmVisible;

        private RepSample(
            float t, PushupPhase phase,
            float leftElbow, float rightElbow, float thetaS, float thetaM,
            Vector2 shoulderMid, bool hasShoulderMid,
            Vector2 hipMid, bool hasHipMid,
            float shoulderWidth, float kappa,
            float kneeMidY, bool hasKneeMid,
            float wristMidY, bool hasWristMid,
            float ankleMidY, bool hasAnkleMid,
            float keyJointVisAvg,
            bool leftArmVisible, bool rightArmVisible)
        {
            TimestampSec = t;
            Phase = phase;
            LeftElbowDeg = leftElbow;
            RightElbowDeg = rightElbow;
            SmoothedElbowDeg = thetaS;
            MedianElbowDeg = thetaM;
            ShoulderMidSq = shoulderMid;
            HasShoulderMid = hasShoulderMid;
            HipMidSq = hipMid;
            HasHipMid = hasHipMid;
            ShoulderWidthSq = shoulderWidth;
            Kappa = kappa;
            KneeMidY = kneeMidY;
            HasKneeMid = hasKneeMid;
            WristMidY = wristMidY;
            HasWristMid = hasWristMid;
            AnkleMidY = ankleMidY;
            HasAnkleMid = hasAnkleMid;
            KeyJointVisAvg = keyJointVisAvg;
            LeftArmVisible = leftArmVisible;
            RightArmVisible = rightArmVisible;
        }

        /// <summary>Build a sample from a frame + values the caller already computed once per frame
        /// (per-side elbows, tracker θs/θm) — the single-computation rule. Everything else is
        /// derived here in square space.</summary>
        public static RepSample From(in PoseFrame f, PushupPhase phase,
                                     float leftElbow, float rightElbow,
                                     float thetaS, float thetaM)
        {
            float aspect = f.Aspect;

            bool ls = f.Visibility(PoseLandmark.LeftShoulder)  >= CVConstants.MinJointVisibility;
            bool rs = f.Visibility(PoseLandmark.RightShoulder) >= CVConstants.MinJointVisibility;
            bool lh = f.Visibility(PoseLandmark.LeftHip)       >= CVConstants.MinJointVisibility;
            bool rh = f.Visibility(PoseLandmark.RightHip)      >= CVConstants.MinJointVisibility;

            Vector2 lsp = ls ? PoseMath.ToSquare(f.Get(PoseLandmark.LeftShoulder).Pos2D, aspect) : default;
            Vector2 rsp = rs ? PoseMath.ToSquare(f.Get(PoseLandmark.RightShoulder).Pos2D, aspect) : default;

            bool hasShMid = ls && rs;
            Vector2 shMid = hasShMid ? (lsp + rsp) * 0.5f : default;
            float sw = hasShMid ? Vector2.Distance(lsp, rsp) : 0f;

            bool hasHpMid = lh && rh;
            Vector2 hpMid = default;
            if (hasHpMid)
                hpMid = (PoseMath.ToSquare(f.Get(PoseLandmark.LeftHip).Pos2D, aspect)
                       + PoseMath.ToSquare(f.Get(PoseLandmark.RightHip).Pos2D, aspect)) * 0.5f;
            else if (lh || rh)
            {
                hpMid = PoseMath.ToSquare(f.Get(lh ? PoseLandmark.LeftHip : PoseLandmark.RightHip).Pos2D, aspect);
                hasHpMid = true; // single visible hip is an acceptable mid proxy
            }

            float kappa = (hasShMid && hasHpMid && sw > 1e-3f)
                ? (hpMid.y - shMid.y) / sw
                : float.NaN;

            MidY(f, PoseLandmark.LeftKnee, PoseLandmark.RightKnee, aspect, out float kneeY, out bool hasKnee);
            MidY(f, PoseLandmark.LeftWrist, PoseLandmark.RightWrist, aspect, out float wristY, out bool hasWrist);
            MidY(f, PoseLandmark.LeftAnkle, PoseLandmark.RightAnkle, aspect, out float ankleY, out bool hasAnkle);

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
                leftElbow, rightElbow, thetaS, thetaM,
                shMid, hasShMid,
                hpMid, hasHpMid,
                sw, kappa,
                kneeY, hasKnee,
                wristY, hasWrist,
                ankleY, hasAnkle,
                visAvg,
                leftArm, rightArm);
        }

        static void MidY(in PoseFrame f, PoseLandmark a, PoseLandmark b, float aspect,
                         out float midY, out bool has)
        {
            bool va = f.Visibility(a) >= CVConstants.MinJointVisibility;
            bool vb = f.Visibility(b) >= CVConstants.MinJointVisibility;
            if (va && vb) { midY = 0.5f * (f.Get(a).Y + f.Get(b).Y); has = true; return; }
            if (va) { midY = f.Get(a).Y; has = true; return; }
            if (vb) { midY = f.Get(b).Y; has = true; return; }
            midY = 0f; has = false;
        }
    }
}
