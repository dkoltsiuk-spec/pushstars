using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PushStars.Core;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Exercises the production ledger with in-memory persistence; never alters PlayerPrefs.</summary>
    public static class CaseRewardsRegression
    {
        [MenuItem("Tools/Push Stars/Rewards/Validate Case Rewards", priority = 340)]
        public static void Run()
        {
            var report = new StringBuilder("Case rewards regression — " + DateTime.UtcNow.ToString("u") + "\n");
            int passed = 0, failed = 0;
            Check("Reward roll ranges and rarity upgrade boundaries", Rules, report, ref passed, ref failed);
            Check("Only positive completed workouts award; duplicate callbacks survive reload", Awards, report, ref passed, ref failed);
            Check("Daily eligibility is consumed on award and survives claim/reload", DailyEligibility, report, ref passed, ref failed);
            Check("UTC rollover, quota configuration and backward clocks", DailyClockAndPolicy, report, ref passed, ref failed);
            Check("Capped workout receipts cannot be replayed on a later day", DailyReplay, report, ref passed, ref failed);
            Check("Version 1 migration preserves every case, balance and receipt", LegacyMigration, report, ref passed, ref failed);
            Check("Failed daily saves do not consume quota or receipts", DailyFailedWrites, report, ref passed, ref failed);
            Check("Invalid actions do not consume randomness or write state", InvalidActions, report, ref passed, ref failed);
            Check("Each accepted tap survives reload; stale taps are rejected", TapPersistence, report, ref passed, ref failed);
            Check("Missed upgrades consume taps and permit a common case to open", MissedUpgrades, report, ref passed, ref failed);
            Check("Opened prize survives reload and is never rerolled", OpenPersistence, report, ref passed, ref failed);
            Check("Claim pays once across reload and cannot re-award the workout", ClaimPersistence, report, ref passed, ref failed);
            Check("Selected cases can be opened out of order without changing others", Queue, report, ref passed, ref failed);
            Check("Persistence failure leaves awards, taps, opens and claims uncommitted", FailedWrites, report, ref passed, ref failed);
            Check("Malformed or unsupported saves are rejected without overwriting them", InvalidSaves, report, ref passed, ref failed);
            report.AppendLine($"RESULT: {passed} passed, {failed} failed.");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/case-rewards-regression.txt", report.ToString());
            if (failed != 0) throw new InvalidOperationException($"Case rewards regression: {failed} failed. See Logs/case-rewards-regression.txt");
            Debug.Log($"[CaseRewardsRegression] PASS — {passed} cases. Logs/case-rewards-regression.txt");
        }

        private static void Rules()
        {
            foreach (CaseRarity rarity in Enum.GetValues(typeof(CaseRarity)))
            {
                double chance = CaseRewards.UpgradeChance(rarity);
                var promoted = rarity == CaseRarity.Legendary ? rarity : (CaseRarity)((int)rarity + 1);
                Require(CaseRewards.UpgradeForRoll(rarity, 0) == promoted, "Lowest roll did not upgrade by one tier.");
                Require(CaseRewards.UpgradeForRoll(rarity, chance) == rarity, "Upgrade threshold must be exclusive.");
                Require(CaseRewards.GemsForRoll(rarity, 0) == CaseRewards.MinimumGems(rarity), "Minimum gem amount is unreachable.");
                Require(CaseRewards.GemsForRoll(rarity, 0.9999999999999999) == CaseRewards.MaximumGems(rarity), "Maximum gem amount is unreachable.");
                for (int i = 0; i < 10000; i++)
                {
                    double roll = i / 10000.0;
                    int gems = CaseRewards.GemsForRoll(rarity, roll);
                    var upgraded = CaseRewards.UpgradeForRoll(rarity, roll);
                    Require(gems >= CaseRewards.MinimumGems(rarity) && gems <= CaseRewards.MaximumGems(rarity), "Reward escaped its rarity range.");
                    Require((int)upgraded >= (int)rarity && (int)upgraded <= Math.Min(3, (int)rarity + 1), "Upgrade skipped or downgraded a tier.");
                }
            }
            foreach (double roll in new[] { -0.01, 1.0, double.NaN, double.PositiveInfinity })
            {
                MustThrow<ArgumentOutOfRangeException>(() => CaseRewards.UpgradeForRoll(CaseRarity.Common, roll));
                MustThrow<ArgumentOutOfRangeException>(() => CaseRewards.GemsForRoll(CaseRarity.Common, roll));
            }
        }

        private static void Awards()
        {
            var f = new Fixture();
            Require(!f.Ledger.AwardForWorkout("empty", 0) && !f.Ledger.AwardForWorkout("negative", -3), "Empty workout received a case.");
            Require(!f.Ledger.AwardForWorkout(null, 2) && !f.Ledger.AwardForWorkout("  ", 2), "Workout without an ID received a case.");
            Require(f.Writes == 0 && f.Ledger.Pending == null, "Rejected awards altered state.");
            Require(f.Ledger.AwardForWorkout("workout-a", 12), "Workout was not awarded.");
            f.Reload();
            Require(!f.Ledger.AwardForWorkout("workout-a", 12), "Duplicate workout received another case after reload.");
            Require(f.Ledger.PendingCount == 1 && f.Writes == 1 && f.Draws == 0, "Award altered count or consumed randomness unexpectedly.");
        }

        private static void InvalidActions()
        {
            var f = new Fixture();
            Require(!f.Ledger.TryUpgrade("missing", 0, out _) && !f.Ledger.TryOpen("missing", out _) &&
                !f.Ledger.TryClaim("missing", out _), "Actions succeeded without a pending case.");
            f.Ledger.AwardForWorkout("a", 1);
            Require(!f.Ledger.TryUpgrade("other", 0, out _), "A foreign case ID was accepted.");
            Require(!f.Ledger.TryUpgrade("a", -1, out _) && !f.Ledger.TryUpgrade("a", 1, out _), "An out-of-order tap was accepted.");
            Require(!f.Ledger.TryOpen("a", out _) && !f.Ledger.TryClaim("a", out _), "Case opened or paid before upgrades.");
            Require(f.Draws == 0 && f.Writes == 1 && f.Ledger.GemsBalance == 0, "Rejected action changed persistence, RNG or balance.");
        }

        private static readonly DateTimeOffset Day = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

        private static void DailyEligibility()
        {
            var f = new Fixture();
            Require(f.Ledger.CanAwardDailyWorkoutCase(Day), "A fresh day had no quota.");
            Require(f.Ledger.TryAwardDailyWorkoutCase("day-a", 12, Day), "First daily workout was rejected.");
            Require(!f.Ledger.CanAwardDailyWorkoutCase(Day) && f.Ledger.RemainingDailyWorkoutCases(Day) == 0, "Award did not consume daily quota.");
            f.Reload();
            Require(!f.Ledger.CanAwardDailyWorkoutCase(Day), "Reload reset daily quota.");
            Require(!f.Ledger.TryAwardDailyWorkoutCase("day-b", 20, Day), "Second workout received another daily case.");
            f.UpgradeAll("day-a");
            f.Ledger.TryOpen("day-a", out _);
            Require(!f.Ledger.CanAwardDailyWorkoutCase(Day), "Opening reset daily quota.");
            Require(f.Ledger.TryClaim("day-a", out _), "Claim failed.");
            f.Reload();
            Require(!f.Ledger.CanAwardDailyWorkoutCase(Day), "Claim or reload reset daily quota.");
            Require(!f.Ledger.TryAwardDailyWorkoutCase("day-c", 12, Day), "Claim enabled an extra workout case.");
            Require(f.Ledger.TryGrantCase("promotion:gold"), "An explicit other source was blocked by workout quota.");
            Require(!f.Ledger.TryGrantCase("promotion:gold"), "Duplicate explicit receipt was paid.");
            Require(!f.Ledger.CanAwardDailyWorkoutCase(Day), "Other source changed daily quota.");
        }

        private static void DailyClockAndPolicy()
        {
            var f = new Fixture();
            var beforeMidnight = new DateTimeOffset(2026, 9, 10, 23, 59, 59, TimeSpan.Zero);
            Require(f.Ledger.TryAwardDailyWorkoutCase("a", 1, beforeMidnight), "First UTC day award failed.");
            Require(!f.Ledger.CanAwardDailyWorkoutCase(beforeMidnight.ToOffset(TimeSpan.FromHours(3))), "Local midnight changed UTC quota.");
            var midnight = beforeMidnight.AddSeconds(1);
            Require(f.Ledger.CanAwardDailyWorkoutCase(midnight), "UTC midnight did not restore eligibility.");
            Require(f.Ledger.TryAwardDailyWorkoutCase("b", 1, midnight), "New UTC day did not award.");
            f.Reload();
            Require(!f.Ledger.TryAwardDailyWorkoutCase("backdated", 1, Day), "Turning the clock back reopened an old day.");
            Require(f.Ledger.PendingCount == 2 && f.Ledger.Find("a") != null && f.Ledger.Find("b") != null, "Rollover replaced unclaimed inventory.");

            var configured = new Fixture();
            Require(!configured.Ledger.TryAwardDailyWorkoutCase("short", 4, Day, 2, 5), "Configured minimum reps was ignored.");
            Require(configured.Writes == 1 && configured.Ledger.RemainingDailyWorkoutCases(Day, 2) == 2,
                "A short completed workout was not recorded independently of quota.");
            Require(!configured.Ledger.TryAwardDailyWorkoutCase("short", 4, Day.AddDays(1), 2, 1),
                "A changed minimum allowed replaying a short workout on a later day.");
            Require(configured.Ledger.TryAwardDailyWorkoutCase("first", 5, Day, 2, 5), "Configured first case failed.");
            Require(configured.Ledger.RemainingDailyWorkoutCases(Day, 2) == 1, "Configured remaining count is wrong.");
            Require(configured.Ledger.TryAwardDailyWorkoutCase("second", 5, Day, 2, 5), "Configured second case failed.");
            Require(!configured.Ledger.TryAwardDailyWorkoutCase("third", 5, Day, 2, 5), "Configured quota exceeded.");
            Require(!configured.Ledger.CanAwardDailyWorkoutCase(Day.AddDays(1), 0), "Disabled policy still advertised eligibility.");
            Require(!configured.Ledger.TryAwardDailyWorkoutCase("disabled", 5, Day.AddDays(1), 0), "Disabled source granted a case.");
            Require(configured.Ledger.PendingCount == 2, "Policy changes altered existing inventory.");
        }

        private static void DailyReplay()
        {
            var f = new Fixture();
            Require(f.Ledger.TryAwardDailyWorkoutCase("a", 1, Day), "Initial award failed.");
            Require(!f.Ledger.TryAwardDailyWorkoutCase("b", 1, Day), "Capped workout was awarded.");
            int writes = f.Writes;
            Require(!f.Ledger.TryAwardDailyWorkoutCase("b", 1, Day) && f.Writes == writes, "Duplicate capped callback rewrote state.");
            f.Reload();
            Require(!f.Ledger.TryAwardDailyWorkoutCase("a", 99, Day.AddDays(1)), "Awarded workout replay claimed another day.");
            Require(!f.Ledger.TryAwardDailyWorkoutCase("b", 99, Day.AddDays(1)), "Previously capped workout replay claimed another day.");
            Require(!f.Ledger.TryGrantCase("b"), "Explicit API bypassed a processed workout receipt.");
            Require(f.Ledger.TryAwardDailyWorkoutCase("c", 1, Day.AddDays(1)), "Rejected replays consumed the new day's quota.");
            f.Reload();
            Require(f.Ledger.PendingCount == 2 && f.Ledger.Pending.Id == "a" && f.Ledger.Find("c") != null,
                "The later award replaced an older pending case.");
        }

        private static void LegacyMigration()
        {
            string saved = "{\"version\":1,\"gems\":11,\"awardedWorkouts\":[\"a\",\"b\",\"claimed-old\"],\"cases\":[" +
                "{\"id\":\"a\",\"rarity\":0,\"taps\":3,\"opened\":true,\"gems\":20}," +
                "{\"id\":\"b\",\"rarity\":1,\"taps\":1,\"opened\":false,\"gems\":0}]}";
            int writes = 0;
            Action<string> persist = json => { saved = json; writes++; };
            var ledger = new CaseRewardLedger(saved, () => 0.99, persist);
            Require(writes == 0 && ledger.GemsBalance == 11 && ledger.PendingCount == 2, "Migration rewrote or lost existing inventory.");
            Require(ledger.Find("a").Opened && ledger.Find("a").Gems == 20 && ledger.Find("b").UpgradeTapsUsed == 1,
                "Migration changed an opened prize or accepted tap.");
            Require(!ledger.TryAwardDailyWorkoutCase("claimed-old", 10, Day) && !ledger.TryGrantCase("a"), "Historic receipt was replayed.");
            Require(ledger.TryAwardDailyWorkoutCase("new", 10, Day), "Migration did not enable the first new dated workout.");
            ledger = new CaseRewardLedger(saved, () => 0.99, persist);
            Require(ledger.PendingCount == 3 && !ledger.CanAwardDailyWorkoutCase(Day), "Migrated quota or queue did not survive reload.");
            Require(ledger.TryClaim("a", out int gems) && gems == 20, "Legacy opened prize could not be claimed.");
            Require(ledger.GemsBalance == 31 && ledger.Find("b").UpgradeTapsUsed == 1 && ledger.Find("new") != null,
                "Claim of legacy prize changed other cases.");
        }

        private static void DailyFailedWrites()
        {
            var f = new Fixture();
            f.FailWrites = true;
            MustThrow<IOException>(() => f.Ledger.TryAwardDailyWorkoutCase("a", 1, Day));
            Require(f.Ledger.CanAwardDailyWorkoutCase(Day) && f.Ledger.PendingCount == 0, "Failed daily award consumed quota.");
            f.FailWrites = false;
            Require(f.Ledger.TryAwardDailyWorkoutCase("a", 1, Day), "Failed award consumed the receipt.");
            f.FailWrites = true;
            MustThrow<IOException>(() => f.Ledger.TryAwardDailyWorkoutCase("b", 1, Day));
            f.FailWrites = false;
            f.Reload();
            Require(f.Ledger.TryAwardDailyWorkoutCase("b", 1, Day.AddDays(1)), "Failed denied-receipt save was exposed as committed.");
            Require(f.Ledger.PendingCount == 2, "Failed denied receipt damaged existing inventory.");
        }

        private static void TapPersistence()
        {
            var f = new Fixture(0, 0, 0);
            f.Ledger.AwardForWorkout("a", 2);
            PendingCase firstSnapshot = f.Ledger.Pending;
            for (int tap = 0; tap < CaseRewards.UpgradeTapCount; tap++)
            {
                Require(f.Ledger.TryUpgrade("a", tap, out var updated), "Expected tap was rejected.");
                Require(updated.UpgradeTapsUsed == tap + 1 && (int)updated.Rarity == tap + 1, "Successful tap did not consume one attempt and one tier.");
                f.Reload();
                Require(f.Ledger.Pending.UpgradeTapsUsed == tap + 1 && f.Ledger.Pending.Rarity == updated.Rarity, "Reload lost accepted tap.");
                Require(!f.Ledger.TryUpgrade("a", tap, out _), "Repeated callback consumed an additional tap.");
            }
            Require(firstSnapshot.UpgradeTapsUsed == 0 && firstSnapshot.Rarity == CaseRarity.Common, "A returned snapshot was mutated by later transitions.");
            Require(!f.Ledger.TryUpgrade("a", 3, out _) && f.Ledger.Pending.CanOpen, "More than three upgrades were accepted or opening is blocked.");
            Require(f.Draws == 3 && f.Writes == 4, "Duplicate taps used randomness or generated saves.");
        }

        private static void MissedUpgrades()
        {
            var f = new Fixture(0.99, 0.99, 0.99, 0);
            f.Ledger.AwardForWorkout("a", 1);
            f.UpgradeAll("a");
            Require(f.Ledger.Pending.Rarity == CaseRarity.Common && f.Ledger.Pending.CanOpen, "Failed upgrades did not count as attempts.");
            Require(f.Ledger.TryOpen("a", out var opened) && opened.Gems == 10, "Common case could not be opened after three misses.");
        }

        private static void OpenPersistence()
        {
            var f = new Fixture(0, 0, 0, 0.5);
            f.Ledger.AwardForWorkout("a", 1);
            f.UpgradeAll("a");
            Require(f.Ledger.TryOpen("a", out var opened), "Ready case failed to open.");
            Require(opened.Opened && opened.Gems == 240 && f.Ledger.GemsBalance == 0, "Opening did not record its expected prize or paid before claim.");
            f.Reload();
            Require(f.Ledger.Pending.Opened && f.Ledger.Pending.Gems == opened.Gems, "Reload discarded an opened prize.");
            Require(f.Ledger.TryOpen("a", out var replay) && replay.Gems == opened.Gems, "Reopening rerolled the prize.");
            Require(!f.Ledger.TryUpgrade("a", 3, out _) && f.Draws == 4 && f.Writes == 5, "Opened case accepted another random transition.");
        }

        private static void ClaimPersistence()
        {
            var f = new Fixture(0, 0, 0, 0.5);
            f.Ledger.AwardForWorkout("a", 1);
            f.UpgradeAll("a");
            f.Ledger.TryOpen("a", out _);
            f.Reload();
            Require(f.Ledger.TryClaim("a", out int gems) && gems == 240, "Claim did not pay the saved prize.");
            f.Reload();
            Require(f.Ledger.GemsBalance == 240 && f.Ledger.PendingCount == 0, "Claim and removal did not persist together.");
            Require(!f.Ledger.TryClaim("a", out int duplicate) && duplicate == 0, "Repeated claim paid twice.");
            Require(!f.Ledger.AwardForWorkout("a", 999), "Claimed workout could be awarded again.");
            Require(f.Writes == 6 && f.Ledger.GemsBalance == gems, "Duplicate claim or award changed wallet.");
        }

        private static void Queue()
        {
            var f = new Fixture(0, 0.99, 0.99, 0.99, 0, 0.99, 0.99, 0);
            f.Ledger.TryGrantCase("a");
            f.Ledger.TryUpgrade("a", 0, out _);
            f.Ledger.TryGrantCase("b");
            f.Reload();
            Require(f.Ledger.PendingCount == 2 && f.Ledger.Pending.Id == "a" && f.Ledger.Pending.UpgradeTapsUsed == 1, "New award replaced or reset the current case.");
            Require(!f.Ledger.TryOpen("b", out _), "An unready selected case opened.");
            f.UpgradeAll("b");
            Require(f.Ledger.TryOpen("b", out var selected) && selected.Id == "b" && selected.Gems == 10, "Selected queued case did not open independently.");
            f.Reload();
            Require(f.Ledger.Pending.Id == "a" && f.Ledger.Pending.UpgradeTapsUsed == 1 && f.Ledger.Find("b").Opened,
                "Selected opening altered another case or was lost on reload.");
            Require(f.Ledger.TryClaim("b", out int selectedGems) && selectedGems == 10, "Selected queued prize could not be claimed.");
            Require(f.Ledger.Pending.Id == "a" && f.Ledger.Find("b") == null, "Selected claim removed the wrong case.");
            Require(!f.Ledger.TryOpen("b", out _) && !f.Ledger.TryClaim("b", out _), "Stale selected receipt affected remaining inventory.");
            f.Ledger.TryUpgrade("a", 1, out _);
            f.Ledger.TryUpgrade("a", 2, out _);
            f.Ledger.TryOpen("a", out _);
            f.Ledger.TryClaim("a", out _);
            f.Reload();
            Require(!f.Ledger.TryOpen("a", out _) && !f.Ledger.TryUpgrade("a", 0, out _) && !f.Ledger.TryClaim("a", out _), "Stale case callbacks were accepted.");
            Require(f.Ledger.PendingCount == 0 && f.Ledger.GemsBalance == 40, "Queue claims lost inventory or paid the wrong total.");
        }

        private static void FailedWrites()
        {
            var f = new Fixture();
            f.FailWrites = true;
            MustThrow<IOException>(() => f.Ledger.AwardForWorkout("a", 1));
            Require(f.Ledger.PendingCount == 0, "Failed award was exposed to the UI.");
            f.FailWrites = false;
            Require(f.Ledger.AwardForWorkout("a", 1), "Failed award permanently consumed session ID.");
            f.FailWrites = true;
            MustThrow<IOException>(() => f.Ledger.TryUpgrade("a", 0, out _));
            Require(f.Ledger.Pending.UpgradeTapsUsed == 0, "Failed tap advanced state.");
            f.FailWrites = false;
            f.UpgradeAll("a");
            f.FailWrites = true;
            MustThrow<IOException>(() => f.Ledger.TryOpen("a", out _));
            Require(!f.Ledger.Pending.Opened, "Failed opening exposed an unsaved prize.");
            f.FailWrites = false;
            f.Ledger.TryOpen("a", out var opened);
            f.FailWrites = true;
            MustThrow<IOException>(() => f.Ledger.TryClaim("a", out _));
            Require(f.Ledger.Pending.Opened && f.Ledger.GemsBalance == 0, "Failed claim removed the case or changed balance.");
            f.FailWrites = false;
            f.Reload();
            Require(f.Ledger.TryClaim("a", out int gems) && gems == opened.Gems, "Failed claim could not be retried from saved prize.");
        }

        private static void InvalidSaves()
        {
            foreach (string json in new[]
            {
                "{\"version\":99,\"gems\":0,\"awardedWorkouts\":[],\"cases\":[]}",
                "{\"version\":2,\"gems\":0,\"awardedWorkouts\":[],\"cases\":[],\"processedWorkoutIds\":[],\"dailyWorkoutUtcDay\":\"2026-02-31\",\"dailyWorkoutCount\":1}",
                "{\"version\":2,\"gems\":0,\"awardedWorkouts\":[],\"cases\":[],\"processedWorkoutIds\":[],\"dailyWorkoutCount\":-1}",
                "{\"version\":1,\"gems\":-1,\"awardedWorkouts\":[],\"cases\":[]}",
                "{\"version\":1,\"gems\":0,\"awardedWorkouts\":[\"a\"],\"cases\":[{\"id\":\"a\",\"rarity\":3,\"taps\":0}]}",
                "{\"version\":1,\"gems\":0,\"awardedWorkouts\":[\"a\"],\"cases\":[{\"id\":\"a\",\"rarity\":0,\"taps\":3,\"opened\":true,\"gems\":9999}]}"
            })
            {
                int writes = 0;
                MustThrow<InvalidOperationException>(() => new CaseRewardLedger(json, () => 0, _ => writes++));
                Require(writes == 0, "Unsupported save was silently overwritten.");
            }
        }

        private sealed class Fixture
        {
            public CaseRewardLedger Ledger;
            public int Draws, Writes;
            public bool FailWrites;
            private string _saved;
            private readonly Queue<double> _rolls;

            public Fixture(params double[] rolls)
            {
                _rolls = new Queue<double>(rolls);
                Reload();
            }

            public void Reload() => Ledger = new CaseRewardLedger(_saved, () =>
            {
                Draws++;
                return _rolls.Count > 0 ? _rolls.Dequeue() : 0.99;
            }, json =>
            {
                if (FailWrites) throw new IOException("Simulated save failure");
                _saved = json;
                Writes++;
            });

            public void UpgradeAll(string id)
            {
                for (int i = 0; i < CaseRewards.UpgradeTapCount; i++)
                    Require(Ledger.TryUpgrade(id, i, out _), "Upgrade failed in fixture setup.");
            }
        }

        private static void Check(string name, Action action, StringBuilder report, ref int passed, ref int failed)
        {
            try { action(); passed++; report.AppendLine("PASS " + name); }
            catch (Exception exception) { failed++; report.AppendLine("FAIL " + name + ": " + exception); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void MustThrow<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
