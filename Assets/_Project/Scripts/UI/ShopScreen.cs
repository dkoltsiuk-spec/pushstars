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
        public static readonly int[] GemAmounts = System.Array.ConvertAll(StoreCatalog.GemPacks, p => p.Gems);
        public static readonly string[] GemPrices = System.Array.ConvertAll(StoreCatalog.GemPacks, p => p.FallbackPrice);
        private TextMeshProUGUI _packStatus, _restoreStatus;
        private int _showingPack;
        private bool _packBuying;
        private int _openedFrame;
        // Catalog slot behind each offer card: the authored cards first, then one per hero on sale.
        private int[] _offerSlots;
        private const float OfferColumn = 184f, OfferRowGap = 18f;
        public const int OfferPreviewSlot = 20;
        private ShopEmoteSection _emotes;
        private ShopArenaSection _arenas;
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
            AddHeroOffers();
            Entry.onClick.AddListener(Show);
            Back.onClick.AddListener(Hide);
            Home.onClick.AddListener(Hide);
            Okay.onClick.AddListener(Hide);
            for (int i = 0; i < Offers.Length; i++)
            {
                int index = i;
                Offers[i].onClick.AddListener(() => Preview(_offerSlots[index]));
                Info[i].onClick.AddListener(() => Preview(_offerSlots[index]));
            }
            for (int i = 0; i < Packs.Length; i++)
            {
                int index = i;
                Packs[i].onClick.AddListener(() => ShowPack(index));
            }
            LayoutOffers();
            _arenas = ShopArenaSection.Install(this);
            if (EmoteCatalog.Enabled) _emotes = ShopEmoteSection.Install(this);
            HideGemPacks();
            BuildPackBuying();
            if (StoreAccess.RealMoney) AddRestoreButton();
            IapStore.Changed += StoreChanged;
            IapStore.Start();
            Hide();
            RefreshNoticeBadge();
        }

        private void OnDestroy() => IapStore.Changed -= StoreChanged;

        private void StoreChanged()
        {
            RefreshPackPrices();
            if (IsOpen) Refresh();
        }

        private void RefreshPackPrices()
        {
            for (int i = 0; i < Packs.Length && i < StoreCatalog.GemPacks.Length; i++)
            {
                var price = Packs[i].transform.Find("Price")?.GetComponent<TextMeshProUGUI>();
                if (price != null) price.text = PackPriceText(i);
            }
        }

        private static string PackPriceText(int index) =>
            IapStore.PriceOf(StoreCatalog.GemPacks[index].ProductId, StoreCatalog.GemPacks[index].FallbackPrice);

        /// <summary>The pack dialog's button buys, and tapping outside the panel closes it, like the
        /// arena dialog. Done after the arena section has copied the dialog.</summary>
        private void BuildPackBuying()
        {
            var panel = PackDialog.transform.Find("Panel");
            _packStatus = panel.Find("Availability").GetComponent<TextMeshProUGUI>();
            panel.Find("Description").gameObject.SetActive(false);
            var dim = PackDialog.AddComponent<Button>();
            dim.transition = Selectable.Transition.None;
            dim.onClick.AddListener(ClosePack);
            panel.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // swallow taps
            DialogClose.onClick.AddListener(BuyShownPack);
            DialogClose.GetComponentInChildren<TextMeshProUGUI>(true).text = "BUY";
        }

        private void BuyShownPack()
        {
            if (_packBuying) return;
            _packBuying = true;
            DialogClose.interactable = false;
            _packStatus.text = "";
            IapStore.Buy(StoreCatalog.GemPacks[_showingPack].ProductId, (outcome, text) =>
            {
                if (this == null) return;
                _packBuying = false;
                DialogClose.interactable = true;
                if (outcome == IapOutcome.Bought) { ClosePack(); Refresh(); }
                else _packStatus.text = text;
            });
        }

        /// <summary>Under the gem packs, in the list: Apple asks for it where heroes are sold for money.</summary>
        private void AddRestoreButton()
        {
            var restore = Instantiate(Okay.gameObject, Content, false).GetComponent<Button>();
            restore.name = "RestorePurchases";
            restore.onClick = new Button.ButtonClickedEvent();
            var rect = (RectTransform)restore.transform;
            rect.sizeDelta = new Vector2(250f, rect.sizeDelta.y);
            float top = Content.rect.height + 6f;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(70f, -top);
            var label = restore.GetComponentInChildren<TextMeshProUGUI>(true);
            label.text = "RESTORE PURCHASES";
            label.rectTransform.sizeDelta = new Vector2(rect.sizeDelta.x - 16f, label.rectTransform.sizeDelta.y);
            label.enableAutoSizing = true; label.fontSizeMin = 12f; label.fontSizeMax = label.fontSize;

            _restoreStatus = Instantiate(Balance, Content, false);
            _restoreStatus.name = "RestoreStatus";
            var statusRect = _restoreStatus.rectTransform;
            statusRect.anchorMin = statusRect.anchorMax = statusRect.pivot = new Vector2(0f, 1f);
            statusRect.anchoredPosition = new Vector2(20f, -(top + rect.sizeDelta.y + 4f));
            statusRect.sizeDelta = new Vector2(350f, 22f);
            _restoreStatus.alignment = TextAlignmentOptions.Center;
            _restoreStatus.enableAutoSizing = true; _restoreStatus.fontSizeMin = 10f; _restoreStatus.fontSizeMax = 14f;
            _restoreStatus.text = "";
            Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, top + rect.sizeDelta.y + 34f);

            restore.onClick.AddListener(() =>
            {
                restore.interactable = false;
                _restoreStatus.text = "";
                IapStore.Restore((ok, text) =>
                {
                    if (this == null) return;
                    restore.interactable = true;
                    _restoreStatus.text = text;
                    Refresh();
                });
            });
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
            foreach (int i in _offerSlots ?? System.Array.Empty<int>())
            {
                var offer = AvatarCatalog.At(i);
                if (AvatarCatalog.IsListed(i))
                    notices.Add($"offer:{offer.Id}:{offer.Kind}:{offer.Price}:{offer.StoreProductId}");
            }
            for (int i = 0; i < (StoreAccess.RealMoney ? Packs.Length : 0); i++)
                notices.Add($"gems:{GemAmounts[i]}:{GemPrices[i]}");
            notices.AddRange(ShopArenaSection.Notices());
            foreach (var emote in EmoteCatalog.Enabled ? EmoteCatalog.All : System.Array.Empty<EmoteDef>())
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

        /// <summary>Heroes sold for gems or money get an offer card each, copied from the authored
        /// one: the catalog decides what the shop sells, the scene only says what a card looks like.</summary>
        private void AddHeroOffers()
        {
            var slots = new List<int>();
            for (int i = 0; i < Offers.Length; i++) slots.Add(i);
            if (Offers.Length > 0)
                for (int i = Offers.Length; i < AvatarCatalog.All.Length; i++)
                {
                    var offer = AvatarCatalog.At(i);
                    if (!AvatarCatalog.IsListed(i) || (offer.Kind != AvatarPurchaseKind.Gems && offer.Kind != AvatarPurchaseKind.Dollars)) continue;
                    var prefab = AvatarCatalog.LoadPrefab(i);
                    if (prefab == null) continue;
                    var template = Offers[0];
                    var card = Instantiate(template, template.transform.parent);
                    card.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + slots.Count);
                    card.name = offer.Id + "Offer";
                    card.transform.Find("Name").GetComponent<TextMeshProUGUI>().text = offer.Name;
                    var preview = card.GetComponentInChildren<AvatarCardPreview>(true);
                    preview.Slot = OfferPreviewSlot + i;
                    preview.SetPrefab(prefab);
                    System.Array.Resize(ref Offers, Offers.Length + 1);
                    System.Array.Resize(ref Info, Offers.Length);
                    System.Array.Resize(ref OfferActions, Offers.Length);
                    Offers[Offers.Length - 1] = card;
                    Info[Offers.Length - 1] = card.transform.Find("Info").GetComponent<Button>();
                    OfferActions[Offers.Length - 1] = card.transform.Find("Action").GetComponent<TextMeshProUGUI>();
                    slots.Add(i);
                }
            _offerSlots = slots.ToArray();
        }

        /// <summary>Lays the listed offers out two to a row from where the first one was authored
        /// and moves every row below to follow. Offers whose catalog slot is retired are hidden;
        /// when none are left, the SPECIAL OFFERS header (the sibling right above the first offer)
        /// goes too, so the shop never shows an empty section.</summary>
        private void LayoutOffers()
        {
            if (Offers.Length == 0) return;
            var first = (RectTransform)Offers[0].transform;
            Vector2 origin = first.anchoredPosition;
            float rowPitch = first.rect.height + OfferRowGap;
            int shown = 0;
            for (int i = 0; i < Offers.Length; i++)
            {
                bool listed = AvatarCatalog.IsListed(_offerSlots[i]);
                Offers[i].gameObject.SetActive(listed);
                if (!listed) continue;
                ((RectTransform)Offers[i].transform).anchoredPosition = origin + new Vector2(shown % 2 * OfferColumn, -(shown / 2) * rowPitch);
                shown++;
            }

            int headerIndex = first.GetSiblingIndex() - 1;
            int nextIndex = first.GetSiblingIndex() + 1;
            while (nextIndex < Content.childCount && System.Array.Exists(Offers, o => o.transform == Content.GetChild(nextIndex))) nextIndex++;
            if (first.parent != Content || headerIndex < 0 || nextIndex >= Content.childCount) return;

            var header = (RectTransform)Content.GetChild(headerIndex);
            // Up over the whole section when it is empty, down by the rows added to it otherwise.
            float lift = shown == 0
                ? header.anchoredPosition.y - ((RectTransform)Content.GetChild(nextIndex)).anchoredPosition.y
                : -((shown + 1) / 2 - 1) * rowPitch;
            if (shown == 0) header.gameObject.SetActive(false);
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
                else if (_arenas != null && _arenas.DialogOpen) _arenas.Dismiss();
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
            if (_restoreStatus != null) _restoreStatus.text = "";
            IapStore.Start();
            RefreshPackPrices();
            Refresh();
            MarkNoticesSeen();
            Canvas.ForceUpdateCanvases();
            Fit();
            Scroll.StopMovement();
            Scroll.verticalNormalizedPosition = 1;
            for (int i = 0; i < Offers.Length; i++) Offers[i].GetComponent<UiTactile>()?.Reveal(.04f + i * .06f);
            for (int i = 0; i < Packs.Length; i++) Packs[i].GetComponent<UiTactile>()?.Reveal(.16f + i * .05f);
            if (_arenas != null) _arenas.Reveal(.12f);
        }

        public void Hide()
        {
            if (IsOpen) MarkNoticesSeen();
            if (_emotes != null) _emotes.ClosePreview();
            if (_arenas != null) _arenas.CloseDialog();
            PackDialog.SetActive(false);
            Overlay.SetActive(false);
            RefreshNoticeBadge();
        }
        public void ClosePack() => PackDialog.SetActive(false);

        /// <summary>The arena buy dialog for a locked gem arena tapped in the avatar screen's ARENAS
        /// tab; buying or dismissing it goes back to that tab.</summary>
        public void BuyArena(string id)
        {
            Show();
            _arenas?.Open(id, fromArenas: true);
        }

        public void Preview(int index)
        {
            Hide();
            Collection.ShowPreview(index, Show);
        }

        public void Refresh()
        {
            Balance.text = $"{CaseRewards.GemsBalance:N0}";
            if (_emotes != null) _emotes.Refresh();
            if (_arenas != null) _arenas.Refresh();
            for (int i = 0; i < Offers.Length; i++)
            {
                int slot = _offerSlots[i];
                if (AvatarCatalog.IsListed(slot))
                    OfferActions[i].text = Collection.IsLocked(slot) ? AvatarCollectionScreen.PriceLabel(slot)
                    : Collection.IsSelected(slot) ? "SELECTED" : "OWNED";
            }
        }

        /// <summary>Gem packs cost real money: with <see cref="StoreAccess.RealMoney"/> off their
        /// header and cards come out and whatever sits below moves up into the gap.</summary>
        private void HideGemPacks()
        {
            if (StoreAccess.RealMoney || Packs.Length == 0 || Packs[0].transform.parent != Content) return;
            int headerIndex = Packs[0].transform.GetSiblingIndex() - 1;
            var hidden = new List<RectTransform>();
            if (headerIndex >= 0) hidden.Add((RectTransform)Content.GetChild(headerIndex));
            foreach (var pack in Packs) hidden.Add((RectTransform)pack.transform);
            float top = float.MaxValue, bottom = float.MinValue;
            foreach (var item in hidden)
            {
                top = Mathf.Min(top, -item.anchoredPosition.y);
                bottom = Mathf.Max(bottom, -item.anchoredPosition.y + item.rect.height);
                item.gameObject.SetActive(false);
            }

            float above = 0, below = float.MaxValue;
            foreach (RectTransform child in Content)
            {
                if (!child.gameObject.activeSelf) continue;
                float y = -child.anchoredPosition.y;
                if (y < top) above = Mathf.Max(above, y + child.rect.height);
                else if (y >= bottom - .5f) below = Mathf.Min(below, y);
            }
            if (below < float.MaxValue)
            {
                // Whatever is below moves up into the header's place.
                float lift = below - top;
                foreach (RectTransform child in Content)
                    if (-child.anchoredPosition.y >= bottom - .5f) child.anchoredPosition += new Vector2(0, lift);
                Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(0, Content.rect.height - lift));
            }
            // Nothing below: the list ends where the section above ends, keeping the bottom margin.
            else Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, above + Mathf.Max(0, Content.rect.height - bottom));
        }

        public void ShowPack(int index)
        {
            _showingPack = index;
            PackTitle.text = GemAmounts[index] + " GEMS";
            PackPrice.text = PackPriceText(index);
            _packStatus.text = "";
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
