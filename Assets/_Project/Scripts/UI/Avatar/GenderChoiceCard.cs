using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// A live character choice. Selection blends scale, saturation and the blue halo;
    /// the bottom pivot keeps both characters' feet planted. Each runtime card owns its
    /// saturation material, leaving shared character and UI assets unchanged.
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

        [Header("Animated selection")]
        [SerializeField] private Sprite _selectedSprite;
        [SerializeField] private Sprite _unselectedSprite;
        [SerializeField, Range(.5f, 1f)] private float _unselectedScale = .78f;
        private Material _selectionMaterial;
        private float _selection, _target;
        private bool _initialized;
        public RawImage Portrait => _portrait;

        /// <summary>Paints the card as chosen or not. Safe to call every time the page is shown —
        /// it writes the same values again rather than toggling, so state cannot drift out of step
        /// with what was actually saved.</summary>
        public void SetSelected(bool selected)
        {
            _target = selected ? 1f : 0f;
            if (!_initialized || !Application.isPlaying) _selection = _target;
            _initialized = true;
            if (Application.isPlaying && _selectionMaterial == null && _saturated != null)
                _selectionMaterial = new Material(_saturated);
            if (_portrait != null) _portrait.material = _selectionMaterial != null
                ? _selectionMaterial : (selected ? _saturated : _drained);
            if (_dotRing is Image ring && _selectedSprite != null)
            {
                ring.sprite = selected ? _selectedSprite : _unselectedSprite;
                ring.color = Color.white;
            }
            else if (_dotRing != null) _dotRing.color = selected ? _ringOn : _ringOff;
            if (_dotCore != null) _dotCore.color = selected ? _coreOn : _coreOff;
            Paint();
        }

        private void Update()
        {
            if (Mathf.Abs(_selection - _target) < .0001f) return;
            _selection = Mathf.MoveTowards(_selection, _target, Time.unscaledDeltaTime / .32f);
            Paint();
        }

        private void Paint()
        {
            float t = Mathf.SmoothStep(0f, 1f, _selection);
            if (_selectionMaterial != null) _selectionMaterial.SetFloat("_Saturation", t);
            if (_portrait != null) _portrait.rectTransform.localScale = Vector3.one * Mathf.Lerp(_unselectedScale, 1f, t);
            if (_glow != null)
            {
                _glow.enabled = t > .001f;
                _glow.color = new Color(1f, 1f, 1f, t);
            }
        }

        private void OnDestroy()
        {
            if (_selectionMaterial != null) Destroy(_selectionMaterial);
        }
    }
}
