using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Duel (VS) tab placeholder — main screen. Phase 03 adds UI kit, Phase 14 adds matchmaking logic.
    /// </summary>
    public class DuelView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _placeholderLabel;

        private void OnEnable()
        {
            if (_placeholderLabel != null)
                _placeholderLabel.text = "Duel (VS)\n[Phase 03 / 14]";
        }
    }
}
