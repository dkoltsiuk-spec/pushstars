using System;
using System.Collections;
using System.Threading.Tasks;
using PushStars.Core;
using Unity.Notifications;
#if UNITY_ANDROID
using Unity.Notifications.Android;
#elif UNITY_IOS
using iOSNotificationCenter = Unity.Notifications.iOS.iOSNotificationCenter;
using AuthorizationStatus = Unity.Notifications.iOS.AuthorizationStatus;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Reminders
{
    /// <summary>
    /// Keeps the daily-goal and streak reminders scheduled on the device, no server involved.
    /// The whole set is replaced on start, on returning Home and when the app goes to the
    /// background, so it always reflects today's progress. The OS permission is asked once, on
    /// Home after the first finished daily goal — never on first launch — and the Settings
    /// notification switch turns everything off. Boots itself; nothing references it.
    ///
    /// <para>Also the app's door to the OS permission (<see cref="NotificationAccess"/>): the search
    /// screen's "notify me" asks through it, and the Android "Duels" channel the server's pushes
    /// arrive on is registered here.</para>
    /// </summary>
    public sealed class LocalReminders : MonoBehaviour
    {
        private const string ChannelId = "daily";
        /// <summary>Server pushes ("an opponent is waiting", functions/src/push.js): heads-up on Android.</summary>
        private const string DuelsChannelId = "duels";
        private const string KeyPermissionAsked = "reminders.permission_asked";
        private static LocalReminders _instance;
        private bool _initialized, _asking;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => _instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_instance != null) return;
            var root = new GameObject("LocalReminders");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<LocalReminders>();
        }

        private IEnumerator Start()
        {
            SceneManager.sceneLoaded += SceneLoaded;
            NotificationAccess.Granted += PermissionGranted;
#if !UNITY_EDITOR
            try
            {
                var args = NotificationCenterArgs.Default;
#if UNITY_IOS
                // iOS shows nothing on screen without Alert. No badge: the app is not an inbox.
                args.PresentationOptions = NotificationPresentation.Alert | NotificationPresentation.Sound;
#else
                // Android: Sound alone = default importance, in the tray without a heads-up popup.
                args.PresentationOptions = NotificationPresentation.Sound;
#endif
                args.AndroidChannelId = ChannelId;
                args.AndroidChannelName = Localization.Text("Daily reminders");
                args.AndroidChannelDescription = Localization.Text("Your daily goal and streak.");
                // White flame silhouette, registered in Project Settings > Mobile Notifications.
                args.AndroidSmallIcon = "icon_small";
                NotificationCenter.Initialize(args);
                NotificationCenter.CancelAllDeliveredNotifications();
#if UNITY_ANDROID
                AndroidNotificationCenter.RegisterNotificationChannel(new AndroidNotificationChannel(DuelsChannelId,
                    Localization.Text("Duels"), Localization.Text("When an opponent is waiting for you."), Importance.High));
#endif
                _initialized = true;
                NotificationAccess.Current = CurrentPermission;
                NotificationAccess.Request = RequestPermission;
                NotificationAccess.OpenSettings = () => NotificationCenter.OpenNotificationSettings();
            }
            catch (Exception exception) { Debug.LogException(exception, this); }
            if (_initialized)
            {
                var opened = NotificationCenter.QueryLastRespondedNotification();
                yield return opened;
                if (opened.State == QueryLastRespondedNotificationState.HaveRespondedNotification)
                    Analytics.PushOpened(opened.Notification.Data ?? "unknown");
            }
#endif
            yield return null;
            SceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            NotificationAccess.Granted -= PermissionGranted;
        }

        /// <summary>Allowed from elsewhere (the search screen's "notify me"): reminders may start too.</summary>
        private void PermissionGranted()
        {
            if (PlayerPrefs.GetInt(KeyPermissionAsked, 0) != 0) return;
            PlayerPrefs.SetInt(KeyPermissionAsked, 1);
            PlayerPrefs.Save();
            Reschedule();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Reschedule();
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != FightConfig.MainSceneName) return;
            MaybeAskPermission();
            Reschedule();
        }

        private void MaybeAskPermission()
        {
            if (_asking || PlayerPrefs.GetInt(KeyPermissionAsked, 0) != 0) return;
            if (!new PlayerPrefsSettingsStore().NotificationsEnabled) return;
            try { if (!LocalProfile.TodayGoal.Done) return; }
            catch (Exception exception) { Debug.LogException(exception, this); return; }
            StartCoroutine(AskPermission());
        }

        private IEnumerator AskPermission()
        {
            _asking = true;
            var asked = NotificationAccess.Ask();
            while (!asked.IsCompleted) yield return null;
            bool granted = asked.Status == TaskStatus.RanToCompletion && asked.Result;
            PlayerPrefs.SetInt(KeyPermissionAsked, 1);
            PlayerPrefs.Save();
            Analytics.NotificationPermission(granted);
            _asking = false;
            Reschedule();
            yield break;
        }

        private static NotificationAccess.State CurrentPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            switch (AndroidNotificationCenter.UserPermissionToPost)
            {
                case PermissionStatus.Allowed: return NotificationAccess.State.Granted;
                case PermissionStatus.DeniedDontAskAgain:
                case PermissionStatus.NotificationsBlockedForApp: return NotificationAccess.State.Denied;
                default: return NotificationAccess.State.Unknown;
            }
#elif UNITY_IOS && !UNITY_EDITOR
            switch (iOSNotificationCenter.GetNotificationSettings().AuthorizationStatus)
            {
                case AuthorizationStatus.Authorized:
                case AuthorizationStatus.Provisional:
                case AuthorizationStatus.Ephemeral: return NotificationAccess.State.Granted;
                case AuthorizationStatus.Denied: return NotificationAccess.State.Denied;
                default: return NotificationAccess.State.Unknown;
            }
#else
            return NotificationAccess.State.Granted;
#endif
        }

        private Task<bool> RequestPermission()
        {
            var done = new TaskCompletionSource<bool>();
            StartCoroutine(Request(done));
            return done.Task;
        }

        private IEnumerator Request(TaskCompletionSource<bool> done)
        {
#if !UNITY_EDITOR
            if (_initialized)
            {
                var request = NotificationCenter.RequestPermission();
                yield return request;
                done.TrySetResult(request.Status == NotificationsPermissionStatus.Granted);
                yield break;
            }
#endif
            done.TrySetResult(true);
            yield break;
        }

        /// <summary>Replaces every scheduled reminder with today's plan.</summary>
        public void Reschedule()
        {
            try
            {
#if !UNITY_EDITOR
                if (!_initialized) return;
                NotificationCenter.CancelAllScheduledNotifications();
#endif
                if (!new PlayerPrefsSettingsStore().NotificationsEnabled || PlayerPrefs.GetInt(KeyPermissionAsked, 0) == 0) return;
                var plan = ReminderPlanner.Plan(LocalProfile.History, LocalProfile.TodayGoal, LocalProfile.DailyStreak, DateTime.Now);
                foreach (var reminder in plan)
                {
#if UNITY_EDITOR
                    Debug.Log($"[LocalReminders] {reminder.LocalTime:ddd HH:mm} {reminder.Kind}: " +
                              $"{Localization.Text(reminder.Title)} — {Localization.Text(reminder.Body)}");
#else
                    NotificationCenter.ScheduleNotification(new Notification
                    {
                        Title = Localization.Text(reminder.Title),
                        Text = Localization.Text(reminder.Body),
                        Data = reminder.Kind.ToString(),
                    }, new NotificationDateTimeSchedule(reminder.LocalTime));
#endif
                }
            }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }
}
