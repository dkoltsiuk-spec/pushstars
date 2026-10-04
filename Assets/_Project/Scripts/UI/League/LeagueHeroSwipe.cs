using PushStars.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Two neighbouring hero panels follow the finger and settle horizontally.</summary>
    public sealed class LeagueHeroSwipe : MonoBehaviour, IInitializePotentialDragHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public LeagueView View;
        public ScrollRect PageScroll;
        public bool IsMoving => _dragging || _settling;
        public bool IsSettling => _settling;
        public float SlideOffset => _offset;
        private Vector2 _start, _heroRest, _titleRest, _previewRest;
        private int _axis, _direction;
        private bool _dragging, _settling, _captured, _scrollEnabled;
        private float _offset, _from, _target, _elapsed;
        private Image _preview;
        private TextMeshProUGUI _previewTitle;
        private RectTransform Art => PageScroll != null ? PageScroll.content : (RectTransform)View.transform;
        private float PageWidth => Art.rect.width;

        public void OnInitializePotentialDrag(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) PageScroll?.OnInitializePotentialDrag(e);
        }
        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || _settling || View == null) return;
            var entrance = View.GetComponent<LeagueEntrance>();
            if (entrance != null && entrance.IsPlaying) entrance.SendMessage("Finish");
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Art, e.pressPosition, e.pressEventCamera, out _start);
            _heroRest = View.Hero.rectTransform.anchoredPosition;
            _titleRest = View.Title.rectTransform.anchoredPosition;
            _captured = true; _axis = 0; _dragging = true; _offset = 0;
            _scrollEnabled = PageScroll != null && PageScroll.enabled;
            PageScroll?.StopMovement();
        }
        public void OnDrag(PointerEventData e)
        {
            if (!_dragging) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Art, e.position, e.pressEventCamera, out var point);
            var delta = point - _start;
            if (_axis == 0 && delta.sqrMagnitude >= 36)
            {
                _axis = Mathf.Abs(delta.x) > Mathf.Abs(delta.y) * 1.2f ? 1 : 2;
                if (_axis == 2) PageScroll?.OnBeginDrag(e);
                else if (PageScroll != null) PageScroll.enabled = false;
            }
            if (_axis == 2) { PageScroll?.OnDrag(e); return; }
            if (_axis != 1) return;
            int direction = delta.x < 0 ? 1 : -1;
            PrepareNeighbour(direction);
            SetOffset(Mathf.Clamp(delta.x * (_preview != null ? 1 : .22f), -PageWidth, PageWidth));
        }
        public void OnEndDrag(PointerEventData e)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_axis == 2) { PageScroll?.OnEndDrag(e); _captured = false; return; }
            if (_axis != 1) { CancelSlide(); return; }
            bool commit = _preview != null && Mathf.Abs(_offset) >= PageWidth * .18f;
            _from = _offset; _target = commit ? -_direction * PageWidth : 0;
            _elapsed = 0; _settling = true;
        }
        private void Update() => Advance(Time.unscaledDeltaTime);
        private void Advance(float deltaTime)
        {
            if (!_settling) return;
            _elapsed += deltaTime;
            float t = Mathf.Clamp01(_elapsed / .28f);
            SetOffset(Mathf.Lerp(_from, _target, 1 - Mathf.Pow(1 - t, 3)));
            if (t < 1) return;
            int commit = _target == 0 ? 0 : _direction;
            CancelSlide();
            if (commit != 0) View.BrowseLeague(commit);
        }
        private void PrepareNeighbour(int direction)
        {
            if (_direction == direction && _preview != null) return;
            RemovePreview(); _direction = direction;
            int index = System.Array.FindIndex(Leagues.All, l => l.Id == View.ViewedLeagueId) + direction;
            if (index < 0 || index >= Leagues.All.Length) return;
            var sprite = View.HeroForLeague(Leagues.All[index].Id);
            var go = new GameObject("LeagueSwipePreview", typeof(RectTransform), typeof(Image));
            go.hideFlags = HideFlags.DontSave; go.layer = gameObject.layer;
            _preview = go.GetComponent<Image>();
            _preview.transform.SetParent(View.Hero.transform.parent, false);
            CopyRect(View.Hero.rectTransform, _preview.rectTransform);
            _preview.sprite = sprite; _preview.preserveAspect = true; _preview.raycastTarget = false;
            _previewRest = View.HeroPositionFor(sprite);
            var titleObject = new GameObject("LeagueSwipeTitlePreview", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleObject.hideFlags = HideFlags.DontSave; titleObject.layer = gameObject.layer;
            _previewTitle = titleObject.GetComponent<TextMeshProUGUI>();
            _previewTitle.transform.SetParent(View.Title.transform.parent, false);
            CopyRect(View.Title.rectTransform, _previewTitle.rectTransform);
            _previewTitle.font = View.Title.font; _previewTitle.fontSharedMaterial = View.Title.fontSharedMaterial;
            _previewTitle.fontSize = View.Title.fontSizeMax; _previewTitle.enableAutoSizing = true;
            _previewTitle.fontSizeMin = View.Title.fontSizeMin; _previewTitle.fontSizeMax = View.Title.fontSizeMax;
            _previewTitle.characterSpacing = View.Title.characterSpacing;
            _previewTitle.alignment = View.Title.alignment; _previewTitle.textWrappingMode = TextWrappingModes.NoWrap;
            _previewTitle.raycastTarget = false; _previewTitle.color = LeagueVisualStyle.Colors[index];
            _previewTitle.text = Leagues.All[index].DisplayName.ToUpperInvariant() + " LEAGUE";
        }
        private void SetOffset(float value)
        {
            _offset = value;
            View.Hero.rectTransform.anchoredPosition = _heroRest + Vector2.right * value;
            View.Title.rectTransform.anchoredPosition = _titleRest + Vector2.right * value;
            if (_preview != null) _preview.rectTransform.anchoredPosition = _previewRest + Vector2.right * (value + _direction * PageWidth);
            if (_previewTitle != null) _previewTitle.rectTransform.anchoredPosition = _titleRest + Vector2.right * (value + _direction * PageWidth);
        }
        public void CancelSlide()
        {
            if (_captured && View != null)
            {
                View.Hero.rectTransform.anchoredPosition = _heroRest;
                View.Title.rectTransform.anchoredPosition = _titleRest;
                if (PageScroll != null) PageScroll.enabled = _scrollEnabled;
            }
            _dragging = _settling = _captured = false; _offset = 0; _axis = _direction = 0;
            RemovePreview();
        }
        private void RemovePreview()
        {
            if (_preview != null) Remove(_preview.gameObject);
            if (_previewTitle != null) Remove(_previewTitle.gameObject);
            _preview = null; _previewTitle = null;
        }
        private static void Remove(GameObject go) { go.SetActive(false); if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
        private static void CopyRect(RectTransform source, RectTransform target)
        {
            target.anchorMin = source.anchorMin; target.anchorMax = source.anchorMax; target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta; target.localScale = source.localScale; target.localRotation = source.localRotation;
        }
        private void OnDisable() => CancelSlide();
    }
}