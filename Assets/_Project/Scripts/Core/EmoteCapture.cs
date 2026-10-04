using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// One take recorded in the Emote Lab: the owner performing a gesture in front of the phone,
    /// kept as JPEG frames with their capture times. The desktop mocap pipeline
    /// (Tools/EmoteMocap) turns a take into an emote clip; nothing on the phone reads it back.
    /// </summary>
    [Serializable]
    public sealed class EmoteCaptureMeta
    {
        public int version = 1;
        public string id, name, device, createdUtc;
        public int frames, chunks, width, height;
        /// <summary>WebCamTexture.videoRotationAngle while recording: the frames are stored as the
        /// sensor delivered them, and the desktop side turns them upright.</summary>
        public int rotation;
        public bool verticallyMirrored, frontFacing;
        public float seconds, fps;
        /// <summary>Device-side only: set once every chunk is confirmed in the cloud.</summary>
        public bool uploaded;
    }

    /// <summary>
    /// Takes on the device: <c>persistentDataPath/EmoteCaptures/&lt;id&gt;/meta.json</c> and
    /// <c>chunk_NNN.bin</c>. A chunk is <c>[int32 count]</c> then <c>count</c> ×
    /// <c>[int32 length][float32 seconds][JPEG]</c>, sized to fit one cloud call. The take stays
    /// here until its upload is confirmed, so a failed connection loses nothing.
    /// </summary>
    public static class EmoteCaptureStore
    {
        /// <summary>Under the cloud function's 2.5 MB chunk limit, with room for one more frame.</summary>
        public const int ChunkBytes = 1800 * 1024;

        public static string Root => Path.Combine(Application.persistentDataPath, "EmoteCaptures");
        public static string Dir(string id) => Path.Combine(Root, id);
        public static string ChunkPath(string id, int index) => Path.Combine(Dir(id), $"chunk_{index:000}.bin");
        private static string MetaPath(string id) => Path.Combine(Dir(id), "meta.json");

        public static void Save(EmoteCaptureMeta meta)
        {
            Directory.CreateDirectory(Dir(meta.id));
            string temp = MetaPath(meta.id) + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(meta));
            if (File.Exists(MetaPath(meta.id))) File.Delete(MetaPath(meta.id));
            File.Move(temp, MetaPath(meta.id));
        }

        /// <summary>Every complete take on the device, newest first.</summary>
        public static List<EmoteCaptureMeta> All()
        {
            var list = new List<EmoteCaptureMeta>();
            if (!Directory.Exists(Root)) return list;
            foreach (string dir in Directory.GetDirectories(Root))
            {
                string path = Path.Combine(dir, "meta.json");
                if (!File.Exists(path)) continue;
                try
                {
                    var meta = JsonUtility.FromJson<EmoteCaptureMeta>(File.ReadAllText(path));
                    if (meta != null && !string.IsNullOrEmpty(meta.id) && meta.chunks > 0) list.Add(meta);
                }
                catch (ArgumentException) { }
            }
            list.Sort((a, b) => string.CompareOrdinal(b.createdUtc, a.createdUtc));
            return list;
        }

        /// <summary>Frees the frames of takes already confirmed in the cloud (their metadata stays
        /// as the on-device history).</summary>
        public static void DropUploadedFrames()
        {
            foreach (var meta in All())
            {
                if (!meta.uploaded) continue;
                for (int i = 0; i < meta.chunks; i++)
                    if (File.Exists(ChunkPath(meta.id, i))) File.Delete(ChunkPath(meta.id, i));
            }
        }

        /// <summary>Packs encoded frames into chunk files as they arrive.</summary>
        public sealed class Writer
        {
            private readonly string _id;
            private readonly List<(float seconds, byte[] jpeg)> _pending = new List<(float, byte[])>();
            private int _pendingBytes;
            public int Chunks { get; private set; }
            public int Frames { get; private set; }

            public Writer(string id)
            {
                _id = id;
                Directory.CreateDirectory(Dir(id));
            }

            public void Add(float seconds, byte[] jpeg)
            {
                if (_pendingBytes + jpeg.Length + 8 > ChunkBytes && _pending.Count > 0) Flush();
                _pending.Add((seconds, jpeg));
                _pendingBytes += jpeg.Length + 8;
                Frames++;
            }

            public void Flush()
            {
                if (_pending.Count == 0) return;
                using (var stream = new FileStream(ChunkPath(_id, Chunks), FileMode.Create, FileAccess.Write))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(_pending.Count);
                    foreach (var (seconds, jpeg) in _pending)
                    {
                        writer.Write(jpeg.Length);
                        writer.Write(seconds);
                        writer.Write(jpeg);
                    }
                }
                Chunks++;
                _pending.Clear();
                _pendingBytes = 0;
            }
        }
    }
}
