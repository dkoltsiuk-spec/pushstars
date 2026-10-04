using System;
using System.Collections.Generic;
using System.Linq;
using PushStars.Core;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Builds the emote set: imports the Mixamo clips as humanoid one-shots, authors the free
    /// gesture clips (hello / thumbs), and writes Resources/EmoteCatalog.asset.
    /// Tools ▸ Push Stars ▸ Emotes ▸ Build Emotes. Re-runnable; the catalog asset is updated in
    /// place so its GUID survives.
    /// </summary>
    public static class EmoteSetup
    {
        public const string Root = "Assets/_Project/Art/Emotes";
        public const string ClipDir = Root + "/Clips";
        public const string SoundDir = Root + "/Sounds";
        public const string CatalogPath = "Assets/_Project/Resources/EmoteCatalog.asset";

        /// <summary>id, name, rarity, gems, source (fbx in ClipDir or authored .anim), start and max
        /// seconds of the clip, and the second the silhouette is taken at.</summary>
        private static readonly (string id, string name, EmoteRarity rarity, int price, string source, float start, float max, float pose)[] Table =
        {
            ("hello",   "HELLO",      EmoteRarity.Free,      0,   "Emote_Hello.anim",      0f,   0f,   1.3f),
            ("laugh",   "HA-HA",      EmoteRarity.Free,      0,   "Emote_Laugh.fbx",       1.6f, 3.6f, 4.3f),
            ("loser",   "LOSER",      EmoteRarity.Free,      0,   "Emote_Loser.anim",      0f,   0f,   1.4f),
            ("boo",     "BOO",        EmoteRarity.Free,      0,   "Emote_Boo.anim",        0f,   0f,   1.6f),
            ("you",     "YOU!",       EmoteRarity.Free,      0,   "Emote_You.anim",        0f,   0f,   1.8f),
            ("gg",      "GG",         EmoteRarity.Free,      0,   "Emote_GG.anim",         0f,   0f,   2.0f),
            ("flex",    "FLEX",       EmoteRarity.Rare,      80,  "Emote_Flex.fbx",        0f,   0f,   1.95f),
            ("warmup",  "WARM-UP",    EmoteRarity.Rare,      80,  "Emote_WarmUp.fbx",      .3f,  4.5f, .9f),
            ("threat",  "COME AT ME", EmoteRarity.Rare,      120, "Emote_Threat.fbx",      .4f,  0f,   3.1f),
            ("hiphop",  "HIP-HOP",    EmoteRarity.Rare,      100, "Emote_HipHop.fbx",      .5f,  5f,   .55f),
            ("snake",   "SNAKE",      EmoteRarity.Epic,      200, "Emote_Snake.fbx",       .8f,  5f,   4.45f),
            ("giddyup", "GIDDY-UP",   EmoteRarity.Epic,      250, "Emote_HorseDance.fbx",  .3f,  5.5f, 2.9f),
            ("backflip","BACKFLIP",   EmoteRarity.Legendary, 450, "Emote_Backflip.fbx",    0f,   0f,   1.5f),
        };

        [MenuItem("Tools/Push Stars/Emotes/Build Emotes")]
        public static void Build()
        {
            ConfigureClips();
            EmoteClipAuthoring.AuthorAll(ClipDir);
            BuildCatalog();
        }

        private const string PreviewMenu = "Tools/Push Stars/Emotes/Preview Test Clips (Editor Play Mode)";

        /// <summary>Plays the "_GVHMR" test captures instead of the shipped clips, in the Editor only.</summary>
        [MenuItem(PreviewMenu)]
        private static void TogglePreview()
        {
            bool on = !EditorPrefs.GetBool(EmoteCatalog.PreviewPref, false);
            EditorPrefs.SetBool(EmoteCatalog.PreviewPref, on);
            Debug.Log("[Emotes] Test clip preview " + (on ? "ON — Editor Play Mode only; builds ship the catalog clips." : "OFF"));
        }

        [MenuItem(PreviewMenu, true)]
        private static bool TogglePreviewCheck()
        {
            Menu.SetChecked(PreviewMenu, EditorPrefs.GetBool(EmoteCatalog.PreviewPref, false));
            return true;
        }

        [MenuItem("Tools/Push Stars/Emotes/Rebuild Catalog Only")]
        public static void BuildCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EmoteCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<EmoteCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var list = new List<EmoteDef>();
            foreach (var row in Table)
            {
                var clip = LoadClip(ClipDir + "/" + row.source);
                if (clip == null) { Debug.LogWarning("[Emotes] Missing clip for " + row.id + ": " + row.source); continue; }
                var old = catalog.Emotes?.FirstOrDefault(e => e.Id == row.id);
                list.Add(new EmoteDef
                {
                    Id = row.id, Name = row.name, Rarity = row.rarity, Price = row.price, Clip = clip,
                    Sound = AssetDatabase.LoadAssetAtPath<AudioClip>(SoundDir + "/emote_" + row.id + ".wav"),
                    SoundVolume = old != null && old.SoundVolume > 0f ? old.SoundVolume : .6f,
                    StartSeconds = row.start, MaxSeconds = row.max, PoseSeconds = row.pose,
                });
            }
            catalog.Emotes = list.ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[Emotes] Catalog: " + string.Join(", ", list.Select(e =>
                $"{e.Id} {e.Clip.length:0.0}s{(e.Sound == null ? " (no sound)" : "")}")));
        }

        /// <summary>Every emote at six moments, on the man, into Temp/EmoteSheet.png — for review.</summary>
        [MenuItem("Tools/Push Stars/Emotes/Render Contact Sheet")]
        public static string RenderContactSheet() => RenderContactSheet(false, 6);

        public static string RenderContactSheet(bool silhouette, int columns, string only = null, bool full = false)
        {
            const int w = 160, h = 200;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Main_man/MainMan.prefab");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var emotes = AssetDatabase.LoadAssetAtPath<EmoteCatalog>(CatalogPath).Emotes
                    .Where(e => only == null || only.Split(',').Contains(e.Id)).ToArray();
                var sheet = new Texture2D(w * columns, h * emotes.Length, TextureFormat.RGBA32, false);
                var fill = new Color32[sheet.width * sheet.height];
                for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(70, 110, 200, 255);
                sheet.SetPixels32(fill);
                for (int row = 0; row < emotes.Length; row++)
                    for (int col = 0; col < columns; col++)
                    {
                        var e = emotes[row];
                        float t = columns == 1 ? e.PoseSeconds : full ? e.Clip.length * (col + .5f) / columns
                            : e.StartSeconds + e.Duration * (col + .5f) / columns;
                        var frame = PushStars.UI.EmoteThumbnails.Render(model, e.Clip, t, w, h, silhouette);
                        if (frame == null) continue;
                        var px = frame.GetPixels32();
                        for (int y = 0; y < h; y++)
                            for (int x = 0; x < w; x++)
                            {
                                var c = px[y * w + x];
                                if (c.a < 10) continue;
                                if (silhouette) c = new Color32(0, 0, 0, 255);
                                sheet.SetPixel(col * w + x, (emotes.Length - 1 - row) * h + y, c);
                            }
                        Object.DestroyImmediate(frame);
                    }
                sheet.Apply();
                string path = System.IO.Path.GetFullPath("Temp/EmoteSheet.png");
                System.IO.File.WriteAllBytes(path, sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
                return path + " rows: " + string.Join(", ", emotes.Select(e => e.Id));
            }
            finally { Object.DestroyImmediate(model); }
        }

        private static AnimationClip LoadClip(string path) => path.EndsWith(".anim")
            ? AssetDatabase.LoadAssetAtPath<AnimationClip>(path)
            : AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        /// <summary>Humanoid, one-shot, root baked into the pose: the emote plays on the spot
        /// wherever the hero stands, and a stage framed on the idle keeps it in shot.</summary>
        public static void ConfigureClips()
        {
            foreach (var row in Table.Where(r => r.source.EndsWith(".fbx")))
            {
                string path = ClipDir + "/" + row.source;
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
                {
                    Debug.LogWarning("[Emotes] Clip FBX missing: " + path);
                    continue;
                }
                bool fresh = importer.animationType != ModelImporterAnimationType.Human
                             || importer.clipAnimations.Length == 0;
                if (!fresh && importer.clipAnimations.All(c => c.name == row.id && !c.loopTime)) continue;
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                importer.clipAnimations = new ModelImporterClipAnimation[0];
                importer.SaveAndReimport();
                importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var clips = importer.defaultClipAnimations;
                foreach (var clip in clips)
                {
                    clip.name = row.id;
                    clip.loopTime = false;
                    clip.loopPose = false;
                    clip.lockRootRotation = true;
                    clip.keepOriginalOrientation = true;
                    clip.lockRootHeightY = true;
                    clip.keepOriginalPositionY = true;
                    clip.lockRootPositionXZ = true;
                    clip.keepOriginalPositionXZ = true;
                }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }
        }
    }
}
