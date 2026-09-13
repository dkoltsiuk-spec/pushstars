using PushStars.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Installs press feedback on authored scenes and newly created runtime controls.</summary>
    public sealed class UiSoundInstaller : MonoBehaviour
    {
        private Selectable[] _controls = new Selectable[128];
        private float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var audio = GameAudio.Ensure();
            if (audio != null && audio.GetComponent<UiSoundInstaller>() == null)
                audio.gameObject.AddComponent<UiSoundInstaller>();
        }

        private void OnEnable() { SceneManager.sceneLoaded += SceneLoaded; Install(); }
        private void OnDisable() => SceneManager.sceneLoaded -= SceneLoaded;
        private void SceneLoaded(Scene scene, LoadSceneMode mode) => Install();
        private void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + .25f;
            Install();
        }
        private void Install()
        {
            int capacity = Selectable.allSelectableCount;
            if (_controls.Length < capacity) _controls = new Selectable[capacity + 32];
            int count = Selectable.AllSelectablesNoAlloc(_controls);
            for (int i = 0; i < count; i++)
            {
                var control = _controls[i];
                if (control == null || control.GetComponent<UiSoundFeedback>() != null) continue;
                var feedback = control.gameObject.AddComponent<UiSoundFeedback>();
                if (control.GetComponent<TabButton>() != null) feedback.Configure(SoundCue.Navigate);
                else if (control.GetComponent<PrimaryButton>() != null || control.GetComponent<ReadyButton>() != null)
                    feedback.Configure(SoundCue.Confirm);
                else if (control.GetComponent<ExitButton>() != null) feedback.Configure(SoundCue.Back);
            }
            // Don't retain destroyed scene objects between scans.
            System.Array.Clear(_controls, 0, _controls.Length);
        }
    }
}
