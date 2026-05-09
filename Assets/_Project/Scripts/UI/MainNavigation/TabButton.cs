using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Single pill-nav button. Notifies MainShellView when pressed.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class TabButton : MonoBehaviour
    {
        [SerializeField] private TabId _tabId;
        [SerializeField] private Image _icon;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private Image _activeIndicator;

        public TabId TabId => _tabId;
        public event Action<TabId> OnTabSelected;

        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(() => OnTabSelected?.Invoke(_tabId));
        }

        public void SetActive(bool isActive)
        {
            if (_activeIndicator != null)
                _activeIndicator.enabled = isActive;

            if (_label != null)
                _label.fontStyle = isActive
                    ? TMPro.FontStyles.Bold
                    : TMPro.FontStyles.Normal;
        }
    }
}
