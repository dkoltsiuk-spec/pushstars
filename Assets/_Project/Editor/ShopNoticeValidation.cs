using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using PushStars.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class ShopNoticeValidation
    {
        [MenuItem("Tools/Push Stars/UI/Validate Shop Notices")]
        public static void Run()
        {
            string saved = null;
            int writes = 0;
            bool fail = false;
            Action<string> persist = json =>
            {
                if (fail) throw new IOException("Simulated save failure");
                saved = json; writes++;
            };
            var ledger = new ShopNoticeLedger(null, persist);
            var initial = new[] { "emote:a:Rare:100", "gems:30:$1.99" };
            Check(!ledger.HasUnseen(Array.Empty<string>()), "Empty shop has no notice");
            Check(ledger.HasUnseen(initial), "First available goods are new");
            ledger.MarkSeen(initial);
            Check(!ledger.HasUnseen(initial), "Visit clears notices");
            ledger.MarkSeen(initial.Reverse());
            Check(writes == 1, "Reordering and repeated visits do not write");
            ledger = new ShopNoticeLedger(saved, persist);
            Check(!ledger.HasUnseen(initial), "Seen goods stay seen after restart");
            var added = initial.Concat(new[] { "emote:b:Epic:250" }).ToArray();
            Check(ledger.HasUnseen(added), "New product triggers notice");
            var changed = new[] { "emote:a:Rare:75", initial[1] };
            Check(ledger.HasUnseen(changed), "Changed offer triggers notice");
            var owned = initial.Concat(new[] { "owned-avatar:robot", "owned-emote:a" }).ToArray();
            Check(ledger.HasUnseen(owned), "New acquisition triggers notice");
            Check(!ledger.HasUnseen(initial.Take(1)), "Removal does not trigger notice");
            fail = true;
            try { ledger.MarkSeen(added); throw new InvalidOperationException("Expected save failure"); }
            catch (IOException) { }
            Check(ledger.HasUnseen(added), "Failed save does not acknowledge goods");
            fail = false;
            ledger.MarkSeen(added);
            Check(!new ShopNoticeLedger(saved, persist).HasUnseen(added), "New goods acknowledgement persists");
            Check(new ShopNoticeLedger("invalid", persist).HasUnseen(initial), "Invalid save recovers");
            Directory.CreateDirectory("Logs/Shop");
            File.WriteAllText("Logs/Shop/notices-validation.txt", "PASS: initial goods, visit, restart, additions, price changes, acquisitions, removals, stable ordering and failed persistence.\n");
            Debug.Log("[ShopNoticeValidation] PASS");
        }

        public static void CheckInteractions()
        {
            Check(Application.isPlaying, "Run in Play Mode");
            var shop = Object.FindFirstObjectByType<ShopScreen>();
            Check(shop != null, "Shop exists");
            var field = typeof(ShopScreen).GetField("_notices", BindingFlags.Instance | BindingFlags.NonPublic);
            var refresh = typeof(ShopScreen).GetMethod("RefreshNoticeBadge", BindingFlags.Instance | BindingFlags.NonPublic);
            var original = field.GetValue(shop);
            string saved = null;
            var badge = shop.Entry.transform.Find("ShopNoticeBadge");
            long gems = CaseRewards.GemsBalance, aura = CaseRewards.AuraBalance;
            try
            {
                field.SetValue(shop, new ShopNoticeLedger(null, json => saved = json));
                shop.Hide(); refresh.Invoke(shop, null);
                Check(shop.HasUnseenUpdates && badge.gameObject.activeSelf, "Unseen goods show badge");
                Check(badge.GetComponent<TrophyBadgeGraphic>().InfoOnly && !badge.GetComponent<TrophyBadgeGraphic>().raycastTarget,
                    "Shared trophy graphic does not block taps");
                var legacy = shop.Entry.transform.Find("InfoBadge");
                Check(legacy == null || !legacy.gameObject.activeSelf, "Legacy circle hidden");
                Check(!shop.CaptureNotices().Any(n => n.Contains("sonic")), "Retired offer does not notify");
                shop.Entry.onClick.Invoke();
                Check(shop.IsOpen && !shop.HasUnseenUpdates && !badge.gameObject.activeSelf, "Entry acknowledges notices");
                shop.Back.onClick.Invoke();
                Check(!shop.IsOpen && !badge.gameObject.activeSelf, "Closing does not restore badge");
                field.SetValue(shop, new ShopNoticeLedger(saved, json => saved = json));
                refresh.Invoke(shop, null);
                Check(!badge.gameObject.activeSelf, "Restart does not restore badge");
                var price = ShopScreen.GemPrices[0];
                try
                {
                    ShopScreen.GemPrices[0] = "$0.99";
                    refresh.Invoke(shop, null);
                    Check(shop.HasUnseenUpdates && badge.gameObject.activeSelf, "Changed live pack reappears");
                    shop.Show(); shop.Hide();
                    Check(!badge.gameObject.activeSelf, "Viewing changed pack clears notice");
                }
                finally { ShopScreen.GemPrices[0] = price; }
                Check(gems == CaseRewards.GemsBalance && aura == CaseRewards.AuraBalance, "Wallet unchanged");
                File.AppendAllText("Logs/Shop/notices-validation.txt", "PASS: live badge, entry/back, restart, changed pack, shared graphic, retired offer exclusion and unchanged wallet; notice persistence replaced with memory only.\n");
                Debug.Log("[ShopNoticeValidation] Play Mode PASS");
            }
            finally
            {
                shop.Hide();
                field.SetValue(shop, original);
                refresh.Invoke(shop, null);
            }
        }

        private static void Check(bool okay, string message)
        {
            if (!okay) throw new InvalidOperationException(message);
        }
    }
}
