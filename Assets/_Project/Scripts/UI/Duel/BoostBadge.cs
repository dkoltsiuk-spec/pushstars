using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// A chunky "x2" badge on the BATTLE button's corner while the player holds case boosts: the Aura
    /// skull and a big outlined "x2" on a purple plate with a black keyline and drop shadow, like the
    /// game's other buttons. More than one boost adds a red counter bubble. It bobs gently and
    /// disappears once the last boost is spent.
    /// </summary>
    public sealed class BoostBadge : MonoBehaviour
    {
        private static readonly Color32 Ink = new Color32(18, 12, 34, 255), Plate = new Color32(122, 52, 232, 255),
            Shine = new Color32(255, 255, 255, 46), Counter = new Color32(238, 44, 64, 255);

        /// <summary>The badge is drawn at 92×44 and shown at this scale.</summary>
        private const float Size = .85f;

        private CanvasGroup _group;
        private GameObject _counter;
        private TextMeshProUGUI _count;
        private int _shown = -1;

        public static void Attach(Component button)
        {
            if (button == null || button.GetComponentInChildren<BoostBadge>(true) != null) return;
            var font = button.GetComponentInChildren<TextMeshProUGUI>(true);
            var root = ArenaUi.Rect(button.transform, "BoostBadge", new Vector2(92, 44), Vector2.zero);
            root.anchorMin = root.anchorMax = new Vector2(1, 1);
            root.anchoredPosition = new Vector2(-16, 0);
            root.localScale = Vector3.one * Size;
            var badge = root.gameObject.AddComponent<BoostBadge>();
            badge._group = root.gameObject.AddComponent<CanvasGroup>();
            badge._group.blocksRaycasts = false;

            Rounded(root, "Shadow", new Vector2(92, 44), new Vector2(0, -4), 15, new Color32(0, 0, 0, 110));
            Rounded(root, "Keyline", new Vector2(92, 44), Vector2.zero, 15, Ink);
            Rounded(root, "Plate", new Vector2(86, 38), Vector2.zero, 12, Plate);
            Rounded(root, "Shine", new Vector2(80, 14), new Vector2(0, 9), 7, Shine);

            var aura = Resources.Load<PushStarsTheme>("PushStarsTheme")?.IconAura;
            if (aura != null)
            {
                var icon = ArenaUi.Rect(root, "Aura", new Vector2(34, 34), new Vector2(-24, 1)).gameObject.AddComponent<Image>();
                icon.sprite = aura; icon.preserveAspect = true; icon.raycastTarget = false;
            }
            var label = ArenaUi.Label(root, "Label", "x2", new Vector2(52, 40), new Vector2(aura != null ? 15 : 0, 1), 25);
            Letter(label, font);

            badge._counter = ArenaUi.Rect(root, "Counter", new Vector2(24, 24), new Vector2(44, 20)).gameObject;
            var bubble = (RectTransform)badge._counter.transform;
            Rounded(bubble, "Keyline", new Vector2(24, 24), Vector2.zero, 12, Ink);
            Rounded(bubble, "Fill", new Vector2(19, 19), Vector2.zero, 9.5f, Counter);
            badge._count = ArenaUi.Label(bubble, "Count", "2", new Vector2(24, 24), new Vector2(0, 1), 15);
            Letter(badge._count, font);
            badge.Refresh();
        }

        private static void Rounded(Transform parent, string name, Vector2 size, Vector2 position, float radius, Color32 color)
        {
            var shape = ArenaUi.Rect(parent, name, size, position).gameObject.AddComponent<ArenaRoundedGraphic>();
            shape.Radius = radius; shape.color = color; shape.raycastTarget = false;
        }

        /// <summary>The BATTLE label's font with only a thin dark outline: the button's own heavy
        /// outline made the small badge read as a blob.</summary>
        private static void Letter(TextMeshProUGUI label, TextMeshProUGUI source)
        {
            if (source != null && source.font != null) { label.font = source.font; label.fontSharedMaterial = source.font.material; }
            label.outlineWidth = .09f;
            label.outlineColor = Ink;
            // Thinner strokes than the bold face: shrink the glyphs a touch inside their outline.
            label.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, -.14f);
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
        }

        private void Refresh()
        {
            int boosts = CaseRewards.Boosts;
            if (boosts == _shown) return;
            _shown = boosts;
            _group.alpha = boosts > 0 ? 1 : 0;
            _counter.SetActive(boosts > 1);
            _count.text = boosts.ToString();
        }

        private void Update()
        {
            Refresh();
            if (_shown > 0) transform.localScale = Vector3.one * Size * (1f + .04f * Mathf.Sin(Time.unscaledTime * 3f));
        }
    }
}
