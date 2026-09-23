using PushStars.Core;
using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    public sealed class AvatarAuraBalance : MonoBehaviour
    {
        public TextMeshProUGUI Label;
        private long _shown = -1;
        private void OnEnable() { _shown = -1; Update(); }
        private void Update()
        {
            long balance = CaseRewards.AuraBalance;
            if (Label == null || balance == _shown) return;
            _shown = balance; Label.text = balance.ToString("N0");
        }
    }
}
