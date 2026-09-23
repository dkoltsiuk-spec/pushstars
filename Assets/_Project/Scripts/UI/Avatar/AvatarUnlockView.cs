using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed class AvatarUnlockView : MonoBehaviour
    {
        public GameObject ProgressRoot;
        public AvatarShardBar Bar;
        public TextMeshProUGUI Count;
        public Image Head;

        public void Refresh(AvatarOffer offer)
        {
            bool show = offer != null && offer.UsesCards && !CaseRewards.OwnsAvatar(offer.Id);
            ProgressRoot.SetActive(show);
            if (!show) return;
            int count = CaseRewards.CardsFor(offer.Id);
            Bar.SetProgress((float)count / offer.RequiredCards);
            Count.text = $"{count} / {offer.RequiredCards}";
            Head.sprite = Resources.Load<Sprite>(offer.HeadIcon);
            Head.enabled = Head.sprite != null;
        }
    }
}
