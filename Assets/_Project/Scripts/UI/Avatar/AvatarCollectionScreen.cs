using TMPro;
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
        public bool IsOpen => Overlay != null && Overlay.activeSelf;

        private void Awake()
        {
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

        private void Update()
        {
            if (!IsOpen) return;
            Fit();
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
                bool visible = _filter == 0 || (_filter == 1 && !IsLocked(i)) ||
                    (_filter == 2 && (kind == AvatarPurchaseKind.Gems || kind == AvatarPurchaseKind.Dollars));
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
            if (IsLocked(index)) return;
            if (index == 0) Roster.SetSonic();
            else if (index == 3) Roster.SetRobot();
            else if (index == 4) Roster.SetGladiator();
            else Roster.SetGender(index == 1 ? CharacterGender.Female : CharacterGender.Male);
            RefreshSelection();
        }

        public void RefreshSelection()
        {
            if (Balance != null) Balance.text = $"{CaseRewards.AuraBalance:N0} <sprite name=\"aura\">";
            for (int i = 0; i < Cards.Length; i++)
            {
                bool selected = IsSelected(i);
                bool locked = IsLocked(i);
                Plates[i].sprite = selected ? Gold : Purple;
                Footers[i].sprite = selected ? GoldFooter : PurpleFooter;
                foreach (var surface in Cards[i].GetComponentsInChildren<AvatarCardSurface>(true)) { surface.SetSelected(selected); surface.SetLocked(locked); }
                var lockIcon = Cards[i].transform.Find("LockIcon");
                if (lockIcon != null) lockIcon.gameObject.SetActive(locked);
                var preview = Cards[i].GetComponentInChildren<AvatarCardPreview>(true);
                if (preview != null) { preview.Tint = locked ? new Color(.70f, .70f, .70f, 1) : Color.white; preview.Image.color = preview.Tint; }
                Cards[i].GetComponent<AvatarUnlockView>()?.Refresh(AvatarCatalog.At(i));
                Actions[i].text = selected ? "SELECTED" : locked ? PriceLabel(i) : "SELECT";
            }
        }

        public bool IsSelected(int index) => !IsLocked(index) && Roster != null && (Roster.IsRobot ? index == 3 : Roster.IsGladiator ? index == 4 : Roster.IsSonic ? index == 0 :
            index == 1 && Roster.Gender == CharacterGender.Female || index == 2 && Roster.Gender == CharacterGender.Male);

        public bool IsLocked(int index) => !CaseRewards.OwnsAvatar(AvatarCatalog.At(index)?.Id);

        public static string PriceLabel(int index)
        {
            var offer = AvatarCatalog.At(index);
            if (offer == null) return "UNAVAILABLE";
            if (offer.Kind == AvatarPurchaseKind.Dollars) return "$" + (offer.Price / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            return $"{CaseRewards.AvatarPrice(offer.Id):N0} {(offer.Kind == AvatarPurchaseKind.Gems ? "GEMS" : "<sprite name=\"aura\">")}";
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
                "Sonic is coming to your collection.\n\nPreview only for now.",
                "Available in your collection.\n\nTap SELECT to use this avatar.",
                "Available in your collection.\n\nTap SELECT to use this avatar.",
                "Robot is coming to your collection.\n\nPreview only for now.",
                "Gladiator is coming to your collection.\n\nPreview only for now." };
            InfoTitle.text = AvatarName(index);
            InfoBody.text = descriptions[index];
            InfoPanel.SetActive(true);
        }

        public static string AvatarName(int index) => AvatarCatalog.At(index)?.Name ?? "AVATAR";
    }
}
