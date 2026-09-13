using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PushStars.UI
{
    /// <summary>Explicit fight/reward type styles, independent of the first label in a scene.</summary>
    [CreateAssetMenu(fileName = "FightTypography", menuName = "Push Stars/Fight Typography")]
    public sealed class FightTypography : ScriptableObject
    {
        public enum Role { Title, Heading, Subtitle, Label, Caption, Value, Button }

        [SerializeField] private TMP_FontAsset _bold;
        [SerializeField] private TMP_FontAsset _boldItalic;

        private static FightTypography _instance;
        private readonly Dictionary<Role, Material> _materials = new Dictionary<Role, Material>();

        public static void Apply(TextMeshProUGUI label, Role role)
        {
            if (label == null) return;
            if (_instance == null) _instance = Resources.Load<FightTypography>("FightTypography");
            if (_instance == null)
            {
                Debug.LogError("FightTypography resource is missing.", label);
                return;
            }
            _instance.ApplyStyle(label, role);
        }

        private void ApplyStyle(TextMeshProUGUI label, Role role)
        {
            var font = role == Role.Title || role == Role.Subtitle ? _boldItalic : _bold;
            if (font == null)
            {
                Debug.LogError("FightTypography requires Rubik Bold and BoldItalic font assets.", this);
                return;
            }

            // These assets already contain the drawn bold/italic outlines. TMP's synthetic
            // Bold/Italic would add weight and shear a second time, closing letter counters.
            label.font = font;
            label.fontStyle = FontStyles.Normal;
            label.fontWeight = FontWeight.Regular;
            label.characterSpacing = 0f;
            label.wordSpacing = 0f;
            label.enableVertexGradient = false;
            if (!_materials.TryGetValue(role, out var material) || material == null)
            {
                material = CreateMaterial(font, role);
                _materials[role] = material;
            }
            label.fontSharedMaterial = material;
            label.UpdateMeshPadding();
        }

        private static Material CreateMaterial(TMP_FontAsset font, Role role)
        {
            // Clone the material with its correct atlas; never restyle the shared font asset.
            var material = new Material(font.material)
            {
                name = "Fight Rubik " + role,
                hideFlags = HideFlags.HideAndDontSave
            };
            float outline = 0f, shadow = 0f;
            switch (role)
            {
                case Role.Title: outline = 0.04f; break;
                case Role.Heading: outline = 0.15f; shadow = -0.25f; break;
                case Role.Caption: outline = 0.12f; break;
                case Role.Value: outline = 0.11f; shadow = -0.65f; break;
                case Role.Button: outline = 0.20f; shadow = -0.30f; break;
            }
            material.SetColor("_FaceColor", Color.white);
            material.SetColor("_OutlineColor", Color.black);
            material.SetFloat("_OutlineWidth", outline);
            // Matching dilation moves the keyline outside the original face, keeping its
            // natural weight. Changing outline alone leaves the font asset's old dilation.
            material.SetFloat("_FaceDilate", outline);
            material.SetFloat("_OutlineSoftness", 0f);
            material.SetFloat("_WeightNormal", 0f);
            material.SetFloat("_WeightBold", 0f);
            material.SetFloat("_Sharpness", 0.15f);
            material.SetColor("_UnderlayColor", Color.black);
            material.SetFloat("_UnderlayOffsetX", 0f);
            material.SetFloat("_UnderlayOffsetY", shadow);
            material.SetFloat("_UnderlayDilate", role == Role.Value ? 0.10f : 0f);
            material.SetFloat("_UnderlaySoftness", 0f);
            material.DisableKeyword("UNDERLAY_INNER");
            if (shadow != 0f) material.EnableKeyword("UNDERLAY_ON");
            else material.DisableKeyword("UNDERLAY_ON");
            ShaderUtilities.UpdateShaderRatios(material);
            return material;
        }

        private void OnDisable()
        {
            foreach (var material in _materials.Values)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            _materials.Clear();
            if (_instance == this) _instance = null;
        }
    }
}
