using System;
using UnityEngine;

namespace PushStars.Core
{
    public enum AvatarPurchaseKind { Included, Aura, Gems, Dollars }

    [Serializable]
    public sealed class AvatarOffer
    {
        public string Id, Name, HeadIcon;
        public AvatarPurchaseKind Kind;
        [Min(0)] public int Price;
        [Min(0)] public int RequiredCards;
        public string StoreProductId;
        public bool UsesCards => Kind == AvatarPurchaseKind.Aura && RequiredCards > 0;
        public int PriceAfterCards(int collected) => UsesCards
            ? (int)(((long)Price * (RequiredCards - Mathf.Clamp(collected, 0, RequiredCards)) + RequiredCards - 1) / RequiredCards)
            : Price;
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
        public static AvatarOffer[] Defaults() => new[] {
            new AvatarOffer { Id = "sonic", Name = "SONIC", Kind = AvatarPurchaseKind.Included },
            new AvatarOffer { Id = "madam-engry", Name = "MADAM ENGRY", Kind = AvatarPurchaseKind.Included },
            new AvatarOffer { Id = "fighter", Name = "FIGHTER", Kind = AvatarPurchaseKind.Included },
            new AvatarOffer { Id = "robot", Name = "ROBOT", Kind = AvatarPurchaseKind.Aura, Price = 300, RequiredCards = 60, HeadIcon = "AvatarCollection/RobotHead" },
            new AvatarOffer { Id = "gladiator", Name = "GLADIATOR", Kind = AvatarPurchaseKind.Aura, Price = 600, RequiredCards = 120, HeadIcon = "AvatarCollection/GladiatorHead" }
        };
    }
}
