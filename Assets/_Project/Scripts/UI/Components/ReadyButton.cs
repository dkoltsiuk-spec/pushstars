using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Pill-shaped success/confirm button for "ГОТОВ" (I'm Ready).
    ///
    /// Visual layers (bottom to top):
    ///   BorderLayer  — full-size pill, neon lime (#6BFF4A).  Visible as 2 px stroke.
    ///   FillLayer    — same pill, inset 2 px, very dark forest green (#081804).
    ///   Label        — bold white TMP label stretched over the fill.
    ///
    /// Phase 17: replace _fallbackText with a proper localisation lookup via _localizationKey.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class ReadyButton : MonoBehaviour
    {
        [SerializeField] private Image            _border;
        [SerializeField] private Image            _fill;
        [SerializeField] private TextMeshProUGUI  _label;

#pragma warning disable CS0414
        [SerializeField] private string _localizationKey = "btn.ready";
#pragma warning restore CS0414
        [SerializeField] private string _fallbackText    = "ГОТОВ";

        public Button Button { get; private set; }

        private void Awake()
        {
            Button = GetComponent<Button>();
        }

        /// <summary>Sets the button label at runtime (before Phase 17 localisation).</summary>
        public void SetLabel(string text)
        {
            _fallbackText = text;
            if (_label != null) _label.text = text;
        }

        public void SetInteractable(bool interactable)
        {
            if (Button != null) Button.interactable = interactable;
            float a = interactable ? 1f : 0.45f;
            if (_border != null) { var c = _border.color; c.a = a; _border.color = c; }
            if (_fill   != null) { var c = _fill.color;   c.a = a; _fill.color   = c; }
        }
    }
}
