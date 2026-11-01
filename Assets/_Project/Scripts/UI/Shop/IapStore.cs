using System;
using System.Collections.Generic;
using System.Linq;
using PushStars.Core;
using PushStars.Services;
using UnityEngine;
using UnityEngine.Purchasing;

namespace PushStars.UI
{
    public enum IapOutcome { Bought, Cancelled, Deferred, Failed }

    /// <summary>
    /// The real store behind <see cref="StoreCatalog"/> (docs/architecture/in-app-purchases.md). A
    /// purchase goes: the store takes the money, the server verifies Apple's signed transaction and
    /// claims it for this account, the wallet is credited, and only then the transaction is
    /// finished. Anything failing on the way leaves it open, and the store delivers it again on the
    /// next launch, so a paid purchase is never lost; the wallet keys gems by transaction, so it is
    /// never paid twice. Heroes the Apple ID already owns come back by themselves on connecting.
    /// </summary>
    public static class IapStore
    {
        private static StoreController _store;
        private static bool _ready, _connecting;
        private static readonly Dictionary<string, Action<IapOutcome, string>> Waiting = new Dictionary<string, Action<IapOutcome, string>>();
        private static readonly HashSet<string> InFlight = new HashSet<string>();
        private static Action<bool, string> _restoring;

        /// <summary>Prices arrived, or a purchase changed what the player has.</summary>
        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _store = null; _ready = _connecting = false; _restoring = null; Changed = null;
            Waiting.Clear(); InFlight.Clear();
        }

        /// <summary>Connects once; safe to call from every screen that sells something.</summary>
        public static async void Start()
        {
            if (!StoreAccess.RealMoney || _ready || _connecting) return;
            _connecting = true;
            try
            {
                if (_store == null)
                {
                    _store = UnityIAPServices.StoreController();
                    _store.OnStoreConnected += FetchProducts;
                    _store.OnProductsFetched += OnProductsFetched;
                    _store.OnProductsFetchFailed += failed => Debug.LogWarning("[Store] products: " + failed.FailureReason);
                    _store.OnPurchasesFetched += OnPurchasesFetched;
                    _store.OnPurchasesFetchFailed += failed => Debug.LogWarning("[Store] purchases: " + failed.FailureReason);
                    _store.OnPurchasePending += OnPending;
                    _store.OnPurchaseConfirmed += OnConfirmed;
                    _store.OnPurchaseFailed += OnFailed;
                    _store.OnPurchaseDeferred += order => Finish(ProductOf(order), IapOutcome.Deferred, "WAITING FOR APPROVAL");
                    _store.OnStoreDisconnected += description => { _ready = false; Debug.LogWarning("[Store] disconnected: " + description.Message); };
                }
                await _store.Connect();
            }
            catch (Exception e) { Debug.LogWarning("[Store] connect: " + e.Message); }
            finally { _connecting = false; }
        }

        private static void FetchProducts()
        {
            var products = StoreCatalog.GemPacks.Select(pack => new ProductDefinition(pack.ProductId, ProductType.Consumable)).ToList();
            foreach (var offer in AvatarCatalog.All)
                if (offer != null && offer.Kind == AvatarPurchaseKind.Dollars && !string.IsNullOrEmpty(offer.StoreProductId))
                    products.Add(new ProductDefinition(offer.StoreProductId, ProductType.NonConsumable));
            _store.FetchProducts(products);
        }

        private static void OnProductsFetched(List<Product> products)
        {
            _ready = true;
            Changed?.Invoke();
            _store.FetchPurchases();
        }

        /// <summary>The store's price in the player's own currency, or the catalog's until it answers.</summary>
        public static string PriceOf(string productId, string fallback)
        {
            string price = _ready ? _store.GetProductById(productId)?.metadata?.localizedPriceString : null;
            return string.IsNullOrEmpty(price) ? fallback : price;
        }

        /// <param name="done">Called on the main thread with the outcome and, if the player should
        /// read something, a line of text.</param>
        public static void Buy(string productId, Action<IapOutcome, string> done)
        {
            var product = _ready ? _store.GetProductById(productId) : null;
            if (product == null || !product.availableToPurchase)
            {
                Start();
                done?.Invoke(IapOutcome.Failed, "STORE UNAVAILABLE. TRY AGAIN");
                return;
            }
            Waiting[productId] = done;
            _store.PurchaseProduct(product);
        }

        /// <summary>Apple asks for this button: heroes bought on this Apple ID come back.</summary>
        public static void Restore(Action<bool, string> done)
        {
            if (!_ready) { Start(); done?.Invoke(false, "STORE UNAVAILABLE. TRY AGAIN"); return; }
            _restoring = done;
            _store.RestoreTransactions((ok, error) =>
            {
                if (!ok) { Debug.LogWarning("[Store] restore: " + error); FinishRestore(false, "COULDN'T RESTORE. TRY AGAIN"); return; }
                _store.FetchPurchases();
            });
        }

        private static void FinishRestore(bool ok, string text)
        {
            var done = _restoring;
            _restoring = null;
            done?.Invoke(ok, text);
        }

        private static string ProductOf(Order order) => order?.CartOrdered?.Items()?.FirstOrDefault()?.Product?.definition?.id;

        private static async void OnPending(PendingOrder order)
        {
            string productId = ProductOf(order), transaction = order.Info.TransactionID;
            if (!InFlight.Add(transaction)) return;
            try
            {
                var grant = await Verified(order.Info, productId);
                if (grant.status == PurchaseGrant.Ok) Credit(grant, transaction);
                _store.ConfirmPurchase(order);
                Changed?.Invoke();
                if (grant.status == PurchaseGrant.Ok) Finish(productId, IapOutcome.Bought, "");
                else Finish(productId, IapOutcome.Failed, grant.status == "claimed" ? "ALREADY USED ON ANOTHER ACCOUNT" : "PURCHASE WAS REFUNDED");
            }
            catch (Exception e)
            {
                // Not finished at the store: it comes back on the next launch and is credited then.
                Debug.LogWarning("[Store] not credited yet: " + e.Message);
                Finish(productId, IapOutcome.Failed, "PAID. YOUR ITEM ARRIVES WHEN YOU'RE BACK ONLINE");
            }
            finally { InFlight.Remove(transaction); }
        }

        /// <summary>Finished transactions the store still lists: heroes this account does not have
        /// yet (a reinstall, a new account on this Apple ID, the restore button).</summary>
        private static async void OnPurchasesFetched(Orders orders)
        {
            int restored = 0;
            foreach (var order in orders.ConfirmedOrders)
            {
                var hero = StoreCatalog.HeroFor(ProductOf(order));
                if (hero == null || CaseRewards.OwnsAvatar(hero.Id)) continue;
                try
                {
                    var grant = await Verified(order.Info, hero.StoreProductId);
                    if (grant.status == PurchaseGrant.Ok && CaseRewards.TryGrantBoughtAvatar(grant.hero)) restored++;
                }
                catch (Exception e) { Debug.LogWarning("[Store] restore " + hero.Id + ": " + e.Message); }
            }
            if (restored > 0) Changed?.Invoke();
            FinishRestore(true, restored > 0 ? "PURCHASES RESTORED" : "NOTHING TO RESTORE");
        }

        private static async System.Threading.Tasks.Task<PurchaseGrant> Verified(IOrderInfo info, string productId)
        {
#if UNITY_EDITOR
            // The Editor's fake store signs nothing, and no real money moved.
            await System.Threading.Tasks.Task.Yield();
            StoreCatalog.TryGemPack(productId, out var pack);
            return new PurchaseGrant { status = PurchaseGrant.Ok, gems = pack.Gems, hero = StoreCatalog.HeroFor(productId)?.Id ?? "", transactionId = info.TransactionID };
#else
            string signed = info.Apple?.jwsRepresentation;
            if (string.IsNullOrEmpty(signed)) throw new InvalidOperationException("The store sent no signed transaction.");
            return await PurchaseClient.Verify(signed);
#endif
        }

        private static void Credit(PurchaseGrant grant, string transaction)
        {
            if (grant.gems > 0) CaseRewards.TryCreditGems("store:" + transaction, grant.gems);
            if (!string.IsNullOrEmpty(grant.hero)) CaseRewards.TryGrantBoughtAvatar(grant.hero);
        }

        private static void OnConfirmed(Order order)
        {
            if (order is FailedOrder failed) Debug.LogWarning("[Store] confirm: " + failed.FailureReason + " " + failed.Details);
        }

        private static void OnFailed(FailedOrder order)
        {
            Debug.Log("[Store] purchase failed: " + order.FailureReason + " " + order.Details);
            if (order.FailureReason == PurchaseFailureReason.UserCancelled) Finish(ProductOf(order), IapOutcome.Cancelled, "");
            else Finish(ProductOf(order), IapOutcome.Failed, "PURCHASE FAILED. YOU WERE NOT CHARGED");
        }

        private static void Finish(string productId, IapOutcome outcome, string text)
        {
            if (string.IsNullOrEmpty(productId) || !Waiting.TryGetValue(productId, out var done)) return;
            Waiting.Remove(productId);
            done?.Invoke(outcome, text);
        }
    }
}
