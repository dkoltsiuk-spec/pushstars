using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Stream the music loops; decompress short cues up front for low-latency mobile playback.
    /// Cues ship as ADPCM — a quarter of PCM in the download, and DecompressOnLoad turns them back
    /// into PCM in memory, so playback latency is the same. Music at Vorbis 50% (was 80%) to
    /// save ~5 MB of the download.
    /// </summary>
    public sealed class GameAudioImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Resources/Audio/";
        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder, System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            bool music = System.IO.Path.GetFileNameWithoutExtension(assetPath).StartsWith("music_", System.StringComparison.Ordinal);
            var settings = importer.defaultSampleSettings;
            settings.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            settings.quality = music ? .5f : 1f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = !music;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = music;
        }
    }
}
