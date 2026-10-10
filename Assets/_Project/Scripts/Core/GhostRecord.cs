using System;
using System.IO;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// A recorded set of up to 60 seconds. Rep timestamps own the score; the optional compressed
    /// Humanoid stream owns the visible motion. Both use original elapsed seconds, never a
    /// count-derived uniform interval. Old timestamp-only recordings remain readable.
    ///
    /// <see cref="UnityEngine.JsonUtility"/> serializes this, so the fields are public and plain.
    /// </summary>
    [Serializable]
    public sealed class GhostRecord
    {
        /// <summary>MVP has one exercise; the field exists so a second one doesn't need a migration.</summary>
        public string exercise = "pushups";
        public string arenaId = ArenaCatalog.DefaultId;
        /// <summary>Reps credited in the recorded session. Equals <c>repTimes.Length</c>.</summary>
        public int reps;
        public float durationSec = FightConfig.DuelDurationSec;
        /// <summary>Seconds from the start of the live phase, ascending, all &lt; durationSec.</summary>
        public float[] repTimes = Array.Empty<float>();
        /// <summary>When a clap push-up was confirmed (on its landing), same clock as
        /// <see cref="repTimes"/>. Empty in recordings made before claps were kept — those
        /// replay with the clap visible in the pose at most, and no hit.</summary>
        public float[] clapTimes = Array.Empty<float>();
        /// <summary>Optional PSR1 Humanoid animation. Older timestamp-only saves still load.</summary>
        public string motionBase64 = "";
        /// <summary>Mean FORM (0..100) across the recorded reps — shown on the result screen.</summary>
        public float avgForm;
        /// <summary>ISO-8601 UTC. String rather than a tick count so the file is readable by eye.</summary>
        public string recordedAtUtc = "";
        /// <summary>Which flow produced it: "calibration" or "duel". Debug/telemetry only.</summary>
        public string source = "calibration";

        public bool IsValid => reps > 0 && repTimes != null && repTimes.Length == reps;

        public DateTime RecordedAtUtc =>
            DateTime.TryParse(recordedAtUtc, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : DateTime.MinValue;

        public static GhostRecord From(float[] repTimes, float avgForm, string source)
        {
            var times = repTimes ?? Array.Empty<float>();
            return new GhostRecord
            {
                reps          = times.Length,
                durationSec   = FightConfig.DuelDurationSec,
                repTimes      = times,
                avgForm       = avgForm,
                recordedAtUtc = DateTime.UtcNow.ToString("o"),
                source        = source,
                arenaId       = ArenaProfile.SelectedId,
            };
        }
    }

    /// <summary>
    /// Local storage for the player's best recorded set — the opponent every duel is fought
    /// against until real players arrive.
    ///
    /// <para>A JSON file in <see cref="Application.persistentDataPath"/> rather than PlayerPrefs:
    /// the record is an array that grows with the player's strength, and PlayerPrefs on iOS is a
    /// plist read whole on every access. Uploading it to Storage and matching against other
    /// players' records is phase 12/12.5; nothing here assumes a network.</para>
    /// </summary>
    public static class GhostStore
    {
        private const string FileName = "ghost_pushups.json";

        /// <summary>Cached so the fight screen can ask for it every frame without touching disk.
        /// Invalidated by every write that goes through this class.</summary>
        private static GhostRecord _cached;
        private static bool _loaded;

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        /// <summary>True when there is a record to fight. Drives whether the duel entry is offered.</summary>
        public static bool HasRecord => Load() != null;

        public static GhostRecord Load()
        {
            if (_loaded) return _cached;
            _loaded = true;
            _cached = null;

            try
            {
                if (!File.Exists(FilePath)) return null;
                var record = JsonUtility.FromJson<GhostRecord>(File.ReadAllText(FilePath));
                if (record != null && record.IsValid) _cached = record;
                else Debug.LogWarning("[GhostStore] Stored record is malformed — ignoring it.");
            }
            catch (Exception e)
            {
                // A corrupt file must not brick the app: the player simply has no ghost yet and
                // the flow sends them back through the level test.
                Debug.LogError($"[GhostStore] Could not read {FilePath}: {e.Message}");
            }
            return _cached;
        }

        public static void Save(GhostRecord record)
        {
            if (record == null || !record.IsValid)
            {
                Debug.LogWarning("[GhostStore] Refusing to save an invalid record.");
                return;
            }

            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(record, prettyPrint: true));
                _cached = record;
                _loaded = true;
                Debug.Log($"[GhostStore] Saved ghost: {record.reps} reps, form {record.avgForm:0}.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GhostStore] Could not write {FilePath}: {e.Message}");
            }
        }

        /// <summary>Stores <paramref name="record"/> only when it beats the stored one. Returns true
        /// when it became the new best (including the very first record).</summary>
        public static bool SaveIfBest(GhostRecord record)
        {
            if (record == null || !record.IsValid) return false;

            var current = Load();
            if (current != null && record.reps <= current.reps) return false;

            Save(record);
            return true;
        }

        /// <summary>Wipes the record. Used by the debug "reset onboarding" path.</summary>
        public static void Clear()
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception e) { Debug.LogError($"[GhostStore] Could not delete {FilePath}: {e.Message}"); }
            _cached = null;
            _loaded = true;
        }
    }
}
