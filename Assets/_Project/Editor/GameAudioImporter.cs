using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>Stream the loop; decompress short cues up front for low-latency mobile playback.</summary>
    public sealed class GameAudioImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Resources/Audio/";
        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            bool music = System.IO.Path.GetFileNameWithoutExtension(assetPath) == Core.GameAudio.MusicName;
            var settings = importer.defaultSampleSettings;
            settings.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.quality = music ? .8f : 1f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = !music;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = music;
        }
    }
}
