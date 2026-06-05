using Cysharp.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Binds the Profile tab to the player's <c>users/{uid}</c> document: name, rank, streak and
    /// the three KPI badges (wins / win-rate / total reps). Also kicks off the first page of match
    /// history and toggles the empty state. Reads happen on enable; defaults render when the
    /// document or backend isn't available yet.
    ///
    /// Match-row rendering and "load more" land once real matches exist (phases 12–14); the
    /// repository and empty-state plumbing are already wired here.
    /// </summary>
    public class ProfilePresenter : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _rankText;
        [SerializeField] private TextMeshProUGUI _streakText;

        [Header("KPIs")]
        [SerializeField] private StatBadge _winsBadge;
        [SerializeField] private StatBadge _winRateBadge;
        [SerializeField] private StatBadge _repsBadge;

        [Header("History")]
        [SerializeField] private GameObject _historyEmptyState;

        private readonly UserProfileRepository  _profileRepo = new UserProfileRepository();
        private readonly MatchHistoryRepository _historyRepo = new MatchHistoryRepository();

        private void OnEnable() => Refresh().Forget();

        private async UniTask Refresh()
        {
            var profile = await _profileRepo.GetAsync();
            await UniTask.SwitchToMainThread();
            Bind(profile);

            _historyRepo.Reset();
            var page = await _historyRepo.GetNextPageAsync();
            await UniTask.SwitchToMainThread();
            if (_historyEmptyState != null) _historyEmptyState.SetActive(page.Count == 0);
        }

        private void Bind(UserProfile p)
        {
            if (_nameText   != null) _nameText.text   = p.DisplayName;
            if (_rankText   != null) _rankText.text   = RankLabel(p.Rank);
            if (_streakText != null) _streakText.text = $"СЕРИЯ ПОБЕД: {p.WinStreak}";

            if (_winsBadge    != null) _winsBadge.SetStat(p.TotalWins.ToString("N0"), "ПОБЕДЫ");
            if (_winRateBadge != null) _winRateBadge.SetStat($"{p.WinRatePercent}%", "ВИНРЕЙТ");
            if (_repsBadge    != null) _repsBadge.SetStat(p.TotalReps.ToString("N0"), "ВСЕГО");
        }

        private static string RankLabel(string rank) => rank switch
        {
            "silver"  => "СЕРЕБРО",
            "gold"    => "ЗОЛОТО",
            "diamond" => "АЛМАЗ",
            _         => "БРОНЗА",
        };
    }
}
