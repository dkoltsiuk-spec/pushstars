using System;
using System.Linq;
using System.Threading.Tasks;
using Firebase;
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
        // A guest that just linked Google or Apple may carry the address only on the provider entry.
        public string Email
        {
            get
            {
                var user = _auth?.CurrentUser;
                if (user == null) return null;
                return !string.IsNullOrEmpty(user.Email) ? user.Email
                    : user.ProviderData.Select(p => p.Email).FirstOrDefault(e => !string.IsNullOrEmpty(e));
            }
        }
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

        /// <summary>The sign-in method shown in Settings: Apple, then Google, then email.</summary>
        public string PrimaryProvider => new[] { "apple.com", "google.com", "password" }.FirstOrDefault(HasProvider);

        public Task SendPasswordResetAsync(string email) => _auth.SendPasswordResetEmailAsync(email.Trim());

        /// <summary>
        /// Links Apple or Google to the current guest so its UID stays. Returns null once linked.
        /// When the account already belongs to another profile, returns the switch to that profile
        /// instead of running it: switching UID never happens without the player's confirmation.
        /// </summary>
        public async Task<Func<Task>> LinkProviderAsync(string providerId)
        {
            var user = _auth?.CurrentUser ?? throw new InvalidOperationException("Connection unavailable. Please try again.");
#if UNITY_ANDROID && !UNITY_EDITOR
            if (providerId == "apple.com")
            {
                var provider = new FederatedOAuthProvider();
                provider.SetProviderData(new FederatedOAuthProviderData
                {
                    ProviderId = providerId,
                    Scopes = new System.Collections.Generic.List<string> { "email", "name" }
                });
                try { await user.LinkWithProviderAsync(provider); return null; }
                catch (Exception e) when (IsTaken(e))
                {
                    var updated = (e as FirebaseAccountLinkException)?.UserInfo?.UpdatedCredential;
                    if (updated != null && updated.IsValid()) return () => SignInAsync(updated);
                    return () => _auth.SignInWithProviderAsync(provider);
                }
            }
#endif
            var credential = await NativeSignIn.GetCredentialAsync(providerId);
            try
            {
                await user.LinkWithCredentialAsync(credential);
                credential.Dispose();
                return null;
            }
            catch (Exception e) when (IsTaken(e))
            {
                // Apple's nonce makes the first credential single-use; Firebase hands back a fresh one.
                var updated = (e as FirebaseAccountLinkException)?.UserInfo?.UpdatedCredential;
                if (updated == null || !updated.IsValid()) return () => SignInAsync(credential);
                credential.Dispose();
                return () => SignInAsync(updated);
            }
            catch
            {
                credential.Dispose();
                throw;
            }
        }

        /// <summary>Email counterpart of <see cref="LinkProviderAsync"/>: a new email becomes this
        /// guest's account; a registered one returns the switch, which checks the password.</summary>
        public async Task<Func<Task>> LinkEmailAsync(string email, string password)
        {
            var user = _auth?.CurrentUser ?? throw new InvalidOperationException("Connection unavailable. Please try again.");
            email = email.Trim();
            using (var credential = EmailAuthProvider.GetCredential(email, password))
            {
                try { await user.LinkWithCredentialAsync(credential); return null; }
                catch (Exception e) when (IsTaken(e))
                {
                    return () => _auth.SignInWithEmailAndPasswordAsync(email, password);
                }
            }
        }

        /// <summary>Leaves the account; Boot then starts a fresh guest session.</summary>
        public void SignOut() => _auth?.SignOut();

        private async Task SignInAsync(Credential credential)
        {
            using (credential) await _auth.SignInWithCredentialAsync(credential);
        }

        private static bool IsTaken(Exception e)
        {
            var code = AuthErrorOf(e);
            return code == AuthError.CredentialAlreadyInUse || code == AuthError.EmailAlreadyInUse;
        }

        /// <summary>Link collisions arrive as FirebaseAccountLinkException, which is not a FirebaseException.</summary>
        public static AuthError? AuthErrorOf(Exception e)
        {
            if (e is AggregateException aggregate) e = aggregate.GetBaseException();
            if (e is FirebaseAccountLinkException link) return (AuthError)link.ErrorCode;
            if (e is FirebaseException firebase) return (AuthError)firebase.ErrorCode;
            return null;
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
