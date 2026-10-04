using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Full-screen charcoal search background, dark bolts and a warm VS halo.</summary>
    public sealed class SearchScreenAppearance : MonoBehaviour
    {
        private readonly Dictionary<TextMeshProUGUI, Material> _textMaterials = new Dictionary<TextMeshProUGUI, Material>();
        private RectTransform _halo, _ring;

        public static void Ensure(GameObject overlay)
        {
            var appearance = overlay.GetComponent<SearchScreenAppearance>() ?? overlay.AddComponent<SearchScreenAppearance>();
            appearance.Apply();
        }

        public void Apply()
        {
            var background = transform.Find("Background")?.GetComponent<Image>();
            if (background != null)
            {
                background.sprite = null;
                background.color = new Color32(31, 34, 41, 255);
                background.type = Image.Type.Simple;
                background.raycastTarget = true;
            }

            var field = GetComponentInChildren<LightningField>(true);
            if (field != null)
            {
                var darkBolt = Resources.Load<Sprite>("SearchDarkLightning");
                field.gameObject.SetActive(true);
                foreach (var bolt in field.GetComponentsInChildren<Image>(true))
                {
                    if (darkBolt != null) bolt.sprite = darkBolt;
                    bolt.material = null;
                    bolt.color = new Color(1, 1, 1, bolt.color.a);
                }
            }

            var content = transform.Find("SafeArea/Content");
            if (content == null) return;
            _ring = content.Find("RingWrap") as RectTransform;
            var dashes = content.Find("RingWrap/LoadingVsRing/Ring");
            if (dashes != null) dashes.gameObject.SetActive(false);
            var badge = content.Find("RingWrap/LoadingVsRing/VsBadge") as RectTransform;
            if (badge != null) badge.sizeDelta = new Vector2(88, 88);

            if (_halo == null) _halo = transform.Find("SearchWarmHalo") as RectTransform;
            if (_halo == null)
            {
                var glow = new GameObject("SearchWarmHalo", typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
                _halo = glow.rectTransform;
                _halo.SetParent(transform, false);
                _halo.anchorMin = _halo.anchorMax = _halo.pivot = new Vector2(.5f, .5f);
                _halo.sizeDelta = new Vector2(430, 430);
                var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
                glow.sprite = theme != null ? theme.GlowRadial : null;
                glow.color = new Color(1f, .64f, .08f, .10f);
                glow.raycastTarget = false;
                _halo.SetSiblingIndex(1);
            }
            _halo.GetComponent<Image>().color = new Color(1f, .64f, .08f, .10f);

            Style(content.Find("Title")?.GetComponent<TextMeshProUGUI>(), 20);
            Style(content.Find("TipBlock/Header")?.GetComponent<TextMeshProUGUI>(), 18);
            var tip = content.Find("TipBlock/Body")?.GetComponent<TextMeshProUGUI>();
            Style(tip, 16);
            if (tip != null) tip.text = "Use Aura to buy unique animations in the shop.";
            content.Find("ExitButton")?.GetComponent<ExitButton>()?.SetLabel("CANCEL");
            PositionHalo();
        }

        private void Style(TextMeshProUGUI label, float size)
        {
            if (label == null) return;
            if (!_textMaterials.TryGetValue(label, out var material))
            {
                material = new Material(label.font.material) { name = "Search clean text" };
                material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0);
                material.SetFloat(ShaderUtilities.ID_FaceDilate, 0);
                material.DisableKeyword("UNDERLAY_ON");
                material.DisableKeyword("UNDERLAY_INNER");
                _textMaterials.Add(label, material);
            }
            label.fontSharedMaterial = material;
            label.color = new Color32(202, 210, 246, 255);
            label.fontSize = size;
            label.fontStyle = FontStyles.Normal;
        }

        private void LateUpdate() => PositionHalo();
        private void PositionHalo()
        {
            if (_halo != null && _ring != null) _halo.position = _ring.position;
        }

        private void OnDestroy()
        {
            foreach (var material in _textMaterials.Values) Release(material);
        }

        private static void Release(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
