using System;
using System.Collections.Generic;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed class ProfileDashboard : MonoBehaviour
    {
        public TextMeshProUGUI PlayerName, PlayerId, Level, Xp, Wins, WinRate, Reps, Activity, ActivityNote, Empty;
        public Image XpFill;
        public RectTransform[] Bars;
        public RectTransform History;
        public GameObject RowTemplate, PreviewNote;
        public Button[] Filters;
        public Sprite SelectedPlate, IdlePlate, WinIcon, LossIcon;
        public Image NavActive;
        public Sprite ProfileActiveSprite;
        [Header("Athlete dashboard")]
        public TextMeshProUGUI BestSet, NextMilestone, Streak, StreakNote, LeagueName, Trophies, ActiveDays, HistoryCount, AchievementCount;
        public TextMeshProUGUI[] DayLabels, DayValues, MilestoneValues;
        public Image[] MilestoneFills;
        public Graphic[] MilestoneIcons;
        public Image RecordFill, LeagueIcon;
        public Sprite[] LeagueCups;
        public Image ProfileEmblem;
        public Sprite[] RankBadges;
        public Sprite TrainingIcon, AssessmentIcon;
        public Button LeagueButton, MoreHistory;
        public Button TrainButton;
        public GameObject WeeklyEmpty;
        public MainShellView Shell;
        public ProfileAchievements Achievements;
        public float HistoryTop = 545, RowHeight = 82, ActivityHeight = 82;
        public bool AthleteDesign;
        private int _visibleLimit = 10;
        private int _filterIndex;
        private Sprite _originalNav;
        private readonly List<MatchRecord> _matches = new List<MatchRecord>();
        private string _mode = "";
        private RectTransform _background, _canvasRect;

        // Keep the background owned by this tab so it hides with it, but cover the
        // entire canvas, including the space outside the content's safe area.
        private void LateUpdate()
        {
            if (_background == null || _canvasRect == null) return;
            var parent = _background.parent as RectTransform;
            if (parent == null) return;
            var bounds = _canvasRect.rect;
            Vector2 first = parent.InverseTransformPoint(_canvasRect.TransformPoint(bounds.min));
            Vector2 opposite = parent.InverseTransformPoint(_canvasRect.TransformPoint(bounds.max));
            _background.anchorMin = Vector2.zero;
            _background.anchorMax = Vector2.one;
            _background.offsetMin = Vector2.Min(first, opposite) - parent.rect.min;
            _background.offsetMax = Vector2.Max(first, opposite) - parent.rect.max;
        }

        private void Awake()
        {
            _background = transform.Find("ProfileBackground") as RectTransform;
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) _canvasRect = canvas.rootCanvas.transform as RectTransform;
            for (int i = 0; i < Filters.Length; i++)
            {
                int index = i;
                Filters[i].onClick.AddListener(() => SelectFilter(index));
            }
            if (NavActive != null) _originalNav = NavActive.sprite;
            if (LeagueButton != null) LeagueButton.onClick.AddListener(() => { if (Shell != null) Shell.SwitchTab(TabId.League); });
            if (MoreHistory != null) MoreHistory.onClick.AddListener(() => { _visibleLimit += 20; RenderHistory(); });
            if (TrainButton != null) TrainButton.onClick.AddListener(OpenTraining);
        }

        private void OnEnable()
        {
            if (PreviewNote != null) PreviewNote.SetActive(false);
            if (NavActive != null) NavActive.sprite = ProfileActiveSprite;
            SelectFilter(0);
            Refresh();
            var scroll = GetComponentInChildren<ScrollRect>();
            if (scroll != null) scroll.verticalNormalizedPosition = 1;
        }

        private void OnDisable()
        {
            if (NavActive != null) NavActive.sprite = _originalNav;
        }

        private void OpenTraining()
        {
            if (Shell == null) return;
            Shell.SwitchTab(TabId.Duel);
            var modes = Shell.transform.root.GetComponentInChildren<ModeSelectionController>(true);
            if (modes != null) { modes.Show(); modes.Select(GameMode.Training); }
        }

        public void Refresh()
        {
            RenderSnapshot(LocalProfile.Profile, LocalProfile.History, LocalProfile.BestReps, DateTime.Today);
        }

        // Also used by editor previews. Never changes the player's save or identity.
        public void RenderSnapshot(UserProfile profile, IReadOnlyList<MatchRecord> history, int best, DateTime today)
        {
            Bind(profile);
            _matches.Clear();
            for (int i = 0; i < history.Count; i++) _matches.Add(history[i]);
            _matches.Sort((a,b) => b.CreatedAt.CompareTo(a.CreatedAt));
            RenderHistory();
            DrawActivity(today);
            if (!AthleteDesign) return;
            var insights = ProfileInsights.From(_matches, today, best);
            if (BestSet != null) BestSet.text = insights.BestSet.ToString("N0");
            int goal = insights.BestSet < 10 ? 10 : insights.BestSet < 25 ? 25 : insights.BestSet < 50 ? 50
                : insights.BestSet < 100 ? 100 : (insights.BestSet / 50 + 1) * 50;
            if (NextMilestone != null) NextMilestone.text = "NEXT MILESTONE: " + goal;
            if (RecordFill != null) RecordFill.fillAmount = Mathf.Clamp01((float)insights.BestSet / goal);
            if (Streak != null) Streak.text = insights.StreakDays.ToString();
            if (StreakNote != null) StreakNote.text = insights.StreakDays == 0 ? "START YOUR STREAK" : "DAYS IN A ROW";
            ActiveDays.text = insights.ActiveDays + "/7 ACTIVE DAYS";
            if (LeagueName != null) LeagueName.text = profile.League.DisplayName.ToUpperInvariant();
            if (Trophies != null) Trophies.text = profile.Trophies.ToString("N0");
            int tier = profile.League.Id == "diamond" ? 3 : profile.League.Id == "gold" ? 2 : profile.League.Id == "silver" ? 1 : 0;
            if (LeagueIcon != null && LeagueCups != null && tier < LeagueCups.Length) LeagueIcon.sprite = LeagueCups[tier];
            if (ProfileEmblem != null && RankBadges != null && tier < RankBadges.Length) ProfileEmblem.sprite = RankBadges[tier];
            if (Achievements != null) { Achievements.Bind(profile, history, best, today); return; }
            long[] progress = { profile.TotalReps > 0 ? 1 : 0, profile.TotalReps, profile.TotalWins };
            int[] goals = { 1, 100, 10 };
            int unlocked = 0;
            for (int i = 0; i < goals.Length; i++)
            {
                bool earned = progress[i] >= goals[i];
                if (earned) unlocked++;
                MilestoneValues[i].text = earned ? "COMPLETED" : progress[i] + " / " + goals[i];
                MilestoneValues[i].color = earned ? new Color32(96,255,210,255) : new Color32(190,206,245,255);
                MilestoneFills[i].fillAmount = Mathf.Clamp01((float)progress[i]/goals[i]);
                MilestoneIcons[i].color = earned ? Color.white : new Color32(143,153,184,255);
                MilestoneIcons[i].SetVerticesDirty();
            }
            AchievementCount.text = unlocked + " / 3";
        }

        private void Bind(UserProfile p)
        {
            PlayerName.text = ProfileIdentityEditor.ResolveName(p.Exists ? p.DisplayName : "PLAYER");
            PlayerId.text = "ON DEVICE";
            if (ServiceLocator.TryGet<FirebaseAuthService>(out var auth) && !string.IsNullOrEmpty(auth.Uid))
                PlayerId.text = "#" + auth.Uid.Substring(0, Math.Min(8, auth.Uid.Length)).ToUpperInvariant();
            Level.text = "<size=9>LVL</size>\n" + p.Level;
            long needed = LevelCalculator.XpForLevelUp(p.Level);
            Xp.text = (needed - LevelCalculator.XpRemainingToNext(p.Xp)) + "/" + needed;
            XpFill.fillAmount = p.LevelProgress;
            if (Wins != null) Wins.text = p.TotalWins.ToString("N0");
            WinRate.text = AthleteDesign && p.Games == 0 ? "—" : p.WinRatePercent + "%";
            Reps.text = p.TotalReps.ToString("N0");
        }

        public void SelectFilter(int index)
        {
            _filterIndex = index;
            _visibleLimit = 10;
            _mode = index == 1 ? "pvp" : index == 2 ? "ghost" : "";
            for (int i = 0; i < Filters.Length; i++)
            {
                if (!AthleteDesign) Filters[i].image.sprite = i == index ? SelectedPlate : IdlePlate;
                else
                {
                    Filters[i].image.sprite = i == index ? SelectedPlate : IdlePlate;
                    Filters[i].GetComponentInChildren<TextMeshProUGUI>().color = i == index ? new Color32(18,30,68,255) : Color.white;
                }
            }
            RenderHistory();
        }

        private void DrawActivity(DateTime today)
        {
            int[] values = ProfileInsights.From(_matches, today, 0).Reps;
            int max = 1, total = 0;
            foreach (int v in values) { max = Math.Max(max, v); total += v; }
            for (int i = 0; i < Math.Min(Bars.Length, values.Length); i++)
            {
                Bars[i].sizeDelta = new Vector2(Bars[i].sizeDelta.x, 3 + ActivityHeight * values[i] / max);
                if (AthleteDesign)
                {
                    string[] weekdays = { "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT" };
                    DayLabels[i].text = i == 6 ? "TODAY" : weekdays[(int)today.AddDays(i-6).DayOfWeek];
                    DayValues[i].text = values[i] > 0 ? values[i].ToString() : "";
                    DayValues[i].rectTransform.anchoredPosition = new Vector2(DayValues[i].rectTransform.anchoredPosition.x, Bars[i].anchoredPosition.y + Bars[i].sizeDelta.y + 20);
                    Bars[i].GetComponent<Image>().color = values[i] == 0 ? new Color32(63,82,135,255) : i == 6 ? new Color32(255,213,79,255) : new Color32(42,146,255,255);
                }
            }
            Activity.text = total.ToString();
            ActivityNote.text = AthleteDesign ? "REPS THIS WEEK" : "LAST 7 DAYS";
            if (WeeklyEmpty != null) WeeklyEmpty.SetActive(total == 0);
        }

        private bool MatchesFilter(MatchRecord m) => !AthleteDesign
            ? _mode == "" || m.Mode == _mode || (_mode == "ghost" && m.Mode == "boss")
            : _filterIndex == 0 || (_filterIndex == 1 && m.IsSolo)
              || (_filterIndex == 2 && (m.Mode == "pvp" || m.Mode == "ghost")) || (_filterIndex == 3 && m.Mode == "boss");

        private void RenderHistory()
        {
            if (History == null || RowTemplate == null) return;
            // Authored previews survive a scene reload; remove them as well as runtime rows.
            for (int child = History.childCount - 1; child >= 0; child--)
            {
                var row = History.GetChild(child).gameObject;
                if (!row.name.StartsWith("Match_", StringComparison.Ordinal)) continue;
                row.SetActive(false);
                if (Application.isPlaying) Destroy(row); else DestroyImmediate(row);
            }
            int index = 0;
            int total = 0;
            foreach (var m in _matches)
            {
                if (!MatchesFilter(m)) continue;
                total++;
                if (AthleteDesign && index >= _visibleLimit) continue;
                var row = Instantiate(RowTemplate, History);
                row.name = "Match_" + index;
                row.SetActive(true);
                var rt = (RectTransform)row.transform;
                rt.anchoredPosition = new Vector2(0, -index * RowHeight);
                var resultIcon = row.transform.Find("Result").GetComponent<Image>();
                resultIcon.enabled = AthleteDesign || (!m.Draw && !m.IsSolo);
                resultIcon.sprite = AthleteDesign && (m.IsSolo || m.Draw) ? (m.Mode == "assessment" ? AssessmentIcon : TrainingIcon) : m.Won ? WinIcon : LossIcon;
                row.transform.Find("Opponent").GetComponent<TextMeshProUGUI>().text = m.IsSolo
                    ? (m.Mode == "assessment" ? "ASSESSMENT" : "TRAINING SET")
                    : "vs " + (string.IsNullOrEmpty(m.OpponentName) ? "OPPONENT" : m.OpponentName);
                row.transform.Find("Opponent").GetComponent<TextMeshProUGUI>().richText = false;
                var score = row.transform.Find("Score").GetComponent<TextMeshProUGUI>();
                score.text = m.IsSolo ? m.MyReps + " REPS" : m.MyReps + " : " + m.OpponentReps + (m.Draw && !AthleteDesign ? "  DRAW" : "");
                score.color = m.IsSolo || m.Draw ? Color.white : m.Won ? new Color32(207, 255, 0, 255) : new Color32(255, 75, 30, 255);
                row.transform.Find("Detail").GetComponent<TextMeshProUGUI>().text =
                    m.CreatedAt.ToLocalTime().ToString("dd.MM") + "  /  " + m.DurationSec + "s  /  " + (m.Exercise ?? "pushups").ToUpperInvariant();
                row.transform.Find("Record").GetComponent<TextMeshProUGUI>().text = m.IsRecord ? "NEW RECORD" : "";
                var outcome = row.transform.Find("Outcome");
                if (outcome != null) outcome.GetComponent<TextMeshProUGUI>().text = m.IsSolo ? "SOLO" : m.Draw ? "DRAW" : m.Won ? "VICTORY" : "DEFEAT";
                index++;
            }
            Empty.gameObject.SetActive(index == 0);
            Empty.text = _filterIndex == 0 ? "NO WORKOUTS YET\n<size=13>Complete a set or battle to start your history.</size>"
                : "NOTHING HERE YET\n<size=13>Try another filter to see your workouts.</size>";
            if (HistoryCount != null) HistoryCount.text = total.ToString();
            if (MoreHistory != null)
            {
                MoreHistory.gameObject.SetActive(total > index);
                MoreHistory.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -Math.Max(1,index)*RowHeight);
            }
            var layout = History.GetComponentInParent<DashboardWidthFit>();
            if (layout != null) layout.DesignHeight = Math.Max(690, HistoryTop + Math.Max(1, index) * RowHeight + (total > index ? 62 : 20));
        }
    }
}
