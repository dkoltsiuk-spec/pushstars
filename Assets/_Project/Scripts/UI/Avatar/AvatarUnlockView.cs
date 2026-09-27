using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>The Aura hero's unlock bar: peak Aura plus collected cards against the hero's goal,
    /// e.g. "32K / 50K". It only ever fills — losing Aura never takes progress back.</summary>
    public sealed class AvatarUnlockView : MonoBehaviour
    {
        public GameObject ProgressRoot;
        public AvatarShardBar Bar;
        public TextMeshProUGUI Count;
        public Image Head;

        public void Refresh(AvatarOffer offer)
        {
            bool show = offer != null && offer.UnlocksByAura && offer.AuraGoal > 0 && !CaseRewards.OwnsAvatar(offer.Id);
            ProgressRoot.SetActive(show);
            if (!show) return;
            long progress = CaseRewards.AvatarUnlockProgress(offer.Id);
            Bar.SetProgress((float)((double)progress / offer.AuraGoal));
            Count.text = $"{AuraFormat.Short(progress)} / {AuraFormat.Short(offer.AuraGoal)}";
            Head.sprite = Resources.Load<Sprite>(offer.HeadIcon);
            Head.enabled = Head.sprite != null;
        }
    }
}
