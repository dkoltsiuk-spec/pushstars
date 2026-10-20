using UnityEngine;

namespace PushStars.Core
{
    /// <summary>
    /// <see cref="ISettingsStore"/> backed by Unity <see cref="PlayerPrefs"/>. Every setter writes
    /// through immediately (and calls <see cref="PlayerPrefs.Save"/>) so toggles persist even if the
    /// app is killed before a graceful quit. Defaults: sound/vibration/notifications on, language
    /// follows the device (RU / PT-BR / otherwise EN) until the player picks one, which then persists.
    /// </summary>
    public sealed class PlayerPrefsSettingsStore : ISettingsStore
    {
        private const string KeySound         = "settings.sound";
        private const string KeyVibration     = "settings.vibration";
        private const string KeyNotifications = "settings.notifications";
        private const string KeyLanguage      = "settings.language";

        public const string LangRu = "ru";
        public const string LangEn = "en";
        public const string LangPtBr = "pt-BR";

        public bool SoundEnabled
        {
            get => GetBool(KeySound, true);
            set => SetBool(KeySound, value);
        }

        public bool VibrationEnabled
        {
            get => GetBool(KeyVibration, true);
            set => SetBool(KeyVibration, value);
        }

        public bool NotificationsEnabled
        {
            get => GetBool(KeyNotifications, true);
            set => SetBool(KeyNotifications, value);
        }

        public string Language
        {
            get => PlayerPrefs.HasKey(KeyLanguage)
                ? Localization.NormalizeLanguage(PlayerPrefs.GetString(KeyLanguage))
                : DeviceLanguage();
            set
            {
                string language = Localization.NormalizeLanguage(value);
                PlayerPrefs.SetString(KeyLanguage, language);
                PlayerPrefs.Save();
                Localization.NotifyLanguageChanged();
            }
        }

        /// <summary>Phone UI language mapped onto the supported set; not stored, so it keeps following
        /// the device until the player picks a language in settings.</summary>
        public static string DeviceLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Russian: return LangRu;
                case SystemLanguage.Portuguese: return LangPtBr;
                default: return LangEn;
            }
        }

        private static bool GetBool(string key, bool fallback) =>
            PlayerPrefs.GetInt(key, fallback ? 1 : 0) != 0;

        private static void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
