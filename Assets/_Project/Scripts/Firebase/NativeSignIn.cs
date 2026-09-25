using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Firebase.Auth;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>Native provider UI returns tokens only to Firebase, never to logs or prefs.</summary>
    public sealed class NativeSignIn : MonoBehaviour
    {
        private static NativeSignIn _instance;
        private TaskCompletionSource<Credential> _pending;
        private string _provider, _nonce, _requestId;
        private float _deadline;

        [Serializable] private class Reply
        {
            public string requestId, idToken, accessToken, authorizationCode, error;
            public bool cancelled;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void PSAuthSignIn(string receiver, string requestId, string provider, string nonceHash);
#endif

        public static Task<Credential> GetCredentialAsync(string provider)
        {
#if UNITY_EDITOR || (!UNITY_IOS && !UNITY_ANDROID)
            throw new PlatformNotSupportedException("Apple and Google sign-in are available in the mobile app. Use Email here.");
#else
            if (_instance == null)
            {
                _instance = new GameObject("PushStarsNativeSignIn").AddComponent<NativeSignIn>();
                DontDestroyOnLoad(_instance.gameObject);
            }
            return _instance.Begin(provider);
#endif
        }

        private Task<Credential> Begin(string provider)
        {
            if (_pending != null) throw new InvalidOperationException("Sign-in is already in progress.");
            if (provider != "apple.com" && provider != "google.com") throw new ArgumentException("Unknown provider.");
            _provider = provider;
            _requestId = Guid.NewGuid().ToString("N");
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            _nonce = Convert.ToBase64String(bytes);
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(_nonce))).Replace("-", "").ToLowerInvariant();
            _pending = new TaskCompletionSource<Credential>();
            var task = _pending.Task;
            _deadline = Time.realtimeSinceStartup + 180f;
            try
            {
#if UNITY_IOS && !UNITY_EDITOR
                PSAuthSignIn(gameObject.name, _requestId, provider, hash);
#elif UNITY_ANDROID && !UNITY_EDITOR
                using (var bridge = new AndroidJavaClass("com.pushstars.auth.GoogleSignInBridge"))
                    bridge.CallStatic("signIn", gameObject.name, _requestId);
#endif
            }
            catch (Exception)
            {
                CompleteError(new InvalidOperationException("Sign-in could not start. Please try again."));
            }
            return task;
        }

        // UnitySendMessage entry point. Request IDs reject late callbacks from expired attempts.
        [UnityEngine.Scripting.Preserve]
        public void OnNativeSignIn(string json)
        {
            if (_pending == null) return;
            Reply reply;
            try { reply = JsonUtility.FromJson<Reply>(json); }
            catch (Exception) { CompleteError(new InvalidOperationException("Invalid sign-in response.")); return; }
            if (reply == null || reply.requestId != _requestId) return;
            if (reply.cancelled) { CompleteError(new OperationCanceledException()); return; }
            if (!string.IsNullOrEmpty(reply.error)) { CompleteError(new InvalidOperationException(reply.error)); return; }
            if (string.IsNullOrEmpty(reply.idToken)) { CompleteError(new InvalidOperationException("No sign-in token was returned. Please try again.")); return; }
            try
            {
                var credential = _provider == "apple.com"
                    ? OAuthProvider.GetCredential("apple.com", reply.idToken, _nonce, reply.authorizationCode)
                    : GoogleAuthProvider.GetCredential(reply.idToken, reply.accessToken);
                var pending = _pending;
                _pending = null;
                _nonce = null;
                pending.TrySetResult(credential);
            }
            catch (Exception) { CompleteError(new InvalidOperationException("Invalid sign-in credentials.")); }
        }

        private void Update()
        {
            if (_pending != null && Time.realtimeSinceStartup >= _deadline)
                CompleteError(new TimeoutException("Sign-in timed out. Please try again."));
        }

        private void CompleteError(Exception error)
        {
            var pending = _pending;
            _pending = null;
            _nonce = null;
            pending?.TrySetException(error);
        }

        private void OnDestroy()
        {
            CompleteError(new OperationCanceledException());
            if (_instance == this) _instance = null;
        }
    }
}
