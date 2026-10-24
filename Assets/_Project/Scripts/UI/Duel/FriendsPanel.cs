using System;
using System.Collections.Generic;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// FRIENDS under SHOP on the main screen: everyone this player fought in a friend-code duel
    /// (the server keeps the list, functions/src/friends.js), who of them is online right now —
    /// a green count on the tile — and a FIGHT button per friend that opens a room and calls that
    /// friend into it, no code to read out. A friend's call to this player pops over BATTLE
    /// ("DENIS invites you [FIGHT]") and joins their room in one tap.
    /// The room itself stays <see cref="FriendDuelController"/>'s.
    /// </summary>
    public sealed class FriendsPanel : MonoBehaviour
    {
        private const float PollSec = 45f, RowHeight = 78f, RowGap = 12f, ListTop = 112f, SectionHeight = 31f;
        private static readonly Color Online = new Color32(82, 255, 2, 255);
        private static readonly Color Gold = new Color32(255, 210, 31, 255);
        private static readonly Color Blue = new Color32(46, 111, 242, 255);
        private static readonly Color Red = new Color32(232, 64, 64, 255);
        private static readonly Color Muted = new Color32(196, 206, 255, 255);
        /// <summary>SHOP's screen blue (ShopSceneSetup).</summary>
        private static readonly Color ShopBlue = new Color32(25, 74, 224, 255);
        /// <summary>The purple of SHOP's offer cards.</summary>
        private static readonly Color CardColor = new Color32(62, 38, 200, 255);

        private FriendDuelController _duel;
        private TMP_FontAsset _font;
        private PushStarsTheme _theme;
        private RectTransform _tile;
        private GameObject _badge;
        private TextMeshProUGUI _badgeLabel;
        private GameObject _overlay;
        private RectTransform _art, _view, _content;
        private ScrollRect _scroll;
        private Button _code;
        private TextMeshProUGUI _status, _empty;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private FriendEntry[] _friends = Array.Empty<FriendEntry>();
        private string _removing = "";
        private bool _loading, _loaded;
        private float _nextLoad = 2f;
        private IDisposable _inviteWatch;
        private float _nextWatch;
        private FriendInvite _invite;
        private string _bannerShownFor = "", _answered = "";
        private CallBanner _banner;

        public bool IsOpen => _overlay != null && _overlay.activeSelf;

        /// <summary>Puts FRIENDS under the SHOP tile and the friends sheet over the main screen.</summary>
        public static FriendsPanel Install(FriendDuelController duel)
        {
            var shop = FindAnyObjectByType<ShopScreen>(FindObjectsInactive.Include);
            var entry = shop != null ? shop.Entry : null;
            if (duel == null || entry == null) return null;
            var panel = duel.gameObject.GetComponent<FriendsPanel>();
            if (panel == null) panel = duel.gameObject.AddComponent<FriendsPanel>();
            panel.Build(duel, shop);
            return panel;
        }

        private void Build(FriendDuelController duel, ShopScreen shop)
        {
            if (_tile != null) return;
            _duel = duel;
            _font = duel.BattleCaption != null ? duel.BattleCaption.font : TMP_Settings.defaultFontAsset;
            _theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            BuildTile((RectTransform)shop.Entry.transform);
            BuildScreen(shop);
        }

        // ── Tile ────────────────────────────────────────────────────────────────────────────

        private void BuildTile(RectTransform shopTile)
        {
            _tile = New("FriendsTile", shopTile.parent);
            _tile.SetSiblingIndex(shopTile.GetSiblingIndex() + 1);
            _tile.anchorMin = shopTile.anchorMin; _tile.anchorMax = shopTile.anchorMax; _tile.pivot = shopTile.pivot;
            _tile.sizeDelta = shopTile.sizeDelta;
            // The icon crowns the plate by about half its height, as SHOP's does.
            _tile.anchoredPosition = shopTile.anchoredPosition - new Vector2(0f, shopTile.sizeDelta.y + 30f);

            var plate = NewImage("Plate", _tile, Color.white, Resources.Load<Sprite>("Friends/FriendsPlate"));
            if (plate.sprite == null) plate.color = Blue;
            Fill(plate.rectTransform);
            var button = _tile.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            button.onClick.AddListener(Open);

            // SHOP's own label, copied: the same font, material, size and place.
            var shopLabel = shopTile.Find("Plate/Label");
            TextMeshProUGUI label;
            if (shopLabel != null && shopLabel.GetComponent<TextMeshProUGUI>() != null)
            {
                label = Instantiate(shopLabel.gameObject, plate.transform, false).GetComponent<TextMeshProUGUI>();
                label.name = "Label";
            }
            else
            {
                label = NewText("Label", plate.rectTransform, 12f, Color.white, TextAlignmentOptions.Center);
                label.enableAutoSizing = true; label.fontSizeMin = 8f; label.fontSizeMax = 12f;
                var lr = label.rectTransform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = new Vector2(1f, 0f); lr.pivot = new Vector2(.5f, 0f);
                lr.sizeDelta = new Vector2(-6f, 16f); lr.anchoredPosition = new Vector2(0f, 6f);
            }
            label.text = "FRIENDS";

            var icon = NewImage("Icon", _tile, Color.white, Resources.Load<Sprite>("Friends/FriendsIcon"));
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var ir = icon.rectTransform;
            ir.anchorMin = ir.anchorMax = new Vector2(.5f, 1f);
            ir.sizeDelta = new Vector2(42f, 40f); ir.anchoredPosition = new Vector2(0f, -3f);
            if (icon.sprite == null) icon.gameObject.SetActive(false);

            // Green count of friends online right now, where SHOP keeps its notice badge.
            var badge = NewImage("OnlineBadge", _tile, Online, _theme != null ? _theme.CircleShape : null);
            badge.raycastTarget = false;
            var br = badge.rectTransform;
            br.anchorMin = br.anchorMax = br.pivot = Vector2.one;
            br.anchoredPosition = new Vector2(7f, 9f); br.sizeDelta = new Vector2(22f, 22f);
            var ring = badge.gameObject.AddComponent<Outline>();
            ring.effectColor = new Color(0f, 0f, 0f, .85f); ring.effectDistance = new Vector2(1.5f, -1.5f);
            _badgeLabel = NewText("Count", badge.rectTransform, 13f, new Color32(12, 40, 0, 255), TextAlignmentOptions.Center);
            Fill(_badgeLabel.rectTransform);
            _badge = badge.gameObject;
            _badge.SetActive(false);
        }

        // ── Screen ──────────────────────────────────────────────────────────────────────────

        /// <summary>A full screen like SHOP's: its blue and lightning pattern, its back and home
        /// buttons and title, its section bars, and its yellow button along the bottom.</summary>
        private void BuildScreen(ShopScreen shop)
        {
            var root = NewImage("Friends", shop.Overlay.transform.parent, ShopBlue, null);
            Fill(root.rectTransform);
            _overlay = root.gameObject;
            var pattern = shop.Overlay.transform.Find("HomeLightningPattern");
            if (pattern != null) Instantiate(pattern.gameObject, root.transform, false).SetActive(true);
            var safe = New("SafeArea", root.transform);
            Fill(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            _art = New("Art", safe);
            _art.anchorMin = _art.anchorMax = _art.pivot = new Vector2(.5f, 1f);
            _art.sizeDelta = new Vector2(390f, 780f);

            // SHOP's own header, copied: the same art at the same places.
            Clone(shop.Back, _art).onClick.AddListener(Hide);
            Clone(shop.Home, _art).onClick.AddListener(Hide);
            var shopTitle = shop.Art.Find("Title");
            if (shopTitle != null)
            {
                var title = Instantiate(shopTitle.gameObject, _art, false).GetComponent<TextMeshProUGUI>();
                title.name = "Title";
                title.text = "FRIENDS";
            }

            _status = NewText("Status", _art, 14f, Muted, TextAlignmentOptions.Center);
            _status.textWrappingMode = TextWrappingModes.Normal;
            Place(_status.rectTransform, 20f, 80f, 350f, 26f);

            _view = New("Viewport", _art);
            Place(_view, 0f, ListTop, 390f, 560f);
            _view.gameObject.AddComponent<RectMask2D>();
            _view.gameObject.AddComponent<Image>().color = Color.clear;
            _content = New("Content", _view);
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(.5f, 1f);
            _content.sizeDelta = Vector2.zero;
            _scroll = _view.gameObject.AddComponent<ScrollRect>();
            _scroll.content = _content; _scroll.horizontal = false; _scroll.viewport = _view;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            _empty = NewText("Empty", _view, 18f, Color.white, TextAlignmentOptions.Center);
            _empty.textWrappingMode = TextWrappingModes.Normal;
            _empty.text = "Fight a friend with a code once —\nthey'll stay here for one-tap duels.";
            Fill(_empty.rectTransform);
            _empty.rectTransform.offsetMin = new Vector2(30f, 0f); _empty.rectTransform.offsetMax = new Vector2(-30f, 0f);

            // A new friend still comes through a code: the friend-duel sheet. SHOP's OK, wider.
            _code = Clone(shop.Okay, _art);
            _code.name = "InviteByCode";
            var codeRect = (RectTransform)_code.transform;
            codeRect.sizeDelta = new Vector2(250f, codeRect.sizeDelta.y);
            codeRect.anchoredPosition = new Vector2(70f, codeRect.anchoredPosition.y);
            var codeLabel = _code.GetComponentInChildren<TextMeshProUGUI>(true);
            if (codeLabel != null)
            {
                codeLabel.text = "INVITE BY CODE";
                codeLabel.rectTransform.sizeDelta = new Vector2(codeRect.sizeDelta.x - 16f, codeLabel.rectTransform.sizeDelta.y);
                codeLabel.enableAutoSizing = true; codeLabel.fontSizeMin = 12f; codeLabel.fontSizeMax = codeLabel.fontSize;
            }
            _code.onClick.AddListener(() => { Hide(); _duel.Open(); });

            _overlay.SetActive(false);
        }

        private static Button Clone(Button source, RectTransform parent)
        {
            var copy = Instantiate(source.gameObject, parent, false).GetComponent<Button>();
            copy.name = source.name;
            copy.onClick = new Button.ButtonClickedEvent();
            return copy;
        }

        public void Open()
        {
            _removing = "";
            _overlay.SetActive(true);
            _overlay.transform.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();
            Fit();
            Render();
            _scroll.StopMovement();
            _scroll.verticalNormalizedPosition = 1f;
            Load();
        }

        public void Hide()
        {
            _removing = "";
            if (_overlay != null) _overlay.SetActive(false);
        }

        // As SHOP's: the 390-wide art scaled to the screen width, the list taking the height left.
        private void Fit()
        {
            var space = ((RectTransform)_art.parent).rect.size;
            float scale = Mathf.Max(.001f, space.x / 390f);
            float height = space.y / scale;
            _art.localScale = Vector3.one * scale;
            _art.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            _view.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(1f, height - ListTop - 92f));
            var code = (RectTransform)_code.transform;
            code.anchoredPosition = new Vector2(code.anchoredPosition.x, -(height - 65f));
        }

        // ── List ────────────────────────────────────────────────────────────────────────────

        private async void Load()
        {
            if (_loading || string.IsNullOrEmpty(FriendsClient.Uid)) return;
            _loading = true;
            _nextLoad = Time.unscaledTime + PollSec;
            if (!_loaded && IsOpen) { _status.text = "Loading…"; }
            try
            {
                var list = await FriendsClient.Load();
                if (this == null) return;
                _friends = list?.friends ?? Array.Empty<FriendEntry>();
                _loaded = true;
                if (IsOpen) _status.text = "";
            }
            catch (Exception e)
            {
                if (this == null) return;
                if (IsOpen) _status.text = FriendsClient.Explain(e);
                Debug.LogWarning("[Friends] load: " + e.Message);
            }
            finally { if (this != null) _loading = false; }
            if (this == null) return;
            RefreshBadge();
            if (IsOpen) Render();
        }

        private void RefreshBadge()
        {
            int online = 0;
            foreach (var f in _friends) if (f.online) online++;
            _badge.SetActive(online > 0);
            _badgeLabel.text = online > 9 ? "9+" : online.ToString();
        }

        private void Render()
        {
            foreach (var row in _rows) Destroy(row);
            _rows.Clear();
            _empty.gameObject.SetActive(_loaded && _friends.Length == 0);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // The server sends the ones online first: a yellow bar over them, a white one over the rest.
            float y = 8f;
            for (int i = 0; i < _friends.Length; i++)
            {
                bool first = i == 0 || _friends[i - 1].online != _friends[i].online;
                if (first)
                {
                    _rows.Add(Section(_friends[i].online ? "ONLINE NOW" : i == 0 ? "FRIENDS" : "OTHER FRIENDS", y, _friends[i].online));
                    y += SectionHeight + 12f;
                }
                _rows.Add(BuildRow(_friends[i], y, now));
                y += RowHeight + RowGap;
            }
            _content.sizeDelta = new Vector2(0f, y + 8f);
        }

        /// <summary>SHOP's section bar ("SPECIAL OFFERS").</summary>
        private GameObject Section(string title, float y, bool yellow)
        {
            var rect = New(title, _content);
            Place(rect, 23f, y, 344f, SectionHeight);
            var bar = rect.gameObject.AddComponent<ShopSectionGraphic>();
            bar.raycastTarget = false;
            var label = NewText("Label", rect, 17f, yellow ? new Color32(255, 224, 0, 255) : Color.white, TextAlignmentOptions.Left);
            label.fontSharedMaterial = _font.material;
            Place(label.rectTransform, 12f, 1f, 326f, 28f);
            label.text = title;
            return rect.gameObject;
        }

        private GameObject BuildRow(FriendEntry friend, float y, long now)
        {
            var row = New("Friend", _content);
            Place(row, 20f, y, 350f, RowHeight);
            // An offer card's purple plate, edged and shadowed like the game's buttons.
            var face = row.gameObject.AddComponent<DuelActionPlate>();
            face.Slope = 0f; face.Radius = 14f;
            face.color = CardColor;
            // Tapping the row (not FIGHT) offers to remove the friend.
            row.gameObject.AddComponent<Button>().onClick.AddListener(() =>
            { _removing = _removing == friend.uid ? "" : friend.uid; Render(); });

            // Avatar: the hero's head when it has one, else the first letter on a colour of the name.
            var avatar = NewImage("Avatar", row, Tint(friend.uid), _theme != null ? _theme.CircleShape : null);
            avatar.raycastTarget = false;
            var ar = avatar.rectTransform;
            ar.anchorMin = ar.anchorMax = new Vector2(0f, .5f);
            ar.anchoredPosition = new Vector2(36f, 0f); ar.sizeDelta = new Vector2(52f, 52f);
            var head = AvatarCatalog.At(friend.hero) is { } offer && !string.IsNullOrEmpty(offer.HeadIcon)
                ? Resources.Load<Sprite>(offer.HeadIcon) : null;
            if (head != null)
            {
                var headImage = NewImage("Head", ar, Color.white, head);
                headImage.preserveAspect = true; headImage.raycastTarget = false;
                Fill(headImage.rectTransform);
            }
            else
            {
                var letter = NewText("Initial", ar, 24f, Color.white, TextAlignmentOptions.Center);
                letter.name = "Name"; // the initial is the friend's, never translated
                letter.text = string.IsNullOrEmpty(friend.name) ? "?" : friend.name.Substring(0, 1).ToUpperInvariant();
                letter.fontSharedMaterial = _font.material;
                Fill(letter.rectTransform);
            }
            var dot = NewImage("Presence", ar, friend.online ? Online : new Color32(90, 98, 120, 255), _theme != null ? _theme.CircleShape : null);
            dot.raycastTarget = false;
            var dr = dot.rectTransform;
            dr.anchorMin = dr.anchorMax = new Vector2(1f, 0f);
            dr.anchoredPosition = new Vector2(-6f, 6f); dr.sizeDelta = new Vector2(15f, 15f);
            var dotRing = dot.gameObject.AddComponent<Outline>();
            dotRing.effectColor = CardColor; dotRing.effectDistance = new Vector2(2f, -2f);

            var name = NewText("Name", row, 18f, Color.white, TextAlignmentOptions.BottomLeft);
            name.text = friend.name;
            var nr = name.rectTransform;
            nr.anchorMin = new Vector2(0f, .5f); nr.anchorMax = new Vector2(1f, 1f);
            nr.offsetMin = new Vector2(72f, 2f); nr.offsetMax = new Vector2(-104f, -10f);

            var status = NewText("Status", row, 13f, friend.online ? Online : Muted, TextAlignmentOptions.TopLeft);
            status.text = Seen(friend, now);
            status.enableAutoSizing = true; status.fontSizeMin = 10f; status.fontSizeMax = 13f;
            var sr = status.rectTransform;
            sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, .5f);
            sr.offsetMin = new Vector2(72f, 10f); sr.offsetMax = new Vector2(-164f, -2f);

            var trophies = NewText("Trophies", row, 13f, Gold, TextAlignmentOptions.TopRight);
            trophies.name = "Name"; // a number
            trophies.text = friend.trophies.ToString();
            trophies.overflowMode = TextOverflowModes.Overflow;
            trophies.enableAutoSizing = true; trophies.fontSizeMin = 10f; trophies.fontSizeMax = 13f;
            var tr = trophies.rectTransform;
            tr.anchorMin = new Vector2(1f, 0f); tr.anchorMax = new Vector2(1f, .5f);
            tr.offsetMin = new Vector2(-162f, 10f); tr.offsetMax = new Vector2(-122f, -2f);
            if (_theme != null && _theme.IconCup != null)
            {
                var cup = NewImage("Cup", row, Color.white, _theme.IconCup);
                cup.preserveAspect = true; cup.raycastTarget = false;
                var cr = cup.rectTransform;
                cr.anchorMin = cr.anchorMax = new Vector2(1f, .5f);
                cr.anchoredPosition = new Vector2(-112f, -12f); cr.sizeDelta = new Vector2(16f, 16f);
            }

            bool removing = _removing == friend.uid;
            bool calling = _invite != null && _invite.FromUid == friend.uid && _invite.Live(now) && _answered != _invite.MatchId;
            var action = New("Fight", row);
            action.anchorMin = action.anchorMax = new Vector2(1f, .5f);
            action.anchoredPosition = new Vector2(-50f, 1f); action.sizeDelta = new Vector2(88f, 44f);
            var actionFace = action.gameObject.AddComponent<DuelActionPlate>();
            actionFace.Slope = CallBanner.Slope;
            actionFace.color = removing ? Red : calling ? Online : Gold;
            var actionButton = action.gameObject.AddComponent<Button>();
            actionButton.targetGraphic = actionFace;
            var actionLabel = NewText("Label", action, 18f, Color.white, TextAlignmentOptions.Midline);
            CallBanner.StyleFight(actionLabel, _font);
            actionLabel.text = removing ? "REMOVE" : calling ? "JOIN" : "FIGHT";
            actionLabel.enableAutoSizing = true; actionLabel.fontSizeMin = 11f; actionLabel.fontSizeMax = 18f;
            if (removing) actionButton.onClick.AddListener(() => Remove(friend));
            else if (calling) { var invite = _invite; actionButton.onClick.AddListener(() => Accept(invite)); }
            else actionButton.onClick.AddListener(() => Fight(friend));
            return row.gameObject;
        }

        private static string Seen(FriendEntry friend, long now)
        {
            if (friend.online) return Localization.Text("Online");
            if (friend.lastSeenMs <= 0) return Localization.Text("Offline");
            long minutes = Math.Max(1, (now - friend.lastSeenMs) / 60000);
            if (minutes < 60) return string.Format(Localization.Text("Seen {0} min ago"), minutes);
            if (minutes < 60 * 24) return string.Format(Localization.Text("Seen {0} h ago"), minutes / 60);
            return string.Format(Localization.Text("Seen {0} d ago"), minutes / (60 * 24));
        }

        private static Color Tint(string seed)
        {
            uint h = 2166136261;
            foreach (char c in seed ?? "") h = (h ^ c) * 16777619;
            return Color.HSVToRGB((h % 360) / 360f, .55f, .85f);
        }

        private void Fight(FriendEntry friend)
        {
            Hide();
            _duel.CallFriend(friend.uid, friend.name);
        }

        private async void Remove(FriendEntry friend)
        {
            _removing = "";
            _friends = Array.FindAll(_friends, f => f.uid != friend.uid);
            Render(); RefreshBadge();
            try { await FriendsClient.Remove(friend.uid); }
            catch (Exception e)
            {
                if (this == null) return;
                _status.text = FriendsClient.Explain(e);
                Load();
            }
        }

        // ── A friend's call ─────────────────────────────────────────────────────────────────

        private void WatchInvite()
        {
            if (_inviteWatch != null || Time.unscaledTime < _nextWatch) return;
            _nextWatch = Time.unscaledTime + 2f;
            _inviteWatch = FriendsClient.WatchInvite(invite => _pending = invite ?? NoInvite);
        }

        // Firestore answers off the main thread's frame: the call is taken over in Update.
        private static readonly FriendInvite NoInvite = new FriendInvite("", "", 0, 0, "", "", 0, 0);
        private volatile FriendInvite _pending;

        private void TakeInvite(FriendInvite invite)
        {
            _invite = invite == NoInvite ? null : invite;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_invite == null || !_invite.Live(now) || _invite.FromUid == FriendsClient.Uid) { HideBanner(); return; }
            if (IsOpen) Render();
            ShowBanner(false);
        }

        private void ShowBanner(bool again)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var invite = _invite;
            if (invite == null || !invite.Live(now) || _answered == invite.MatchId) return;
            if (!again && _bannerShownFor == invite.MatchId) return;
            // Already in this room (or in another one): the room sheet says what is going on.
            if (_duel.HasRoom) return;
            _bannerShownFor = invite.MatchId;
            if (_banner == null)
            {
                _banner = CallBanner.Create((RectTransform)_duel.BattleButton.transform, _font, () => Accept(_invite));
                _banner.name = "FriendCallBubble";
            }
            // A stranger's search may already be up there: a friend's call is the one to show.
            foreach (var other in _duel.BattleButton.GetComponentsInChildren<CallBanner>(true))
                if (other != _banner) other.Hide();
            string name = string.IsNullOrEmpty(invite.FromName) ? "FRIEND" : invite.FromName;
            _banner.Show(string.Format(Localization.Text("{0} invites you"), $"<color=#FFD21F>{name}</color>"));
        }

        private void HideBanner()
        {
            if (_banner != null && _banner.gameObject.activeSelf) _banner.Hide();
        }

        private void Accept(FriendInvite invite)
        {
            if (invite == null) return;
            _answered = invite.MatchId;
            HideBanner(); Hide();
            _duel.AcceptCall(invite.Code, invite.FromName);
        }

        // ── Loop ────────────────────────────────────────────────────────────────────────────

        private void Update()
        {
            if (_tile == null) return;
            var pending = _pending;
            if (pending != null) { _pending = null; TakeInvite(pending); }
            WatchInvite();
            if (Time.unscaledTime >= _nextLoad && _tile.gameObject.activeInHierarchy) Load();
            if (_overlay.activeSelf)
            {
                Fit();
                if (Input.GetKeyDown(KeyCode.Escape)) Hide();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) return;
            // Back from the background (perhaps through the push): fresh online marks, and the
            // call shown again if it still stands.
            _nextLoad = Time.unscaledTime + .5f;
            ShowBanner(true);
        }

        private void OnDestroy()
        {
            _inviteWatch?.Dispose();
            _inviteWatch = null;
        }

        // ── Building blocks ─────────────────────────────────────────────────────────────────

        private static RectTransform New(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static Image NewImage(string name, Transform parent, Color color, Sprite sprite)
        {
            var image = New(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            return image;
        }

        private TextMeshProUGUI NewText(string name, Transform parent, float size, Color color, TextAlignmentOptions align)
        {
            var label = New(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = _font;
            label.fontSize = size;
            label.fontStyle = FontStyles.Bold;
            label.color = color;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static void Fill(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Top-left placement in SHOP's 390-wide art coordinates.</summary>
        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }
    }
}
