using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace PushStars.UI
{
    /// <summary>Prepares ahead of the placement step; plays only while its panel is visible.</summary>
    [DisallowMultipleComponent]
    public sealed class PlacementTutorialVideo : MonoBehaviour
    {
        [SerializeField] private VideoClip _clip;
        [SerializeField] private Texture _poster;
        [SerializeField] private RawImage _display;
        private VideoPlayer _player;
        private bool _preparing, _applicationPaused;

        private void Awake() => EnsurePlayer(gameObject);

        // The panel is inactive until placement. Host the decoder on the active guide so
        // Prepare can finish during the earlier onboarding steps without showing the panel.
        public void Preload(GameObject host)
        {
            EnsurePlayer(host);
            Prepare();
        }

        private void EnsurePlayer(GameObject host)
        {
            if (_player != null) return;
            _player = host.AddComponent<VideoPlayer>();
            _player.playOnAwake = false;
            _player.isLooping = true;
            _player.waitForFirstFrame = true;
            _player.renderMode = VideoRenderMode.APIOnly;
            _player.audioOutputMode = VideoAudioOutputMode.None;
            _player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            _player.source = VideoSource.VideoClip;
            _player.clip = _clip;
            _player.prepareCompleted += Prepared;
            _player.errorReceived += Failed;
        }

        private void OnEnable() => Begin();

        private void Begin()
        {
            _display.texture = _poster;
            if (_applicationPaused) return;
            if (_player.isPrepared) _player.Play();
            else Prepare();
        }

        private void Prepare()
        {
            if (_clip == null || _preparing || _player.isPrepared) return;
            _preparing = true;
            _player.Prepare();
        }

        private void Prepared(VideoPlayer player)
        {
            _preparing = false;
            if (isActiveAndEnabled && !_applicationPaused) player.Play();
        }

        private void Update()
        {
            if (_player.isPlaying && _player.frame >= 0 && _player.texture != null)
                _display.texture = _player.texture;
        }

        private void OnDisable()
        {
            Rewind();
            if (_display != null) _display.texture = _poster;
        }

        private void Rewind()
        {
            if (_player == null || !_player.isPrepared) return;
            // Stop discards the prepared decoder and causes another startup delay.
            _player.Pause();
            _player.frame = 0;
        }

        private void OnApplicationPause(bool paused)
        {
            _applicationPaused = paused;
            if (!isActiveAndEnabled) return;
            if (paused) Rewind();
            else Begin();
        }

        private void Failed(VideoPlayer player, string message)
        {
            player.Stop();
            _preparing = false;
            _display.texture = _poster;
            Debug.LogWarning("[Onboarding] Placement video unavailable: " + message, this);
        }

        private void OnDestroy()
        {
            if (_player == null) return;
            _player.prepareCompleted -= Prepared;
            _player.errorReceived -= Failed;
            Destroy(_player);
        }
    }
}
