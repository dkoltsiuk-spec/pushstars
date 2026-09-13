using System;
using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>Local design fixture. No backend reads or player-state writes.</summary>
    public sealed class LeagueView : MonoBehaviour
    {
        [Serializable] public sealed class Entry { public string Name; public int Trophies; }
        public string LeagueName = "SILVER LEAGUE";
        public int DisplayedTrophies = 955;
        public int OnlinePlayers = 126;
        public float SeasonHours = 108;
        [Range(0, 1)] public float Progress = .77f;
        public Entry[] Players = {
            new Entry { Name = "NOX_92", Trophies = 799 },
            new Entry { Name = "ALEX_M", Trophies = 721 },
            new Entry { Name = "NOX_92", Trophies = 611 },
            new Entry { Name = "NOX_92", Trophies = 554 },
            new Entry { Name = "IRON_MAX", Trophies = 512 },
            new Entry { Name = "LUNA_FIT", Trophies = 487 },
            new Entry { Name = "PUSH_KING", Trophies = 453 },
            new Entry { Name = "BEAST_07", Trophies = 421 },
            new Entry { Name = "KIRA", Trophies = 398 },
            new Entry { Name = "TITAN_X", Trophies = 365 }
        };
        public TextMeshProUGUI Title, Score, Season, Online;
        public TextMeshProUGUI[] Names, Scores;
        public LeagueProgressGraphic ProgressBar;
        private double _seasonEnds;
        private float _nextRefresh;
        private void Awake() => _seasonEnds = Time.realtimeSinceStartupAsDouble + SeasonHours * 3600d;
        private void OnEnable()
        {
            Refresh();
            if (Application.isPlaying) GetComponent<LeagueEntrance>()?.Play();
        }
        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1;
            RefreshSeason();
        }
        public void Refresh()
        {
            if (Title != null) Title.text = LeagueName;
            if (Score != null) Score.text = DisplayedTrophies.ToString();
            if (Online != null) Online.text = OnlinePlayers + " online";
            if (ProgressBar != null) { ProgressBar.Fill = Progress; ProgressBar.Refresh(); }
            for (int i = 0; Players != null && i < Players.Length; i++)
            {
                if (Players[i] == null) continue;
                if (Names != null && i < Names.Length && Names[i] != null) Names[i].text = Players[i].Name;
                if (Scores != null && i < Scores.Length && Scores[i] != null) Scores[i].text = Players[i].Trophies.ToString();
            }
            RefreshSeason();
        }
        private void RefreshSeason()
        {
            if (Season == null) return;
            double seconds = Application.isPlaying ? Math.Max(0, _seasonEnds - Time.realtimeSinceStartupAsDouble) : SeasonHours * 3600d;
            var remaining = TimeSpan.FromSeconds(Math.Ceiling(seconds));
            string time = remaining.TotalDays >= 1 ? $"{remaining.Days}d {remaining.Hours}h" : $"{(int)remaining.TotalHours}h {remaining.Minutes}m";
            Season.text = "Season ends in <color=#FFCA00>" + time + "</color>";
        }
    }
}
