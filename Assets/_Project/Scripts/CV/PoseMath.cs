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
    }
}
