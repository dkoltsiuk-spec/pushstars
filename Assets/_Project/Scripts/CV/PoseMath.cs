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

        /// <summary>Convert a normalized-image point to the aspect-corrected "square" space where
        /// x and y are metrically comparable (x' = x·(W/H)). All frontal-view (phase 08.1 addendum)
        /// metrics — shoulder width, torso length, κ incline, drift — are computed here so a circle
        /// on screen measures as a circle regardless of the source resolution.</summary>
        public static Vector2 ToSquare(Vector2 p, float aspect)
            => new Vector2(p.x * Mathf.Max(aspect, 1e-3f), p.y);

        /// <summary>Like <see cref="ElbowAngle"/> but returns false instead of the 180° sentinel
        /// when NEITHER full arm (shoulder+elbow+wrist) clears MinJointVisibility. The sentinel must
        /// never enter the AmplitudeTracker filter chain: a one-frame visibility dropout at the
        /// bottom of a rep would read as 92→180→92 and punch a false top-latch through the
        /// One-Euro filter (which is deliberately fast on large jumps).</summary>
        public static bool TryElbowAngle(in PoseFrame f, out float angleDeg)
        {
            bool left  = SideVisible(f, PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist);
            bool right = SideVisible(f, PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist);
            if (!left && !right)
            {
                angleDeg = 180f;
                return false;
            }

            float l = left  ? AngleDeg(f, PoseLandmark.LeftShoulder, PoseLandmark.LeftElbow, PoseLandmark.LeftWrist)  : 0f;
            float r = right ? AngleDeg(f, PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist) : 0f;
            angleDeg = (left && right) ? 0.5f * (l + r) : (left ? l : r);
            return true;
        }

        /// <summary>Per-side elbow angle, NaN when that arm isn't fully visible. Used by anti-cheat
        /// (phase 08.1) bilateral symmetry — averaging the two sides hides the cheat where one arm
        /// stays straight while the other does the work.</summary>
        public static float SideElbowAngle(in PoseFrame f, bool left)
        {
            PoseLandmark shoulder = left ? PoseLandmark.LeftShoulder : PoseLandmark.RightShoulder;
            PoseLandmark elbow    = left ? PoseLandmark.LeftElbow    : PoseLandmark.RightElbow;
            PoseLandmark wrist    = left ? PoseLandmark.LeftWrist    : PoseLandmark.RightWrist;
            if (!SideVisible(f, shoulder, elbow, wrist)) return float.NaN;
            return AngleDeg(f, shoulder, elbow, wrist);
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

        /// <summary>Sanity gate: does this frame plausibly show a person doing a pushup? Rejects casual
        /// non-pushup motion (e.g. waving the arms) without rejecting real reps when the camera framing
        /// tightens at the bottom (legs/lower body leave the frame). All checks are rotation-invariant
        /// (joint visibility + interior angles), so they hold regardless of camera orientation.</summary>
        public static bool LooksLikePushup(in PoseFrame f)
        {
            if (!f.IsValid) return false;

            // 1) Both shoulders in frame.
            if (!Vis(f, PoseLandmark.LeftShoulder) || !Vis(f, PoseLandmark.RightShoulder)) return false;

            // 2) At least one full arm (to read the elbow angle that drives the FSM).
            bool armL = SideVisible(f, PoseLandmark.LeftShoulder,  PoseLandmark.LeftElbow,  PoseLandmark.LeftWrist);
            bool armR = SideVisible(f, PoseLandmark.RightShoulder, PoseLandmark.RightElbow, PoseLandmark.RightWrist);
            if (!armL && !armR) return false;

            // 3) If — and only if — the lower body is visible, require an extended (plank) line so a
            //    curled/sitting pose is rejected. At the bottom of a real pushup the legs often leave
            //    the frame; then this check is simply skipped and the elbow-angle + min-duration gates
            //    in PushupRepCounter carry the detection. This is what previously broke counting: the
            //    bottom frame was dropped because the legs weren't visible.
            if (PlankBodyLine(f, out float plank) && plank < CVConstants.MinPlankBodyLine)
                return false;

            return true;
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
