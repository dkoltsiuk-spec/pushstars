using System;
using System.Collections;
using PushStars.Core;
using Unity.Notifications;
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
    /// </summary>
    public sealed class LocalReminders : MonoBehaviour
    {
        private const string ChannelId = "daily";
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
                _initialized = true;
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

        private void OnDestroy() => SceneManager.sceneLoaded -= SceneLoaded;

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
            bool granted = true;
#if !UNITY_EDITOR
            if (_initialized)
            {
                var request = NotificationCenter.RequestPermission();
                yield return request;
                granted = request.Status == NotificationsPermissionStatus.Granted;
            }
#endif
            PlayerPrefs.SetInt(KeyPermissionAsked, 1);
            PlayerPrefs.Save();
            Analytics.NotificationPermission(granted);
            _asking = false;
            Reschedule();
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
