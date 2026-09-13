using System;
using System.IO;
using System.Linq;
using System.Text;
using PushStars.Core;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Inspect the real Editor audio path, including its separate Game-view mute.</summary>
    [InitializeOnLoad]
    public static class GameAudioDiagnostics
    {
        private const string Sentinel = "Temp/pushstars-audio-diagnostics";
        private static double _next;
        static GameAudioDiagnostics() => EditorApplication.update += Poll;

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + .5;
            if (File.Exists(Sentinel)) WriteReport();
        }

        [MenuItem("Tools/Push Stars/Audio/Write Live Diagnostics")]
        public static void WriteReport()
        {
            var text = new StringBuilder(DateTime.UtcNow.ToString("u") + "\n");
            text.AppendLine($"play={EditorApplication.isPlaying} paused={EditorApplication.isPaused} editorMute={EditorUtility.audioMasterMute}");
            text.AppendLine($"settings.sound={GameAudio.SoundEnabled} listenerVolume={AudioListener.volume} listenerPause={AudioListener.pause} dspTime={AudioSettings.dspTime:F3} sampleRate={AudioSettings.outputSampleRate}");
            foreach (var listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                text.AppendLine($"listener={listener.name} scene={listener.gameObject.scene.name} enabled={listener.isActiveAndEnabled}");
            foreach (var audio in UnityEngine.Object.FindObjectsByType<GameAudio>(FindObjectsSortMode.None))
            {
                text.AppendLine($"manager={audio.name} enabled={audio.isActiveAndEnabled}");
                foreach (var source in audio.GetComponents<AudioSource>())
                {
                    float[] data = new float[1024];
                    if (source.isPlaying) source.GetOutputData(data, 0);
                    text.AppendLine($"source={source.clip?.name ?? "none"} playing={source.isPlaying} volume={source.volume:F3} mute={source.mute} pitch={source.pitch:F3} time={source.time:F3} load={source.clip?.loadState} peak={data.Max(x => Mathf.Abs(x)):F6}");
                }
            }
            if (EditorApplication.isPlaying)
            {
                var data = new float[1024]; AudioListener.GetOutputData(data, 0);
                text.AppendLine($"listenerPeak={data.Max(x => Mathf.Abs(x)):F6}");
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/audio-live-diagnostics.txt", text.ToString());
        }
    }
}
