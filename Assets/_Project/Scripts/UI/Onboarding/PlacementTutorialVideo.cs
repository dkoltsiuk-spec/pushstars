using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace PushStars.UI
{
    /// <summary>Silent local placement guide; only decodes while its panel is visible.</summary>
    [DisallowMultipleComponent]
    public sealed class PlacementTutorialVideo : MonoBehaviour
    {
        [SerializeField] private VideoClip _clip;
        [SerializeField] private Texture _poster;
        [SerializeField] private RawImage _display;
        private VideoPlayer _player;

        private void Awake()
        {
            _player = gameObject.AddComponent<VideoPlayer>();
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
            if (_clip != null) _player.Prepare();
        }

        private void Prepared(VideoPlayer player)
        {
            if (isActiveAndEnabled) player.Play();
        }

        private void Update()
        {
            if (_player.isPlaying && _player.frame >= 0 && _player.texture != null)
                _display.texture = _player.texture;
        }

        private void OnDisable()
        {
            if (_player != null) _player.Stop();
            if (_display != null) _display.texture = _poster;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!isActiveAndEnabled) return;
            if (paused) _player.Stop();
            else Begin();
        }

        private void Failed(VideoPlayer player, string message)
        {
            player.Stop();
            _display.texture = _poster;
            Debug.LogWarning("[Onboarding] Placement video unavailable: " + message, this);
        }

        private void OnDestroy()
        {
            if (_player == null) return;
            _player.prepareCompleted -= Prepared;
            _player.errorReceived -= Failed;
        }
    }
}
