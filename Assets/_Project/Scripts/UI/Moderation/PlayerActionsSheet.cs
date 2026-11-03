using System;
using System.Collections.Generic;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// What a player can do about another player whose name is on the screen — report the name,
    /// report cheating, block — opened from the league, the friends list and a fight's result (App
    /// Store guideline 1.2). Also the list of blocked players in Settings. Built at runtime over
    /// whatever screen opened it, in the look of the shop's dialog; the server does the work
    /// (<see cref="ModerationClient"/>).
    /// </summary>
    public sealed class PlayerActionsSheet : MonoBehaviour
    {
        public sealed class Target
        {
            public string Uid, BotId, Name, Where;
            /// <summary>Set when the player is on this one's friends list.</summary>
            public Action RemoveFriend;
            /// <summary>A block changed: what the screen behind shows may have to change too.</summary>
            public Action Changed;
        }

        private const float ArtWidth = 390f, PanelWidth = 332f, ButtonHeight = 50f, ButtonGap = 10f;
        private static readonly Color PanelColor = new Color32(32, 63, 164, 255);
        private static readonly Color PlateColor = new Color32(46, 111, 242, 255);
        private static readonly Color DangerColor = new Color32(214, 58, 58, 255);
        private static readonly Color Muted = new Color32(196, 206, 255, 255);

        private TMP_FontAsset _font;
        private RectTransform _panel;
        private TextMeshProUGUI _status;
        private readonly List<Button> _buttons = new List<Button>();
        private bool _busy;
        private Target _target;
        private TextMeshProUGUI _blockLabel;
        private RectTransform _listContent;
        private ScrollRect _list;

        public static void Show(Component anchor, Target target)
        {
            if (anchor == null || target == null) return;
            Create(anchor).BuildActions(target);
        }

        public static void ShowBlockedList(Component anchor)
        {
            if (anchor == null) return;
            Create(anchor).BuildBlockedList();
        }

        /// <summary>Stretched over the whole canvas, dim behind, tapping outside closes.</summary>
        private static PlayerActionsSheet Create(Component anchor)
        {
            var canvas = anchor.GetComponentInParent<Canvas>(true).rootCanvas;
            var root = New("PlayerActionsSheet", canvas.transform);
            Fill(root);
            root.SetAsLastSibling();
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, .02f, .15f, .85f);
            var sheet = root.gameObject.AddComponent<PlayerActionsSheet>();
            var button = root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(sheet.Close);
            var sample = anchor.GetComponentInChildren<TMP_Text>(true);
            sheet._font = sample != null ? sample.font : TMP_Settings.defaultFontAsset;

            sheet._panel = New("Panel", root);
            sheet._panel.gameObject.AddComponent<Image>().color = PanelColor;
            sheet._panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // swallow taps
            float scale = Mathf.Min(1f, ((RectTransform)root).rect.width / ArtWidth);
            sheet._panel.localScale = Vector3.one * (scale > 0f ? scale : 1f);
            return sheet;
        }

        private void Close()
        {
            if (!_busy) Destroy(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        // ── Report and block ────────────────────────────────────────────────────────────────

        private void BuildActions(Target target)
        {
            _target = target;
            float y = 18f;
            var title = Label("Name", 23f, Color.white); // "Name": a player's name, never translated
            title.text = string.IsNullOrWhiteSpace(target.Name) ? "PLAYER" : target.Name;
            Place(title.rectTransform, 15f, y, PanelWidth - 30f, 34f);
            y += 44f;

            AddButton("REPORT NAME", PlateColor, ref y, () => Report(ModerationClient.ReasonName));
            AddButton("REPORT CHEATING", PlateColor, ref y, () => Report(ModerationClient.ReasonCheating));
            var block = AddButton("", DangerColor, ref y, ToggleBlock);
            _blockLabel = block.GetComponentInChildren<TextMeshProUGUI>(true);
            DrawBlock();
            if (target.RemoveFriend != null)
                AddButton("REMOVE FRIEND", PlateColor, ref y, () => { target.RemoveFriend(); Close(); });

            _status = Label("Status", 13f, Muted);
            _status.textWrappingMode = TextWrappingModes.Normal;
            Place(_status.rectTransform, 15f, y, PanelWidth - 30f, 40f);
            y += 48f;
            AddButton("CLOSE", PlateColor, ref y, Close);
            FitPanel(y + 8f);
        }

        private string BlockId => string.IsNullOrEmpty(_target.Uid) ? _target.BotId : _target.Uid;

        private void DrawBlock() => _blockLabel.text = ModerationClient.IsBlocked(BlockId) ? "UNBLOCK PLAYER" : "BLOCK PLAYER";

        private async void Report(string reason)
        {
            if (_busy) return;
            SetBusy(true);
            try
            {
                await ModerationClient.Report(_target.Uid, _target.BotId, _target.Name, reason, _target.Where);
                if (this == null) return;
                _status.text = "REPORT SENT. WE REVIEW REPORTS WITHIN 24 HOURS.";
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Moderation] report: " + e.Message);
                if (this != null) _status.text = "NO CONNECTION. TRY AGAIN.";
            }
            finally { if (this != null) SetBusy(false); }
        }

        private async void ToggleBlock()
        {
            if (_busy) return;
            bool blocked = !ModerationClient.IsBlocked(BlockId);
            SetBusy(true);
            try
            {
                await ModerationClient.SetBlocked(_target.Uid, _target.BotId, blocked, _target.Name);
                if (this == null) return;
                _status.text = blocked ? "BLOCKED. YOU WON'T MEET THIS PLAYER AGAIN." : "UNBLOCKED.";
                DrawBlock();
                _target.Changed?.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Moderation] block: " + e.Message);
                if (this != null) _status.text = "NO CONNECTION. TRY AGAIN.";
            }
            finally { if (this != null) SetBusy(false); }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            foreach (var button in _buttons) if (button != null) button.interactable = !busy;
        }

        // ── Blocked players ─────────────────────────────────────────────────────────────────

        private void BuildBlockedList()
        {
            const float listHeight = 300f;
            float y = 18f;
            var title = Label("Title", 23f, Color.white);
            title.text = "BLOCKED PLAYERS";
            Place(title.rectTransform, 15f, y, PanelWidth - 30f, 34f);
            y += 44f;

            var view = New("List", _panel);
            Place(view, 15f, y, PanelWidth - 30f, listHeight);
            view.gameObject.AddComponent<RectMask2D>();
            view.gameObject.AddComponent<Image>().color = Color.clear;
            _listContent = New("Content", view);
            _listContent.anchorMin = new Vector2(0f, 1f); _listContent.anchorMax = Vector2.one; _listContent.pivot = new Vector2(.5f, 1f);
            _listContent.sizeDelta = Vector2.zero;
            _list = view.gameObject.AddComponent<ScrollRect>();
            _list.content = _listContent; _list.horizontal = false; _list.viewport = view;
            _list.movementType = ScrollRect.MovementType.Clamped;

            _status = Label("Status", 15f, Muted);
            _status.textWrappingMode = TextWrappingModes.Normal;
            Place(_status.rectTransform, 15f, y + 110f, PanelWidth - 30f, 60f);
            _status.text = "Loading…";
            y += listHeight + 12f;
            AddButton("CLOSE", PlateColor, ref y, Close);
            FitPanel(y + 8f);
            LoadBlocked();
        }

        /// <summary>The bots blocked on this phone show at once; the account's list follows from the
        /// server. Offline, the phone's own list still stands.</summary>
        private async void LoadBlocked()
        {
            Render(ModerationClient.LocalPlayers(), false);
            try
            {
                var players = await ModerationClient.Load();
                if (this != null) Render(players, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Moderation] list: " + e.Message);
                if (this != null) _status.text = _listContent.childCount == 0 ? "NO CONNECTION. TRY AGAIN." : "";
            }
        }

        private void Render(BlockedPlayer[] players, bool final)
        {
            _status.text = players.Length > 0 ? "" : final ? "Nobody is blocked." : "Loading…";
            float y = 0f;
            foreach (Transform old in _listContent) Destroy(old.gameObject);
            foreach (var player in players) { AddBlockedRow(player, y); y += ButtonHeight + ButtonGap; }
            _listContent.sizeDelta = new Vector2(0f, y);
        }

        private void AddBlockedRow(BlockedPlayer player, float y)
        {
            const float width = PanelWidth - 30f, buttonWidth = 120f;
            var row = New("Blocked", _listContent);
            Place(row, 0f, y, width, ButtonHeight);
            var name = Label("Name", 16f, Color.white, row);
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.text = player.name;
            Place(name.rectTransform, 4f, 0f, width - buttonWidth - 12f, ButtonHeight);
            var unblock = MakeButton("UNBLOCK", PlateColor, row, async () =>
            {
                if (_busy) return;
                SetBusy(true);
                try
                {
                    await ModerationClient.SetBlocked(player.uid, player.botId, false);
                    if (this == null) return;
                    SetBusy(false);
                    LoadBlocked();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Moderation] unblock: " + e.Message);
                    if (this != null) { SetBusy(false); _status.text = "NO CONNECTION. TRY AGAIN."; }
                }
            });
            Place((RectTransform)unblock.transform, width - buttonWidth, 4f, buttonWidth, ButtonHeight - 8f);
        }

        // ── Building blocks ─────────────────────────────────────────────────────────────────

        private Button AddButton(string text, Color color, ref float y, Action act)
        {
            var button = MakeButton(text, color, _panel, act);
            Place((RectTransform)button.transform, 40f, y, PanelWidth - 80f, ButtonHeight);
            y += ButtonHeight + ButtonGap;
            return button;
        }

        private Button MakeButton(string text, Color color, Transform parent, Action act)
        {
            var rect = New("Button", parent);
            var face = rect.gameObject.AddComponent<DuelActionPlate>();
            face.Slope = 0f; face.Radius = 12f;
            face.color = color;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = face;
            button.onClick.AddListener(() => act());
            var label = Label("Label", 17f, Color.white, rect);
            label.text = text;
            label.enableAutoSizing = true; label.fontSizeMin = 10f; label.fontSizeMax = 17f;
            Fill(label.rectTransform);
            _buttons.Add(button);
            return button;
        }

        private TextMeshProUGUI Label(string name, float size, Color color, Transform parent = null)
        {
            var label = New(name, parent != null ? parent : _panel).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = _font;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private void FitPanel(float height)
        {
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(.5f, .5f);
            _panel.anchoredPosition = Vector2.zero;
            _panel.sizeDelta = new Vector2(PanelWidth, height);
        }

        private static RectTransform New(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>Top-left placement in the panel's own coordinates.</summary>
        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
