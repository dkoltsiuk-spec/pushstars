using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Tap-to-read HUD explanations, kept inside the home screen's safe area.</summary>
    public sealed class HudCurrencyHints : MonoBehaviour, IPointerClickHandler, ICancelHandler
    {
        private readonly List<RectTransform> _pills = new List<RectTransform>();
        private readonly Vector3[] _corners = new Vector3[4];
        private RectTransform _surface, _body, _pointer, _selected;
        private TextMeshProUGUI _text;
        private Material _textMaterial;
        private UiTactile _reveal;
        public Button TrophyButton { get; set; }

        public static HudCurrencyHints Install(RectTransform panel)
        {
            var existing = panel.GetComponentInChildren<HudCurrencyHints>(true);
            if (existing != null) return existing;

            var sprites = Resources.LoadAll<Sprite>("HudCurrencyHint");
            Sprite body = null, pointer = null;
            foreach (var sprite in sprites)
            {
                if (sprite.name == "Body") body = sprite;
                if (sprite.name == "Pointer") pointer = sprite;
            }
            if (body == null || pointer == null)
            {
                Debug.LogError("HUD hint sprites are missing.");
                return null;
            }

            var root = new GameObject("CurrencyHints", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var surface = (RectTransform)root.transform;
            surface.SetParent(panel, false);
            Stretch(surface);
            root.GetComponent<Image>().color = Color.clear; // Dismiss without activating controls underneath.
            var hints = root.AddComponent<HudCurrencyHints>();
            hints._surface = surface;

            foreach (var label in panel.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (label.name != "Number" || Description(label.transform.parent.name) == null) continue;
                var pill = (RectTransform)label.transform.parent;
                hints._pills.Add(pill);
                var graphic = pill.GetComponent<Graphic>();
                if (graphic == null)
                {
                    graphic = pill.gameObject.AddComponent<Image>();
                    graphic.color = Color.clear;
                }
                graphic.raycastTarget = true;
                var button = pill.GetComponent<Button>() ?? pill.gameObject.AddComponent<Button>();
                button.targetGraphic = graphic;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => hints.Toggle(pill));
                if (pill.GetComponent<UiTactile>() == null) pill.gameObject.AddComponent<UiTactile>();
                if (hints._text == null) hints.BuildBubble(body, pointer, label.font);
            }
            root.SetActive(false);
            return hints;
        }

        private void BuildBubble(Sprite body, Sprite pointer, TMP_FontAsset font)
        {
            var image = MakeImage("Bubble", _surface, body);
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 2.5f;
            _body = image.rectTransform;
            _body.anchorMin = _body.anchorMax = new Vector2(.5f, .5f);
            _body.pivot = new Vector2(.5f, 1);
            _reveal = _body.gameObject.AddComponent<UiTactile>();
            _reveal.RevealScale = .88f;

            _pointer = MakeImage("Pointer", _body, pointer).rectTransform;
            _pointer.anchorMin = _pointer.anchorMax = new Vector2(.5f, 1);
            _pointer.pivot = new Vector2(.5f, 0);
            _pointer.sizeDelta = new Vector2(16, 12);

            _text = new GameObject("Explanation", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            _text.transform.SetParent(_body, false);
            _text.font = font;
            _text.fontSize = 12;
            _text.fontStyle = FontStyles.Normal;
            _text.color = Color.black;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.richText = true;
            _text.enableWordWrapping = true;
            _text.raycastTarget = false;
            _textMaterial = new Material(font.material) { name = "Currency hint text" };
            _textMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0);
            _textMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0);
            _textMaterial.DisableKeyword("UNDERLAY_ON");
            _textMaterial.DisableKeyword("UNDERLAY_INNER");
            _text.fontSharedMaterial = _textMaterial;
            Stretch(_text.rectTransform);
            _text.rectTransform.offsetMin = new Vector2(15, 17);
            _text.rectTransform.offsetMax = new Vector2(-19, -13);
        }

        public void Toggle(RectTransform pill)
        {
            if (_selected == pill && gameObject.activeSelf) { Hide(); return; }
            if (!_pills.Contains(pill)) return;
            _selected = pill;
            _text.text = Description(pill.name);
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            PositionBubble();
            _reveal.Reveal();
        }

        public void Hide()
        {
            _selected = null;
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_selected == null || !_selected.gameObject.activeInHierarchy) { Hide(); return; }
            PositionBubble();
        }

        private void PositionBubble()
        {
            var bounds = _surface.rect;
            float width = Mathf.Min(210, bounds.width - 16);
            float height = _text.GetPreferredValues(_text.text, width - 34, Mathf.Infinity).y + 34;
            _body.sizeDelta = new Vector2(width, height);
            _selected.GetWorldCorners(_corners);
            Vector3 left = _surface.InverseTransformPoint(_corners[0]);
            Vector3 right = _surface.InverseTransformPoint(_corners[3]);
            float targetX = (left.x + right.x) * .5f;
            float x = Mathf.Clamp(targetX, bounds.xMin + width * .5f + 8, bounds.xMax - width * .5f - 8);
            float y = Mathf.Min(left.y, right.y) - 17;
            y = Mathf.Clamp(y, bounds.yMin + height + 8, bounds.yMax - 20);
            _body.localPosition = new Vector3(x, y, 0);
            _pointer.anchoredPosition = new Vector2(Mathf.Clamp(targetX - x, -width * .5f + 22, width * .5f - 22), -1);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (TrophyButton != null && TrophyButton.IsActive() && TrophyButton.IsInteractable() &&
                RectTransformUtility.RectangleContainsScreenPoint((RectTransform)TrophyButton.transform,
                    eventData.position, eventData.pressEventCamera))
            {
                Hide();
                TrophyButton.onClick.Invoke();
                return;
            }
            foreach (var pill in _pills)
                if (pill.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(pill, eventData.position, eventData.pressEventCamera))
                { Toggle(pill); return; }
            Hide();
        }

        public void OnCancel(BaseEventData eventData) => Hide();
        private void OnDestroy()
        {
            if (_textMaterial == null) return;
            if (Application.isPlaying) Destroy(_textMaterial);
            else DestroyImmediate(_textMaterial);
        }

        private static Image MakeImage(string name, Transform parent, Sprite sprite)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).AddComponent<Image>();
            image.transform.SetParent(parent, false);
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static string Description(string pill)
        {
            switch (pill)
            {
                case "GemPill": return "<color=#35C91C>Gems</color> let you buy unique animations in the shop and special decorations.";
                case "AuraPill": return "<color=#A968E8>Aura</color> rewards training, victories and achievements. Collect Aura to unlock new characters.";
                case "StreakPill": return "<color=#E99100>Streak</color> counts consecutive training days. Train every day to keep your streak and earn bonuses.";
                default: return null;
            }
        }
    }
}
