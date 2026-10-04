using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>Versioned Humanoid poses on three independent, unscaled playback timelines.
    /// Captures the final displayed pose (including mirroring, pauses and claps), not a rep count.</summary>
    public sealed class GhostMotionClip
    {
        public const byte Preparation = 0, Setup = 1, Live = 2;
        public const int SampleRate = 30, MaxFrames = 6000, MuscleCount = 95;
        public const int MaxCompressedBytes = 2 * 1024 * 1024;
        public const int FrameBytes = 1 + 4 + (14 + MuscleCount) * 4;
        public sealed class Frame
        {
            public byte phase;
            public float time;
            public Vector3 bodyPosition, rootPosition;
            public Quaternion bodyRotation, rootRotation;
            public float[] muscles;
        }
        public readonly List<Frame> Frames = new List<Frame>();

        public bool HasPhase(byte phase) => Frames.Exists(f => f.phase == phase);

        // Binary search stays correct when a new round/phase seeks backwards. Never normalise
        // these timestamps to 60 seconds: an early finish and long pauses are part of the take.
        public bool Sample(byte phase, float time, ref HumanPose pose, out Vector3 rootPosition, out Quaternion rootRotation)
        {
            rootPosition = default; rootRotation = Quaternion.identity;
            int lo = 0, hi = Frames.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (Frames[mid].phase < phase) lo = mid + 1; else hi = mid; }
            int start = lo;
            if (start == Frames.Count || Frames[start].phase != phase) return false;
            hi = Frames.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (Frames[mid].phase <= phase) lo = mid + 1; else hi = mid; }
            int end = lo;
            lo = start; hi = end;
            while (lo < hi) { int mid = (lo + hi) / 2; if (Frames[mid].time <= time) lo = mid + 1; else hi = mid; }
            var a = Frames[Math.Max(start, lo - 1)];
            var b = Frames[Math.Min(end - 1, lo)];
            float blend = b.time > a.time ? Mathf.Clamp01((time - a.time) / (b.time - a.time)) : 0;
            if (pose.muscles == null || pose.muscles.Length != MuscleCount) pose.muscles = new float[MuscleCount];
            for (int i = 0; i < MuscleCount; i++) pose.muscles[i] = Mathf.Lerp(a.muscles[i], b.muscles[i], blend);
            pose.bodyPosition = Vector3.Lerp(a.bodyPosition, b.bodyPosition, blend);
            pose.bodyRotation = Quaternion.Slerp(a.bodyRotation, b.bodyRotation, blend);
            rootPosition = Vector3.Lerp(a.rootPosition, b.rootPosition, blend);
            rootRotation = Quaternion.Slerp(a.rootRotation, b.rootRotation, blend);
            return true;
        }

        public string Encode()
        {
            if (Frames.Count == 0) return "";
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
            using (var w = new BinaryWriter(gzip))
            {
                w.Write(0x31525350); // PSR1, little endian
                w.Write(MuscleCount); w.Write(Frames.Count);
                foreach (var f in Frames)
                {
                    w.Write(f.phase); w.Write(f.time);
                    Write(w, f.bodyPosition); Write(w, f.bodyRotation);
                    Write(w, f.rootPosition); Write(w, f.rootRotation);
                    foreach (float muscle in f.muscles) w.Write(muscle);
                }
            }
            return Convert.ToBase64String(output.ToArray());
        }

        public static GhostMotionClip Decode(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return null;
            if (encoded.Length > MaxCompressedBytes * 4 / 3 + 4) throw new InvalidDataException("Recording is too large.");
            byte[] bytes = Convert.FromBase64String(encoded);
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var r = new BinaryReader(gzip);
            if (r.ReadInt32() != 0x31525350 || r.ReadInt32() != MuscleCount)
                throw new InvalidDataException("Unsupported recording version.");
            int count = r.ReadInt32();
            if (count < 1 || count > MaxFrames) throw new InvalidDataException("Invalid frame count.");
            var clip = new GhostMotionClip();
            for (int i = 0; i < count; i++)
            {
                var f = new Frame { phase = r.ReadByte(), time = ReadFinite(r), bodyPosition = ReadVector(r),
                    bodyRotation = ReadRotation(r), rootPosition = ReadVector(r), rootRotation = ReadRotation(r), muscles = new float[MuscleCount] };
                if (f.phase > Live || f.time < 0 || f.time > 60.1f ||
                    (i > 0 && (f.phase < clip.Frames[i-1].phase || (f.phase == clip.Frames[i-1].phase && f.time <= clip.Frames[i-1].time))))
                    throw new InvalidDataException("Invalid recording clock.");
                for (int m = 0; m < MuscleCount; m++) f.muscles[m] = ReadFinite(r);
                clip.Frames.Add(f);
            }
            if (r.BaseStream.ReadByte() != -1) throw new InvalidDataException("Unexpected recording data.");
            return clip;
        }
        private static float ReadFinite(BinaryReader r)
        {
            float value = r.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value) || Mathf.Abs(value) > 10000) throw new InvalidDataException("Invalid pose value.");
            return value;
        }
        private static Vector3 ReadVector(BinaryReader r) => new Vector3(ReadFinite(r), ReadFinite(r), ReadFinite(r));
        private static Quaternion ReadRotation(BinaryReader r) => new Quaternion(ReadFinite(r), ReadFinite(r), ReadFinite(r), ReadFinite(r));
        private static void Write(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        private static void Write(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
    }
}
