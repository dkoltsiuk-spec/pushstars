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
        public bool IsOpen => Overlay != null && Overlay.activeSelf;

        private void Awake()
        {
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
            Hide();
        }

        private void Update()
        {
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
            Canvas.ForceUpdateCanvases();
            Fit();
            Scroll.StopMovement();
            Scroll.verticalNormalizedPosition = 1;
            for (int i = 0; i < Offers.Length; i++) Offers[i].GetComponent<UiTactile>()?.Reveal(.04f + i * .06f);
            for (int i = 0; i < Packs.Length; i++) Packs[i].GetComponent<UiTactile>()?.Reveal(.16f + i * .05f);
        }

        public void Hide() { PackDialog.SetActive(false); Overlay.SetActive(false); }
        public void ClosePack() => PackDialog.SetActive(false);

        public void Preview(int index)
        {
            Hide();
            Collection.ShowPreview(index, Show);
        }

        public void Refresh()
        {
            Balance.text = $"{CaseRewards.GemsBalance:N0}";
            for (int i = 0; i < Offers.Length; i++)
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
