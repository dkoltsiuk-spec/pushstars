using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// Profile tab placeholder. Phase 06 will add stats and match history from Firestore.
    /// </summary>
    public class ProfileView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _placeholderLabel;

        private void OnEnable()
        {
            if (_placeholderLabel != null)
                _placeholderLabel.text = "Profile\n[Phase 06]";
        }
    }
}
