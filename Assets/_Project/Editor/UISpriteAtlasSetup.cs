using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PushStars.Editor
{
    /// <summary>
    /// Packs the UI sprites into a single Sprite Atlas so the whole HUD/menus draw in far
    /// fewer draw calls (important for mobile FPS). The full-screen BG.png is left out — a
    /// 2048-px background would just fill the atlas; it's better standalone. So are the glows
    /// (SpriteImporter.IsSoftGradient): the atlas is block-compressed, and that is the one kind
    /// of art block compression turns into squares.
    ///
    /// Sprites keep their normal references; Unity redirects them to the atlas at runtime,
    /// so nothing in prefabs/scenes needs to change.
    ///
    /// Menu: Tools → Push Stars → Build UI Sprite Atlas
    /// </summary>
    public static class UISpriteAtlasSetup
    {
        const string AtlasPath = "Assets/_Project/UI/UISpriteAtlas.spriteatlas";

        [MenuItem("Tools/Push Stars/Build UI Sprite Atlas", priority = 206)]
        public static void Run()
        {
            var atlas  = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
            bool isNew = atlas == null;
            if (isNew) atlas = new SpriteAtlas();

            atlas.SetIncludeInBuild(true);

            atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                blockOffset        = 1,
                enableRotation     = false, // UI sprites must not rotate
                enableTightPacking = false, // rect packing — safe for 9-slice
                padding            = 4,
            });

            atlas.SetTextureSettings(new SpriteAtlasTextureSettings
            {
                readable        = false,
                generateMipMaps = false,
                sRGB            = true,
                filterMode      = FilterMode.Bilinear,
            });

            // GPU-native compression, same as the per-sprite importer settings.
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings
            {
                name = "iPhone", overridden = true, maxTextureSize = 2048,
                format = TextureImporterFormat.ASTC_6x6, compressionQuality = 100,
            });
            atlas.SetPlatformSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 2048,
                format = TextureImporterFormat.ETC2_RGBA8, compressionQuality = 100,
            });

            // Collect every UI sprite except the full-screen background and the glows.
            var sprites = new List<Object>();
            foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { SpriteFactory.SpritesDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (name == "bg") continue; // full-screen BG.png stays standalone
                // A packed sprite is drawn from the atlas texture and takes the atlas's format,
                // so packing a glow would block-compress it again — squares in the falloff — and
                // silently undo the uncompressed import SpriteImporter gives it. They cost a few
                // draw calls standalone; that is the trade.
                if (SpriteImporter.IsSoftGradient(name)) continue;
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) sprites.Add(sprite);
            }

            // Replace packables (clear old set, add the current one).
            var existing = atlas.GetPackables();
            if (existing != null && existing.Length > 0) atlas.Remove(existing);
            atlas.Add(sprites.ToArray());

            if (isNew) AssetDatabase.CreateAsset(atlas, AtlasPath);
            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            SpriteAtlasUtility.PackAllAtlases(EditorUserBuildSettings.activeBuildTarget);

            Debug.Log($"[UISpriteAtlas] Packed {sprites.Count} sprites → {AtlasPath}");
            EditorUtility.DisplayDialog(
                "Push Stars — UI Sprite Atlas",
                $"Packed {sprites.Count} UI sprites into one atlas:\n{AtlasPath}\n\n" +
                "This cuts UI draw calls (better FPS on mobile). The full-screen BG.png and " +
                "the glows are intentionally left out.\n\nSprite references in prefabs/scenes " +
                "are unchanged — Unity redirects them to the atlas at runtime.",
                "OK");
        }
    }
}
