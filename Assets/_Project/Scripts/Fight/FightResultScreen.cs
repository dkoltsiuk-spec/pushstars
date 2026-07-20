using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using PushStars.Core;

namespace PushStars.Fight
{
    /// <summary>
    /// Full-screen result overlay of the boss duel: ПОБЕДА / ПОРАЖЕНИЕ / НИЧЬЯ, the rep score,
    /// the XP earned, and ДАЛЕЕ back to Main. Hidden until <see cref="Show"/>; the fight HUD
    /// stays underneath but this covers it.
    /// </summary>
    public sealed class FightResultScreen : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TextMeshProUGUI _title;
        [SerializeField] private TextMeshProUGUI _score;
        [SerializeField] private TextMeshProUGUI _xp;
        [SerializeField] private Button _continueButton;

        private static readonly Color WinColor  = new Color32(107, 255,  74, 255); // AccentLime
        private static readonly Color LossColor = new Color32(255,  80,  80, 255);
        private static readonly Color DrawColor = new Color32(245, 200,  66, 255); // AccentYellow

        private void Awake()
        {
            if (_root != null) _root.SetActive(false);
            if (_continueButton != null) _continueButton.onClick.AddListener(Continue);
        }

        private void OnDestroy()
        {
            if (_continueButton != null) _continueButton.onClick.RemoveListener(Continue);
        }

        public void Show(bool win, bool draw, int myReps, int bossReps, long xp, string bossName)
        {
            if (_root == null) return;
            _root.SetActive(true);

            if (_title != null)
            {
                _title.text  = draw ? "НИЧЬЯ" : win ? "ПОБЕДА!" : "ПОРАЖЕНИЕ";
                _title.color = draw ? DrawColor : win ? WinColor : LossColor;
            }
            if (_score != null)
                _score.text = $"ТЫ  {myReps} : {bossReps}  {bossName}";
            if (_xp != null)
                _xp.text = xp > 0 ? $"+{xp} XP" : "";
        }

        private void Continue() => SceneManager.LoadScene(FightConfig.MainSceneName);
    }
}
