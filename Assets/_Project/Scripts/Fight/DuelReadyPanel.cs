using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// The card shown before a duel starts: who you are, who you are about to fight, and what each
    /// of you brings. Nothing happens until ГОТОВ is pressed.
    ///
    /// <para><b>Why a screen at all.</b> The set is sixty seconds of maximum effort and it begins
    /// the moment the plank is confirmed — which, without this, could be while the player is still
    /// getting the phone into position. The card is the beat where they read the opponent's numbers,
    /// decide what they are going for, and start on their own terms.</para>
    ///
    /// <para>A level test skips it: there is no opponent to size up, and the result screen is where
    /// its numbers first mean something.</para>
    /// </summary>
    public sealed class DuelReadyPanel : MonoBehaviour
    {
        /// <summary>One fighter's card. <see cref="Unknown"/> marks a number the app cannot honestly
        /// fill in yet — a ghost has no win rate of its own, and rendering a 0 there would be a
        /// claim rather than a gap.</summary>
        public readonly struct Side
        {
            public const int Unknown = -1;

            public readonly string Name;
            public readonly int Trophies;
            public readonly int BestReps;
            public readonly int WinRatePercent;

            public Side(string name, int trophies, int bestReps, int winRatePercent)
            {
                Name = name;
                Trophies = trophies;
                BestReps = bestReps;
                WinRatePercent = winRatePercent;
            }
        }

        [SerializeField] private GameObject _root;

        [Header("Opponent (top)")]
        [SerializeField] private TextMeshProUGUI _opponentName;
        [SerializeField] private TextMeshProUGUI _opponentTrophies;
        [SerializeField] private TextMeshProUGUI _opponentBest;
        [SerializeField] private TextMeshProUGUI _opponentWinRate;

        [Header("Player (bottom)")]
        [SerializeField] private TextMeshProUGUI _playerName;
        [SerializeField] private TextMeshProUGUI _playerTrophies;
        [SerializeField] private TextMeshProUGUI _playerBest;
        [SerializeField] private TextMeshProUGUI _playerWinRate;

        [Header("Action")]
        [SerializeField] private Button _readyButton;

        /// <summary>Raised when ГОТОВ is pressed. The controller starts looking for the plank.</summary>
        public event Action OnReady;

        private void Awake()
        {
            if (_root != null) _root.SetActive(false);
            if (_readyButton != null) _readyButton.onClick.AddListener(Ready);
        }

        private void OnDestroy()
        {
            if (_readyButton != null) _readyButton.onClick.RemoveListener(Ready);
        }

        public void Show(in Side player, in Side opponent)
        {
            if (_root == null) return;
            _root.SetActive(true);

            Fill(opponent, _opponentName, _opponentTrophies, _opponentBest, _opponentWinRate);
            Fill(player, _playerName, _playerTrophies, _playerBest, _playerWinRate);
        }

        public void Hide()
        {
            if (_root != null) _root.SetActive(false);
        }

        private static void Fill(in Side side, TextMeshProUGUI name, TextMeshProUGUI trophies,
                                 TextMeshProUGUI best, TextMeshProUGUI winRate)
        {
            if (name != null) name.text = side.Name;
            if (trophies != null) trophies.text = Number(side.Trophies);
            if (best != null) best.text = Number(side.BestReps);
            if (winRate != null) winRate.text = side.WinRatePercent == Side.Unknown
                ? "—"
                : $"{side.WinRatePercent}%";
        }

        private static string Number(int value) => value == Side.Unknown ? "—" : value.ToString();

        private void Ready()
        {
            Hide();
            OnReady?.Invoke();
        }
    }
}
