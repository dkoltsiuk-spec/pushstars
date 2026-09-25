using System;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Auth;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>
    /// Restores sessions and upgrades guests without changing their cloud profile UID.
    /// </summary>
    public class FirebaseAuthService : IService
    {
        private FirebaseAuth _auth;
        private static Task<FirebaseAuthService> _initialization;
        public const string RememberBackendKey = "auth.backendEnabled";

        public string Uid => _auth?.CurrentUser?.UserId;
        public bool IsSignedIn => !string.IsNullOrEmpty(Uid);
        public bool IsAnonymous => _auth?.CurrentUser?.IsAnonymous ?? true;
        public string Email => _auth?.CurrentUser?.Email;
        public bool HasProvider(string id) => _auth?.CurrentUser?.ProviderData.Any(p => p.ProviderId == id) == true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInitialization() => _initialization = null;

        // Shared by Boot and Settings; concurrent callers never replace the live session.
        public static Task<FirebaseAuthService> EnsureReadyAsync()
        {
            if (ServiceLocator.TryGet<FirebaseAuthService>(out var auth) && auth.IsSignedIn)
                return Task.FromResult(auth);
            if (_initialization == null || _initialization.IsCompleted)
                _initialization = InitializeAsync();
            return _initialization;
        }

        private static async Task<FirebaseAuthService> InitializeAsync()
        {
            if (!ServiceLocator.TryGet<FirebaseService>(out var firebase) || !firebase.IsReady)
            {
                firebase = new FirebaseService();
                await firebase.InitializeAsync();
                if (!firebase.IsReady) throw new InvalidOperationException("Connection unavailable. Please try again.");
                ServiceLocator.Register(firebase);
            }
            if (!ServiceLocator.TryGet<FirebaseAuthService>(out var auth))
            {
                auth = new FirebaseAuthService();
                ServiceLocator.Register(auth);
            }
            await auth.SignInAnonymouslyAsync();
            if (!ServiceLocator.TryGet<FirestoreService>(out _))
            {
                var firestore = new FirestoreService();
                firestore.Configure();
                ServiceLocator.Register(firestore);
            }
            PlayerPrefs.SetInt(RememberBackendKey, 1);
            PlayerPrefs.Save();
            return auth;
        }

        public async Task SignInAnonymouslyAsync()
        {
            _auth = FirebaseAuth.DefaultInstance;

            // Reuse the persisted session if Firebase already restored one.
            if (_auth.CurrentUser != null)
            {
                Debug.Log($"[Auth] Restored existing session. uid={Uid}");
                return;
            }

            var result = await _auth.SignInAnonymouslyAsync();
            Debug.Log($"[Auth] Signed in anonymously. uid={Uid}");
        }

        public async Task RegisterEmailAsync(string email, string password)
        {
            using (var credential = EmailAuthProvider.GetCredential(email.Trim(), password))
                await ConnectCredentialAsync(credential, true);
        }

        public async Task SignInEmailAsync(string email, string password)
        {
            await _auth.SignInWithEmailAndPasswordAsync(email.Trim(), password);
        }

        public Task SendPasswordResetAsync(string email) => _auth.SendPasswordResetEmailAsync(email.Trim());

        public async Task ConnectProviderAsync(string providerId, bool saveProgress)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (providerId == "apple.com")
            {
                var provider = new FederatedOAuthProvider();
                provider.SetProviderData(new FederatedOAuthProviderData
                {
                    ProviderId = providerId,
                    Scopes = new System.Collections.Generic.List<string> { "email", "name" }
                });
                if (saveProgress && HasProvider(providerId))
                    await _auth.CurrentUser.ReauthenticateWithProviderAsync(provider);
                else if (saveProgress)
                    await _auth.CurrentUser.LinkWithProviderAsync(provider);
                else
                    await _auth.SignInWithProviderAsync(provider);
                return;
            }
#endif
            using (var credential = await NativeSignIn.GetCredentialAsync(providerId))
            {
                if (saveProgress && HasProvider(providerId))
                    await _auth.CurrentUser.ReauthenticateAsync(credential);
                else
                    await ConnectCredentialAsync(credential, saveProgress);
            }
        }

        private async Task ConnectCredentialAsync(Credential credential, bool saveProgress)
        {
            // Never fall back to sign-in on a link collision: that would silently switch UID.
            if (saveProgress)
                await _auth.CurrentUser.LinkWithCredentialAsync(credential);
            else
                await _auth.SignInWithCredentialAsync(credential);
        }

        /// <summary>
        /// Deletes the currently signed-in account (GDPR / store requirement, phase 07). Server-side
        /// cleanup of <c>users/{uid}</c> and related docs is handled by the <c>onUserDeleted</c> Cloud
        /// Function (phase 05) — the client only triggers the auth deletion. After this returns the
        /// app should restart from Boot, which signs in a fresh anonymous account.
        /// </summary>
        public async Task DeleteAccountAsync()
        {
            var user = _auth?.CurrentUser ?? FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null)
            {
                Debug.LogWarning("[Auth] DeleteAccountAsync called with no signed-in user.");
                return;
            }

            await user.DeleteAsync();
            Debug.Log($"[Auth] Account deleted. uid={Uid}");
        }
    }
}
