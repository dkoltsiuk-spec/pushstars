using System;
using System.IO;
using NUnit.Framework;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Tests
{
    /// <summary>
    /// The cloud copy's format. The snapshot built here is also the golden file the server's rules
    /// are tested against (<c>functions/test/fixtures</c>), so the two sides cannot drift apart.
    /// Set PUSHSTARS_WRITE_FIXTURES=1 to rewrite the golden files after a deliberate format change.
    /// None of these tests touch PlayerPrefs or the persistent data folder.
    /// </summary>
    public sealed class ProgressSnapshotTests
    {
        private static string FixtureDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "functions", "test", "fixtures"));
        private static readonly DateTimeOffset Day = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

        /// <summary>A guest's first two days, built through the real ledgers.</summary>
        internal static ProgressSnapshot BuildGuestProgress()
        {
            string ledgerJson = null;
            var ledger = new CaseRewardLedger(null, () => 0.5, json => ledgerJson = json, Array.Empty<AvatarOffer>());
            Assert.IsTrue(ledger.TryAwardAssessmentCase(30));
            Assert.IsTrue(ledger.TryUpgrade(CaseRewards.AssessmentReceipt, 0, out _));
            Assert.IsTrue(ledger.TryUpgrade(CaseRewards.AssessmentReceipt, 1, out _));
            Assert.IsTrue(ledger.TryUpgrade(CaseRewards.AssessmentReceipt, 2, out _));
            Assert.IsTrue(ledger.TryOpen(CaseRewards.AssessmentReceipt, out _));
            Assert.IsTrue(ledger.TryClaim(CaseRewards.AssessmentReceipt, out _));
            Assert.IsTrue(ledger.TryAwardDailyWorkoutCase("duel-1", 34, Day.AddDays(1)));
            ledger.ApplyAura(new[] { new AuraGrant("duel-1:win", EconomyConfig.AuraWin), new AuraGrant("duel-1:record", EconomyConfig.AuraNewRecord) });
            Assert.IsTrue(ledger.TryBuyArena("sunset", 0));

            string workoutsJson = null;
            var workouts = new LocalWorkoutLedger(null, json => workoutsJson = json, null, TimeZoneInfo.Utc);
            workouts.Record(new LocalWorkout { Id = "assessment-1", Mode = "assessment", Reps = 30, Xp = 450, DurationSec = 60, UtcTicks = Day.UtcTicks });
            workouts.Record(new LocalWorkout { Id = "duel-1", Mode = "ghost", Reps = 34, Xp = 660, OpponentReps = 30, OpponentName = "YOUR GHOST", Won = true, DurationSec = 60, UtcTicks = Day.AddDays(1).UtcTicks });

            var snapshot = new ProgressSnapshot { ledger = ledgerJson, workouts = workoutsJson };
            snapshot.prefs.Add(new ProgressPref { key = "boss_progress", intValue = 1 });
            snapshot.prefs.Add(new ProgressPref { key = "onboarding.intro_seen", intValue = 1 });
            snapshot.prefs.Add(new ProgressPref { key = "onboarding.level_test_done", intValue = 1 });
            snapshot.prefs.Add(new ProgressPref { key = "arena.selected", isString = true, stringValue = "sunset" });
            snapshot.prefs.Add(new ProgressPref { key = "profile.identity.{uid}.name", isString = true, stringValue = "Дана ★" });
            return snapshot;
        }

        [Test]
        public void Snapshot_RoundTripsThroughJsonAndTransport()
        {
            var snapshot = BuildGuestProgress();
            string json = snapshot.ToJson();
            var back = ProgressSnapshot.FromJson(ProgressCodec.Unpack(ProgressCodec.Pack(json)));
            Assert.AreEqual(json, back.ToJson());
            Assert.AreEqual("Дана ★", back.FindPref("profile.identity.{uid}.name").stringValue);
            back.Validate();
        }

        [Test]
        public void Snapshot_RestoresTheSameWallet()
        {
            var back = ProgressSnapshot.FromJson(BuildGuestProgress().ToJson());
            var ledger = new CaseRewardLedger(back.ledger, () => 0.5, _ => { }, Array.Empty<AvatarOffer>());
            Assert.AreEqual(CaseRewards.AssessmentAura + EconomyConfig.AuraWin + EconomyConfig.AuraNewRecord, ledger.AuraBalance);
            Assert.AreEqual(ledger.AuraBalance, ledger.AuraPeak);
            Assert.Greater(ledger.GemsBalance, 0);
            Assert.IsTrue(ledger.OwnsArena("sunset"));
            Assert.IsTrue(ledger.HasCaseReceipt("duel-1"));
            // Receipts travel too: a replayed grant on the new phone pays nothing.
            Assert.AreEqual(0, ledger.ApplyAura(new[] { new AuraGrant("duel-1:win", EconomyConfig.AuraWin) }).Applied.Count);

            var workouts = new LocalWorkoutLedger(back.workouts, _ => { }, null, TimeZoneInfo.Utc);
            Assert.AreEqual(1110, workouts.Snapshot.Xp);
            Assert.AreEqual(64, workouts.Snapshot.TotalReps);
            Assert.AreEqual(2, workouts.Snapshot.DailyStreak);
            Assert.AreEqual(2, workouts.History.Count);
        }

        [Test]
        public void Rank_ReadsPeakXpAndWorkouts()
        {
            var rank = BuildGuestProgress().Rank();
            Assert.AreEqual(CaseRewards.AssessmentAura + EconomyConfig.AuraWin + EconomyConfig.AuraNewRecord, rank.PeakAura);
            Assert.AreEqual(1110, rank.Xp);
            Assert.AreEqual(2, rank.Workouts);
            Assert.IsTrue(new ProgressSnapshot().Rank().IsEmpty);
        }

        [Test]
        public void Rank_SaveFromBeforePeakAura_UsesTheBalance()
            => Assert.AreEqual(700, ProgressRank.Of("{\"version\":5,\"aura\":700}", "").PeakAura);

        [Test]
        public void Validate_RejectsALedgerThatWouldNotLoad()
        {
            var snapshot = BuildGuestProgress();
            snapshot.ledger = snapshot.ledger.Replace("\"gems\":", "\"gems\":-");
            Assert.Throws<InvalidOperationException>(snapshot.Validate);
            snapshot = BuildGuestProgress();
            snapshot.workouts = snapshot.workouts.Replace("\"Version\":1", "\"Version\":9");
            Assert.Throws<InvalidOperationException>(snapshot.Validate);
        }

        [Test]
        public void FromJson_RejectsUnknownVersionsAndEmptyText()
        {
            Assert.Throws<InvalidOperationException>(() => ProgressSnapshot.FromJson(""));
            Assert.Throws<InvalidOperationException>(() => ProgressSnapshot.FromJson("{\"version\":2}"));
            var old = ProgressSnapshot.FromJson("{\"version\":1}");
            Assert.AreEqual("", old.ledger);
            Assert.IsNotNull(old.prefs);
        }

        [Test]
        public void Codec_HashFollowsContent()
        {
            Assert.AreEqual("", ProgressCodec.Hash(""));
            Assert.AreEqual(ProgressCodec.Hash("a"), ProgressCodec.Hash("a"));
            Assert.AreNotEqual(ProgressCodec.Hash("a"), ProgressCodec.Hash("b"));
            Assert.AreEqual("", ProgressCodec.Unpack(ProgressCodec.Pack("")));
        }

        [Test]
        public void Keys_ExpandToTheAccountAndRefuseEverythingElse()
        {
            Assert.AreEqual("profile.identity.abc.name", ProgressStore.ExpandKey("profile.identity.{uid}.name", "abc"));
            Assert.AreEqual("profile.identity.local.name", ProgressStore.ExpandKey("profile.identity.{uid}.name", null));
            Assert.IsTrue(ProgressStore.IsProgressKey("profile.identity.abc.name", "abc"));
            Assert.IsTrue(ProgressStore.IsProgressKey("boss_progress", "abc"));
            Assert.IsTrue(ProgressStore.IsProgressKey("arena.owned.sunset", "abc"));
            Assert.IsFalse(ProgressStore.IsProgressKey("profile.identity.someone-else.name", "abc"));
            Assert.IsFalse(ProgressStore.IsProgressKey("settings.language", "abc"));
            Assert.IsFalse(ProgressStore.IsProgressKey("cloud.owner_uid", "abc"));
        }

        [Test]
        public void GoldenFile_MatchesWhatTheServerIsTestedWith()
        {
            string json = BuildGuestProgress().ToJson();
            string jsonPath = Path.Combine(FixtureDir, "progress-snapshot.json");
            string packedPath = Path.Combine(FixtureDir, "progress-snapshot.packed.txt");
            if (Environment.GetEnvironmentVariable("PUSHSTARS_WRITE_FIXTURES") == "1")
            {
                Directory.CreateDirectory(FixtureDir);
                File.WriteAllText(jsonPath, json);
                File.WriteAllText(packedPath, ProgressCodec.Pack(json));
            }
            Assert.AreEqual(json, File.ReadAllText(jsonPath), "Client format changed: rewrite the fixtures and rerun functions tests.");
            Assert.AreEqual(json, ProgressCodec.Unpack(File.ReadAllText(packedPath).Trim()));
        }
    }
}
