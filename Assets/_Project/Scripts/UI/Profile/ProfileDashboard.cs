using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
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
        private Sprite _originalNav;
        private readonly List<MatchRecord> _matches = new List<MatchRecord>();
        private readonly List<GameObject> _rows = new List<GameObject>();
        private int _generation;
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
                Filters[i].onClick.AddListener(() => Filter(index));
            }
            if (NavActive != null) _originalNav = NavActive.sprite;
        }

        private void OnEnable()
        {
            if (PreviewNote != null) PreviewNote.SetActive(false);
            if (NavActive != null) NavActive.sprite = ProfileActiveSprite;
            Filter(0);
            Refresh(++_generation).Forget();
        }

        private void OnDisable()
        {
            _generation++;
            if (NavActive != null) NavActive.sprite = _originalNav;
        }

        private async UniTask Refresh(int generation)
        {
            // Render the offline cache immediately, then replace it with the cloud snapshot.
            Bind(new UserProfile { TotalWins = LocalProfile.Wins, TotalLosses = LocalProfile.Losses,
                TotalReps = LocalProfile.TotalReps });
            _matches.Clear();
            RenderHistory();
            DrawActivity();
            var profile = await new UserProfileRepository().GetAsync();
            await UniTask.SwitchToMainThread();
            if (this == null || generation != _generation) return;
            if (profile.Exists) Bind(profile);
            var matches = await new MatchHistoryRepository().GetNextPageAsync();
            await UniTask.SwitchToMainThread();
            if (this == null || generation != _generation) return;
            _matches.AddRange(matches);
            RenderHistory();
            DrawActivity();
        }

        private void Bind(UserProfile p)
        {
            PlayerName.text = ProfileIdentityEditor.ResolveName(p.Exists ? p.DisplayName : "PLAYER");
            PlayerId.text = "LOCAL PROFILE";
            if (ServiceLocator.TryGet<FirebaseAuthService>(out var auth) && !string.IsNullOrEmpty(auth.Uid))
                PlayerId.text = "#" + auth.Uid.Substring(0, Math.Min(8, auth.Uid.Length)).ToUpperInvariant();
            Level.text = "<size=9>LVL</size>\n" + p.Level;
            long needed = LevelCalculator.XpForLevelUp(p.Level);
            Xp.text = (needed - LevelCalculator.XpRemainingToNext(p.Xp)) + "/" + needed;
            XpFill.fillAmount = p.LevelProgress;
            Wins.text = p.TotalWins.ToString("N0");
            WinRate.text = p.WinRatePercent + "%";
            Reps.text = p.TotalReps.ToString("N0");
        }

        private void Filter(int index)
        {
            _mode = index == 1 ? "pvp" : index == 2 ? "ghost" : "";
            for (int i = 0; i < Filters.Length; i++)
                Filters[i].image.sprite = i == index ? SelectedPlate : IdlePlate;
            RenderHistory();
        }

        private void DrawActivity()
        {
            int[] values = new int[7];
            foreach (var match in _matches)
            {
                int days = (DateTime.Today - match.CreatedAt.ToLocalTime().Date).Days;
                if (days >= 0 && days < 7) values[6 - days] += match.MyReps;
            }
            int max = 1, total = 0;
            foreach (int v in values) { max = Math.Max(max, v); total += v; }
            for (int i = 0; i < Bars.Length; i++)
                Bars[i].sizeDelta = new Vector2(Bars[i].sizeDelta.x, 4 + 82f * values[i] / max);
            Activity.text = total.ToString();
            ActivityNote.text = "7 DAYS / RECENT 20 MATCHES";
        }

        private void RenderHistory()
        {
            if (History == null || RowTemplate == null) return;
            foreach (var row in _rows) { row.SetActive(false); Destroy(row); }
            _rows.Clear();
            int index = 0;
            foreach (var m in _matches)
            {
                if (_mode != "" && m.Mode != _mode && !(_mode == "ghost" && m.Mode == "boss")) continue;
                var row = Instantiate(RowTemplate, History);
                row.name = "Match_" + index;
                row.SetActive(true);
                var rt = (RectTransform)row.transform;
                rt.anchoredPosition = new Vector2(0, -index * 82);
                row.transform.Find("Result").GetComponent<Image>().sprite = m.Won ? WinIcon : LossIcon;
                row.transform.Find("Opponent").GetComponent<TextMeshProUGUI>().text = "vs " +
                    (string.IsNullOrEmpty(m.OpponentName) ? (m.Mode == "ghost" || m.Mode == "boss" ? "BOSS" : "OPPONENT") : m.OpponentName);
                var score = row.transform.Find("Score").GetComponent<TextMeshProUGUI>();
                score.text = m.MyReps + " - " + m.OpponentReps;
                score.color = m.Won ? new Color32(207, 255, 0, 255) : new Color32(255, 75, 30, 255);
                row.transform.Find("Detail").GetComponent<TextMeshProUGUI>().text =
                    m.CreatedAt.ToLocalTime().ToString("dd MMM") + "  /  " + (m.Exercise ?? "pushups").ToUpperInvariant();
                row.transform.Find("Record").GetComponent<TextMeshProUGUI>().text = m.IsRecord ? "NEW RECORD" : "";
                _rows.Add(row);
                index++;
            }
            Empty.gameObject.SetActive(index == 0);
            Empty.text = "NO MATCHES YET\n<size=13>Finish a battle to start your history.</size>";
            var layout = History.GetComponentInParent<DashboardWidthFit>();
            if (layout != null) layout.DesignHeight = Math.Max(690, 545 + Math.Max(1, index) * 82);
        }
    }
}
