using System;
using System.Collections.Generic;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>Raised after any progress save on this phone, so the cloud copy can follow.</summary>
    public static class ProgressEvents
    {
        public static event Action Changed;
        private static int _muted;

        public static void MarkChanged()
        {
            if (_muted == 0) Changed?.Invoke();
        }

        /// <summary>Restores write many saves at once; they are one change, reported by the caller.</summary>
        internal static void Mute(bool mute) => _muted += mute ? 1 : -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Changed = null; _muted = 0; }
    }

    /// <summary>
    /// Reads every local progress store into a <see cref="ProgressSnapshot"/> and writes one back.
    /// What counts as progress (see <c>docs/architecture/cloud-progress.md</c>): the reward ledger
    /// (wallet, Aura and peak, heroes, arenas, emotes, cases), the workout ledger (XP and level,
    /// stats, trophies, streak, history and so the daily goal), the boss ladder, the onboarding
    /// flags, the profile name/avatar/country and the hero/arena picks. Device settings (sound,
    /// language, reminders, layout) and "seen" badges stay on the phone.
    /// </summary>
    public static class ProgressStore
    {
        // Owned by the UI assembly, which Core cannot reference; keep in step with CharacterRoster
        // and ProfileIdentityEditor/ProfileCountry.
        private const string GenderKey = "character.gender";
        private const string HomeAvatarKey = "character.homeAvatar";
        private const string IdentityPrefix = "profile.identity.";
        private const string LocalIdentityPrefix = "profile.identity.local.";
        private const string LegacyArenaPrefix = "arena.owned.";
        private static readonly string[] LegacyProfileKeys =
            { "profile.trophies", "profile.best_reps", "profile.total_reps", "profile.wins", "profile.losses", "profile.seeded", "pending_xp" };

        private static readonly string[] IntKeys =
        {
            BossCatalog.ProgressKey, OnboardingState.KeyIntroSeen, OnboardingState.KeyLevelTestDone,
            OnboardingState.KeyLevelTestReps, OnboardingState.KeyLevelTestSkipped, GenderKey, HomeAvatarKey,
        };
        private static readonly string[] StringKeys = { ArenaProfile.SelectionKey };
        private static readonly (string Name, bool IsString)[] IdentityFields = { ("name", true), ("avatar", false), ("country", true) };

        public static ProgressSnapshot Capture(string uid)
        {
            var snapshot = new ProgressSnapshot { ledger = CaseRewards.ExportSave(), workouts = LocalProfile.ExportSave() };
            foreach (var key in IntKeys) AddPref(snapshot, key, key, false);
            foreach (var key in StringKeys) AddPref(snapshot, key, key, true);
            foreach (var key in LegacyArenaKeys()) AddPref(snapshot, key, key, false);
            foreach (var (name, isString) in IdentityFields)
            {
                string scoped = string.IsNullOrEmpty(uid) ? null : IdentityPrefix + uid + "." + name;
                string source = scoped != null && PlayerPrefs.HasKey(scoped) ? scoped : LocalIdentityPrefix + name;
                AddPref(snapshot, source, IdentityPrefix + ProgressSnapshot.UidToken + "." + name, isString);
            }
            return snapshot;
        }

        public static string CaptureGhost() => GhostStore.ExportJson();

        /// <summary>Replaces the progress on this phone with <paramref name="snapshot"/> for account
        /// <paramref name="uid"/>. Nothing is written unless both saves pass the ledgers' own checks.
        /// <paramref name="ghostJson"/> null leaves the local ghost as it is.</summary>
        public static void Apply(ProgressSnapshot snapshot, string ghostJson, string uid)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.Validate();
            ProgressEvents.Mute(true);
            try
            {
                CaseRewards.ImportSave(snapshot.ledger);
                LocalProfile.ImportSave(snapshot.workouts);
                if (ghostJson != null) GhostStore.ImportJson(ghostJson);
                DeleteProgressPrefs(uid, keepIntro: false);
                foreach (var pref in snapshot.prefs)
                {
                    string key = ExpandKey(pref.key, uid);
                    if (!IsProgressKey(key, uid)) continue;
                    if (pref.isString) PlayerPrefs.SetString(key, pref.stringValue ?? "");
                    else PlayerPrefs.SetInt(key, pref.intValue);
                }
                PlayerPrefs.Save();
            }
            finally { ProgressEvents.Mute(false); }
        }

        /// <summary>A new guest on this phone: nothing earned, the intro not shown again, and the
        /// level test waiting on the main screen rather than in front of it.</summary>
        public static void ResetToFresh(string uid)
        {
            bool introSeen = OnboardingState.IntroSeen;
            ProgressEvents.Mute(true);
            try
            {
                CaseRewards.ImportSave("");
                LocalProfile.ImportSave("");
                GhostStore.Clear();
                DeleteProgressPrefs(uid, keepIntro: true);
                foreach (var key in LegacyProfileKeys) PlayerPrefs.DeleteKey(key);
                if (introSeen) PlayerPrefs.SetInt(OnboardingState.KeyLevelTestSkipped, 1);
                PlayerPrefs.Save();
            }
            finally { ProgressEvents.Mute(false); }
        }

        private static void DeleteProgressPrefs(string uid, bool keepIntro)
        {
            foreach (var key in IntKeys)
                if (!keepIntro || key != OnboardingState.KeyIntroSeen) PlayerPrefs.DeleteKey(key);
            foreach (var key in StringKeys) PlayerPrefs.DeleteKey(key);
            foreach (var key in LegacyArenaKeys(all: true)) PlayerPrefs.DeleteKey(key);
            foreach (var (name, _) in IdentityFields)
            {
                PlayerPrefs.DeleteKey(LocalIdentityPrefix + name);
                if (!string.IsNullOrEmpty(uid)) PlayerPrefs.DeleteKey(IdentityPrefix + uid + "." + name);
            }
        }

        /// <summary>A snapshot key as it is stored for account <paramref name="uid"/>.</summary>
        public static string ExpandKey(string key, string uid)
            => key.Replace(ProgressSnapshot.UidToken, string.IsNullOrEmpty(uid) ? "local" : uid);

        /// <summary>Only keys this class writes may come back from a cloud copy: a forged snapshot
        /// cannot set a device setting or another account's profile.</summary>
        public static bool IsProgressKey(string key, string uid)
        {
            if (Array.IndexOf(IntKeys, key) >= 0 || Array.IndexOf(StringKeys, key) >= 0) return true;
            if (key.StartsWith(LegacyArenaPrefix, StringComparison.Ordinal)) return true;
            string scope = IdentityPrefix + (string.IsNullOrEmpty(uid) ? "local" : uid) + ".";
            foreach (var (name, _) in IdentityFields)
                if (key == scope + name) return true;
            return false;
        }

        private static void AddPref(ProgressSnapshot snapshot, string source, string key, bool isString)
        {
            if (!PlayerPrefs.HasKey(source)) return;
            snapshot.prefs.Add(isString
                ? new ProgressPref { key = key, isString = true, stringValue = PlayerPrefs.GetString(source, "") }
                : new ProgressPref { key = key, intValue = PlayerPrefs.GetInt(source, 0) });
        }

        /// <summary>Arena grants kept as "arena.owned.*" before the gem ledger held them.</summary>
        private static IEnumerable<string> LegacyArenaKeys(bool all = false)
        {
            var catalog = ArenaCatalog.Instance;
            if (catalog == null) yield break;
            foreach (var arena in catalog.Arenas)
            {
                string key = LegacyArenaPrefix + arena.Id;
                if (all || PlayerPrefs.HasKey(key)) yield return key;
            }
        }
    }
}
