using System;
using System.Collections.Generic;
using System.IO;
using PushStars.Core;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Hero unlocks and the meme Aura economy against the production ledger with in-memory
    /// persistence; never touches the player's save.</summary>
    public static class AvatarUnlockRegression
    {
        [MenuItem("Tools/Push Stars/Rewards/Validate Avatar Unlocks")]
        public static void Run()
        {
            Formatting();
            Pricing();
            string saved = null;
            bool fail = false;
            int writes = 0;
            Action<string> persist = json => { if (fail) throw new IOException("Simulated write failure"); saved = json; writes++; };
            // The walk below is written for the two card heroes it names; later catalog heroes would join the drop pool.
            var offers = Array.FindAll(AvatarCatalog.Defaults(), o => string.IsNullOrEmpty(o.Prefab));
            var ledger = new CaseRewardLedger(null, () => 0, persist, offers);
            Require(ledger.OwnsAvatar("fighter") && !ledger.OwnsAvatar("robot"), "Initial ownership");
            Require(!ledger.TryBuyAvatar("robot") && ledger.AuraBalance == 0, "Aura hero bought");
            Open(ledger, "cards-a");
            var prize = ledger.Find("cards-a");
            Require(prize.AvatarId == "robot" && prize.AvatarCards == 10, "Legendary drop");
            Require(ledger.CardsFor("robot") == 0, "Opening granted cards before claiming");
            ledger = new CaseRewardLedger(saved, () => throw new Exception("Prize rerolled"), persist, offers);
            Require(ledger.TryOpen("cards-a", out var replay) && replay.AvatarCards == 10, "Prize persistence");
            fail = true;
            try { ledger.TryClaim("cards-a", out _); throw new Exception("Expected claim save failure"); } catch (IOException) { }
            Require(ledger.CardsFor("robot") == 0 && ledger.Find("cards-a") != null, "Failed claim changed progress");
            fail = false;
            Require(ledger.TryClaim("cards-a", out _) && ledger.CardsFor("robot") == 10 &&
                ledger.AvatarUnlockProgress("robot") == 10000 && ledger.AvatarPrice("robot") == 40000, "Cards fill the bar at 1K each");
            Require(!ledger.TryClaim("cards-a", out _) && ledger.CardsFor("robot") == 10, "Duplicate claim");

            // Wins raise balance and peak; losses lower only the balance and never the bar.
            Require(Apply(ledger, "fight:1", 30000).Change == 30000 && ledger.AuraPeak == 30000 &&
                ledger.AvatarUnlockProgress("robot") == 40000, "Win moved balance and peak");
            Require(Apply(ledger, "fight:2", -20000).Change == -20000 && ledger.AuraBalance == 10000 &&
                ledger.AuraPeak == 30000 && ledger.AvatarUnlockProgress("robot") == 40000, "Loss lowered the peak or the bar");
            Require(Apply(ledger, "fight:2", -20000).Applied.Count == 0 && ledger.AuraBalance == 10000, "Receipt replay");
            Require(Apply(ledger, "fight:3", -999999).Change == -10000 && ledger.AuraBalance == 0 && ledger.AuraPeak == 30000, "Zero floor");
            Require(ledger.TryCreditAura("earned:1", 250) && !ledger.TryCreditAura("earned:1", 250) && ledger.AuraBalance == 250, "Credit receipt replay");
            fail = true;
            try { Apply(ledger, "fight:4", 50000); throw new Exception("Expected Aura save failure"); } catch (IOException) { }
            Require(ledger.AuraBalance == 250 && ledger.AuraPeak == 30000 && !ledger.OwnsAvatar("robot") &&
                !ledger.HasAuraReceipt("fight:4"), "Failed Aura save changed state");
            fail = false;
            var unlock = Apply(ledger, "fight:4", 39750);
            Require(ledger.AuraPeak == 40000 && ledger.OwnsAvatar("robot") && unlock.Unlocked.Count == 1 &&
                unlock.Unlocked[0] == "robot" && ledger.AvatarPrice("robot") == 0, "Peak + cards reaching the goal unlocks");
            Require(Apply(ledger, "fight:5", -40000).Unlocked.Count == 0 && ledger.OwnsAvatar("robot"), "Losing Aura re-locked a hero");
            ledger = new CaseRewardLedger(saved, () => 0, persist, offers);
            Require(ledger.OwnsAvatar("robot") && ledger.AuraBalance == 0 && ledger.AuraPeak == 40000, "Unlock reload");

            // Gladiator: the 40K peak plus 210 cards × 1K fill its 250K bar on the 21st case.
            for (int i = 0; i < 21; i++)
            {
                Require(!ledger.OwnsAvatar("gladiator"), "Gladiator unlocked early");
                Open(ledger, "glad-" + i); Require(ledger.Find("glad-" + i).AvatarId == "gladiator", "Owned hero dropped"); ledger.TryClaim("glad-" + i, out _);
            }
            Require(ledger.OwnsAvatar("gladiator") && ledger.CardsFor("gladiator") == 210 && ledger.AvatarPrice("gladiator") == 0, "Peak + card unlock");
            Open(ledger, "finished");
            Require(ledger.Find("finished").AvatarCards == 0, "Completed collection still dropped cards");

            foreach (var kind in new[] { AvatarPurchaseKind.Gems, AvatarPurchaseKind.Dollars })
            {
                var offer = new AvatarOffer { Id = "exclusive", Kind = kind, Price = 99, RequiredCards = 100 };
                Require(!offer.UsesCards && !offer.UnlocksByAura && offer.UnlockProgress(1000000, 100) == 0, "Premium unlocked by Aura or cards");
                string premiumSave = null;
                var premium = new CaseRewardLedger(null, () => 0, json => premiumSave = json, new[] { offer });
                Open(premium, "premium-case");
                Require(premium.Find("premium-case").AvatarCards == 0, "Premium hero in drop pool");
                premium.TryClaim("premium-case", out _);
                long gemsBefore = premium.GemsBalance;
                premium.TryCreditAura("premium-aura", 999999);
                Require(!premium.OwnsAvatar(offer.Id), "Aura peak unlocked a premium hero");
                bool bought = premium.TryBuyAvatar(offer.Id);
                Require(bought == (kind == AvatarPurchaseKind.Gems), "Premium purchase currency");
                Require(premium.AuraBalance == 999999 && premium.GemsBalance == gemsBefore - (bought ? 99 : 0), "Wrong wallet debited");
                premium = new CaseRewardLedger(premiumSave, () => 0, _ => { }, new[] { offer });
                Require(premium.OwnsAvatar(offer.Id) == bought, "Premium purchase persistence");
            }

            var legacy = new CaseRewardLedger("{\"version\":2,\"gems\":45,\"awardedWorkouts\":[],\"cases\":[],\"processedWorkoutIds\":[]}", () => 0, persist);
            Require(legacy.GemsBalance == 45 && legacy.CardsFor("robot") == 0 && legacy.AuraBalance == 0 && legacy.AuraPeak == 0, "Version 2 migration");
            // v5: the legacy 200 welcome is topped up to 10K once; a hero bought with old Aura stays owned.
            string v5 = "{\"version\":5,\"gems\":0,\"aura\":150,\"auraReceipts\":[\"" + CaseRewards.AssessmentAuraReceipt +
                "\"],\"gemReceipts\":[],\"avatars\":[{\"id\":\"robot\",\"cards\":20,\"owned\":true}],\"awardedWorkouts\":[],\"cases\":[],\"processedWorkoutIds\":[]}";
            string v6 = null;
            var topped = new CaseRewardLedger(v5, () => 0, json => v6 = json);
            Require(topped.AuraBalance == 150 + CaseRewards.AssessmentAura - CaseRewards.LegacyAssessmentAura &&
                topped.AuraPeak == topped.AuraBalance && topped.OwnsAvatar("robot"), "Version 5 migration");
            Require(topped.TryCreditAura("probe", 1), "Migrated ledger cannot commit");
            topped = new CaseRewardLedger(v6, () => 0, _ => { });
            Require(topped.AuraBalance == 150 + CaseRewards.AssessmentAura - CaseRewards.LegacyAssessmentAura + 1, "Top-up paid twice");

            CaseRewardsRegression.Run();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/avatar-unlocks-regression.txt", "PASS: Aura format, fight pricing (win/crush/clutch/comeback/draw/loss/destroyed/gave-it-all/rookie/quit/stakes/performance/streak/boss), level and league Aura, persisted drops, one-time claims, failed saves, cards at 1K on the bar, peak never drops, zero floor, receipt replay, peak+cards unlock, no re-lock, full-card unlock, completed collection, premium exclusion, v2 and v5 migrations.\n");
            Debug.Log("[AvatarUnlockRegression] PASS");
        }

        private static void Formatting()
        {
            var cases = new Dictionary<long, string> {
                { 0, "0" }, { 999, "999" }, { 1000, "1K" }, { 1500, "1.5K" }, { 9999, "9.9K" }, { 10000, "10K" },
                { 150999, "150K" }, { 999999, "999K" }, { 1000000, "1M" }, { 1500000, "1.5M" }, { 12345678, "12M" },
                { 1500000000, "1.5B" }, { -2500, "-2.5K" } };
            foreach (var pair in cases) Require(AuraFormat.Short(pair.Key) == pair.Value, $"Short({pair.Key}) = {AuraFormat.Short(pair.Key)}");
            Require(AuraFormat.Delta(1000) == "+1000" && AuraFormat.Delta(-500) == "-500" && AuraFormat.Delta(10000) == "+10000" &&
                AuraFormat.Delta(150000) == "+150K", "Delta format");
        }

        private static void Pricing()
        {
            var win = new AuraFightInput { Won = true, MyReps = 20, OpponentReps = 18, AverageForm = 80, PersonalBestBefore = 25, RatedFightsBefore = 10, LeagueId = "bronze" };
            Expect(win, 1000, "VICTORY");
            var crush = win; crush.OpponentReps = 10; Expect(crush, 1500, "DOMINATION");
            var clutch = win; clutch.MyReps = 11; clutch.OpponentReps = 10; Expect(clutch, 1500, "VICTORY", "CLUTCH");
            var comeback = win; comeback.LateDeficit = 3; Expect(comeback, 2000, "VICTORY", "COMEBACK");
            var draw = win; draw.Won = false; draw.Draw = true; draw.OpponentReps = 20; Expect(draw, 250, "DRAW");
            var loss = win; loss.Won = false; loss.MyReps = 10; loss.OpponentReps = 12; Expect(loss, -500, "DEFEAT");
            var rookie = loss; rookie.RatedFightsBefore = 4; Expect(rookie, 0, "ROOKIE SHIELD");
            var destroyed = loss; destroyed.OpponentReps = 20; Expect(destroyed, -1000, "DESTROYED");
            var gold = destroyed; gold.LeagueId = "gold"; Expect(gold, -2000, "DESTROYED");
            var effort = loss; effort.MyReps = 23; effort.OpponentReps = 25; Expect(effort, 0, "GAVE IT ALL · NO AURA LOST");
            var quit = new AuraFightInput { Quit = true, RatedFightsBefore = 10, LeagueId = "silver", MyReps = 30, AverageForm = 99 };
            Expect(quit, -3000, "RAGE QUIT");
            var big = win; big.MyReps = 26; big.AverageForm = 95; big.ClapReps = 7; big.WinStreakAfter = 3; big.LeagueId = "diamond";
            Expect(big, 3 * (1000 + 500 + 1250 + 2000 + 1000), "VICTORY", "PERFECT FORM", "CLAP PUSH-UPS x5", "NEW RECORD", "WIN STREAK x3");
            var streak = win; streak.WinStreakAfter = 20; Expect(streak, 11000, "VICTORY", "WIN STREAK x20");
            var boss = new AuraFightInput { Kind = AuraFightKind.Boss, Won = true, MyReps = 8, FirstBossWin = true, IslandKing = true, LeagueId = "diamond" };
            Expect(boss, 10000, "ISLAND KING DOWN");
            var bossLoss = boss; bossLoss.Won = false; Expect(bossLoss, 0);
            var bossQuit = boss; bossQuit.Quit = true; Expect(bossQuit, 0);
            Require(AuraRewards.ForLevel(1) == 0 && AuraRewards.ForLevel(2) == 1000 && AuraRewards.ForLevel(5) == 6000 &&
                AuraRewards.ForLevelUp(1, 5) == 9000 && AuraRewards.ForPromotion("diamond") == 100000, "Level and league Aura");
            var robot = new AvatarOffer { Id = "robot", Kind = AvatarPurchaseKind.Aura, Price = 50000, RequiredCards = 50 };
            Require(robot.CardAura == 1000 && robot.UnlockProgress(30000, 10) == 40000 && robot.UnlockProgress(60000, 0) == 50000 &&
                robot.UnlockProgress(-5, 60) == 50000 && !robot.Unlocked(49999, 0) && robot.Unlocked(49000, 1), "Unlock bar math");
        }

        private static void Expect(AuraFightInput input, long total, params string[] captions)
        {
            var moments = AuraCalculator.ForFight(input);
            var names = moments.ConvertAll(m => m.Caption);
            Require(AuraCalculator.Total(moments) == total && names.Count == captions.Length && names.TrueForAll(n => Array.IndexOf(captions, n) >= 0),
                $"Pricing: expected {total} [{string.Join(", ", captions)}], got {AuraCalculator.Total(moments)} [{string.Join(", ", names)}]");
        }

        private static AuraApplyResult Apply(CaseRewardLedger ledger, string receipt, long delta)
            => ledger.ApplyAura(new[] { new AuraGrant(receipt, delta) });

        private static void Open(CaseRewardLedger ledger, string id)
        {
            Require(ledger.TryGrantCase(id), "Grant");
            for (int i = 0; i < 3; i++) Require(ledger.TryUpgrade(id, i, out _), "Upgrade");
            Require(ledger.TryOpen(id, out _), "Open");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
