using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// The parts of the search screen that make a slow launch bearable, built at runtime under the
    /// overlay's content (the serialized Main scene stays as it is):
    ///   • a pill at the top — "notify me when an opponent is found" (<see cref="Bell"/>);
    ///   • a status line under the title — time searching, players today, "we called a player";
    ///   • an offer card where the tip was — an opponent at the player's level (a recorded set,
    ///     shown as the player it is) or the player's own shadow, with FIGHT.
    /// Only layout and copy live here; <see cref="SearchOpponentController"/> decides what to show.
    /// </summary>
    public sealed class SearchLobbyView : MonoBehaviour
    {
        public enum Bell { Off, On, Blocked }

        public event Action BellTapped, OfferTapped;

        private static readonly Color Muted = new Color32(150, 158, 190, 255);
        private static readonly Color Soft = new Color32(202, 210, 246, 255);
        private static readonly Color Card = new Color32(43, 47, 58, 255);
        private static readonly Color CardOn = new Color32(58, 50, 26, 255);
        private static readonly Color Gold = new Color32(255, 210, 31, 255);

        private RectTransform _content, _offer, _tip;
        private DuelActionPlate _bellPlate;
        private Image _bellIcon;
        private TextMeshProUGUI _bellLabel, _status, _offerHeader, _offerName, _offerDetail, _trophies, _fightLabel;
        private Image _cup;
        private CanvasGroup _offerGroup;
        private float _offerShownAt = -1f;

        public static SearchLobbyView Ensure(GameObject overlay)
        {
            var content = overlay.transform.Find("SafeArea/Content") as RectTransform;
            if (content == null) return null;
            var view = content.GetComponent<SearchLobbyView>();
            if (view == null)
            {
                view = content.gameObject.AddComponent<SearchLobbyView>();
                view.Build(content);
            }
            return view;
        }

        /// <summary>Back to how a fresh search starts: no offer yet, the tip in its place.</summary>
        public void ResetView()
        {
            SetStatus("");
            HideOffer();
        }

        public void SetBell(Bell state)
        {
            if (_bellPlate == null) return;
            _bellPlate.color = state == Bell.On ? CardOn : Card;
            _bellIcon.color = state == Bell.On ? Gold : state == Bell.Blocked ? Muted : Soft;
            _bellLabel.color = state == Bell.On ? Gold : state == Bell.Blocked ? Muted : Soft;
            _bellLabel.text = state == Bell.On ? "We'll notify you — you can close the game"
                : state == Bell.Blocked ? "Allow notifications in Settings" : "Notify me when an opponent is found";
        }

        public void SetStatus(string text)
        {
            if (_status == null || _status.text == text) return;
            _status.text = text;
        }

        public bool OfferShown => _offerShownAt >= 0f;

        /// <param name="trophies">The opponent's trophies, shown with the cup after the detail; null hides them.</param>
        public void ShowOffer(string header, string name, string detail, int? trophies, string button)
        {
            _offerHeader.text = header;
            _offerName.text = name;
            _offerDetail.text = detail;
            _fightLabel.text = button;
            bool cup = trophies.HasValue && _cup.sprite != null;
            _cup.gameObject.SetActive(cup);
            _trophies.gameObject.SetActive(cup);
            if (cup) _trophies.text = trophies.Value.ToString("N0");
            if (_tip != null) _tip.gameObject.SetActive(false);
            if (!OfferShown)
            {
                _offerShownAt = Time.unscaledTime;
                _offer.gameObject.SetActive(true);
            }
        }

        public void HideOffer()
        {
            _offerShownAt = -1f;
            if (_offer != null) _offer.gameObject.SetActive(false);
            if (_tip != null) _tip.gameObject.SetActive(true);
        }

        // Slides up and fades in once, without a tween dependency: an offer, not an interruption.
        private void Update()
        {
            if (!OfferShown || _offerGroup == null) return;
            float t = Mathf.Clamp01((Time.unscaledTime - _offerShownAt) / .35f);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            _offerGroup.alpha = ease;
            _offer.anchoredPosition = new Vector2(0f, 164f - 24f * (1f - ease));
        }

        // The cup follows the detail as it is drawn — after localization, in whatever language.
        private void LateUpdate()
        {
            if (_cup == null || !_cup.gameObject.activeSelf) return;
            float x = 20f + Mathf.Min(150f, _offerDetail.preferredWidth) + 12f;
            _cup.rectTransform.anchoredPosition = new Vector2(x, -71f);
            _trophies.rectTransform.anchoredPosition = new Vector2(x + 22f, -70f);
        }

        private void Build(RectTransform content)
        {
            _content = content;
            _tip = content.Find("TipBlock") as RectTransform;
            var title = content.Find("Title")?.GetComponent<TextMeshProUGUI>();
            var font = title != null ? title.font : TMP_Settings.defaultFontAsset;
            var clean = title != null ? title.fontSharedMaterial : null;

            // ── Notify pill ─────────────────────────────────────────────────────────────────
            var bell = Rect("NotifyPill", content, new Vector2(.5f, 1f), new Vector2(0f, -30f), new Vector2(340f, 44f));
            _bellPlate = bell.gameObject.AddComponent<DuelActionPlate>();
            _bellPlate.color = Card;
            var button = bell.gameObject.AddComponent<Button>();
            button.targetGraphic = _bellPlate;
            button.onClick.AddListener(() => BellTapped?.Invoke());
            _bellIcon = Rect("Bell", bell, new Vector2(0f, .5f), new Vector2(26f, 1f), new Vector2(20f, 20f)).gameObject.AddComponent<Image>();
            _bellIcon.sprite = Resources.Load<Sprite>("SearchBell");
            _bellIcon.preserveAspect = true;
            _bellIcon.raycastTarget = false;
            _bellLabel = Label("Label", bell, font, clean, 14f, Soft, TextAlignmentOptions.MidlineLeft);
            // Copy length differs a lot between languages: shrink a little rather than cut it.
            _bellLabel.enableAutoSizing = true;
            _bellLabel.fontSizeMin = 11f;
            _bellLabel.fontSizeMax = 14f;
            Stretch(_bellLabel.rectTransform, 44f, 14f);

            // ── Status under the title ─────────────────────────────────────────────────────
            _status = Label("SearchStatus", content, font, clean, 15f, Muted, TextAlignmentOptions.Center);
            var status = _status.rectTransform;
            status.anchorMin = new Vector2(0f, 1f); status.anchorMax = new Vector2(1f, 1f); status.pivot = new Vector2(.5f, 1f);
            status.anchoredPosition = new Vector2(0f, -112f);
            status.sizeDelta = new Vector2(-32f, 24f);

            // ── Offer card (where the tip was) ─────────────────────────────────────────────
            _offer = Rect("OfferCard", content, new Vector2(.5f, 0f), new Vector2(0f, 164f), new Vector2(350f, 112f));
            _offer.pivot = new Vector2(.5f, 0f);
            _offerGroup = _offer.gameObject.AddComponent<CanvasGroup>();
            var plate = _offer.gameObject.AddComponent<DuelActionPlate>();
            plate.color = Card;
            plate.raycastTarget = false;
            _offerHeader = Label("Header", _offer, font, clean, 12f, Gold, TextAlignmentOptions.TopLeft);
            Place(_offerHeader.rectTransform, new Vector2(20f, -16f), new Vector2(200f, 18f));
            _offerName = Label("Name", _offer, font, clean, 21f, Color.white, TextAlignmentOptions.TopLeft);
            _offerName.fontStyle = FontStyles.Bold;
            Place(_offerName.rectTransform, new Vector2(20f, -38f), new Vector2(200f, 28f));
            _offerDetail = Label("Detail", _offer, font, clean, 14f, Muted, TextAlignmentOptions.TopLeft);
            Place(_offerDetail.rectTransform, new Vector2(20f, -70f), new Vector2(200f, 22f));
            _cup = Rect("Cup", _offer, Vector2.zero, Vector2.zero, new Vector2(18f, 18f)).gameObject.AddComponent<Image>();
            Place(_cup.rectTransform, new Vector2(0f, -70f), new Vector2(18f, 18f));
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            _cup.sprite = theme != null ? theme.IconCup : null;
            _cup.preserveAspect = true;
            _cup.raycastTarget = false;
            _trophies = Label("Trophies", _offer, font, clean, 14f, Gold, TextAlignmentOptions.TopLeft);
            Place(_trophies.rectTransform, new Vector2(0f, -70f), new Vector2(70f, 22f));

            var fight = Rect("Fight", _offer, new Vector2(1f, .5f), new Vector2(-70f, 0f), new Vector2(118f, 58f));
            var face = fight.gameObject.AddComponent<DuelActionPlate>();
            face.color = Gold;
            var tap = fight.gameObject.AddComponent<Button>();
            tap.targetGraphic = face;
            tap.onClick.AddListener(() => OfferTapped?.Invoke());
            // The CTA keeps the font's own outlined material, like START on the main screen.
            _fightLabel = Label("Label", fight, font, null, 22f, Color.white, TextAlignmentOptions.Center);
            _fightLabel.fontStyle = FontStyles.Bold;
            Stretch(_fightLabel.rectTransform, 6f, 6f);
            _offer.gameObject.SetActive(false);
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        private static TextMeshProUGUI Label(string name, Transform parent, TMP_FontAsset font, Material material,
            float size, Color color, TextAlignmentOptions align)
        {
            var label = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            label.transform.SetParent(parent, false);
            label.font = font;
            if (material != null) label.fontSharedMaterial = material;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
            return label;
        }

        private static void Stretch(RectTransform rt, float left, float right)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, 0f); rt.offsetMax = new Vector2(-right, 0f);
        }

        private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = topLeft;
            rt.sizeDelta = size;
        }
    }
}
