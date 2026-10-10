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
                // Aura heroes cannot be bought: they unlock by themselves when the bar fills.
                if (offer.UnlocksByAura) return;
                try
                {
                    if (!CaseRewards.TryBuyAvatar(offer.Id))
                    { Status.text = "NOT ENOUGH GEMS"; return; }
                }
                catch (System.Exception exception) { Debug.LogException(exception, this); Status.text = "COULDN'T SAVE · TAP TO RETRY"; return; }
            }
            Collection.Select(_index);
            Refresh();
        }

        public void Present(int index)
        {
            _index = index;
            Preview.SetPrefab(index < Prefabs.Length ? Prefabs[index] : AvatarCatalog.LoadPrefab(index));
            Name.text = AvatarCollectionScreen.AvatarName(index);
            Refresh();
        }

        private void Refresh()
        {
            bool available = !Collection.IsLocked(_index);
            bool selected = Collection.IsSelected(_index);
            var offer = AvatarCatalog.At(_index);
            bool byAura = offer.UnlocksByAura;
            Action.interactable = !selected && (available || offer.Kind == AvatarPurchaseKind.Gems);
            ActionText.text = available ? (selected ? "SELECTED" : "SELECT")
                : byAura ? AvatarCollectionScreen.PriceLabel(_index) : "BUY · " + AvatarCollectionScreen.PriceLabel(_index);
            Status.text = available ? "IN YOUR COLLECTION" : byAura
                ? $"UNLOCKS AT {AuraFormat.Short(offer.AuraGoal)} <sprite name=\"aura\"> · WIN FIGHTS TO FILL THE BAR" +
                  (offer.UsesCards ? $"\nEVERY {offer.Name} CARD FROM CASES ADDS {AuraFormat.Short(offer.CardAura)}" : "")
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
