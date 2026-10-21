using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Messaging;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>
    /// This phone's Firebase Cloud Messaging token on the server (registerPushToken,
    /// functions/src/push.js), for "notify me when an opponent is found". Firebase Messaging is
    /// touched only after the player switched notifications on (<see cref="Enable"/>) and the OS
    /// permission is granted, so it never shows a prompt of its own at launch. Once enabled it
    /// starts with the app: the token can change, and a push that opened the app is reported
    /// through <see cref="Received"/>. The Settings notification switch mutes it (<see cref="SetMuted"/>).
    /// </summary>
    public static class PushTokens
    {
        private const string KeyEnabled = "push.enabled";
        [Serializable] private sealed class TokenRequest { public string token, platform, lang; }
        [Serializable] private sealed class Reply { public string status; }

        /// <summary>A push arrived: its kind ("queue") and data; opened = the player tapped it.</summary>
        public static event Action<string, IDictionary<string, string>, bool> Received;

        public static bool Enabled => PlayerPrefs.GetInt(KeyEnabled, 0) == 1;
        private static bool _hooked, _muted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime() { Received = null; _hooked = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            _muted = !new PlayerPrefsSettingsStore().NotificationsEnabled;
            if (Enabled && !_muted) _ = Enable();
        }

        /// <summary>Call once the OS permission is granted: registers this phone for pushes.</summary>
        public static async Task Enable()
        {
            PlayerPrefs.SetInt(KeyEnabled, 1);
            PlayerPrefs.Save();
            _muted = false;
#if UNITY_EDITOR
            await Task.CompletedTask; // no FCM in the Editor; the server simply has no token for it
#else
            try
            {
                await FirebaseAuthService.EnsureReadyAsync();
                Hook();
                // The OS already said yes, so no dialog: on iOS this registers with APNs.
                await FirebaseMessaging.RequestPermissionAsync();
                await Register(await FirebaseMessaging.GetTokenAsync());
            }
            catch (Exception e) { Debug.LogWarning("[Push] enable: " + e.GetBaseException().Message); }
#endif
        }

        /// <summary>The Settings switch: off forgets this phone on the server (no pushes at all);
        /// on registers it again if the player had enabled pushes.</summary>
        public static async void SetMuted(bool muted)
        {
            _muted = muted;
            if (!Enabled) return;
            if (!muted) { await Enable(); return; }
            try { await Register(""); }
            catch (Exception e) { Debug.LogWarning("[Push] mute: " + e.GetBaseException().Message); }
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            var main = SynchronizationContext.Current;
            FirebaseMessaging.TokenReceived += (_, e) => Forget(Register(e.Token));
            FirebaseMessaging.MessageReceived += (_, e) =>
            {
                var data = e.Message?.Data ?? new Dictionary<string, string>();
                string kind = data.TryGetValue("kind", out var k) ? k : "unknown";
                bool opened = e.Message != null && e.Message.NotificationOpened;
                void Deliver()
                {
                    if (opened) Analytics.PushOpened(kind);
                    Received?.Invoke(kind, data, opened);
                }
                // Hop to the main thread in case a platform calls back from its own.
                if (main != null && SynchronizationContext.Current != main) main.Post(_ => Deliver(), null);
                else Deliver();
            };
        }

        private static async Task Register(string token)
        {
            if (_muted && !string.IsNullOrEmpty(token)) return;
            string uid = LeagueClient.Uid;
            if (string.IsNullOrEmpty(uid)) return;
            string platform = Application.platform == RuntimePlatform.IPhonePlayer ? "ios"
                : Application.platform == RuntimePlatform.Android ? "android" : "other";
            await LeagueClient.Call<TokenRequest, Reply>("registerPushToken",
                new TokenRequest { token = token ?? "", platform = platform, lang = Localization.Language }, uid);
        }

        private static async void Forget(Task task)
        {
            try { await task; }
            catch (Exception e) { Debug.LogWarning("[Push] token: " + e.GetBaseException().Message); }
        }
    }
}
