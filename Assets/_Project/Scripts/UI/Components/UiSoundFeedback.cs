using PushStars.Core;
using PushStars.UI.Layout;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Press feedback independent of onClick listeners rebuilt by screen presenters.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Selectable))]
    public sealed class UiSoundFeedback : MonoBehaviour, IPointerDownHandler, ISubmitHandler
    {
        [SerializeField] private SoundCue _cue = SoundCue.Tap;
        private Selectable _control;
        private void Awake() => _control = GetComponent<Selectable>();
        public void Configure(SoundCue cue) => _cue = cue;
        public void OnPointerDown(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) Play();
        }
        public void OnSubmit(BaseEventData data) => Play();
        private void Play()
        {
            if (_control != null && _control.IsActive() && _control.IsInteractable() && !ScreenLayoutRoot.IsAnyEditing)
                GameAudio.Play(_cue);
        }
    }
}
