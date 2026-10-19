using System;
using System.IO;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// One <see cref="GhostMotionClip.Frame"/> on the wire: the same Humanoid pose the ghost
    /// recorder captures, quantised for a live stream. Muscles go as 16-bit (±2 range, ~0.0001
    /// steps — far below what a rendered rig can show), rotations as four 16-bit components,
    /// positions stay floats. 236 bytes a frame; at 12 Hz about 2.8 KB/s per player.
    /// </summary>
    public static class LivePoseCodec
    {
        public const byte Version = 1;
        public const int FrameBytes = 2 + 4 + 12 + 8 + 12 + 8 + GhostMotionClip.MuscleCount * 2;
        private const float MuscleRange = 2f, MuscleScale = 16383f;

        public static byte[] Encode(GhostMotionClip.Frame frame)
        {
            if (frame?.muscles == null || frame.muscles.Length != GhostMotionClip.MuscleCount)
                throw new ArgumentException("A frame needs every Humanoid muscle.");
            var bytes = new byte[FrameBytes];
            using var w = new BinaryWriter(new MemoryStream(bytes));
            w.Write(Version); w.Write(frame.phase); w.Write(frame.time);
            Write(w, frame.bodyPosition); Write(w, frame.bodyRotation);
            Write(w, frame.rootPosition); Write(w, frame.rootRotation);
            foreach (float m in frame.muscles)
                w.Write((short)Mathf.RoundToInt(Mathf.Clamp(Finite(m), -MuscleRange, MuscleRange) * MuscleScale));
            return bytes;
        }

        /// <summary>Rejects anything malformed rather than posing a body from it: the bytes come
        /// from another phone.</summary>
        public static bool TryDecode(byte[] bytes, out GhostMotionClip.Frame frame)
        {
            frame = null;
            if (bytes == null || bytes.Length != FrameBytes || bytes[0] != Version) return false;
            using var r = new BinaryReader(new MemoryStream(bytes));
            r.ReadByte();
            var f = new GhostMotionClip.Frame { phase = r.ReadByte(), time = r.ReadSingle(), muscles = new float[GhostMotionClip.MuscleCount] };
            if (f.phase > GhostMotionClip.Live || !IsFinite(f.time) || f.time < 0 || f.time > 3600) return false;
            if (!TryVector(r, out f.bodyPosition) || !TryRotation(r, out f.bodyRotation)
                || !TryVector(r, out f.rootPosition) || !TryRotation(r, out f.rootRotation)) return false;
            for (int i = 0; i < f.muscles.Length; i++) f.muscles[i] = r.ReadInt16() / MuscleScale;
            frame = f;
            return true;
        }

        private static void Write(BinaryWriter w, Vector3 v) { w.Write(Finite(v.x)); w.Write(Finite(v.y)); w.Write(Finite(v.z)); }
        private static void Write(BinaryWriter w, Quaternion q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (!IsFinite(n) || n < 1e-6f) q = Quaternion.identity; else { q.x /= n; q.y /= n; q.z /= n; q.w /= n; }
            w.Write(Pack(q.x)); w.Write(Pack(q.y)); w.Write(Pack(q.z)); w.Write(Pack(q.w));
        }
        private static short Pack(float unit) => (short)Mathf.RoundToInt(Mathf.Clamp(unit, -1f, 1f) * 32767f);
        private static bool TryVector(BinaryReader r, out Vector3 v)
        {
            v = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            return IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z) && v.sqrMagnitude < 1e6f;
        }
        private static bool TryRotation(BinaryReader r, out Quaternion q)
        {
            q = new Quaternion(r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f, r.ReadInt16() / 32767f);
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (n < .5f) return false;
            q.x /= n; q.y /= n; q.z /= n; q.w /= n;
            return true;
        }
        private static float Finite(float value) => IsFinite(value) ? value : 0f;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
