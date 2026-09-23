using System;
using System.IO;
using PushStars.Core;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    public static class AvatarUnlockRegression
    {
        [MenuItem("Tools/Push Stars/Rewards/Validate Avatar Unlocks")]
        public static void Run()
        {
            string saved = null;
            bool fail = false;
            int writes = 0;
            Action<string> persist = json => { if (fail) throw new IOException("Simulated write failure"); saved = json; writes++; };
            var ledger = new CaseRewardLedger(null, () => 0, persist);
            Require(ledger.OwnsAvatar("fighter") && !ledger.OwnsAvatar("robot"), "Initial ownership");
            Require(!ledger.TryBuyAvatar("robot") && ledger.AuraBalance == 0, "Insufficient balance");
            Open(ledger, "cards-a");
            var prize = ledger.Find("cards-a");
            Require(prize.AvatarId == "robot" && prize.AvatarCards == 10, "Legendary drop");
            Require(ledger.CardsFor("robot") == 0, "Opening granted cards before claiming");
            ledger = new CaseRewardLedger(saved, () => throw new Exception("Prize rerolled"), persist);
            Require(ledger.TryOpen("cards-a", out var replay) && replay.AvatarCards == 10, "Prize persistence");
            fail = true;
            try { ledger.TryClaim("cards-a", out _); throw new Exception("Expected claim save failure"); } catch (IOException) { }
            Require(ledger.CardsFor("robot") == 0 && ledger.Find("cards-a") != null, "Failed claim changed progress");
            fail = false;
            Require(ledger.TryClaim("cards-a", out _) && ledger.CardsFor("robot") == 10 && ledger.AvatarPrice("robot") == 250, "Card discount");
            Require(!ledger.TryClaim("cards-a", out _) && ledger.CardsFor("robot") == 10, "Duplicate claim");
            Require(ledger.TryCreditAura("earned:1", 250) && !ledger.TryCreditAura("earned:1", 250), "Aura receipt replay");
            fail = true;
            try { ledger.TryBuyAvatar("robot"); throw new Exception("Expected purchase save failure"); } catch (IOException) { }
            Require(!ledger.OwnsAvatar("robot") && ledger.AuraBalance == 250, "Failed purchase debited balance");
            fail = false;
            Require(ledger.TryBuyAvatar("robot") && ledger.AuraBalance == 0 && !ledger.TryBuyAvatar("robot"), "Discounted purchase or double debit");
            ledger = new CaseRewardLedger(saved, () => 0, persist);
            Require(ledger.OwnsAvatar("robot") && ledger.CardsFor("robot") == 10, "Purchase reload");
            for (int i = 0; i < 12; i++) { Open(ledger, "glad-" + i); Require(ledger.Find("glad-" + i).AvatarId == "gladiator", "Owned hero dropped"); ledger.TryClaim("glad-" + i, out _); }
            Require(ledger.OwnsAvatar("gladiator") && ledger.CardsFor("gladiator") == 120 && ledger.AvatarPrice("gladiator") == 0 && ledger.AuraBalance == 0, "Free card unlock");
            Open(ledger, "finished");
            Require(ledger.Find("finished").AvatarCards == 0, "Completed collection still dropped cards");
            foreach (var kind in new[] { AvatarPurchaseKind.Gems, AvatarPurchaseKind.Dollars })
            {
                var offer = new AvatarOffer { Id = "exclusive", Kind = kind, Price = 99, RequiredCards = 100 };
                Require(!offer.UsesCards && offer.PriceAfterCards(100) == 99, "Premium discounted by cards");
                string premiumSave = null;
                var premium = new CaseRewardLedger(null, () => 0, json => premiumSave = json, new[] { offer });
                Open(premium, "premium-case");
                Require(premium.Find("premium-case").AvatarCards == 0, "Premium hero in drop pool");
                premium.TryClaim("premium-case", out _);
                long gemsBefore = premium.GemsBalance;
                premium.TryCreditAura("premium-aura", 999);
                bool bought = premium.TryBuyAvatar(offer.Id);
                Require(bought == (kind == AvatarPurchaseKind.Gems), "Premium purchase currency");
                Require(premium.AuraBalance == 999 && premium.GemsBalance == gemsBefore - (bought ? 99 : 0), "Wrong wallet debited");
                premium = new CaseRewardLedger(premiumSave, () => 0, _ => { }, new[] { offer });
                Require(premium.OwnsAvatar(offer.Id) == bought, "Premium purchase persistence");
            }
            var aura = new AvatarOffer { Kind = AvatarPurchaseKind.Aura, Price = 301, RequiredCards = 60 };
            Require(aura.PriceAfterCards(1) == 296 && aura.PriceAfterCards(59) == 6 && aura.PriceAfterCards(60) == 0 && aura.PriceAfterCards(-10) == 301, "Discount rounding/bounds");
            var legacy = new CaseRewardLedger("{\"version\":2,\"gems\":45,\"awardedWorkouts\":[],\"cases\":[],\"processedWorkoutIds\":[]}", () => 0, persist);
            Require(legacy.GemsBalance == 45 && legacy.CardsFor("robot") == 0 && legacy.AuraBalance == 0, "Version 2 migration");
            CaseRewardsRegression.Run();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/avatar-unlocks-regression.txt", "PASS: persisted drops, one-time claims, failed saves, partial discount, insufficient balance, one-time Aura receipts, atomic purchase, ownership reload, full-card unlock, completed collection, premium exclusion, rounding and legacy migration.\n");
            Debug.Log("[AvatarUnlockRegression] PASS");
        }
        private static void Open(CaseRewardLedger ledger, string id)
        {
            Require(ledger.TryGrantCase(id), "Grant");
            for (int i = 0; i < 3; i++) Require(ledger.TryUpgrade(id, i, out _), "Upgrade");
            Require(ledger.TryOpen(id, out _), "Open");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
