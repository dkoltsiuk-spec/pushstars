// Copied into a disposable Unity project by validate_runtime.py; not shipped in the game.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using PushStars.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class AudioRuntimeValidation
{
    private const string Key = "PushStars.AudioValidation";
    private static int _step;
    private static double _due, _deadline;
    private static GameAudio _audio;
    private static AudioSource _music;
    private static AudioListener _sceneListener;
    private static int _nextRep;
    private static readonly System.Text.StringBuilder Report = new System.Text.StringBuilder();
    static AudioRuntimeValidation() => EditorApplication.playModeStateChanged += State;

    public static void Run()
    {
        try
        {
            Require(Application.dataPath.Replace('\\', '/').Contains("audio-validation/Assets"), "Must run in disposable project.");
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/_Project/Resources/Audio" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                bool music = path.Contains(GameAudio.MusicName);
                Require(!path.Contains("voice") && !path.Contains("demo"), "Narration or demo imported.");
                Require(importer.defaultSampleSettings.loadType == (music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad), "Wrong loading policy: " + path);
            }
            SessionState.SetBool(Key, true);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.isPlaying = true;
        }
        catch (Exception e) { Finish(e); }
    }

    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false) || state != PlayModeStateChange.EnteredPlayMode) return;
        AudioListener.volume = 0f; // Validate playback without making the test audible.
        PlayerPrefs.SetInt("settings.sound", 1);
        _step = 0; _deadline = EditorApplication.timeSinceStartup + 40;
        _due = EditorApplication.timeSinceStartup + 1;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Audio runtime test timeout.");
            if (EditorApplication.timeSinceStartup < _due) return;
            switch (_step++)
            {
                case 0:
                    _audio = GameAudio.Ensure();
                    Require(_audio != null && GameAudio.Ensure() == _audio, "Singleton bootstrap failed.");
                    Require(UnityEngine.Object.FindObjectsByType<GameAudio>(FindObjectsSortMode.None).Length == 1, "Duplicate manager.");
                    _music = Get<AudioSource>(_audio, "_music");
                    Require(_music != null && _music.loop && _music.clip.name == GameAudio.MusicName, "Music not configured.");
                    Require(_music.isPlaying, "Music did not start.");
                    Require(Resources.LoadAll<AudioClip>("Audio").Length == 21, "Expected 21 instrumental clips.");
                    var groups = Get<IDictionary>(_audio, "_groups");
                    foreach (var cue in Enum.GetValues(typeof(SoundCue)))
                        Require(Get<AudioClip[]>(groups[cue], "Clips").Length > 0, "Missing cue " + cue);
                    _nextRep = NextRep();
                    GameAudio.Play(SoundCue.Rep); GameAudio.Play(SoundCue.Rep);
                    Require(NextRep() == _nextRep + 1, "Duplicate rep wasn't suppressed.");
                    Report.AppendLine("PASS: singleton, music playback, all cues, 21 voice-free clips, duplicate rep debounce.");
                    Delay(.15); break;
                case 1:
                    GameAudio.Play(SoundCue.Rep);
                    Require(NextRep() == _nextRep + 2, "Next distinct rep suppressed.");
                    PlayerPrefs.SetInt("settings.sound", 0);
                    Delay(.15); break;
                case 2:
                    Require(!_music.isPlaying, "Music still playing after mute.");
                    Require(Get<AudioSource[]>(_audio, "_effects").All(x => !x.isPlaying), "Effects still playing after mute.");
                    GameAudio.Play(SoundCue.Rep);
                    Require(NextRep() == _nextRep + 2, "Muted cue was dispatched.");
                    PlayerPrefs.SetInt("settings.sound", 1);
                    Delay(.2); break;
                case 3:
                    Require(_music.isPlaying, "Music didn't resume after unmute.");
                    Invoke(_audio, "OnApplicationPause", true);
                    GameAudio.Play(SoundCue.Rep);
                    Require(!_music.isPlaying && NextRep() == _nextRep + 2, "Background pause didn't suppress audio.");
                    Invoke(_audio, "OnApplicationPause", false);
                    Report.AppendLine("PASS: live mute, ignored muted cues, resume, background pause.");
                    var scene = SceneManager.CreateScene("AudioSceneWithCamera");
                    var camera = new GameObject("Test camera");
                    SceneManager.MoveGameObjectToScene(camera, scene);
                    _sceneListener = camera.AddComponent<AudioListener>();
                    SceneManager.SetActiveScene(scene);
                    Delay(.2); break;
                case 4:
                    Require(_music == Get<AudioSource>(_audio, "_music") && _music.isPlaying, "Music reset on scene change.");
                    Require(_sceneListener.enabled && !Get<AudioListener>(_audio, "_fallbackListener").enabled, "Fallback duplicates scene listener.");
                    var previous = SceneManager.GetActiveScene();
                    SceneManager.SetActiveScene(SceneManager.CreateScene("RewardSceneWithoutCamera"));
                    SceneManager.UnloadSceneAsync(previous);
                    Delay(.25); break;
                case 5:
                    // An additive scene's listener can outlive activeSceneChanged until unloading.
                    Require(Get<AudioListener>(_audio, "_fallbackListener").enabled, "No listener after camera scene unloaded.");
                    Require(_music.isPlaying, "Music stopped during scene transition.");
                    Report.AppendLine("PASS: continuous music and listener handoff between camera and reward scenes.");
                    Finish(null); break;
            }
        }
        catch (Exception e) { Finish(e); }
    }

    private static int NextRep() => Get<int>(Get<IDictionary>(_audio, "_groups")[SoundCue.Rep], "Next");
    private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(target);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Delay(double seconds) => _due = EditorApplication.timeSinceStartup + seconds;
    private static void Finish(Exception error)
    {
        SessionState.SetBool(Key, false);
        EditorApplication.update -= Tick;
        Report.AppendLine(error == null ? "RESULT: PASS" : "RESULT: FAIL " + error);
        File.WriteAllText("audio-runtime-report.txt", Report.ToString());
        if (error != null) Debug.LogException(error);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
