using UnityEngine;

namespace PushStars.CV
{
    /// <summary>Geometry helpers over <see cref="PoseFrame"/> landmarks (2D image plane).</summary>
    public static class PoseMath
    {
        /// <summary>Interior angle (degrees) at <paramref name="vertex"/> formed by the segments to
        /// <paramref name="a"/> and <paramref name="b"/>. Returns 180 for degenerate input.</summary>
        public static float AngleDeg(Vector2 a, Vector2 vertex, Vector2 b)
        {
            Vector2 u = a - vertex;
            Vector2 v = b - vertex;
            float lu = u.magnitude, lv = v.magnitude;
            if (lu < 1e-6f || lv < 1e-6f) return 180f;
            float cos = Mathf.Clamp(Vector2.Dot(u, v) / (lu * lv), -1f, 1f);
            return Mathf.Acos(cos) * Mathf.Rad2Deg;
        }

        public static float AngleDeg(in PoseFrame f, PoseLandmark a, PoseLandmark vertex, PoseLandmark b)
            => AngleDeg(f.Get(a).Pos2D, f.Get(vertex).Pos2D, f.Get(b).Pos2D);

        /// <summary>Average of the left and right elbow angles (shoulder–elbow–wrist). Uses whichever
        /// side is visible; falls back to 180 if neither is.</summary>
        public static float ElbowAngle(in PoseFrame f)
        {
            bool left  = SideVisible(f, PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist);
            bool right = SideVisible(f, PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist);

            float l = left  ? AngleDeg(f, PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist)  : 0f;
            float r = right ? AngleDeg(f, PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist) : 0f;

            if (left && right) return 0.5f * (l + r);
            if (left)  return l;
            if (right) return r;
            return 180f;
        }

        /// <summary>Body-line angle (shoulder–hip–ankle), averaged across visible sides. 180 = straight
        /// plank; smaller = hips sagging or piking.</summary>
        public static float BodyLineAngle(in PoseFrame f)
        {
            bool left  = SideVisible(f, PoseLandmark.LeftShoulder, PoseLandmark.LeftHip, PoseLandmark.LeftAnkle);
            bool right = SideVisible(f, PoseLandmark.RightShoulder, PoseLandmark.RightHip, PoseLandmark.RightAnkle);

            float l = left  ? AngleDeg(f, PoseLandmark.LeftShoulder, PoseLandmark.LeftHip, PoseLandmark.LeftAnkle)  : 0f;
            float r = right ? AngleDeg(f, PoseLandmark.RightShoulder, PoseLandmark.RightHip, PoseLandmark.RightAnkle) : 0f;

            if (left && right) return 0.5f * (l + r);
            if (left)  return l;
            if (right) return r;
            return CVConstants.StraightBodyAngle;
        }

        static bool SideVisible(in PoseFrame f, PoseLandmark a, PoseLandmark b, PoseLandmark c)
            => f.Visibility(a) >= CVConstants.MinJointVisibility
            && f.Visibility(b) >= CVConstants.MinJointVisibility
            && f.Visibility(c) >= CVConstants.MinJointVisibility;

        static bool Vis(in PoseFrame f, PoseLandmark j) => f.Visibility(j) >= CVConstants.MinJointVisibility;

        /// <summary>Sanity gate: does this frame plausibly show a person in a pushup/plank position?
        /// Rejects the common false positives (waving arms while lying/sitting, only the torso in
        /// frame) so the rep counter can't credit non-pushup motion. All checks are rotation-invariant
        /// (joint visibility + interior angles), so they hold regardless of camera orientation.</summary>
        public static bool LooksLikePushup(in PoseFrame f)
        {
            if (!f.IsValid) return false;

            // 1) Torso must be in frame: both shoulders and both hips visible.
            if (!Vis(f, PoseLandmark.LeftShoulder) || !Vis(f, PoseLandmark.RightShoulder)) return false;
            if (!Vis(f, PoseLandmark.LeftHip)      || !Vis(f, PoseLandmark.RightHip))      return false;

            // 2) At least one full arm (to read the elbow angle that drives the FSM).
            bool armL = SideVisible(f, PoseLandmark.LeftShoulder,  PoseLandmark.LeftElbow,  PoseLandmark.LeftWrist);
            bool armR = SideVisible(f, PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist);
            if (!armL && !armR) return false;

            // 3) Legs must be in frame too (a real pushup shows the whole body; casual arm-waving at the
            //    phone usually shows only the upper body). Use ankle, falling back to knee.
            bool legL = Vis(f, PoseLandmark.LeftAnkle)  || Vis(f, PoseLandmark.LeftKnee);
            bool legR = Vis(f, PoseLandmark.RightAnkle) || Vis(f, PoseLandmark.RightKnee);
            if (!legL && !legR) return false;

            // 4) Body must be extended (plank), not curled/sitting. shoulder–hip–(ankle|knee) angle.
            return PlankBodyLine(f, out float plank) && plank >= CVConstants.MinPlankBodyLine;
        }

        /// <summary>Body-line angle using the ankle (preferred) or knee as the lower joint, averaged
        /// across visible sides. Returns false if neither side is computable.</summary>
        static bool PlankBodyLine(in PoseFrame f, out float angle)
        {
            float sum = 0f; int n = 0;
            if (TrySideLine(f, PoseLandmark.LeftShoulder,  PoseLandmark.LeftHip,  PoseLandmark.LeftAnkle,  PoseLandmark.LeftKnee,  out float l)) { sum += l; n++; }
            if (TrySideLine(f, PoseLandmark.RightShoulder, PoseLandmark.RightHip, PoseLandmark.RightAnkle, PoseLandmark.RightKnee, out float r)) { sum += r; n++; }
            angle = n > 0 ? sum / n : 0f;
            return n > 0;
        }

        static bool TrySideLine(in PoseFrame f, PoseLandmark shoulder, PoseLandmark hip,
                                PoseLandmark ankle, PoseLandmark knee, out float angle)
        {
            angle = 0f;
            if (!Vis(f, shoulder) || !Vis(f, hip)) return false;
            PoseLandmark lower;
            if (Vis(f, ankle))     lower = ankle;
            else if (Vis(f, knee)) lower = knee;
            else return false;
            angle = AngleDeg(f, shoulder, hip, lower);
            return true;
        }
    }
}
