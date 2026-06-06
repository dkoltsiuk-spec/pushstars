using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Binds the Profile tab to <c>users/{uid}</c> (name, rank, streak, KPI badges) and renders the
    /// "RECENT MATCHES" list from <see cref="MatchHistoryRepository"/>, falling back to mock cards
    /// when no real matches exist. The TYPE / MODE dropdowns filter the cached list client-side.
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
        [SerializeField] private Transform  _historyContent;
        [SerializeField] private MatchRow   _matchRowTemplate;
        [SerializeField] private GameObject _historyEmptyState;
        [SerializeField] private bool       _useMockData = true; // demo cards until real matches exist

        [Header("Filters")]
        [SerializeField] private FilterDropdown _typeFilter; // by exercise
        [SerializeField] private FilterDropdown _modeFilter; // by mode (pvp/ghost)

        private readonly UserProfileRepository  _profileRepo = new UserProfileRepository();
        private readonly MatchHistoryRepository _historyRepo = new MatchHistoryRepository();
        private readonly List<GameObject>       _rows        = new List<GameObject>();

        private List<MatchRecord> _allMatches = new List<MatchRecord>();
        private string _typeValue = "";
        private string _modeValue = "";
        private bool   _wired;

        private void OnEnable()
        {
            if (!_wired)
            {
                if (_typeFilter != null) _typeFilter.OnChanged += OnTypeChanged;
                if (_modeFilter != null) _modeFilter.OnChanged += OnModeChanged;
                _wired = true;
            }
            Refresh().Forget();
        }

        private void OnDestroy()
        {
            if (_typeFilter != null) _typeFilter.OnChanged -= OnTypeChanged;
            if (_modeFilter != null) _modeFilter.OnChanged -= OnModeChanged;
        }

        private void OnTypeChanged(string v) { _typeValue = v; ApplyFilter(); }
        private void OnModeChanged(string v) { _modeValue = v; ApplyFilter(); }

        private async UniTask Refresh()
        {
            var profile = await _profileRepo.GetAsync();
            await UniTask.SwitchToMainThread();
            Bind(profile);

            _historyRepo.Reset();
            var matches = await _historyRepo.GetNextPageAsync();
            await UniTask.SwitchToMainThread();

            if (matches.Count == 0 && _useMockData) matches = MockMatches();
            _allMatches = matches;
            ApplyFilter();
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

        private void ApplyFilter()
        {
            var filtered = new List<MatchRecord>();
            foreach (var m in _allMatches)
            {
                if (!string.IsNullOrEmpty(_typeValue) && m.Exercise != _typeValue) continue;
                if (!string.IsNullOrEmpty(_modeValue) && m.Mode     != _modeValue) continue;
                filtered.Add(m);
            }
            RenderHistory(filtered);
        }

        private void RenderHistory(List<MatchRecord> matches)
        {
            foreach (var row in _rows) if (row != null) Destroy(row);
            _rows.Clear();

            if (_historyEmptyState != null) _historyEmptyState.SetActive(matches.Count == 0);

            if (_matchRowTemplate == null || _historyContent == null) return;
            foreach (var m in matches)
            {
                var go = Instantiate(_matchRowTemplate.gameObject, _historyContent);
                go.SetActive(true);
                go.GetComponent<MatchRow>().Set(m);
                _rows.Add(go);
            }
        }

        private static string RankLabel(string rank) => rank switch
        {
            "silver"  => "СЕРЕБРО",
            "gold"    => "ЗОЛОТО",
            "diamond" => "АЛМАЗ",
            _         => "БРОНЗА",
        };

        // Demo history (matches the design cards) — replaced once real matches exist (phases 12–14).
        private static List<MatchRecord> MockMatches()
        {
            var now = DateTime.UtcNow;
            return new List<MatchRecord>
            {
                new MatchRecord { OpponentName = "NOX_92",    Won = true,  MyReps = 18, OpponentReps = 16,
                                  Exercise = "pushups", Mode = "pvp",   DurationSec = 60, IsRecord = true,
                                  CreatedAt = now.AddHours(-2) },
                new MatchRecord { OpponentName = "kara.bear", Won = false, MyReps = 15, OpponentReps = 22,
                                  Exercise = "pushups", Mode = "pvp",   DurationSec = 60,
                                  CreatedAt = now.AddHours(-5) },
                new MatchRecord { OpponentName = "GHOST",     Won = true,  MyReps = 24, OpponentReps = 20,
                                  Exercise = "pushups", Mode = "ghost", DurationSec = 60,
                                  CreatedAt = now.AddHours(-26) },
            };
        }
    }
}
