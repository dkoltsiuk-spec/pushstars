using System;
using System.IO;

namespace PushStars.Core
{
    /// <summary>Who a seat is and what it has said about itself. Re-sent whole on every change
    /// and once a second as the heartbeat, so a reconnecting seat never needs a history.</summary>
    public sealed class LiveDuelSeat
    {
        public string Uid = "", Name = "", Country = "";
        public int Hero, Gender;
        public bool Ready, Armed, Finished;
        public int FinalReps;
    }

    /// <summary>The messages of a live duel. Binary and versioned: both phones must speak the
    /// same <see cref="Version"/>, or the room says so instead of misreading each other.</summary>
    public static class LiveDuelProtocol
    {
        public const byte Version = 1;
        public const byte State = 1, Start = 2, Score = 3, Pose = 4, Bye = 5;
        private const int MaxName = 24;

        public static byte[] EncodeState(LiveDuelSeat s) => Write(w =>
        {
            w.Write(Version); w.Write(Clip(s.Uid, 128)); w.Write(Clip(s.Name, MaxName)); w.Write(Clip(s.Country, 2));
            w.Write(s.Hero); w.Write((byte)s.Gender);
            w.Write((byte)((s.Ready ? 1 : 0) | (s.Armed ? 2 : 0) | (s.Finished ? 4 : 0)));
            w.Write((short)s.FinalReps);
        });

        public static bool TryDecodeState(byte[] bytes, out LiveDuelSeat seat, out bool versionMismatch)
        {
            seat = null; versionMismatch = false;
            try
            {
                using var r = Reader(bytes);
                byte version = r.ReadByte();
                if (version != Version) { versionMismatch = true; return false; }
                var s = new LiveDuelSeat { Uid = Clip(r.ReadString(), 128), Name = Clip(r.ReadString(), MaxName), Country = Clip(r.ReadString(), 2) };
                s.Hero = r.ReadInt32(); s.Gender = r.ReadByte();
                byte flags = r.ReadByte();
                s.Ready = (flags & 1) != 0; s.Armed = (flags & 2) != 0; s.Finished = (flags & 4) != 0;
                s.FinalReps = r.ReadInt16();
                if (s.Hero < 0 || s.Hero > 999 || s.Gender > 1 || s.FinalReps < 0 || s.FinalReps > LiveDuelLink.MaxReps) return false;
                seat = s; return true;
            }
            catch (Exception) { return false; }
        }

        public static byte[] EncodeStart(int startAtMs) => Write(w => w.Write(startAtMs));
        public static bool TryDecodeStart(byte[] bytes, out int startAtMs)
        {
            startAtMs = 0;
            if (bytes == null || bytes.Length != 4) return false;
            startAtMs = BitConverter.ToInt32(bytes, 0);
            return true;
        }

        public struct ScoreUpdate { public int Reps, Claps; public float Form, Elapsed; }
        public static byte[] EncodeScore(ScoreUpdate s) => Write(w =>
        {
            w.Write((short)s.Reps); w.Write((short)s.Claps); w.Write((byte)Math.Max(0, Math.Min(100, (int)Math.Round(s.Form))));
            w.Write(s.Elapsed);
        });
        public static bool TryDecodeScore(byte[] bytes, out ScoreUpdate s)
        {
            s = default;
            if (bytes == null || bytes.Length != 9) return false;
            using var r = Reader(bytes);
            s.Reps = r.ReadInt16(); s.Claps = r.ReadInt16(); s.Form = r.ReadByte(); s.Elapsed = r.ReadSingle();
            return s.Reps >= 0 && s.Reps <= LiveDuelLink.MaxReps && s.Claps >= 0 && s.Claps <= s.Reps
                && !float.IsNaN(s.Elapsed) && s.Elapsed >= 0 && s.Elapsed <= 120;
        }

        private static string Clip(string value, int max)
        {
            value = (value ?? "").Replace("<", "").Replace(">", "");
            return value.Length > max ? value.Substring(0, max) : value;
        }
        private static BinaryReader Reader(byte[] bytes) => new BinaryReader(new MemoryStream(bytes ?? Array.Empty<byte>()));
        private static byte[] Write(Action<BinaryWriter> body)
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream)) body(w);
            return stream.ToArray();
        }
    }
}
