using UnityEngine;

namespace PushStars.CV
{
    /// <summary>Display-space conversion only. Never alters landmarks consumed by rep counting.</summary>
    public static class PoseRetargetMath
    {
        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        public static bool Finite(Quaternion q) => Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w);

        // World landmarks: image-right, image-down, away from the lens. Camera axes, not rig axes.
        public static Vector3 MapDirection(Vector3 direction, bool mirror, Quaternion cameraRotation)
            => cameraRotation * new Vector3(mirror ? -direction.x : direction.x, -direction.y, direction.z);

        public static PoseLandmark SwapSide(PoseLandmark id)
        {
            int n = (int)id;
            if (n >= 11 && n <= 32) return (PoseLandmark)(n % 2 == 1 ? n + 1 : n - 1);
            if (n >= 1 && n <= 3) return (PoseLandmark)(n + 3);
            if (n >= 4 && n <= 6) return (PoseLandmark)(n - 3);
            if (n == 7 || n == 9) return (PoseLandmark)(n + 1);
            if (n == 8 || n == 10) return (PoseLandmark)(n - 1);
            return id;
        }

        public static bool TryBasis(Vector3 right, Vector3 up, out Quaternion basis)
        {
            basis = Quaternion.identity;
            if (!Finite(right) || !Finite(up) || right.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-6f) return false;
            up.Normalize();
            right = Vector3.ProjectOnPlane(right, up);
            if (right.sqrMagnitude < 1e-6f) return false;
            basis = Quaternion.LookRotation(Vector3.Cross(right.normalized, up), up);
            return Finite(basis);
        }

        public static Quaternion Follow(Quaternion previous, Quaternion target, float rate, float speedDeg, float dt)
        {
            if (!Finite(target)) return previous;
            return Quaternion.RotateTowards(previous, Quaternion.Slerp(previous, target, 1f - Mathf.Exp(-rate * dt)), speedDeg * dt);
        }
    }
}
