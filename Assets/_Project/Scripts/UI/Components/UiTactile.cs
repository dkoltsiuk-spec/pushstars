using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>One scale owner for staggered reveals and cancellable press feedback. Unscaled time.</summary>
    [DisallowMultipleComponent]
    public sealed class UiTactile : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IPointerExitHandler, IBeginDragHandler, ICancelHandler, ISubmitHandler
    {
        public float PressScale = .94f;
        public float RevealScale = .84f;
        [Tooltip("Optional visual target. Keeps the pointer hit area stationary while animating a 3D avatar.")]
        public Transform MotionTarget;
        private Transform Visual => MotionTarget != null ? MotionTarget : transform;
        private Vector3 _rest;
        private CanvasGroup _group;
        private Selectable _selectable;
        private bool _initialized, _held, _revealing;
        private int _pointer = int.MinValue;
        private float _press = 1, _from = 1, _to = 1, _pressStart, _pressDuration;
        private float _revealStart, _submitRelease;
        public bool IsRevealing => _revealing;
        public float RevealProgress => !_revealing ? 1 : Mathf.Clamp01((Time.unscaledTime - _revealStart) / .28f);

        private void Awake() => Initialize();
        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _rest = Visual.localScale;
            _selectable = GetComponent<Selectable>();
            _group = GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        }

        public void Reveal(float delay = 0)
        {
            Initialize();
            ResetVisual();
            _revealStart = Time.unscaledTime + delay;
            _revealing = true;
            _group.alpha = 0;
            _group.interactable = false;
            Visual.localScale = _rest * RevealScale;
        }

        private void Update()
        {
            if (_held && ((_selectable != null && !_selectable.IsInteractable()) ||
                (_submitRelease > 0 && Time.unscaledTime >= _submitRelease))) Release();
            float t = _pressDuration <= 0 ? 1 : Mathf.Clamp01((Time.unscaledTime - _pressStart) / _pressDuration);
            _press = Mathf.LerpUnclamped(_from, _to, _to == 1 ? UITween.EaseOutBackSoft(t) : UITween.EaseOutCubic(t));
            float reveal = 1;
            if (_revealing)
            {
                float progress = RevealProgress;
                reveal = Mathf.LerpUnclamped(RevealScale, 1, UITween.EaseOutBackSoft(progress));
                _group.alpha = Mathf.Clamp01(progress * 1.8f);
                _group.interactable = progress >= .75f;
                if (progress >= 1) { _revealing = false; _group.alpha = 1; }
            }
            Visual.localScale = _rest * (reveal * _press);
        }

        public void Press()
        {
            Initialize();
            if (_revealing || (_selectable != null && !_selectable.IsInteractable())) return;
            _held = true; AnimatePress(PressScale, .075f);
        }
        public void Release()
        {
            _held = false; _pointer = int.MinValue; _submitRelease = 0;
            AnimatePress(1, .19f);
        }
        private void AnimatePress(float target, float duration)
        { _from = _press; _to = target; _pressDuration = duration; _pressStart = Time.unscaledTime; }
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || _pointer != int.MinValue) return;
            _pointer = e.pointerId; Press();
        }
        public void OnPointerUp(PointerEventData e) { if (e.pointerId == _pointer) Release(); }
        public void OnPointerExit(PointerEventData e) { if (e.pointerId == _pointer) Release(); }
        public void OnBeginDrag(PointerEventData e) => Release();
        public void OnCancel(BaseEventData e) => Release();
        public void OnSubmit(BaseEventData e) { Press(); _submitRelease = Time.unscaledTime + .08f; }
        private void OnDisable() { if (_initialized) ResetVisual(); }
        public void ResetVisual()
        {
            Initialize();
            _held = _revealing = false; _pointer = int.MinValue; _submitRelease = 0;
            _press = _from = _to = 1; _pressDuration = 0;
            Visual.localScale = _rest;
            _group.alpha = 1; _group.interactable = true;
        }
    }
}
