using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Small pill-shaped chip for mode/filter selection (Duel / Push-Up / 60 sec).
    ///
    /// When Figma sprites are wired (_normalShape / _selectedShape):
    ///   • SetSelected swaps the background sprite and keeps Image.color = white.
    ///   • Text colour still switches between _normalFg / _selectedFg.
    ///
    /// Without Figma sprites falls back to the original colour-tint approach.
    ///
    /// Phase 17: replace _fallbackText with localisation key lookup.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SecondaryChip : MonoBehaviour
    {
        [SerializeField] private Image           _background;
        [SerializeField] private TextMeshProUGUI _label;
        [SerializeField] private Image           _icon;

        [Header("Figma sprites (pre-coloured — use Color.white)")]
        [SerializeField] private Sprite _normalShape;   // type.png — blue border
        [SerializeField] private Sprite _selectedShape; // mode.png — gold border

#pragma warning disable CS0414
        [SerializeField] private string _localizationKey = "";
#pragma warning restore CS0414
        [SerializeField] private string _fallbackText = "Chip";

        [Header("Fallback colours (used when Figma sprites are absent)")]
        [SerializeField] private Color _normalBg   = new Color32( 30,  33,  45, 255);
        [SerializeField] private Color _selectedBg = new Color32(255, 217,  26, 255);
        [SerializeField] private Color _normalFg   = new Color32(148, 152, 170, 255);
        [SerializeField] private Color _selectedFg = new Color32(255, 217,  26, 255);

        public Button Button   { get; private set; }
        public bool   Selected { get; private set; }

        private bool FigmaMode => _normalShape != null || _selectedShape != null;

        private void Awake()
        {
            Button = GetComponent<Button>();
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;

            if (_background != null)
            {
                if (FigmaMode)
                {
                    var target = selected ? _selectedShape : _normalShape;
                    if (target != null) _background.sprite = target;
                    _background.color = Color.white;
                }
                else
                {
                    _background.color = selected ? _selectedBg : _normalBg;
                }
            }

            if (_label != null)
                _label.color = selected ? _selectedFg : _normalFg;
        }

        public void SetLabel(string text)
        {
            _fallbackText = text;
            if (_label != null) _label.text = text;
        }

        public void SetIcon(Sprite sprite)
        {
            if (_icon == null) return;
            _icon.sprite = sprite;
            _icon.gameObject.SetActive(sprite != null);
        }
    }
}
