using System.Threading.Tasks;
using Firebase.Auth;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>
    /// Anonymous authentication. Phase 04 only needs a stable <see cref="Uid"/> so the rest
    /// of the backend (Firestore docs keyed by uid, matchmaking, leaderboard) has an identity
    /// to write under. Later phases can upgrade the anonymous account to Apple / Google.
    /// </summary>
    public class FirebaseAuthService : IService
    {
        private FirebaseAuth _auth;

        public string Uid { get; private set; }
        public bool IsSignedIn => !string.IsNullOrEmpty(Uid);

        public async Task SignInAnonymouslyAsync()
        {
            _auth = FirebaseAuth.DefaultInstance;

            // Reuse the persisted session if Firebase already restored one.
            if (_auth.CurrentUser != null)
            {
                Uid = _auth.CurrentUser.UserId;
                Debug.Log($"[Auth] Restored existing session. uid={Uid}");
                return;
            }

            var result = await _auth.SignInAnonymouslyAsync();
            Uid = result.User.UserId;
            Debug.Log($"[Auth] Signed in anonymously. uid={Uid}");
        }
    }
}
