using System;
using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Single pill-nav button. Notifies MainShellView when pressed, with a light haptic tick so the
    /// tab is felt landing (see <see cref="Haptics"/>).
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
            _button.onClick.AddListener(HandlePressed);
        }

        private void OnDestroy()
        {
            if (_button != null)
                _button.onClick.RemoveListener(HandlePressed);
        }

        private void HandlePressed()
        {
            Haptics.Selection(); // fires on every tap, the active tab included — it is the lightest cue there is
            OnTabSelected?.Invoke(_tabId);
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
