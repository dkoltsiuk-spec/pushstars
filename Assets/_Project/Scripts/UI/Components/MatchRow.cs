using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// One card in the profile's "RECENT MATCHES" list. The W/L badge is a ready design-system
    /// icon (win.png / lose.png) swapped by result; "vs {name}", a meta line ("2h - 60s - PUSHUPS"),
    /// the score (own reps coloured win/loss) and an optional "NEW RECORD" tag. Filled from a
    /// <see cref="Core.MatchRecord"/>.
    /// </summary>
    public class MatchRow : MonoBehaviour
    {
        [SerializeField] private Image           _badge;
        [SerializeField] private Sprite          _winSprite;
        [SerializeField] private Sprite          _loseSprite;
        [SerializeField] private TextMeshProUGUI _vsName;
        [SerializeField] private TextMeshProUGUI _meta;
        [SerializeField] private TextMeshProUGUI _myScore;
        [SerializeField] private TextMeshProUGUI _oppScore;
        [SerializeField] private TextMeshProUGUI _record;

        public void Set(Core.MatchRecord m)
        {
            if (_badge != null) _badge.sprite = m.Won ? _winSprite : _loseSprite;

            // "vs" is dimmed (valid paired <color> tag — <alpha> has no closing tag in TMP).
            if (_vsName  != null) _vsName.text = $"<color=#7E8497>vs</color> {m.OpponentName}";
            if (_meta    != null) _meta.text   = $"{TimeAgo(m.CreatedAt)} - {m.DurationSec}s - {Up(m.Exercise)}";
            if (_myScore != null) { _myScore.text  = m.MyReps.ToString(); _myScore.color = m.Won ? AppColors.AccentLime : AppColors.DangerRed; }
            if (_oppScore != null) _oppScore.text = m.OpponentReps.ToString();
            if (_record   != null) _record.gameObject.SetActive(m.IsRecord);
        }

        private static string Up(string s) => string.IsNullOrEmpty(s) ? "" : s.ToUpperInvariant();

        private static string TimeAgo(System.DateTime utc)
        {
            var span = System.DateTime.UtcNow - utc;
            if (span.TotalMinutes < 60) return $"{Mathf.Max(1, (int)span.TotalMinutes)}m";
            if (span.TotalHours   < 24) return $"{(int)span.TotalHours}h";
            if (span.TotalDays    < 2)  return "yesterday";
            return $"{(int)span.TotalDays}d";
        }
    }
}
