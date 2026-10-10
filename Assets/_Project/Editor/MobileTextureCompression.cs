using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Gives the large uncompressed UI art a compressed format on phones.
    ///
    /// <para>The screen setup tools import their sprites uncompressed so the editor shows the art
    /// exactly as drawn. With no platform override that setting ships as is: about a hundred
    /// full-screen backgrounds and illustrations went into the player as raw RGBA — 420 MB of
    /// texture data, most of the download and all of it resident in memory once shown. This adds
    /// the same iOS/Android override <see cref="SpriteImporter"/> gives the Figma sprites and
    /// leaves the default (editor, desktop) import alone.</para>
    ///
    /// <para>Left uncompressed on purpose: anything under <see cref="MinSide"/> px (icons and
    /// buttons — 22 MB between all of them, and where block artefacts would show first), and
    /// glows, which block compression turns into a grid of squares (see
    /// <see cref="SpriteImporter.IsSoftGradient"/>).</para>
    ///
    /// Menu: Tools → Push Stars → Compress Large Textures For Mobile. Also run by every build.
    /// </summary>
    public static class MobileTextureCompression
    {
        private const int MinSide = 512;

        private static readonly string[] Roots = { "Assets/_Project", "Assets/Character" };

        private const string AppIconFolder = "Assets/_Project/Art/AppIcon/";

        // Glows that are not in SpriteImporter's exact-name list because they did not come
        // through it (onboarding selection-glow, coach-blue-wash).
        private static readonly string[] SoftNameParts = { "glow", "wash" };

        private static readonly string[] Platforms = { "iPhone", "Android" };

        [MenuItem("Tools/Push Stars/Compress Large Textures For Mobile", priority = 204)]
        public static void Apply()
        {
            var changed = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", Roots))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                    if (!NeedsOverride(importer, path)) continue;

                    foreach (var platform in Platforms)
                    {
                        // The cap is copied, not chosen: clamping a sprite shrinks its rect while
                        // the ppu stays, and every SetNativeSize layout shrinks with it.
                        importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                        {
                            name               = platform,
                            overridden         = true,
                            maxTextureSize     = importer.maxTextureSize,
                            format             = TextureImporterFormat.ASTC_6x6,
                            compressionQuality = 100,
                        });
                    }
                    importer.SaveAndReimport();
                    changed.Add(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            Debug.Log($"[MobileTextureCompression] ASTC 6x6 on iOS/Android for {changed.Count} texture(s)" +
                      (changed.Count > 0 ? ":\n" + string.Join("\n", changed) : "."));
        }

        private static bool NeedsOverride(TextureImporter importer, string path)
        {
            if (importer.textureCompression != TextureImporterCompression.Uncompressed) return false;
            // Player Settings cuts the launcher icons from this texture.
            if (path.StartsWith(AppIconFolder)) return false;

            // An existing override is somebody's decision (SpriteImporter keeps glows RGBA32).
            foreach (var platform in Platforms)
                if (importer.GetPlatformTextureSettings(platform).overridden) return false;

            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (SpriteImporter.IsSoftGradient(name)) return false;
            foreach (var part in SoftNameParts)
                if (name.Contains(part)) return false;

            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            return Mathf.Min(Mathf.Max(width, height), importer.maxTextureSize) >= MinSide;
        }
    }
}
