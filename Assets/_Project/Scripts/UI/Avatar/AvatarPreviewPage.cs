using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PushStars.Core;

namespace PushStars.UI
{
    public sealed class AvatarPreviewPage : MonoBehaviour
    {
        public AvatarCollectionScreen Collection;
        public AvatarCardPreview Preview;
        public Button Home, Action;
        public TextMeshProUGUI Name, ActionText, Status;
        public GameObject[] Prefabs;
        public AvatarUnlockView UnlockView;
        private int _index;

        private void Awake()
        {
            Home.onClick.AddListener(Collection.Hide);
            Action.onClick.AddListener(Activate);
        }

        private void Activate()
        {
            var offer = AvatarCatalog.At(_index);
            if (Collection.IsLocked(_index))
            {
                if (offer.Kind == AvatarPurchaseKind.Dollars) { Status.text = "STORE COMING SOON"; return; }
                try
                {
                    if (!CaseRewards.TryBuyAvatar(offer.Id))
                    { Status.text = offer.Kind == AvatarPurchaseKind.Aura ? "NOT ENOUGH AURA · COLLECT CARDS IN CASES" : "NOT ENOUGH GEMS"; return; }
                }
                catch (System.Exception exception) { Debug.LogException(exception, this); Status.text = "COULDN'T SAVE · TAP TO RETRY"; return; }
            }
            Collection.Select(_index);
            Refresh();
        }

        public void Present(int index)
        {
            _index = index;
            Preview.SetPrefab(Prefabs[index]);
            Name.text = AvatarCollectionScreen.AvatarName(index);
            Refresh();
        }

        private void Refresh()
        {
            bool available = !Collection.IsLocked(_index);
            bool selected = Collection.IsSelected(_index);
            var offer = AvatarCatalog.At(_index);
            Action.interactable = !selected && (available || offer.Kind != AvatarPurchaseKind.Dollars);
            ActionText.text = !available ? "BUY · " + AvatarCollectionScreen.PriceLabel(_index) : selected ? "SELECTED" : "SELECT";
            Status.text = available ? "IN YOUR COLLECTION" : offer.UsesCards
                ? $"COLLECT CARDS OR BUY WITH AURA\nYOUR BALANCE: {CaseRewards.AuraBalance:N0} <sprite name=\"aura\">"
                : offer.Kind == AvatarPurchaseKind.Gems ? "GEMS EXCLUSIVE" : "STORE COMING SOON";
            if (UnlockView != null) UnlockView.Refresh(offer);
        }

        private void LateUpdate() => Fit();

        public void Fit()
        {
            var art = (RectTransform)transform.parent;
            float height = ((RectTransform)art.parent).rect.height / Mathf.Max(.001f, art.localScale.y);
            var rect = (RectTransform)Action.transform;
            rect.anchoredPosition = new Vector2((390 - rect.sizeDelta.x) * .5f, -(height - rect.sizeDelta.y - 24));
            Status.rectTransform.anchoredPosition = new Vector2(25, -(height - rect.sizeDelta.y - 73));
            if (UnlockView != null) ((RectTransform)UnlockView.ProgressRoot.transform).anchoredPosition = new Vector2(45, -(height - rect.sizeDelta.y - 113));
        }
    }
}
