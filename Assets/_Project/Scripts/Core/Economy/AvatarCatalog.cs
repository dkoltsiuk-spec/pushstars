using System;
using UnityEngine;

namespace PushStars.Core
{
    public enum AvatarPurchaseKind { Included, Aura, Gems, Dollars }

    /// <summary>
    /// A collection hero. <see cref="AvatarPurchaseKind.Aura"/> heroes are never bought: they unlock
    /// when the player's unlock progress reaches <see cref="Price"/> (the Aura goal). Progress is the
    /// peak Aura ever reached plus every collected card at <see cref="CardAura"/> each, so the one bar
    /// only ever fills. Gems and Dollars heroes use <see cref="Price"/> as a price.
    /// </summary>
    [Serializable]
    public sealed class AvatarOffer
    {
        public string Id, Name, HeadIcon;
        public AvatarPurchaseKind Kind;
        [Tooltip("Aura heroes: peak Aura goal. Gems: price. Dollars: price in cents.")]
        [Min(0)] public int Price;
        [Tooltip("Aura heroes: cards that alone fill the bar; each card is worth Price / RequiredCards Aura.")]
        [Min(0)] public int RequiredCards;
        public string StoreProductId;
        public bool UnlocksByAura => Kind == AvatarPurchaseKind.Aura;
        public bool UsesCards => UnlocksByAura && RequiredCards > 0;
        public long AuraGoal => UnlocksByAura ? Price : 0;
        public long CardAura => UsesCards ? Price / RequiredCards : 0;
        /// <summary>Filled part of the unlock bar, 0..<see cref="AuraGoal"/>.</summary>
        public long UnlockProgress(long peakAura, int cards) => UnlocksByAura
            ? Math.Min(AuraGoal, Math.Max(0, peakAura) + (long)Mathf.Clamp(cards, 0, RequiredCards) * CardAura)
            : 0;
        public bool Unlocked(long peakAura, int cards) => UnlocksByAura && UnlockProgress(peakAura, cards) >= AuraGoal;
    }

    [CreateAssetMenu(menuName = "Push Stars/Rewards/Avatar Catalog")]
    public sealed class AvatarCatalog : ScriptableObject
    {
        // Stable IDs are saved; order matches the authored collection slots.
        public AvatarOffer[] Offers = Defaults();
        private static AvatarOffer[] _offers;
        public static AvatarOffer[] All => _offers ?? (_offers = Resources.Load<AvatarCatalog>("AvatarCatalog")?.Offers ?? Defaults());
        public static AvatarOffer Find(string id) => Array.Find(All, a => a.Id == id);
        public static AvatarOffer At(int index) => index >= 0 && index < All.Length ? All[index] : null;

        // Slot 0 held a third-party character that cannot ship. The slot and its saved ID stay so
        // every card index keeps its meaning, but it is never shown, sold or selectable.
        private const string RetiredId = "sonic";
        public static bool IsListed(int index) => At(index) is { } offer && offer.Id != RetiredId;

        public static AvatarOffer[] Defaults() => new[] {
            new AvatarOffer { Id = RetiredId, Name = "RETIRED", Kind = AvatarPurchaseKind.Dollars, Price = 599 },
            new AvatarOffer { Id = "madam-engry", Name = "MADAM ENGRY", Kind = AvatarPurchaseKind.Included },
            new AvatarOffer { Id = "fighter", Name = "FIGHTER", Kind = AvatarPurchaseKind.Included },
            new AvatarOffer { Id = "robot", Name = "ROBOT", Kind = AvatarPurchaseKind.Aura, Price = 50000, RequiredCards = 50, HeadIcon = "AvatarCollection/RobotHead" },
            new AvatarOffer { Id = "gladiator", Name = "GLADIATOR", Kind = AvatarPurchaseKind.Aura, Price = 250000, RequiredCards = 250, HeadIcon = "AvatarCollection/GladiatorHead" }
        };
    }
}
