namespace PushStars.Core
{
    /// <summary>
    /// Real-money goods in the shop: gem packs and the <see cref="AvatarPurchaseKind.Dollars"/>
    /// heroes. Off leaves them out — a price the player cannot pay is a dead button. On where the
    /// server can verify the store's transactions (functions/src/purchases.js): the App Store today.
    /// Google Play comes off this list once it has products and verification of its own.
    /// </summary>
    public static class StoreAccess
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        public static bool RealMoney => false;
#else
        public static bool RealMoney => true;
#endif
    }
}
