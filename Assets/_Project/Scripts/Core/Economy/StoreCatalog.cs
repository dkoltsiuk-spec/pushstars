using System;

namespace PushStars.Core
{
    /// <summary>A gem pack sold for real money. Its price comes from the store at run time.</summary>
    public readonly struct GemPack
    {
        public readonly string ProductId;
        public readonly int Gems;
        /// <summary>Shown only until the store answers with the player's own currency.</summary>
        public readonly string FallbackPrice;

        public GemPack(string productId, int gems, string fallbackPrice)
        {
            ProductId = productId; Gems = gems; FallbackPrice = fallbackPrice;
        }
    }

    /// <summary>What the store sells: gem packs (consumable) and the Dollars heroes of
    /// <see cref="AvatarCatalog"/> (non-consumable, restorable). Product ids are the same in App
    /// Store Connect and in functions/src/purchaseRules.js, which decides what a purchase pays.</summary>
    public static class StoreCatalog
    {
        public static readonly GemPack[] GemPacks =
        {
            new GemPack("gems_30", 30, "$1.99"),
            new GemPack("gems_120", 120, "$5.99"),
            // 360 gems used to repeat the 120 pack's $5.99: the bigger pack now pays out more per dollar.
            new GemPack("gems_360", 360, "$14.99"),
        };

        /// <summary>The gem pack behind a product id, or false when it is a hero or unknown.</summary>
        public static bool TryGemPack(string productId, out GemPack pack)
        {
            pack = Array.Find(GemPacks, p => p.ProductId == productId);
            return pack.ProductId != null;
        }

        /// <summary>The Dollars hero behind a product id, or null.</summary>
        public static AvatarOffer HeroFor(string productId) =>
            string.IsNullOrEmpty(productId) ? null : Array.Find(AvatarCatalog.All, o => o != null && o.Kind == AvatarPurchaseKind.Dollars && o.StoreProductId == productId);
    }
}
