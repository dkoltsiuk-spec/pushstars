using System;
using System.Collections.Generic;
using PushStars.Core;
using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Root shell of the Main scene. Controls the bottom pill-nav and swaps tab panels.
    /// Canvas-swap strategy: each tab has a root GameObject; only the active one is enabled.
    /// </summary>
    public class MainShellView : MonoBehaviour
    {
        [Header("Tab Buttons (bottom nav)")]
        [SerializeField] private TabButton[] _tabButtons;

        [Header("Tab Panels (same-order as TabId enum)")]
        [SerializeField] private GameObject _leaguePanel;
        [SerializeField] private GameObject _duelPanel;
        [SerializeField] private GameObject _profilePanel;

        private Dictionary<TabId, GameObject> _panels;
        private TabId _currentTab = TabId.Duel;
        private readonly List<TextMeshProUGUI> _balances = new List<TextMeshProUGUI>();
        private float _nextBalanceRefresh;

        private void Awake()
        {
            _panels = new Dictionary<TabId, GameObject>
            {
                { TabId.League,  _leaguePanel  },
                { TabId.Duel,    _duelPanel    },
                { TabId.Profile, _profilePanel }
            };

            foreach (var btn in _tabButtons)
                btn.OnTabSelected += SwitchTab;

            // The HUD pills live beside the shell under the same canvas, not inside it.
            foreach (var label in transform.root.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (label.name == "Number" && (label.transform.parent.name == "TrophyPill" ||
                    label.transform.parent.name == "GemPill" || label.transform.parent.name == "AuraPill"))
                    _balances.Add(label);
            RefreshBalances();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextBalanceRefresh) return;
            _nextBalanceRefresh = Time.unscaledTime + .25f;
            RefreshBalances();
        }

        private void RefreshBalances()
        {
            foreach (var label in _balances)
            {
                string pill = label.transform.parent.name;
                // A reward flight may hold the old value while its icons are still in the air.
                if (!HudBalanceHold.TryGet(pill, out long value))
                    value = pill == "TrophyPill" ? LocalProfile.Trophies
                        : pill == "GemPill" ? CaseRewards.GemsBalance : CaseRewards.AuraBalance;
                // Aura is a meme-scale score: 10K, 150K, 1.5M. The other pills stay exact.
                string text = pill == "AuraPill" ? AuraFormat.Short(value) : value.ToString("N0");
                if (label.text != text) label.text = text;
            }
        }

        private void Start() => SwitchTab(_currentTab);

        public void SwitchTab(TabId tabId)
        {
            _currentTab = tabId;

            foreach (var kv in _panels)
                kv.Value?.SetActive(kv.Key == tabId);

            foreach (var btn in _tabButtons)
                btn.SetActive(btn.TabId == tabId);
        }

        private void OnDestroy()
        {
            foreach (var btn in _tabButtons)
                btn.OnTabSelected -= SwitchTab;
        }
    }
}
