using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// One of the two figures on the "who are you playing as" page, and everything that has to
    /// change when it is the chosen one.
    ///
    /// <para><b>Colour is the selection state</b>, with a warm halo lit behind the one it lands on.
    /// The chosen figure keeps its own colours; the other is drained to grey. Nothing is dimmed,
    /// outlined or shrunk — a player glancing at this screen reads "that one is live, this one is
    /// not" from three metres away without being taught what a highlight means here, and the two
    /// figures stay the same size so neither looks like the better deal.</para>
    ///
    /// <para>Drained through a material swap rather than a tint: see the shader for why a tint
    /// cannot do it. Two shared assets rather than a material per card with its own saturation
    /// value — there are exactly two states to show, the swap costs nothing to allocate, and a
    /// shared asset is visible in the Scene view before anything runs.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GenderChoiceCard : MonoBehaviour
    {
        [Header("Figure")]
        [Tooltip("Surface the 3D stage renders this figure onto.")]
        [SerializeField] private RawImage _portrait;

        [Tooltip("Material used while this figure is the chosen one — saturation 1.")]
        [SerializeField] private Material _saturated;

        [Tooltip("Material used while it is not — saturation 0.")]
        [SerializeField] private Material _drained;

        [Tooltip("Warm halo behind this figure. Lit only while it is the chosen one.")]
        [SerializeField] private Graphic _glow;

        [Header("Radio dot")]
        [SerializeField] private Graphic _dotRing;
        [SerializeField] private Graphic _dotCore;

        [SerializeField] private Color _ringOn = new Color32(245, 200, 66, 255);
        [SerializeField] private Color _ringOff = new Color32(70, 70, 80, 255);
        [SerializeField] private Color _coreOn = new Color32(255, 255, 255, 255);
        [SerializeField] private Color _coreOff = new Color32(210, 210, 218, 255);

        [Header("Identity")]
        [SerializeField] private CharacterGender _gender;

        [Tooltip("Hit target that chooses this body. The whole card, not just the dot.")]
        [SerializeField] private Button _button;

        /// <summary>Which body this card offers. Read by the page to know what a tap on it means.</summary>
        public CharacterGender Gender => _gender;

        /// <summary>The card's own button, so the page can subscribe without knowing its shape.</summary>
        public Button Button => _button;

        /// <summary>Paints the card as chosen or not. Safe to call every time the page is shown —
        /// it writes the same values again rather than toggling, so state cannot drift out of step
        /// with what was actually saved.</summary>
        public void SetSelected(bool selected)
        {
            if (_portrait != null)
            {
                var material = selected ? _saturated : _drained;
                if (material != null) _portrait.material = material;
            }

            // Switched off rather than faded to nothing: an invisible Graphic still draws.
            if (_glow != null) _glow.enabled = selected;

            if (_dotRing != null) _dotRing.color = selected ? _ringOn : _ringOff;
            if (_dotCore != null) _dotCore.color = selected ? _coreOn : _coreOff;
        }
    }
}
