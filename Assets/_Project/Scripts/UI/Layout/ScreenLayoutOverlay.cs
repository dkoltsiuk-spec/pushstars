using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PushStars.UI.Layout
{
    /// <summary>Temporary input shield: dragging UI never presses the gameplay buttons beneath it.</summary>
    public sealed class ScreenLayoutOverlay : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private ScreenLayoutRoot _owner;
        private RectTransform _rect;
        private RectTransform _selected;
        private RectTransform _outline;
        private TextMeshProUGUI _caption;
        private Vector2 _dragStart;
        private Vector2 _positionStart;
        private int _pointerId = int.MinValue;
        private readonly Vector3[] _corners = new Vector3[4];

        public static ScreenLayoutOverlay Open(ScreenLayoutRoot owner)
        {
            var parentCanvas = owner.GetComponentInParent<Canvas>();
            var go = new GameObject("__LayoutEditor", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));
            go.transform.SetParent(parentCanvas != null ? parentCanvas.rootCanvas.transform : owner.transform, false);
            var rect = (RectTransform)go.transform;
            Stretch(rect);
            var canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30000;
            var shield = go.GetComponent<Image>();
            shield.color = new Color(0.02f, 0.03f, 0.09f, 0.12f);
            shield.raycastTarget = true;
            var overlay = go.AddComponent<ScreenLayoutOverlay>();
            overlay._owner = owner;
            overlay._rect = rect;
            overlay.BuildControls();
            return overlay;
        }

        public static GameObject CreateLauncher(ScreenLayoutRoot owner)
        {
            var button = MakeButton(owner.transform, "__LayoutEditButton", "ДВИГАТЬ UI", owner.BeginEditing,
                new Color(0.07f, 0.09f, 0.17f, 0.86f));
            var rect = (RectTransform)button.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = new Vector2(108f, 30f);
            float safeBottom = 0f;
            if (owner.GetComponent<Canvas>() != null && Screen.height > 0)
                safeBottom = Screen.safeArea.yMin * ((RectTransform)owner.transform).rect.height / Screen.height;
            rect.anchoredPosition = new Vector2(-10f, safeBottom + 10f);
            return button.gameObject;
        }

        private void BuildControls()
        {
            var outlineObject = new GameObject("Selection", typeof(RectTransform), typeof(Image), typeof(Outline));
            outlineObject.transform.SetParent(transform, false);
            _outline = (RectTransform)outlineObject.transform;
            _outline.anchorMin = _outline.anchorMax = new Vector2(0.5f, 0.5f);
            var image = outlineObject.GetComponent<Image>();
            image.color = new Color(1f, 0.87f, 0.16f, 0.08f);
            image.raycastTarget = false;
            var border = outlineObject.GetComponent<Outline>();
            border.effectColor = new Color(1f, 0.86f, 0.16f, 1f);
            border.effectDistance = new Vector2(2f, 2f);
            _outline.gameObject.SetActive(false);

            var toolbarObject = new GameObject("Toolbar", typeof(RectTransform), typeof(Image));
            toolbarObject.transform.SetParent(transform, false);
            var toolbar = (RectTransform)toolbarObject.transform;
            toolbar.anchorMin = new Vector2(0f, 1f);
            toolbar.anchorMax = new Vector2(1f, 1f);
            toolbar.pivot = new Vector2(0.5f, 1f);
            toolbar.sizeDelta = new Vector2(0f, 132f);
            float safeTop = Screen.height > 0 ? (Screen.height - Screen.safeArea.yMax) * _rect.rect.height / Screen.height : 0f;
            toolbar.anchoredPosition = new Vector2(0f, -Mathf.Max(10f, safeTop));
            toolbarObject.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.09f, 0.96f);

            _caption = MakeText(toolbar, "Caption", "Нажми на элемент и перетащи", 14f);
            _caption.rectTransform.anchorMin = new Vector2(0f, 1f);
            _caption.rectTransform.anchorMax = Vector2.one;
            _caption.rectTransform.pivot = new Vector2(0.5f, 1f);
            _caption.rectTransform.anchoredPosition = new Vector2(0f, -6f);
            _caption.rectTransform.sizeDelta = new Vector2(-16f, 27f);

            PlaceButton(MakeButton(toolbar, "Save", "СОХРАНИТЬ", _owner.SaveLayout, new Color(0.94f, 0.74f, 0.03f)), 0f, 0.36f, -38f, 36f);
            PlaceButton(MakeButton(toolbar, "Cancel", "ОТМЕНА", _owner.CancelEditing, new Color(0.18f, 0.22f, 0.34f)), 0.36f, 0.68f, -38f, 36f);
            PlaceButton(MakeButton(toolbar, "Reset", "СБРОС", _owner.ResetToDefaults, new Color(0.18f, 0.22f, 0.34f)), 0.68f, 1f, -38f, 36f);
            PlaceButton(MakeButton(toolbar, "Smaller", "− РАЗМЕР", () => Resize(0.95f), new Color(0.12f, 0.16f, 0.26f)), 0f, 0.34f, -82f, 34f);
            PlaceButton(MakeButton(toolbar, "Next", "ЭЛЕМЕНТ →", SelectNext, new Color(0.12f, 0.16f, 0.26f)), 0.34f, 0.68f, -82f, 34f);
            PlaceButton(MakeButton(toolbar, "Larger", "+ РАЗМЕР", () => Resize(1.05f), new Color(0.12f, 0.16f, 0.26f)), 0.68f, 1f, -82f, 34f);
        }

        private static void PlaceButton(Button button, float left, float right, float y, float height)
        {
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(left, 1f);
            rect.anchorMax = new Vector2(right, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(5f, y - height);
            rect.offsetMax = new Vector2(-5f, y);
        }

        private static Button MakeButton(Transform parent, string name, string label, UnityEngine.Events.UnityAction clicked, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(clicked);
            var text = MakeText(go.transform, "Label", label, 12f);
            Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(4f, 2f);
            text.rectTransform.offsetMax = new Vector2(-4f, -2f);
            text.color = color.r > 0.5f ? new Color(0.04f, 0.04f, 0.08f) : Color.white;
            return button;
        }

        private static TextMeshProUGUI MakeText(Transform parent, string name, string value, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
            text.text = value;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = 8f;
            text.fontSizeMax = size;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointerId != int.MinValue && _pointerId != eventData.pointerId) return;
            RectTransform hit = null;
            float smallestArea = float.PositiveInfinity;
            foreach (var target in _owner.Targets)
            {
                if (target.rect == null || !target.rect.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(target.rect, eventData.position, eventData.pressEventCamera)) continue;
                float area = Mathf.Abs(target.rect.rect.width * target.rect.rect.height * target.rect.lossyScale.x * target.rect.lossyScale.y);
                if (area < smallestArea) { hit = target.rect; smallestArea = area; }
            }
            // Keeping the current selection on empty space also lets a target hidden behind
            // the toolbar be moved after selecting it with the "next element" button.
            if (hit != null) Select(hit);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_selected == null || (_pointerId != int.MinValue && _pointerId != eventData.pointerId)) return;
            var parent = _selected.parent as RectTransform;
            if (parent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out _dragStart)) return;
            _positionStart = _selected.anchoredPosition;
            _pointerId = eventData.pointerId;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_selected == null || _pointerId != eventData.pointerId) return;
            var parent = _selected.parent as RectTransform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, eventData.position, eventData.pressEventCamera, out var point))
                _selected.anchoredPosition = _positionStart + point - _dragStart;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_pointerId == eventData.pointerId) _pointerId = int.MinValue;
        }

        private void Select(RectTransform target)
        {
            _selected = target;
            _caption.text = target != null ? target.name + " • перетащи / измени размер" : "Нажми на элемент и перетащи";
            _outline.gameObject.SetActive(target != null);
        }

        private void SelectNext()
        {
            var targets = _owner.Targets;
            if (targets.Count == 0) return;
            int index = -1;
            for (int i = 0; i < targets.Count; i++) if (targets[i].rect == _selected) index = i;
            for (int i = 1; i <= targets.Count; i++)
            {
                var candidate = targets[(index + i) % targets.Count].rect;
                if (candidate != null && candidate.gameObject.activeInHierarchy) { Select(candidate); return; }
            }
        }

        private void Resize(float factor)
        {
            if (_selected == null) return;
            _selected.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(8f, _selected.rect.width * factor));
            _selected.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(8f, _selected.rect.height * factor));
        }

        private void LateUpdate()
        {
            if (_selected == null) return;
            _selected.GetWorldCorners(_corners);
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var corner in _corners)
            {
                Vector2 point = _rect.InverseTransformPoint(corner);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            _outline.anchoredPosition = (min + max) * 0.5f;
            _outline.sizeDelta = max - min;
        }
    }
}
