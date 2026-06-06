using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// A small "TITLE ▾" filter that opens a popup list of options. Selecting one updates the
    /// label, closes the popup and raises <see cref="OnChanged"/> with the option's value
    /// (empty string = "all"). Used for the TYPE / MODE filters on the profile history list.
    /// </summary>
    public class FilterDropdown : MonoBehaviour
    {
        [SerializeField] private Button          _toggle;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private GameObject      _popup;
        [SerializeField] private Button[]        _optionButtons;
        [SerializeField] private string[]        _optionLabels;
        [SerializeField] private string[]        _optionValues;
        [SerializeField] private string          _title = "TYPE";

        public event Action<string> OnChanged;
        public string Value { get; private set; } = "";

        private int _index;

        private void Awake()
        {
            if (_toggle != null) _toggle.onClick.AddListener(Toggle);
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                int idx = i;
                if (_optionButtons[i] != null) _optionButtons[i].onClick.AddListener(() => Pick(idx));
            }
            Close();
        }

        private void OnDisable() => Close(); // leaving the tab hides any open popup

        public void Close() { if (_popup != null) _popup.SetActive(false); }

        private void Toggle()
        {
            if (_popup != null) { _popup.SetActive(!_popup.activeSelf); return; }
            // No popup → cycle to the next option on each tap (works cleanly inside a ScrollRect).
            if (_optionValues == null || _optionValues.Length == 0) return;
            _index = (_index + 1) % _optionValues.Length;
            Apply(_index);
        }

        private void Pick(int i)
        {
            _index = i;
            Close();
            Apply(i);
        }

        private void Apply(int i)
        {
            Value = (i >= 0 && i < _optionValues.Length) ? _optionValues[i] : "";
            if (_label != null)
                _label.text = string.IsNullOrEmpty(Value) ? _title : _optionLabels[i];
            OnChanged?.Invoke(Value);
        }
    }
}
