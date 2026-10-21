using System;
using System.Collections.Generic;
using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// Minimal retention analytics: about ten events sent to the "Pushup Stars" PostHog project by
    /// plain HTTP (<see cref="PostHogSender"/>). Answers two questions only — do players come back
    /// the next day / week, and where does the first run lose them.
    ///
    /// <para><b>What is sent.</b> A random install id (never an account, name, email or device id),
    /// the event name, the app version and platform, and game numbers (mode, reps, won). No camera
    /// data, no pose data, no free text. The project anonymizes IPs on arrival.</para>
    ///
    /// <para><b>Where the key lives.</b> <c>Resources/PostHogConfig.json</c>, gitignored like the
    /// Firebase configs (see docs/architecture/credentials-checklist.md). Without it nothing is
    /// sent and nothing breaks.</para>
    ///
    /// <para><b>Editor runs.</b> Many sessions play the game in one Editor, and each would look like
    /// one very loyal player. The Editor therefore sends only when the config says
    /// <c>sendFromEditor</c>, and every event carries <c>is_editor</c> so the dashboard can filter
    /// those out regardless.</para>
    /// </summary>
    public static class Analytics
    {
        private const string KeyEnabled = "settings.analytics";
        private const string KeyInstallId = "analytics.install_id";
        private const string KeyFirstFightSent = "analytics.first_fight_sent";
        private const string KeyIntroStartSent = "analytics.intro_start_sent";
        private const string KeyGoalDay = "analytics.goal_day";

        [Serializable]
        private sealed class Config
        {
            public string apiKey;
            public string host = "https://eu.i.posthog.com";
            public bool sendFromEditor;
        }

        private static Config _config;
        private static bool _configLoaded;
        private static string _installId;

        /// <summary>Player's opt-out, the "Anonymous statistics" switch in Settings. Off clears
        /// anything still queued. On by default.</summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(KeyEnabled, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(KeyEnabled, value ? 1 : 0);
                PlayerPrefs.Save();
                if (!value) PostHogSender.ClearQueue();
            }
        }

        /// <summary>Random per-install id, the only identity analytics knows.</summary>
        public static string InstallId
        {
            get
            {
                if (!string.IsNullOrEmpty(_installId)) return _installId;
                _installId = PlayerPrefs.GetString(KeyInstallId, "");
                if (string.IsNullOrEmpty(_installId))
                {
                    _installId = Guid.NewGuid().ToString();
                    PlayerPrefs.SetString(KeyInstallId, _installId);
                    PlayerPrefs.Save();
                }
                return _installId;
            }
        }

        internal static string ApiKey => LoadConfig()?.apiKey;
        internal static string Host => (LoadConfig()?.host ?? "https://eu.i.posthog.com").TrimEnd('/');

        /// <summary>True when events would actually leave the device.</summary>
        public static bool Active
        {
            get
            {
                var config = LoadConfig();
                if (config == null || string.IsNullOrEmpty(config.apiKey) || !Enabled) return false;
                return !Application.isEditor || config.sendFromEditor;
            }
        }

        // ── Events ───────────────────────────────────────────────────────────────────────────────

        /// <summary>Cold start, from AppBootstrap once the route is known.</summary>
        public static void AppOpen(string nextScene)
        {
            string route = nextScene == FightConfig.OnboardingSceneName ? "onboarding"
                : nextScene == FightConfig.FightSceneName ? "assessment" : "main";
            Track("app_open", new Dictionary<string, object> { ["cold"] = true, ["route"] = route });
            if (route == "onboarding" && PlayerPrefs.GetInt(KeyIntroStartSent, 0) == 0)
            {
                PlayerPrefs.SetInt(KeyIntroStartSent, 1);
                OnboardingStep("intro_started");
            }
        }

        /// <summary>Back from the background after long enough to count as another visit.</summary>
        internal static void AppResumed(float awaySeconds) =>
            Track("app_open", new Dictionary<string, object>
                { ["cold"] = false, ["away_min"] = Mathf.RoundToInt(awaySeconds / 60f) });

        /// <summary>intro_started, intro_done, assessment_skipped, first_fight.</summary>
        public static void OnboardingStep(string step) =>
            Track("onboarding_step", new Dictionary<string, object> { ["step"] = step });

        /// <summary>The 60-second level test produced a result.</summary>
        public static void AssessmentDone(int reps, bool first)
        {
            Track("assessment_done", new Dictionary<string, object>
                { ["reps"] = reps, ["tier"] = FitnessTest.TierFor(reps).ToString(), ["first"] = first });
        }

        /// <summary>Every finished set, from FightController's result path. Also notices the first
        /// duel ever and today's goal being reached, so neither needs a hook of its own.</summary>
        public static void FightFinished(FightMode mode, bool won, bool draw, int reps, int opponentReps, bool ranked)
        {
            string modeName = ModeName(mode);
            bool duel = mode == FightMode.Ghost || mode == FightMode.Boss;
            var props = new Dictionary<string, object>
            {
                ["mode"] = modeName, ["reps"] = reps, ["ranked"] = ranked,
            };
            if (duel)
            {
                props["won"] = won;
                props["draw"] = draw;
                props["opp_reps"] = opponentReps;
            }
            Track("fight_finished", props);

            if (duel && PlayerPrefs.GetInt(KeyFirstFightSent, 0) == 0)
            {
                PlayerPrefs.SetInt(KeyFirstFightSent, 1);
                PlayerPrefs.Save();
                Track("first_fight_done", new Dictionary<string, object>
                    { ["mode"] = modeName, ["won"] = won, ["reps"] = reps });
                OnboardingStep("first_fight");
            }

            CheckDailyGoal();
        }

        /// <summary>Placeholder until streak freezes land: call when a freeze covers a missed day.</summary>
        public static void StreakFreezeUsed(int streakDays, int freezesLeft) =>
            Track("streak_freeze_used", new Dictionary<string, object>
                { ["streak_days"] = streakDays, ["freezes_left"] = freezesLeft });

        /// <summary>Placeholder until reminders land: the OS notification prompt was answered.</summary>
        public static void NotificationPermission(bool granted) =>
            Track("notif_permission", new Dictionary<string, object> { ["granted"] = granted });

        /// <summary>Placeholder until reminders land: the app was opened from a reminder.</summary>
        public static void PushOpened(string kind) =>
            Track("push_opened", new Dictionary<string, object> { ["kind"] = kind });

        /// <summary>How a press of "Search opponent" ended: live, stranger (a recorded set at the
        /// player's level), shadow (own best), assessment or cancel — and after how long.</summary>
        public static void SearchEnded(string outcome, float seconds, bool notify) =>
            Track("search_ended", new Dictionary<string, object>
                { ["outcome"] = outcome, ["seconds"] = Mathf.RoundToInt(seconds), ["notify"] = notify });

        /// <summary>"Notify me when found" was switched on (or the OS said no).</summary>
        public static void SearchNotify(bool granted) =>
            Track("search_notify", new Dictionary<string, object> { ["granted"] = granted });

        /// <summary>Another player's search called this one: <paramref name="how"/> is "opened"
        /// (the app came up on the call) or "banner" (it was already open).</summary>
        public static void QueueCall(string how) =>
            Track("queue_call", new Dictionary<string, object> { ["how"] = how });

        private static void CheckDailyGoal()
        {
            try
            {
                var goal = LocalProfile.TodayGoal;
                if (!goal.Done || string.IsNullOrEmpty(goal.DayKey)) return;
                if (PlayerPrefs.GetString(KeyGoalDay, "") == goal.DayKey) return;
                PlayerPrefs.SetString(KeyGoalDay, goal.DayKey);
                PlayerPrefs.Save();
                Track("daily_goal_done", new Dictionary<string, object>
                    { ["target"] = goal.Target, ["streak_days"] = LocalProfile.DailyStreak });
            }
            catch (Exception e) { Debug.LogWarning($"[Analytics] Daily goal check failed: {e.Message}"); }
        }

        private static string ModeName(FightMode mode)
        {
            switch (mode)
            {
                case FightMode.LevelTest: return "assessment";
                case FightMode.Ghost: return "ghost";
                case FightMode.Boss: return "boss";
                case FightMode.Training: return "training";
                default: return mode.ToString().ToLowerInvariant();
            }
        }

        // ── Plumbing ─────────────────────────────────────────────────────────────────────────────

        /// <summary>Queues one event. Never throws: analytics must not cost the player a result.</summary>
        public static void Track(string eventName, Dictionary<string, object> properties = null)
        {
            try
            {
                if (!Active) return;
                var props = properties != null ? new Dictionary<string, object>(properties) : new Dictionary<string, object>();
                props["distinct_id"] = InstallId;
                props["$lib"] = "pushstars-unity";
                props["$app_version"] = Application.version;
                props["$os"] = OsName();
                props["platform"] = OsName();
                props["is_editor"] = Application.isEditor;
                props["dev_build"] = Debug.isDebugBuild;

                var json = new System.Text.StringBuilder(256);
                json.Append("{\"event\":");
                MiniJson.WriteString(json, eventName);
                json.Append(",\"uuid\":");
                MiniJson.WriteString(json, Guid.NewGuid().ToString());
                json.Append(",\"timestamp\":");
                MiniJson.WriteString(json, DateTime.UtcNow.ToString("o"));
                json.Append(",\"properties\":");
                MiniJson.WriteObject(json, props);
                json.Append('}');
                PostHogSender.Enqueue(json.ToString());
            }
            catch (Exception e) { Debug.LogWarning($"[Analytics] {eventName} not queued: {e.Message}"); }
        }

        private static string OsName()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "Android";
                case RuntimePlatform.IPhonePlayer: return "iOS";
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer: return "Windows";
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer: return "macOS";
                default: return Application.platform.ToString();
            }
        }

        private static Config LoadConfig()
        {
            if (_configLoaded) return _config;
            _configLoaded = true;
            var asset = Resources.Load<TextAsset>("PostHogConfig");
            if (asset == null) return null;
            try { _config = JsonUtility.FromJson<Config>(asset.text); }
            catch (Exception e) { Debug.LogWarning($"[Analytics] PostHogConfig.json unreadable: {e.Message}"); }
            return _config;
        }

        /// <summary>Just enough JSON for flat event properties.</summary>
        internal static class MiniJson
        {
            public static void WriteObject(System.Text.StringBuilder sb, Dictionary<string, object> values)
            {
                sb.Append('{');
                bool first = true;
                foreach (var pair in values)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, pair.Key);
                    sb.Append(':');
                    WriteValue(sb, pair.Value);
                }
                sb.Append('}');
            }

            private static void WriteValue(System.Text.StringBuilder sb, object value)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                switch (value)
                {
                    case null: sb.Append("null"); break;
                    case bool b: sb.Append(b ? "true" : "false"); break;
                    case int i: sb.Append(i.ToString(inv)); break;
                    case long l: sb.Append(l.ToString(inv)); break;
                    case float f: sb.Append(float.IsNaN(f) || float.IsInfinity(f) ? "null" : f.ToString("R", inv)); break;
                    case double d: sb.Append(double.IsNaN(d) || double.IsInfinity(d) ? "null" : d.ToString("R", inv)); break;
                    default: WriteString(sb, Convert.ToString(value, inv)); break;
                }
            }

            public static void WriteString(System.Text.StringBuilder sb, string s)
            {
                sb.Append('"');
                foreach (char c in s ?? "")
                {
                    switch (c)
                    {
                        case '"': sb.Append("\\\""); break;
                        case '\\': sb.Append("\\\\"); break;
                        case '\n': sb.Append("\\n"); break;
                        case '\r': sb.Append("\\r"); break;
                        case '\t': sb.Append("\\t"); break;
                        default:
                            if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                            else sb.Append(c);
                            break;
                    }
                }
                sb.Append('"');
            }
        }
    }
}
