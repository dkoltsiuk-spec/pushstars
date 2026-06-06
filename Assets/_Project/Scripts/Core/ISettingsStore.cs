namespace PushStars.Core
{
    /// <summary>
    /// Persistent user preferences surfaced on the Settings screen (phase 07). Values survive
    /// between sessions; the concrete store is <see cref="PlayerPrefsSettingsStore"/>. Kept as an
    /// interface so a later phase can back it with a cloud-synced profile without touching the UI.
    /// </summary>
    public interface ISettingsStore
    {
        bool SoundEnabled { get; set; }
        bool VibrationEnabled { get; set; }

        /// <summary>In-app notification flag. The OS permission itself is requested separately.</summary>
        bool NotificationsEnabled { get; set; }

        /// <summary>UI language code, "ru" or "en". Full localization arrives in phase 17.</summary>
        string Language { get; set; }
    }
}
