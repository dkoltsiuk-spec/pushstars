using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Core
{
    public enum SoundCue
    {
        Tap, Confirm, Back, Navigate, Rep, RepRejected, Countdown,
        RewardTick, RewardComplete, CaseUnlock, CaseCharge, CaseReveal,
        CaseUpgrade, CaseUpgradeComplete, Purchase, Victory, AuraStamp, AuraSkull, AuraWhoosh, UiTransition, RewardBurst
    }

    /// <summary>Persistent, bounded 2D audio. All clips are instrumental; no narration is imported.</summary>
    public sealed class GameAudio : MonoBehaviour
    {
        public const string ResourceFolder = "Audio/";
        public const string MusicName = "music_action_groove";
        public const string OnboardingMusicName = "music_onboarding_welcome";
        public const string AssessmentMusicName = "music_assessment_pulse";
        /// <summary>Music level under a live set: room for the rep and hit cues.</summary>
        public const float WorkoutMusicVolume = .15f;
        private const int VoiceCount = 8;
        private const float CrossfadeOutRate = .25f;
        private static GameAudio _instance;
        private static bool _quitting;
        private static string _musicOverride;
        private static float _musicOverrideVolume;
        private static readonly ISettingsStore Settings = new PlayerPrefsSettingsStore();
        private readonly Dictionary<SoundCue, ClipGroup> _groups = new Dictionary<SoundCue, ClipGroup>();
        private readonly Dictionary<string, AudioClip> _themes = new Dictionary<string, AudioClip>();
        private readonly AudioSource[] _effects = new AudioSource[VoiceCount];
        // Two music sources: the current theme, and the previous one fading out under it.
        private AudioSource _music, _musicOut;
        private AudioClip _mainTheme;
        private float _mainThemeResume, _musicStartAt;
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
        private static void ResetStatics() { _instance = null; _quitting = false; _musicOverride = null; }

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

        /// <summary>A one-off clip that is not a <see cref="SoundCue"/> — an emote's stinger. It
        /// takes a free voice or evicts a quieter UI tick, and ducks the music briefly.</summary>
        public static void PlayClip(AudioClip clip, float volume = .6f)
        {
            if (clip == null || !Application.isPlaying || !SoundEnabled || _quitting) return;
            Ensure()?.PlayClipInternal(clip, volume);
        }

        public static void SetWorkoutPaused(bool paused)
        {
            var audio = Ensure();
            if (audio != null) audio._workoutPaused = paused;
        }

        /// <summary>Plays <paramref name="theme"/> instead of the scene's own music until the owner
        /// clears it. A mode inside a shared scene (the assessment in Onboarding or Fight) uses this;
        /// scene themes come from <see cref="SceneTheme"/>.</summary>
        public static void SetMusicOverride(string theme, float volume)
        {
            _musicOverride = theme;
            _musicOverrideVolume = volume;
            Ensure()?.RefreshMusic();
        }

        public static void ClearMusicOverride(string theme)
        {
            if (_musicOverride != theme) return;
            _musicOverride = null;
            if (_instance != null && !_quitting) _instance.RefreshMusic();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            _music = NewSource(200);
            _musicOut = NewSource(200);
            _music.loop = _musicOut.loop = true;
            _music.volume = _musicOut.volume = 0f;
            _mainTheme = Theme(MusicName);
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
            Add(SoundCue.AuraStamp, .7f, .5f, 64, "aura_stamp");            Add(SoundCue.AuraSkull, .65f, .5f, 64, "aura_skull");
            Add(SoundCue.AuraWhoosh, .75f, .5f, 80, "aura_whoosh");
            Add(SoundCue.UiTransition, .5f, .3f, 90, "ui_transition");
            Add(SoundCue.RewardBurst, .45f, .3f, 120, "reward_burst");
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
            if (cue == SoundCue.CaseReveal || cue == SoundCue.CaseUpgradeComplete || cue == SoundCue.Victory || cue == SoundCue.AuraStamp || cue == SoundCue.AuraSkull)
                _duckUntil = now + 1.1f;
        }

        private void PlayClipInternal(AudioClip clip, float volume)
        {
            if (_background) return;
            const int priority = 70;
            AudioSource source = null;
            foreach (var candidate in _effects)
            {
                if (candidate == null) continue;
                if (!candidate.isPlaying) { source = candidate; break; }
                if (candidate.priority >= priority && (source == null || candidate.priority > source.priority))
                    source = candidate;
            }
            if (source == null) return;
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = 1f;
            source.priority = priority;
            source.Play();
            _duckUntil = Mathf.Max(_duckUntil, Time.unscaledTime + Mathf.Min(clip.length, 2.5f));
        }

        private void Update()
        {
            ApplySettings();
            if (_music == null) return;
            float volume = _sceneMusicVolume * (_workoutPaused ? .4f : 1f);
            if (Time.unscaledTime < _duckUntil) volume *= .4f;
            // Duck quickly for a big moment, swell back slowly afterwards.
            float rate = volume < _music.volume ? 1.2f : .35f;
            // Clamp the step: a scene load's long frame must not jump the music in one go.
            float step = Mathf.Min(Time.unscaledDeltaTime, .05f);
            _music.volume = Mathf.MoveTowards(_music.volume, volume, step * rate);
            if (_musicOut.isPlaying)
            {
                _musicOut.volume = Mathf.MoveTowards(_musicOut.volume, 0f, step * CrossfadeOutRate);
                if (_musicOut.volume <= 0f) StopMusic(_musicOut);
            }
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
                if (!_music.isPlaying && _music.clip != null) PlayMusic();
            }
            else
            {
                _music.Pause();
                StopMusic(_musicOut);
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
            RefreshMusic();
            RefreshListener();
        }

        /// <summary>Picks the theme and level for the current screen. A new theme crossfades in over
        /// the old one; the same theme just keeps playing.</summary>
        private void RefreshMusic()
        {
            string scene = SceneManager.GetActiveScene().name;
            _sceneMusicVolume = _musicOverride != null ? _musicOverrideVolume : SceneMusicVolume(scene);
            var clip = Theme(_musicOverride ?? SceneTheme(scene));
            if (clip == null || clip == _music.clip) return;
            // Going straight back to the theme still fading out picks it up where it is.
            bool resume = _musicOut.isPlaying && _musicOut.clip == clip;
            if (!resume) StopMusic(_musicOut);
            (_music, _musicOut) = (_musicOut, _music);
            if (!_wasEnabled) StopMusic(_musicOut);
            if (resume) return;
            _music.clip = clip;
            _music.volume = 0f;
            _musicStartAt = clip == _mainTheme ? _mainThemeResume : 0f;
            if (_wasEnabled) PlayMusic();
        }

        private void PlayMusic()
        {
            _music.Play();
            // Seek after Play: a streamed clip ignores a position set while it is stopped.
            if (_musicStartAt > 0f && _musicStartAt < _music.clip.length) _music.time = _musicStartAt;
            _musicStartAt = 0f;
        }

        private void StopMusic(AudioSource source)
        {
            // The home bed carries on from where it was; the other themes start over each time.
            if (source.clip != null && source.clip == _mainTheme && (source.isPlaying || source.time > 0f))
                _mainThemeResume = source.time;
            source.Stop();
            source.volume = 0f;
        }

        private AudioClip Theme(string name)
        {
            if (_themes.TryGetValue(name, out var clip)) return clip;
            clip = Resources.Load<AudioClip>(ResourceFolder + name);
            if (clip == null) Debug.LogWarning("[GameAudio] Missing music: " + name);
            _themes[name] = clip;
            return clip;
        }

        /// <summary>First launch belongs to the welcome theme from the loading screen on; everything
        /// after the assessment shares the home bed.</summary>
        private static string SceneTheme(string scene)
        {
            if (scene == FightConfig.OnboardingSceneName) return OnboardingMusicName;
            if (scene == FightConfig.BootSceneName && !OnboardingState.IntroSeen) return OnboardingMusicName;
            return MusicName;
        }

        /// <summary>The home bed only breathes between screens. It all but drops out under the Aura
        /// stamp (silence before the hit), sits back for case openings, and swells back in on the
        /// home screen.</summary>
        private static float SceneMusicVolume(string scene)
        {
            switch (scene)
            {
                case "AuraReward": return .03f;
                case "CaseAward": case "CaseOpening": case "CaseReward": return .13f;
                case FightConfig.TrainingSceneName: return WorkoutMusicVolume;
                default: return scene == FightConfig.FightSceneName ? WorkoutMusicVolume : .2f;
            }
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
