namespace PushStars.Core
{
    /// <summary>
    /// Real-money gem packs in the shop. Off leaves them out — a price the player cannot pay is a
    /// dead button. On since 2026-10-19: in-app purchases are being wired in, and the packs, like
    /// the <see cref="AvatarPurchaseKind.Dollars"/> heroes, must be sellable before a store release.
    /// </summary>
    public static class StoreAccess
    {
        public static bool RealMoney => true;
    }
}
