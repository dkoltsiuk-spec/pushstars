using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>
    /// League tab placeholder. Phase 11 will populate with real Firestore data.
    /// </summary>
    public class LeagueView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _placeholderLabel;

        private void OnEnable()
        {
            if (_placeholderLabel != null)
                _placeholderLabel.text = "League\n[Phase 11]";
        }
    }
}
