using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Local league progress; no invented online ranking or season timer.</summary>
    public sealed partial class LeagueView : MonoBehaviour
    {
        private long DisplayTrophies => _cloud?.own?.trophies ?? _lastOwn?.trophies ?? LocalProfile.Trophies;
        public string ViewedLeagueId => !string.IsNullOrEmpty(_selectedLeague) ? _selectedLeague : _cloud?.league ?? LocalProfile.League.Id;
        public string LeagueName => Leagues.ById(ViewedLeagueId).DisplayName.ToUpperInvariant() + " LEAGUE";
        public float Progress
        {
            get
            {
                var league = Leagues.ById(ViewedLeagueId);
                return league.IsTop ? (DisplayTrophies >= league.MinTrophies ? 1 : 0)
                    : Mathf.Clamp01((float)(DisplayTrophies - league.MinTrophies) / (league.MaxTrophies + 1 - league.MinTrophies));
            }
        }

        public bool BrowseLeague(int direction)
        {
            int current = System.Array.FindIndex(Leagues.All, l => l.Id == ViewedLeagueId);
            int next = Mathf.Clamp(current + System.Math.Sign(direction), 0, Leagues.All.Length - 1);
            if (next == current) return false;
            _lastOwn = _cloud?.own ?? _lastOwn;
            _selectedLeague = Leagues.All[next].Id;
            _generation++; _loading = false; _cloud = null; _players.Clear(); _notice = "";
            ClearGenerated(); Refresh();
            var layout = GetComponent<LeagueLayout>();
            if (layout?.PageScroll != null) { layout.PageScroll.StopMovement(); layout.PageScroll.verticalNormalizedPosition = 1; }
            if (Application.isPlaying) _ = LoadPage(false, true);
            return true;
        }
        public TextMeshProUGUI Title, Score, Season, Online;
        public TextMeshProUGUI[] Names, Scores;
        public LeagueProgressGraphic ProgressBar;

        [Header("League artwork")]
        public Image Hero;
        public Sprite BronzeHero, SilverHero, GoldHero, DiamondHero;

        private void OnEnable()
        {
            Refresh();
            if (Application.isPlaying) GetComponent<LeagueEntrance>()?.Play();
            if (Application.isPlaying) StartOnline();
        }

        public void Refresh()
        {
            if (_cloud != null) { RenderOnline(); return; }
            var hero = HeroForLeague(ViewedLeagueId);
            ApplyHero(hero);
            GetComponent<LeagueVisualStyle>()?.Refresh(ViewedLeagueId, DisplayTrophies);
            if (Title != null) Title.text = LeagueName;
            if (Score != null) Score.text = DisplayTrophies.ToString();
            if (Online != null) Online.text = "OFFLINE";
            if (Season != null) Season.text = "LOCAL PROGRESS · CONNECT TO RANK";
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
                bool own = row.name == "CurrentPlayerRow" && Leagues.ForTrophies(DisplayTrophies).Id == ViewedLeagueId;
                row.gameObject.SetActive(own);
                if (own && Names.Length > 0 && Names[0] != null)
                    ((RectTransform)row).anchoredPosition = ((RectTransform)Names[0].transform.parent).anchoredPosition;
                Names[i].text = own ? ProfileIdentityEditor.ResolveName("PLAYER") : "";
                if (Scores != null && i < Scores.Length && Scores[i] != null)
                    Scores[i].text = own ? DisplayTrophies.ToString() : "";
                var rank = row.Find("Rank")?.GetComponent<TextMeshProUGUI>();
                if (rank != null) rank.text = "—";
            }
            GetComponent<LeagueLayout>()?.Fit();
        }

        private void ApplyHero(Sprite sprite)
        {
            if (Hero == null || sprite == null) return;
            var swipe = Hero.GetComponent<LeagueHeroSwipe>();
            if (swipe != null && swipe.IsMoving && Hero.sprite == sprite) return;
            swipe?.CancelSlide();
            Hero.sprite = sprite;
            Hero.rectTransform.pivot = new Vector2(.5f, .5f);
            Hero.rectTransform.anchoredPosition = HeroPositionFor(sprite);
        }
        // Optical axis of the trophy stem in each source, excluding surrounding stickers and bursts.
        public float HeroCupCenterX(Sprite sprite) => sprite == BronzeHero ? .52f : .5f;
        public Vector2 HeroPositionFor(Sprite sprite)
        {
            var size = Hero.rectTransform.rect.size;
            float imageWidth = Mathf.Min(size.x, size.y * sprite.rect.width / sprite.rect.height);
            return new Vector2((.5f - HeroCupCenterX(sprite)) * imageWidth, 0);
        }
        public Sprite HeroForLeague(string leagueId)
        {
            Sprite hero;
            switch (leagueId)
            {
                case "silver": hero = SilverHero; break;
                case "gold": hero = GoldHero; break;
                case "diamond": hero = DiamondHero; break;
                default: hero = BronzeHero; break;
            }
            return hero != null ? hero : BronzeHero;
        }
    }
}
