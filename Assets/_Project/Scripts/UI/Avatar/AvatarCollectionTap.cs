using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PushStars.UI
{
    /// <summary>A tap opens the collection; dragging keeps the existing turntable behavior.</summary>
    public sealed class AvatarCollectionTap : MonoBehaviour, IPointerDownHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler
    {
        public AvatarCollectionScreen Screen;
        private bool _dragged;
        private Vector2 _down;
        private Coroutine _opening;
        private void OnDisable()
        {
            if (_opening != null) StopCoroutine(_opening);
            _opening = null;
        }
        public void OnPointerDown(PointerEventData e)
        {
            _dragged = false; _down = e.position;
            if (e.button == PointerEventData.InputButton.Left && !Screen.IsOpen)
                GetComponent<UiTactile>()?.Press();
        }
        public void OnBeginDrag(PointerEventData e) { _dragged = true; GetComponent<UiTactile>()?.Release(); }
        public void OnDrag(PointerEventData e) => _dragged = true;
        public void OnPointerClick(PointerEventData e)
        {
            float threshold = EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10;
            if (!_dragged && e.button == PointerEventData.InputButton.Left &&
                Vector2.Distance(_down, e.position) <= threshold && _opening == null && !Screen.IsOpen)
                _opening = StartCoroutine(OpenAfterPress());
        }

        private IEnumerator OpenAfterPress()
        {
            var feedback = GetComponent<UiTactile>();
            if (feedback != null) feedback.Press();
            yield return new WaitForSecondsRealtime(.16f);
            if (feedback != null) feedback.Release();
            Screen.Show();
            _opening = null;
        }
    }
}
