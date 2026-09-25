using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Local league progress; no invented online ranking or season timer.</summary>
    public sealed class LeagueView : MonoBehaviour
    {
        public string LeagueName => LocalProfile.League.DisplayName.ToUpperInvariant() + " LEAGUE";
        public float Progress => Leagues.Progress(LocalProfile.Trophies);
        public TextMeshProUGUI Title, Score, Season, Online;
        public TextMeshProUGUI[] Names, Scores;
        public LeagueProgressGraphic ProgressBar;

        private void OnEnable()
        {
            Refresh();
            if (Application.isPlaying) GetComponent<LeagueEntrance>()?.Play();
        }

        public void Refresh()
        {
            if (Title != null) Title.text = LeagueName;
            if (Score != null) Score.text = LocalProfile.Trophies.ToString();
            if (Online != null) Online.text = "OFFLINE";
            if (Season != null) Season.text = "ONLINE RANKING COMING SOON";
            if (ProgressBar != null) { ProgressBar.Fill = Progress; ProgressBar.Refresh(); }
            if (Online != null)
                foreach (var name in new[] { "OnlineGlow", "OnlineDot" })
                    Online.transform.parent.Find(name)?.gameObject.SetActive(false);
            if (Names == null) return;
            for (int i = 0; i < Names.Length; i++)
            {
                if (Names[i] == null) continue;
                var row = Names[i].transform.parent;
                // Keep the player's authored card and suppress all unbacked ranking rows.
                bool own = row.name == "CurrentPlayerRow";
                row.gameObject.SetActive(own);
                if (own && Names.Length > 0 && Names[0] != null)
                    ((RectTransform)row).anchoredPosition = ((RectTransform)Names[0].transform.parent).anchoredPosition;
                Names[i].text = own ? ProfileIdentityEditor.ResolveName("PLAYER") : "";
                if (Scores != null && i < Scores.Length && Scores[i] != null)
                    Scores[i].text = own ? LocalProfile.Trophies.ToString() : "";
                var rank = row.Find("Rank")?.GetComponent<TextMeshProUGUI>();
                if (rank != null) rank.text = "—";
            }
            GetComponent<LeagueLayout>()?.Fit();
        }
    }
}
