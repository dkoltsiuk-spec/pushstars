using System;
using System.Collections.Generic;

using PushStars.Core;

using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Binds the Profile tab to locally saved workout statistics and renders the
    /// workout history, including training sets and assessments.
    /// The TYPE / MODE dropdowns filter the cached list client-side.
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


        [Header("Filters")]
        [SerializeField] private FilterDropdown _typeFilter; // by exercise
        [SerializeField] private FilterDropdown _modeFilter; // by mode (pvp/ghost)



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
            Refresh();
        }

        private void OnDestroy()
        {
            if (_typeFilter != null) _typeFilter.OnChanged -= OnTypeChanged;
            if (_modeFilter != null) _modeFilter.OnChanged -= OnModeChanged;
        }

        private void OnTypeChanged(string v) { _typeValue = v; ApplyFilter(); }
        private void OnModeChanged(string v) { _modeValue = v; ApplyFilter(); }

        private void Refresh()
        {
            Bind(LocalProfile.Profile);
            _allMatches = LocalProfile.History;
            ApplyFilter();
        }
        private void Bind(UserProfile p)
        {
            if (_nameText   != null) _nameText.text   = ProfileIdentityEditor.ResolveName(p.DisplayName);
            if (_rankText   != null) _rankText.text   = RankLabel(p.Rank);
            if (_streakText != null) _streakText.text = $"WIN STREAK: {p.WinStreak}";

            if (_winsBadge    != null) _winsBadge.SetStat(p.TotalWins.ToString("N0"), "WINS");
            if (_winRateBadge != null) _winRateBadge.SetStat($"{p.WinRatePercent}%", "WIN RATE");
            if (_repsBadge    != null) _repsBadge.SetStat(p.TotalReps.ToString("N0"), "TOTAL");
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
            "silver"  => "SILVER",
            "gold"    => "GOLD",
            "diamond" => "DIAMOND",
            _         => "BRONZE",
        };

    }
}
