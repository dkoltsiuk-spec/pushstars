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
    public sealed partial class DuelReadyPanel : MonoBehaviour
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

        [Header("Opponent (top-right)")]
        [SerializeField] private TextMeshProUGUI _opponentName;
        [SerializeField] private TextMeshProUGUI _opponentTrophies;
        [SerializeField] private TextMeshProUGUI _opponentBest;
        [SerializeField] private TextMeshProUGUI _opponentWinRate;
        // Legacy scene references: hide miniature duplicates in already-saved scenes.
        [SerializeField, HideInInspector] private RawImage _opponentAvatarImage;
        [SerializeField] private RawImage _opponentAvatarSource;

        [Header("Player (bottom-left)")]
        [SerializeField] private TextMeshProUGUI _playerName;
        [SerializeField] private TextMeshProUGUI _playerTrophies;
        [SerializeField] private TextMeshProUGUI _playerBest;
        [SerializeField] private TextMeshProUGUI _playerWinRate;
        [SerializeField, HideInInspector] private RawImage _playerAvatarImage;
        [SerializeField] private RawImage _playerAvatarSource;

        [Header("Action")]
        [SerializeField] private Button _readyButton;

        /// <summary>Raised when ГОТОВ is pressed. The controller starts looking for the plank.</summary>
        public event Action OnReady;
        private FightAvatar[] _preparationAvatars;

        private void Awake()
        {
            HideLegacyPortraits();
            if (_root != null) _root.SetActive(false);
            if (_readyButton != null) _readyButton.onClick.AddListener(Ready);
        }

        private void OnDestroy()
        {
            RestoreAvatarPresentation();
            if (_readyButton != null) _readyButton.onClick.RemoveListener(Ready);
        }

        public void Show(in Side player, in Side opponent)
        {
            if (_root == null) return;
            _root.SetActive(true);
            BuildReferenceLayout();
            _preparationAvatars = FindObjectsByType<FightAvatar>(FindObjectsSortMode.None);
            foreach (var avatar in _preparationAvatars) avatar.SetPreparationPresentation(true);

            Fill(opponent, _opponentName, _opponentTrophies, _opponentBest, _opponentWinRate);
            Fill(player, _playerName, _playerTrophies, _playerBest, _playerWinRate);

            RefreshPortraits();
            LoadPlayerName();
        }

        private async void LoadPlayerName()
        {
            try
            {
                var profile = await new PushStars.Services.UserProfileRepository().GetAsync();
                if (this != null && _playerName != null && profile.Exists && !string.IsNullOrWhiteSpace(profile.DisplayName))
                    _playerName.text = profile.DisplayName;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ReadyPanel] Profile name unavailable: {exception.Message}");
            }
        }

        public void Hide()
        {
            RestoreAvatarPresentation();
            if (_root != null) _root.SetActive(false);
        }

        private void OnDisable() => RestoreAvatarPresentation();

        private void RestoreAvatarPresentation()
        {
            if (_preparationAvatars == null) return;
            foreach (var avatar in _preparationAvatars)
                if (avatar != null) avatar.SetPreparationPresentation(false);
            _preparationAvatars = null;
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

        private void HideLegacyPortraits()
        {
            // Hide until the composition has placed and bound these surfaces.
            if (_opponentAvatarImage != null) _opponentAvatarImage.gameObject.SetActive(false);
            if (_playerAvatarImage != null) _playerAvatarImage.gameObject.SetActive(false);
        }

        private static string Number(int value) => value == Side.Unknown ? "—" : value.ToString();

        private void Ready()
        {
            Hide();
            OnReady?.Invoke();
        }
    }
}
