using System.Collections.Generic;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Shop presentation; avatar ownership and purchases stay in the shared collection.</summary>
    public sealed class ShopScreen : MonoBehaviour
    {
        public GameObject Overlay, PackDialog;
        public RectTransform Art, Viewport, Content;
        public ScrollRect Scroll;
        public AvatarCollectionScreen Collection;
        public Button Entry, Back, Home, Okay, DialogClose;
        public Button[] Offers, Info, Packs;
        public TextMeshProUGUI[] OfferActions;
        public TextMeshProUGUI Balance, PackTitle, PackPrice;
        public Image PackImage;
        public Sprite[] PackSprites;
        public static readonly int[] GemAmounts = { 30, 120, 360 };
        public static readonly string[] GemPrices = { "$1.99", "$5.99", "$5.99" };
        private int _openedFrame;
        private ShopEmoteSection _emotes;
        public const string NoticeSaveKey = "shop.seen_notices.v1";
        private ShopNoticeLedger _notices;
        private TrophyBadgeGraphic _noticeBadge;
        private float _nextNoticeRefresh;
        public bool HasUnseenUpdates => _notices != null && _notices.HasUnseen(CaptureNotices());
        public bool IsOpen => Overlay != null && Overlay.activeSelf;

        private void Awake()
        {
            _notices = new ShopNoticeLedger(PlayerPrefs.GetString(NoticeSaveKey, string.Empty), json =>
            {
                PlayerPrefs.SetString(NoticeSaveKey, json);
                PlayerPrefs.Save();
            });
            _noticeBadge = EnsureEntryBadge(Entry);
            Entry.onClick.AddListener(Show);
            Back.onClick.AddListener(Hide);
            Home.onClick.AddListener(Hide);
            Okay.onClick.AddListener(Hide);
            DialogClose.onClick.AddListener(ClosePack);
            for (int i = 0; i < Offers.Length; i++)
            {
                int index = i;
                Offers[i].onClick.AddListener(() => Preview(index));
                Info[i].onClick.AddListener(() => Preview(index));
            }
            for (int i = 0; i < Packs.Length; i++)
            {
                int index = i;
                Packs[i].onClick.AddListener(() => ShowPack(index));
            }
            HideUnlistedOffers();
            _emotes = ShopEmoteSection.Install(this);
            Hide();
            RefreshNoticeBadge();
        }

        /// <summary>Also used by scene authoring; the legacy circular info icon is retired.</summary>
        public static TrophyBadgeGraphic EnsureEntryBadge(Button entry)
        {
            var legacy = entry.transform.Find("InfoBadge");
            if (legacy != null) legacy.gameObject.SetActive(false);
            var badge = entry.transform.Find("ShopNoticeBadge") as RectTransform;
            if (badge == null)
            {
                badge = new GameObject("ShopNoticeBadge", typeof(RectTransform)).GetComponent<RectTransform>();
                badge.SetParent(entry.transform, false);
            }
            badge.anchorMin = badge.anchorMax = badge.pivot = Vector2.one;
            badge.anchoredPosition = new Vector2(7f, 9f);
            badge.sizeDelta = new Vector2(24f, 20f);
            var graphic = badge.GetComponent<TrophyBadgeGraphic>() ?? badge.gameObject.AddComponent<TrophyBadgeGraphic>();
            graphic.InfoOnly = true;
            graphic.raycastTarget = false;
            badge.gameObject.SetActive(false);
            return graphic;
        }

        /// <summary>Only goods shown in the shop and actual acquisitions generate notices.
        /// Balance changes, retired heroes and unavailable emote clips do not.</summary>
        public List<string> CaptureNotices()
        {
            var notices = new List<string>();
            for (int i = 0; i < Offers.Length; i++)
            {
                var offer = AvatarCatalog.At(i);
                if (AvatarCatalog.IsListed(i))
                    notices.Add($"offer:{offer.Id}:{offer.Kind}:{offer.Price}:{offer.StoreProductId}");
            }
            for (int i = 0; i < Packs.Length; i++)
                notices.Add($"gems:{GemAmounts[i]}:{GemPrices[i]}");
            foreach (var emote in EmoteCatalog.All)
            {
                if (emote.Clip == null) continue;
                notices.Add($"emote:{emote.Id}:{emote.Rarity}:{emote.Price}");
                if (!emote.Free && EmoteCatalog.Owns(emote)) notices.Add("owned-emote:" + emote.Id);
            }
            for (int i = 0; i < AvatarCatalog.All.Length; i++)
            {
                var offer = AvatarCatalog.At(i);
                if (AvatarCatalog.IsListed(i) && offer.Kind != AvatarPurchaseKind.Included && CaseRewards.OwnsAvatar(offer.Id))
                    notices.Add("owned-avatar:" + offer.Id);
            }
            return notices;
        }

        private void RefreshNoticeBadge()
        {
            if (_noticeBadge != null) _noticeBadge.gameObject.SetActive(!IsOpen && HasUnseenUpdates);
        }

        private void MarkNoticesSeen()
        {
            _notices?.MarkSeen(CaptureNotices());
            RefreshNoticeBadge();
        }

        /// <summary>Hides offers whose catalog slot is retired. When none are left, the
        /// SPECIAL OFFERS header (the sibling right above the first offer) goes too and every row
        /// below moves up into its space, so the shop never shows an empty section.</summary>
        private void HideUnlistedOffers()
        {
            bool anyListed = false;
            for (int i = 0; i < Offers.Length; i++)
            {
                bool listed = AvatarCatalog.IsListed(i);
                Offers[i].gameObject.SetActive(listed);
                anyListed |= listed;
            }
            if (anyListed || Offers.Length == 0) return;

            var first = Offers[0].transform;
            int headerIndex = first.GetSiblingIndex() - 1;
            int nextIndex = first.GetSiblingIndex() + 1;
            while (nextIndex < Content.childCount && System.Array.Exists(Offers, o => o.transform == Content.GetChild(nextIndex))) nextIndex++;
            if (first.parent != Content || headerIndex < 0 || nextIndex >= Content.childCount) return;

            var header = (RectTransform)Content.GetChild(headerIndex);
            float lift = header.anchoredPosition.y - ((RectTransform)Content.GetChild(nextIndex)).anchoredPosition.y;
            header.gameObject.SetActive(false);
            for (int s = nextIndex; s < Content.childCount; s++)
                ((RectTransform)Content.GetChild(s)).anchoredPosition += new Vector2(0, lift);
            Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0, Content.rect.height - lift));
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextNoticeRefresh)
            {
                _nextNoticeRefresh = Time.unscaledTime + .5f;
                RefreshNoticeBadge();
            }
            if (!IsOpen) return;
            Fit();
            if (Time.frameCount != _openedFrame && Input.GetKeyDown(KeyCode.Escape))
            {
                if (PackDialog.activeSelf) ClosePack();
                else Hide();
            }
        }

        public void Show()
        {
            _openedFrame = Time.frameCount;
            Collection.Hide();
            Overlay.SetActive(true);
            Overlay.transform.SetAsLastSibling();
            PackDialog.SetActive(false);
            Refresh();
            MarkNoticesSeen();
            Canvas.ForceUpdateCanvases();
            Fit();
            Scroll.StopMovement();
            Scroll.verticalNormalizedPosition = 1;
            for (int i = 0; i < Offers.Length; i++) Offers[i].GetComponent<UiTactile>()?.Reveal(.04f + i * .06f);
            for (int i = 0; i < Packs.Length; i++) Packs[i].GetComponent<UiTactile>()?.Reveal(.16f + i * .05f);
        }

        public void Hide()
        {
            if (IsOpen) MarkNoticesSeen();
            if (_emotes != null) _emotes.ClosePreview();
            PackDialog.SetActive(false);
            Overlay.SetActive(false);
            RefreshNoticeBadge();
        }
        public void ClosePack() => PackDialog.SetActive(false);

        public void Preview(int index)
        {
            Hide();
            Collection.ShowPreview(index, Show);
        }

        public void Refresh()
        {
            Balance.text = $"{CaseRewards.GemsBalance:N0}";
            if (_emotes != null) _emotes.Refresh();
            for (int i = 0; i < Offers.Length; i++)
                if (AvatarCatalog.IsListed(i))
                    OfferActions[i].text = Collection.IsLocked(i) ? AvatarCollectionScreen.PriceLabel(i)
                    : Collection.IsSelected(i) ? "SELECTED" : "OWNED";
        }

        public void ShowPack(int index)
        {
            PackTitle.text = GemAmounts[index] + " GEMS";
            PackPrice.text = GemPrices[index];
            PackImage.sprite = PackSprites[index];
            PackDialog.SetActive(true);
            PackDialog.GetComponentInChildren<UiTactile>()?.Reveal();
        }

        public void Fit()
        {
            var space = ((RectTransform)Art.parent).rect.size;
            float scale = Mathf.Max(.001f, space.x / 390f);
            float height = space.y / scale;
            Art.localScale = Vector3.one * scale;
            Art.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            Viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(1, height - 202));
            ((RectTransform)Okay.transform).anchoredPosition = new Vector2(140, -(height - 65));
        }
    }
}
