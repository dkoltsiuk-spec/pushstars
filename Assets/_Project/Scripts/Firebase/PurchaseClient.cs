using System;
using System.Threading.Tasks;

namespace PushStars.Services
{
    /// <summary>The server's word on a store transaction (functions/src/purchases.js).</summary>
    [Serializable] public sealed class PurchaseGrant
    {
        public const string Ok = "ok";
        /// <summary>ok, claimed (another account took this gem pack) or revoked (refunded).</summary>
        public string status, hero, transactionId;
        public int gems;
    }

    public static class PurchaseClient
    {
        [Serializable] private sealed class VerifyRequest { public string jws; }

        /// <summary>Next to Firestore, like the queue.</summary>
        private const string Region = "europe-west1";

        /// <summary>Checks Apple's signed transaction and claims it for this account. Throws when
        /// the server cannot say: the purchase stays open at the store, which delivers it again.</summary>
        public static async Task<PurchaseGrant> Verify(string signedTransaction)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            return await LeagueClient.Call<VerifyRequest, PurchaseGrant>("verifyPurchase", new VerifyRequest { jws = signedTransaction }, LeagueClient.Uid, Region);
        }
    }
}
