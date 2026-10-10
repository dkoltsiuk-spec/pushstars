using TMPro;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PushStars.Core;

namespace PushStars.UI
{
    public sealed class AvatarCollectionScreen : MonoBehaviour
    {
        public GameObject Overlay, InfoPanel;
        public RectTransform Art, Grid;
        public CharacterRoster Roster;
        public Button Back, Home, InfoClose;
        public Button[] Tabs, Cards, InfoButtons;
        public Image[] Plates, Footers;
        public TextMeshProUGUI[] Actions;
        public TextMeshProUGUI InfoTitle, InfoBody, Empty;
        public Sprite Purple, Gold, PurpleFooter, GoldFooter, TabOn, TabOff;
        public Sprite ShortTabOn, ShortTabOff;
        public AvatarPreviewPage PreviewPage;
        public GameObject[] CollectionContent;
        public TextMeshProUGUI Balance;
        private int _filter;
        private System.Action _returnToSource;
        private Material _selectedGlowMaterial;
        private readonly Dictionary<Image, Material> _originalGlowMaterials = new Dictionary<Image, Material>();
        private AvatarCardPreview[] _previews;
        private readonly Vector3[] _corners = new Vector3[4];
        public bool IsOpen => Overlay != null && Overlay.activeSelf;

        private void Awake()
        {
            AddCatalogCards();
            _previews = new AvatarCardPreview[Cards.Length];
            for (int i = 0; i < Cards.Length; i++) _previews[i] = Cards[i].GetComponentInChildren<AvatarCardPreview>(true);
            Back.onClick.AddListener(Hide);
            Home.onClick.AddListener(Hide);
            InfoClose.onClick.AddListener(CloseInfo);
            for (int i = 0; i < Tabs.Length; i++)
            {
                int index = i;
                Tabs[i].onClick.AddListener(() => Filter(index));
            }
            for (int i = 0; i < Cards.Length; i++)
            {
                int index = i;
                Cards[i].onClick.AddListener(() => { if (IsLocked(index)) ShowInfo(index); else Select(index); });
                InfoButtons[i].onClick.AddListener(() => ShowInfo(index));
            }
            Overlay.SetActive(false);
            InfoPanel.SetActive(false);
        }

        /// <summary>The scene authors the first cards. Every later catalog slot gets a copy of the
        /// last authored one, so a new hero is a catalog row and a prefab, not a scene edit.</summary>
        private void AddCatalogCards()
        {
            int authored = Cards.Length, total = AvatarCatalog.All.Length;
            if (authored == 0 || total <= authored) return;
            var template = Cards[authored - 1];
            System.Array.Resize(ref Cards, total);
            System.Array.Resize(ref InfoButtons, total);
            System.Array.Resize(ref Plates, total);
            System.Array.Resize(ref Footers, total);
            System.Array.Resize(ref Actions, total);
            for (int i = authored; i < total; i++)
            {
                var card = Instantiate(template, template.transform.parent);
                card.name = "Card" + i;
                card.transform.Find("Name").GetComponent<TextMeshProUGUI>().text = AvatarName(i);
                var preview = card.GetComponentInChildren<AvatarCardPreview>(true);
                // Each preview owns a patch of world by slot; 8 and 20+ are the detail page and the shop.
                preview.Slot = CatalogCardSlot + i;
                preview.SetPrefab(AvatarCatalog.LoadPrefab(i));
                Cards[i] = card;
                InfoButtons[i] = card.transform.Find("Info").GetComponent<Button>();
                Plates[i] = card.image;
                Footers[i] = card.transform.Find("Footer").GetComponent<Image>();
                Actions[i] = card.transform.Find("Action").GetComponent<TextMeshProUGUI>();
            }
        }

        public const int CatalogCardSlot = 40;

        /// <summary>Every card renders its hero through a camera of its own; only the cards
        /// inside the scroll window need theirs running.</summary>
        private void CullPreviews()
        {
            if (!(Grid.parent is RectTransform viewport)) return;
            viewport.GetWorldCorners(_corners);
            float bottom = _corners[0].y, top = _corners[1].y;
            for (int i = 0; i < Cards.Length; i++)
            {
                if (_previews[i] == null || !Cards[i].gameObject.activeInHierarchy) continue;
                ((RectTransform)Cards[i].transform).GetWorldCorners(_corners);
                bool visible = _corners[1].y >= bottom && _corners[0].y <= top;
                if (_previews[i].enabled != visible) _previews[i].enabled = visible;
            }
        }

        private void Update()
        {
            if (!IsOpen) return;
            Fit();
            CullPreviews();
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (InfoPanel.activeSelf) CloseInfo();
                else Hide();
            }
        }

        public void Fit()
        {
            var space = ((RectTransform)Art.parent).rect.size;
            float scale = Mathf.Max(.001f, space.x / 390f);
            float height = space.y / scale;
            Art.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            Art.localScale = Vector3.one * scale;
            var scroll = Grid.parent.GetComponent<ScrollRect>();
            if (scroll != null)
            {
                var viewport = (RectTransform)scroll.transform;
                viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    Mathf.Max(1, height + viewport.anchoredPosition.y));
            }
        }

        public void Show()
        {
            _returnToSource = null;
            Overlay.SetActive(true);
            Overlay.transform.SetAsLastSibling();
            InfoPanel.SetActive(false);
            SetCollectionVisible(true);
            Filter(0);
            Fit();
            Overlay.GetComponent<UiTactile>()?.Reveal();
        }

        public void Hide() { _returnToSource = null; InfoPanel.SetActive(false); Overlay.SetActive(false); }

        public void ShowPreview(int index, System.Action onBack)
        {
            Show();
            _returnToSource = onBack;
            ShowInfo(index);
        }

        private void SetCollectionVisible(bool visible)
        {
            if (CollectionContent == null) return;
            foreach (var item in CollectionContent) if (item != null) item.SetActive(visible);
        }

        public void CloseInfo()
        {
            if (_returnToSource != null)
            {
                var onBack = _returnToSource;
                Hide();
                onBack();
                return;
            }
            InfoPanel.SetActive(false);
            SetCollectionVisible(true);
            Filter(_filter);
        }

        public void Filter(int index)
        {
            _filter = Mathf.Clamp(index, 0, 2);
            for (int i = 0; i < Tabs.Length; i++)
                Tabs[i].image.sprite = i == 0 && ShortTabOn != null && ShortTabOff != null
                    ? (i == _filter ? ShortTabOn : ShortTabOff)
                    : (i == _filter ? TabOn : TabOff);
            for (int i = 0; i < Cards.Length; i++)
            {
                var kind = AvatarCatalog.At(i)?.Kind;
                bool visible = AvatarCatalog.IsListed(i) && (_filter == 0 || (_filter == 1 && !IsLocked(i)) ||
                    (_filter == 2 && (kind == AvatarPurchaseKind.Gems || kind == AvatarPurchaseKind.Dollars)));
                Cards[i].gameObject.SetActive(visible);
            }
            bool any = false;
            foreach (var card in Cards) any |= card.gameObject.activeSelf;
            Empty.gameObject.SetActive(!any);
            RefreshSelection();
            LayoutRebuilder.ForceRebuildLayoutImmediate(Grid);
            var layout = Grid.GetComponent<GridLayoutGroup>();
            if (layout != null)
            {
                int visibleCount = 0;
                foreach (var card in Cards) if (card.gameObject.activeSelf) visibleCount++;
                int rows = Mathf.CeilToInt(visibleCount / (float)Mathf.Max(1, layout.constraintCount));
                Grid.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                    layout.padding.vertical + rows * layout.cellSize.y + Mathf.Max(0, rows - 1) * layout.spacing.y + 12);
            }
            Fit();
            Canvas.ForceUpdateCanvases();
            var scroll = Grid.parent.GetComponent<ScrollRect>();
            if (scroll != null)
            {
                scroll.StopMovement();
                scroll.verticalNormalizedPosition = 1;
            }
            int order = 0;
            foreach (var card in Cards)
                if (card.gameObject.activeInHierarchy) card.GetComponent<UiTactile>()?.Reveal(.05f + order++ * .065f);
        }

        public void Select(int index)
        {
            if (IsLocked(index) || !AvatarCatalog.IsListed(index)) return;
            if (index >= AvatarCatalog.FirstPrefabSlot) Roster.SetHero(index);
            else if (index == 0) Roster.SetSonic();
            else if (index == 3) Roster.SetRobot();
            else if (index == 4) Roster.SetGladiator();
            else Roster.SetGender(index == 1 ? CharacterGender.Female : CharacterGender.Male);
            RefreshSelection();
        }

        public void RefreshSelection()
        {
            if (Balance != null) Balance.text = $"{AuraFormat.Short(CaseRewards.AuraBalance)} <sprite name=\"aura\">";
            for (int i = 0; i < Cards.Length; i++)
            {
                bool selected = IsSelected(i);
                bool locked = IsLocked(i);
                Plates[i].sprite = selected ? Gold : Purple;
                Footers[i].sprite = selected ? GoldFooter : PurpleFooter;
                var glow = Cards[i].transform.Find("Glow")?.GetComponent<Image>();
                if (glow != null)
                {
                    if (!_originalGlowMaterials.TryGetValue(glow, out var original))
                    {
                        original = glow.material;
                        _originalGlowMaterials.Add(glow, original);
                    }
                    if (selected && _selectedGlowMaterial == null)
                    {
                        var shader = Resources.Load<Shader>("AvatarSelectedGlow");
                        if (shader != null) _selectedGlowMaterial = new Material(shader) { name = "Selected avatar warm glow" };
                    }
                    glow.material = selected && _selectedGlowMaterial != null ? _selectedGlowMaterial : original;
                }
                foreach (var surface in Cards[i].GetComponentsInChildren<AvatarCardSurface>(true)) { surface.SetSelected(selected); surface.SetLocked(locked); }
                var lockIcon = Cards[i].transform.Find("LockIcon");
                if (lockIcon != null) lockIcon.gameObject.SetActive(locked);
                var preview = Cards[i].GetComponentInChildren<AvatarCardPreview>(true);
                if (preview != null) { preview.Tint = locked ? new Color(.70f, .70f, .70f, 1) : Color.white; preview.Image.color = preview.Tint; }
                Cards[i].GetComponent<AvatarUnlockView>()?.Refresh(AvatarCatalog.At(i));
                Actions[i].text = selected ? "SELECTED" : locked ? PriceLabel(i) : "SELECT";
            }
        }

        public bool IsSelected(int index) => !IsLocked(index) && Roster != null && (Roster.HeroSlot != 0 ? index == Roster.HeroSlot : Roster.IsRobot ? index == 3 : Roster.IsGladiator ? index == 4 : Roster.IsSonic ? index == 0 :
            index == 1 && Roster.Gender == CharacterGender.Female || index == 2 && Roster.Gender == CharacterGender.Male);

        private void OnDestroy()
        {
            if (_selectedGlowMaterial == null) return;
            if (Application.isPlaying) Destroy(_selectedGlowMaterial);
            else DestroyImmediate(_selectedGlowMaterial);
        }

        public bool IsLocked(int index) => !CaseRewards.OwnsAvatar(AvatarCatalog.At(index)?.Id);

        public static string PriceLabel(int index)
        {
            var offer = AvatarCatalog.At(index);
            if (offer == null) return "UNAVAILABLE";
            if (offer.Kind == AvatarPurchaseKind.Dollars) return "$" + (offer.Price / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            // Aura heroes are never bought: the label is how much Aura is still missing.
            if (offer.UnlocksByAura) return $"{AuraFormat.Short(CaseRewards.AvatarPrice(offer.Id))} <sprite name=\"aura\"> TO GO";
            return $"{CaseRewards.AvatarPrice(offer.Id):N0} GEMS";
        }

        public void ShowInfo(int index)
        {
            if (PreviewPage != null)
            {
                PreviewPage.Present(index);
                InfoTitle.text = AvatarName(index);
                SetCollectionVisible(false);
                InfoPanel.SetActive(true);
                PreviewPage.Preview.GetComponent<UiTactile>()?.Reveal(.03f);
                PreviewPage.Action.GetComponent<UiTactile>()?.Reveal(.12f);
                return;
            }
            string[] descriptions = {
                "Not available.",
                "Available in your collection.\n\nTap SELECT to use this avatar.",
                "Available in your collection.\n\nTap SELECT to use this avatar.",
                "Robot is coming to your collection.\n\nPreview only for now.",
                "Gladiator is coming to your collection.\n\nPreview only for now." };
            InfoTitle.text = AvatarName(index);
            InfoBody.text = descriptions[Mathf.Min(index, descriptions.Length - 1)];
            InfoPanel.SetActive(true);
        }

        public static string AvatarName(int index) => AvatarCatalog.At(index)?.Name ?? "AVATAR";
    }
}
