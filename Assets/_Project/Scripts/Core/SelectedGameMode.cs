using UnityEngine;

namespace PushStars.Core
{
    public enum GameMode { Pvp = 0, Boss = 1, Training = 2 }

    /// <summary>The home selection survives a fight and a cold launch.</summary>
    public static class SelectedGameMode
    {
        private const string Key = "selected_game_mode";
        public static GameMode Current
        {
            get => (GameMode)Mathf.Clamp(PlayerPrefs.GetInt(Key, 0), 0, 2);
            set { PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); }
        }
    }
}
