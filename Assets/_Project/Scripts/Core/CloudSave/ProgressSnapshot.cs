using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>One PlayerPrefs value carried in a <see cref="ProgressSnapshot"/>.</summary>
    [Serializable]
    public sealed class ProgressPref
    {
        public string key;
        public bool isString;
        public int intValue;
        public string stringValue = "";
    }

    /// <summary>
    /// Everything that is the player's progress, as the cloud copy of an account holds it. The two
    /// ledgers travel as their own save JSON, untouched, so a restore is byte-for-byte the save the
    /// other phone wrote and goes through the same validation and migrations as a local load. The
    /// ghost set is not in here: it is large and changes rarely, so it travels on its own.
    /// </summary>
    [Serializable]
    public sealed class ProgressSnapshot
    {
        public const int CurrentVersion = 1;
        /// <summary>Stands in for the account's UID in uid-scoped keys, so a snapshot can move
        /// between accounts on a merge without carrying an old UID along.</summary>
        public const string UidToken = "{uid}";

        public int version = CurrentVersion;
        /// <summary><see cref="CaseRewards"/> save: wallet, Aura and peak, heroes, arenas, emotes, cases.</summary>
        public string ledger = "";
        /// <summary><see cref="LocalProfile"/> save: XP, stats, trophies, streak and workout history.</summary>
        public string workouts = "";
        /// <summary>Boss ladder, onboarding flags, profile name/avatar/country, hero and arena picks.</summary>
        public List<ProgressPref> prefs = new List<ProgressPref>();

        public string ToJson() => JsonUtility.ToJson(this);

        public static ProgressSnapshot FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("Empty progress snapshot.");
            var snapshot = JsonUtility.FromJson<ProgressSnapshot>(json);
            if (snapshot == null || snapshot.version < 1 || snapshot.version > CurrentVersion)
                throw new InvalidOperationException("Unsupported progress snapshot.");
            if (snapshot.prefs == null) snapshot.prefs = new List<ProgressPref>();
            if (snapshot.ledger == null) snapshot.ledger = "";
            if (snapshot.workouts == null) snapshot.workouts = "";
            return snapshot;
        }

        public ProgressPref FindPref(string key) => prefs.Find(p => p.key == key);

        /// <summary>How far along this copy is, used only when two phones both changed one account.</summary>
        public ProgressRank Rank() => ProgressRank.Of(ledger, workouts);

        /// <summary>Throws unless both saves load in the real ledgers — the only check a restore trusts.</summary>
        public void Validate()
        {
            if (!string.IsNullOrEmpty(ledger))
                new CaseRewardLedger(ledger, () => 0, _ => { });
            if (!string.IsNullOrEmpty(workouts))
                new LocalWorkoutLedger(workouts, _ => { });
            foreach (var pref in prefs)
                if (pref == null || string.IsNullOrWhiteSpace(pref.key))
                    throw new InvalidOperationException("Invalid progress value.");
        }
    }

    /// <summary>Peak Aura, then XP, then workouts done: the order "more progressed" is judged in.</summary>
    public readonly struct ProgressRank : IComparable<ProgressRank>
    {
        [Serializable] private sealed class LedgerHead { public long peakAura; public long aura; public int version; }
        [Serializable] private sealed class WorkoutsHead { public long Xp; public List<LocalWorkout> Workouts; }

        public readonly long PeakAura, Xp;
        public readonly int Workouts;
        public bool IsEmpty => PeakAura == 0 && Xp == 0 && Workouts == 0;

        public ProgressRank(long peakAura, long xp, int workouts) { PeakAura = peakAura; Xp = xp; Workouts = workouts; }

        public static ProgressRank Of(string ledgerJson, string workoutsJson)
        {
            long peak = 0, xp = 0;
            int workouts = 0;
            if (!string.IsNullOrEmpty(ledgerJson))
            {
                var head = JsonUtility.FromJson<LedgerHead>(ledgerJson);
                // Saves from before v6 had no peak; the balance was the highest they had reached.
                if (head != null) peak = Math.Max(head.peakAura, head.aura);
            }
            if (!string.IsNullOrEmpty(workoutsJson))
            {
                var head = JsonUtility.FromJson<WorkoutsHead>(workoutsJson);
                if (head != null) { xp = head.Xp; workouts = head.Workouts?.Count ?? 0; }
            }
            return new ProgressRank(peak, xp, workouts);
        }

        public int CompareTo(ProgressRank other)
        {
            int byPeak = PeakAura.CompareTo(other.PeakAura);
            if (byPeak != 0) return byPeak;
            int byXp = Xp.CompareTo(other.Xp);
            return byXp != 0 ? byXp : Workouts.CompareTo(other.Workouts);
        }

        public override string ToString() => $"peak {PeakAura}, xp {Xp}, workouts {Workouts}";
    }

    /// <summary>Transport encoding shared with <c>functions/src/progressRules.js</c>: gzip, then base64.</summary>
    public static class ProgressCodec
    {
        public static string Pack(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            var bytes = Encoding.UTF8.GetBytes(json);
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, true))
                    gzip.Write(bytes, 0, bytes.Length);
                return Convert.ToBase64String(output.ToArray());
            }
        }

        public static string Unpack(string packed)
        {
            if (string.IsNullOrEmpty(packed)) return "";
            using (var input = new MemoryStream(Convert.FromBase64String(packed)))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        /// <summary>Content hash: whether a save changed since the copy the cloud holds.</summary>
        public static string Hash(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            using (var sha = SHA256.Create())
            {
                var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var hex = new StringBuilder(digest.Length * 2);
                foreach (byte b in digest) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }
    }
}
