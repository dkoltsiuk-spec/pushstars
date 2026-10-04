using System;
using System.IO;
using UnityEngine;

namespace PushStars.Core
{
    [Serializable]
    public sealed class BotRecording
    {
        public int version = 1;
        public string id, ownerUid, displayName = "TEST BOT", countryCode = "MD";
        public int trophies, avatar, gender;
        public string[] achievementIds = Array.Empty<string>();
        public GhostRecord fight;
        public string cloudPath = "";
        public bool IsValid => version == 1 && !string.IsNullOrEmpty(id) && fight != null && fight.IsValid && !string.IsNullOrEmpty(fight.motionBase64);
    }

    /// <summary>Keep every take. A failed upload or new recording cannot overwrite a pending take.</summary>
    public static class BotRecordingStore
    {
        public static string DirectoryPath => Path.Combine(Application.persistentDataPath, "bot-recordings-v1");
        public static void Save(BotRecording recording)
        {
            if (recording == null || !recording.IsValid || !Guid.TryParseExact(recording.id, "N", out _))
                throw new InvalidDataException("The recording has no repetitions or animation. Please record again.");
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, recording.id + ".json"), temp = path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(recording));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            File.WriteAllText(Path.Combine(DirectoryPath, "latest.txt"), recording.id);
        }
        public static BotRecording Latest()
        {
            string latest = Path.Combine(DirectoryPath, "latest.txt");
            if (!File.Exists(latest)) return null;
            string id = File.ReadAllText(latest);
            if (!Guid.TryParseExact(id, "N", out _)) return null;
            string path = Path.Combine(DirectoryPath, id + ".json");
            if (!File.Exists(path)) return null;
            var result = JsonUtility.FromJson<BotRecording>(File.ReadAllText(path));
            return result != null && result.IsValid ? result : null;
        }
    }
}
