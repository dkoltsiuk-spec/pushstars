using System;
using PushStars.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// "Leave the duel?" over a live set. Built at runtime on the HUD (no scene regeneration), two
    /// answers: STAY, the big one, and LEAVE. It is not a pause — the duel runs on underneath —
    /// so it says its piece in two lines and gets out of the way.
    ///
    /// <para>Drawn in the app's own kit: a blue plate with a black keyline and a hard drop shadow
    /// (the battle-settings sheet's gradient), the yellow slanted action button of the result
    /// screen for STAY, and a red one — the search's CANCEL — for LEAVE. All of it procedural
    /// (<see cref="RewardPlateGraphic"/>), so it needs no sprite loaded at runtime.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LiveExitConfirm : MonoBehaviour
    {
        // Blended in linear space: .82 reads on screen as the ~55% dim a gamma-space .55 would.
        private static readonly Color Dim = new Color(0f, .01f, .06f, .82f);
        private static readonly Color CardTop = new Color32(10, 62, 226, 255), CardBottom = new Color32(46, 154, 255, 255);
        private static readonly Color StayTop = new Color32(255, 220, 30, 255), StayBottom = new Color32(255, 192, 0, 255);
        private static readonly Color LeaveTop = new Color32(255, 84, 84, 255), LeaveBottom = new Color32(214, 30, 44, 255);
        private static readonly Color Keyline = new Color32(6, 8, 22, 255);
        private const float ButtonSlant = 12f;

        private RectTransform _root;
        private TextMeshProUGUI _title, _body;
        private Action _onLeave;

        public bool Showing => _root != null && _root.gameObject.activeSelf;

        /// <summary>On the HUD's root canvas, so the dim covers the whole screen whatever rect the
        /// HUD's own object has.</summary>
        public static LiveExitConfirm Attach(Transform hud)
        {
            var canvas = hud.GetComponentInParent<Canvas>();
            var host = canvas != null ? canvas.rootCanvas.transform : hud;
            var confirm = host.GetComponent<LiveExitConfirm>();
            return confirm != null ? confirm : host.gameObject.AddComponent<LiveExitConfirm>();
        }

        public void Show(string title, string body, Action onLeave)
        {
            Build();
            _onLeave = onLeave;
            _title.text = title; _body.text = body;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
        }

        public void Hide()
        {
            _onLeave = null;
            if (_root != null) _root.gameObject.SetActive(false);
        }

        private void Build()
        {
            if (_root != null) return;
            _root = Rect("LiveExitConfirm", transform);
            Stretch(_root);
            // Its own canvas, sorted over every HUD layer (banners, rep pop-ups, the stages).
            var canvas = _root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true; canvas.sortingOrder = 500;
            _root.gameObject.AddComponent<GraphicRaycaster>();
            var shade = Rect("Dim", _root);
            Stretch(shade);
            var dim = shade.gameObject.AddComponent<Image>();
            dim.color = Dim;   // and a tap outside the card does nothing: the answer has to be chosen

            var card = Rect("Card", _root);
            card.sizeDelta = new Vector2(326, 300);
            Plate(card, CardTop, CardBottom, 20, 4, 7, 0, .08f);

            _title = Label("Title", card, 30, FightTypography.Role.Heading, new Vector2(0, 108), new Vector2(300, 42));
            _body = Label("Body", card, 17, FightTypography.Role.Caption, new Vector2(0, 54), new Vector2(296, 52));

            // ~25 px of air between STAY's shadow and LEAVE, so the second answer is never a slip.
            Button("Stay", card, "STAY", StayTop, StayBottom, new Vector2(0, -24), new Vector2(232, 62), 28, Hide);
            Button("Leave", card, "LEAVE", LeaveTop, LeaveBottom, new Vector2(0, -108), new Vector2(170, 42), 19, () =>
            {
                var leave = _onLeave;
                Hide();
                leave?.Invoke();
            });
        }

        private static RewardPlateGraphic Plate(RectTransform rect, Color top, Color bottom, float radius, float border,
            float shadow, float slant, float gloss)
        {
            var plate = rect.gameObject.AddComponent<RewardPlateGraphic>();
            plate.Top = top; plate.Bottom = bottom; plate.Border = Keyline;
            plate.Shadow = new Color(Keyline.r, Keyline.g, Keyline.b, .95f);
            plate.Radius = radius; plate.BorderWidth = border; plate.ShadowOffset = shadow;
            plate.Slant = slant; plate.Gloss = gloss;
            return plate;
        }

        private static void Button(string name, RectTransform parent, string text, Color top, Color bottom, Vector2 at,
            Vector2 size, float fontSize, Action onClick)
        {
            var rect = Rect(name, parent);
            rect.anchoredPosition = at; rect.sizeDelta = size;
            var plate = Plate(rect, top, bottom, 12, 3.5f, size.y > 50 ? 7 : 5, ButtonSlant, .12f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.pressedColor = new Color(.82f, .82f, .82f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            var label = Label("Label", rect, fontSize, FightTypography.Role.Button, new Vector2(0, 2), size);
            label.text = text;
        }

        private static TextMeshProUGUI Label(string name, RectTransform parent, float size, FightTypography.Role role, Vector2 at, Vector2 box)
        {
            var rect = Rect(name, parent);
            rect.anchoredPosition = at; rect.sizeDelta = box;
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.fontSize = size; label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false; label.textWrappingMode = TextWrappingModes.Normal;
            FightTypography.Apply(label, role);
            return label;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.gameObject.layer = parent.gameObject.layer;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }
    }
}
