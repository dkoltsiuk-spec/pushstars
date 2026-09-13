using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Core
{
    public enum SoundCue
    {
        Tap, Confirm, Back, Navigate, Rep, RepRejected, Countdown,
        RewardTick, RewardComplete, CaseUnlock, CaseCharge, CaseReveal,
        CaseUpgrade, CaseUpgradeComplete, Purchase, Victory
    }

    /// <summary>Persistent, bounded 2D audio. All clips are instrumental; no narration is imported.</summary>
    public sealed class GameAudio : MonoBehaviour
    {
        public const string ResourceFolder = "Audio/";
        public const string MusicName = "music_sport_loop_120bpm";
        private const int VoiceCount = 8;
        private static GameAudio _instance;
        private static bool _quitting;
        private static readonly ISettingsStore Settings = new PlayerPrefsSettingsStore();
        private readonly Dictionary<SoundCue, ClipGroup> _groups = new Dictionary<SoundCue, ClipGroup>();
        private readonly AudioSource[] _effects = new AudioSource[VoiceCount];
        private AudioSource _music;
        private AudioListener _fallbackListener;
        private bool _wasEnabled, _background, _workoutPaused;
        private float _duckUntil;
        private float _sceneMusicVolume = .2f;

        private sealed class ClipGroup
        {
            public AudioClip[] Clips;
            public float Volume, Cooldown, LastPlayed = -100f;
            public int Next, Priority;
        }

        public static bool SoundEnabled => Settings.SoundEnabled;

        public static bool KeepExternalMusic
        {
            get => PlayerPrefs.GetInt("settings.keep_external_music", 0) != 0;
            set { PlayerPrefs.SetInt("settings.keep_external_music", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; _quitting = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot() => Ensure();

        public static GameAudio Ensure()
        {
            if (!Application.isPlaying || _quitting) return null;
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<GameAudio>();
                if (_instance == null) new GameObject("Push Stars Audio").AddComponent<GameAudio>();
            }
            return _instance;
        }

        public static void Play(SoundCue cue, float pitch = 1f)
        {
            if (!Application.isPlaying || !SoundEnabled || _quitting) return;
            Ensure()?.PlayInternal(cue, pitch);
        }

        public static void SetWorkoutPaused(bool paused)
        {
            var audio = Ensure();
            if (audio != null) audio._workoutPaused = paused;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _music = NewSource(200);
            _music.loop = true;
            _music.volume = 0f;
            _music.clip = Resources.Load<AudioClip>(ResourceFolder + MusicName);
            if (_music.clip == null) Debug.LogWarning("[GameAudio] Missing music loop.");
            for (int i = 0; i < VoiceCount; i++) _effects[i] = NewSource(128);
            Add(SoundCue.Tap, .28f, .045f, 160, "ui_tap_01", "ui_tap_02", "ui_tap_03");
            Add(SoundCue.Confirm, .36f, .12f, 150, "ui_confirm");
            Add(SoundCue.Back, .3f, .12f, 150, "ui_back");
            Add(SoundCue.Navigate, .23f, .12f, 160, "ui_swipe");
            Add(SoundCue.Rep, .46f, .08f, 32, "rep_01", "rep_02", "rep_03");
            Add(SoundCue.RepRejected, .3f, .5f, 40, "rep_rejected");
            Add(SoundCue.Countdown, .32f, .2f, 40, "countdown");
            Add(SoundCue.RewardTick, .15f, .065f, 170, "reward_tick");
            Add(SoundCue.RewardComplete, .48f, .2f, 80, "reward_complete");
            Add(SoundCue.CaseUnlock, .5f, .2f, 90, "case_unlock");
            Add(SoundCue.CaseCharge, .45f, .7f, 90, "case_charge_short");
            Add(SoundCue.CaseReveal, .62f, .4f, 64, "case_reveal_epic");
            Add(SoundCue.CaseUpgrade, .4f, .3f, 90, "case_upgrade_short");
            Add(SoundCue.CaseUpgradeComplete, .5f, .45f, 64, "case_upgrade_finish");
            Add(SoundCue.Purchase, .45f, .3f, 80, "shop_purchase");
            Add(SoundCue.Victory, .55f, .5f, 64, "victory");
            SceneManager.sceneLoaded += SceneLoaded;
            SceneManager.sceneUnloaded += SceneUnloaded;
            SceneManager.activeSceneChanged += ActiveSceneChanged;
            RefreshScene(SceneManager.GetActiveScene());
            ApplySettings();
        }

        private AudioSource NewSource(int priority)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = priority;
            source.dopplerLevel = 0f;
            return source;
        }

        private void Add(SoundCue cue, float volume, float cooldown, int priority, params string[] names)
        {
            var clips = new List<AudioClip>();
            foreach (string name in names)
            {
                var clip = Resources.Load<AudioClip>(ResourceFolder + name);
                if (clip != null) clips.Add(clip);
                else Debug.LogWarning("[GameAudio] Missing clip: " + name);
            }
            _groups.Add(cue, new ClipGroup { Clips = clips.ToArray(), Volume = volume, Cooldown = cooldown, Priority = priority });
        }

        private void PlayInternal(SoundCue cue, float pitch)
        {
            if (_background || !_groups.TryGetValue(cue, out var group) || group.Clips.Length == 0) return;
            float now = Time.unscaledTime;
            if (now - group.LastPlayed < group.Cooldown) return;
            AudioSource source = null;
            foreach (var candidate in _effects)
            {
                if (!candidate.isPlaying) { source = candidate; break; }
                // Never evict a rep or a reveal to fit a quiet UI/reward tick.
                if (candidate.priority >= group.Priority && (source == null || candidate.priority > source.priority))
                    source = candidate;
            }
            if (source == null) return;
            source.Stop();
            source.clip = group.Clips[group.Next++ % group.Clips.Length];
            source.volume = group.Volume;
            source.pitch = Mathf.Clamp(pitch, .75f, 1.5f);
            source.priority = group.Priority;
            source.Play();
            group.LastPlayed = now;
            if (cue == SoundCue.CaseReveal || cue == SoundCue.CaseUpgradeComplete || cue == SoundCue.Victory)
                _duckUntil = now + 1.1f;
        }

        private void Update()
        {
            ApplySettings();
            if (_music == null) return;
            float volume = _sceneMusicVolume * (_workoutPaused ? .4f : 1f);
            if (Time.unscaledTime < _duckUntil) volume *= .4f;
            _music.volume = Mathf.MoveTowards(_music.volume, volume, Time.unscaledDeltaTime * .35f);
        }

        private void ApplySettings()
        {
            bool enabled = SoundEnabled && !_background && !KeepExternalMusic;
            if (enabled == _wasEnabled) return;
            _wasEnabled = enabled;
            if (enabled)
            {
                _music.volume = 0f;
                _music.UnPause();
                if (!_music.isPlaying && _music.clip != null) _music.Play();
            }
            else
            {
                _music.Pause();
                if (!SoundEnabled || _background)
                    foreach (var source in _effects) source.Stop();
            }
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => RefreshListener();
        private void SceneUnloaded(Scene scene) => RefreshListener();
        private void ActiveSceneChanged(Scene before, Scene after) => RefreshScene(after);
        private void RefreshScene(Scene scene)
        {
            _workoutPaused = false;
            _sceneMusicVolume = scene.name == FightConfig.FightSceneName || scene.name == "Training" ? .15f : .2f;
            RefreshListener();
        }

        private void RefreshListener()
        {
            // Reward scenes have no camera listener. Use one only while the scene has none.
            bool sceneHasListener = false;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener != _fallbackListener && listener.isActiveAndEnabled) { sceneHasListener = true; break; }
            if (!sceneHasListener && _fallbackListener == null) _fallbackListener = gameObject.AddComponent<AudioListener>();
            if (_fallbackListener != null) _fallbackListener.enabled = !sceneHasListener;
        }

        private void OnApplicationPause(bool paused) { _background = paused; ApplySettings(); }
        private void OnApplicationQuit() => _quitting = true;
        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            SceneManager.activeSceneChanged -= ActiveSceneChanged;
            if (_instance == this) _instance = null;
        }
    }
}
