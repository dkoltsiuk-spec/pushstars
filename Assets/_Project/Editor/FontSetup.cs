using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace PushStars.Editor
{
    /// <summary>
    /// Generates TextMeshPro Font Assets from the static Rubik TTF files and
    /// wires them into the design system.
    ///
    /// Source fonts (Assets/_Project/UI/Fonts/):
    ///   Rubik-Regular.ttf   Rubik-Bold.ttf
    ///   Rubik-Italic.ttf    Rubik-BoldItalic.ttf
    ///
    /// Generated TMP assets (same folder):
    ///   Rubik TMP.asset           ← used by default
    ///   Rubik Bold TMP.asset      ← swapped in for FontStyles.Bold text
    ///   Rubik Italic TMP.asset
    ///   Rubik BoldItalic TMP.asset
    ///
    /// Menu: Tools → Push Stars → Setup Rubik Font
    /// Then re-run Setup UI Gallery to apply the font to all prefabs.
    /// </summary>
    public static class FontSetup
    {
        public const string FontsDir = "Assets/_Project/UI/Fonts";

        // Source TTFs
        public const string RegularTTF    = FontsDir + "/Rubik-Regular.ttf";
        public const string MediumTTF     = FontsDir + "/Rubik-Medium.ttf";
        public const string BoldTTF       = FontsDir + "/Rubik-Bold.ttf";
        public const string ItalicTTF     = FontsDir + "/Rubik-Italic.ttf";
        public const string BoldItalicTTF = FontsDir + "/Rubik-BoldItalic.ttf";

        // Generated TMP assets
        public const string RegularAsset    = FontsDir + "/Rubik TMP.asset";
        public const string MediumAsset     = FontsDir + "/Rubik Medium TMP.asset";
        public const string BoldAsset       = FontsDir + "/Rubik Bold TMP.asset";
        public const string ItalicAsset     = FontsDir + "/Rubik Italic TMP.asset";
        public const string BoldItalicAsset = FontsDir + "/Rubik BoldItalic TMP.asset";

        // Sampling quality: 90 pt, 1024×1024 atlas, SDFAA, dynamic population.
        private const int SamplingSize = 90;
        private const int Padding      = 9;
        private const int AtlasSize    = 1024;

        [MenuItem("Tools/Push Stars/Setup Rubik Font", priority = 204)]
        public static void Run()
        {
            bool any = false;
            any |= CreateAsset(RegularTTF,    RegularAsset,    "Regular");
            any |= CreateAsset(MediumTTF,     MediumAsset,     "Medium");
            any |= CreateAsset(BoldTTF,       BoldAsset,       "Bold");
            any |= CreateAsset(ItalicTTF,     ItalicAsset,     "Italic");
            any |= CreateAsset(BoldItalicTTF, BoldItalicAsset, "BoldItalic");

            if (!any) return;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            WireFallbacks();

            Debug.Log("[FontSetup] ✓ Rubik TMP font assets ready. " +
                      "Run  Tools → Push Stars → Setup UI Gallery  to apply to prefabs.");

            EditorUtility.DisplayDialog(
                "Push Stars — Rubik Font",
                "TMP Font Assets created:\n\n" +
                $"  • {RegularAsset}\n" +
                $"  • {MediumAsset}\n" +
                $"  • {BoldAsset}\n" +
                $"  • {ItalicAsset}\n" +
                $"  • {BoldItalicAsset}\n\n" +
                "Run  Tools → Push Stars → Setup UI Gallery  to apply Rubik to all prefabs.",
                "OK");
        }

        // ── Public accessors ──────────────────────────────────────────────────────

        public static TMP_FontAsset LoadRegular()    =>
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(RegularAsset);
        public static TMP_FontAsset LoadMedium()     =>
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumAsset);
        public static TMP_FontAsset LoadBold()       =>
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BoldAsset);
        public static TMP_FontAsset LoadItalic()     =>
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ItalicAsset);
        public static TMP_FontAsset LoadBoldItalic() =>
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BoldItalicAsset);

        /// <summary>
        /// Returns the appropriate font asset for the given style flags, and outputs
        /// the remaining style flags that TMP should still apply (e.g. Underline).
        /// Bold/Italic are handled by swapping the font asset, not via simulation.
        /// </summary>
        public static TMP_FontAsset Resolve(FontStyles style, out FontStyles remaining)
        {
            bool bold   = (style & FontStyles.Bold)   != 0;
            bool italic = (style & FontStyles.Italic)  != 0;

            // Strip Bold/Italic — already encoded in the chosen font asset.
            remaining = style & ~FontStyles.Bold & ~FontStyles.Italic;

            if (bold && italic) return LoadBoldItalic() ?? LoadBold() ?? LoadRegular();
            if (bold)           return LoadBold()       ?? LoadRegular();
            if (italic)         return LoadItalic()     ?? LoadRegular();
            return LoadRegular();
        }

        // ── Internal ──────────────────────────────────────────────────────────────

        static bool CreateAsset(string ttfPath, string assetPath, string label)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(ttfPath);
            if (font == null)
            {
                Debug.LogWarning($"[FontSetup] TTF not found, skipping {label}: {ttfPath}");
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);

            var fa = TMP_FontAsset.CreateFontAsset(
                font, SamplingSize, Padding,
                GlyphRenderMode.SDFAA, AtlasSize, AtlasSize,
                AtlasPopulationMode.Dynamic, enableMultiAtlasSupport: true);

            fa.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            // ── Save main asset ───────────────────────────────────────────────────
            AssetDatabase.CreateAsset(fa, assetPath);

            // ── Embed atlas texture(s) as sub-assets ──────────────────────────────
            // TMP's own Font Asset Creator does this; without it m_AtlasTextures is
            // null after serialisation and TMP throws UnassignedReferenceException.
            var textures = fa.atlasTextures;
            if (textures == null || textures.Length == 0 || textures[0] == null)
            {
                // Dynamic mode may produce no initial texture — create a blank one.
                var tex = new Texture2D(AtlasSize, AtlasSize, TextureFormat.Alpha8, false, true);
                tex.name      = fa.name + " Atlas";
                fa.atlasTextures = new[] { tex };
                AssetDatabase.AddObjectToAsset(tex, assetPath);
            }
            else
            {
                for (int i = 0; i < textures.Length; i++)
                {
                    if (textures[i] == null) continue;
                    textures[i].name = fa.name + (i == 0 ? " Atlas" : $" Atlas {i}");
                    if (!AssetDatabase.Contains(textures[i]))
                        AssetDatabase.AddObjectToAsset(textures[i], assetPath);
                }
            }

            // ── Embed material as sub-asset ───────────────────────────────────────
            if (fa.material != null && !AssetDatabase.Contains(fa.material))
            {
                fa.material.name = fa.name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, assetPath);
            }

            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();

            // Finalise internal glyph/character tables.
            fa.ReadFontAssetDefinition();

            Debug.Log($"[FontSetup] ✓ {label}: {assetPath}");
            return true;
        }

        // Wire cross-fallbacks so TMP can always find glyphs.
        static void WireFallbacks()
        {
            var regular    = LoadRegular();
            var bold       = LoadBold();
            var italic     = LoadItalic();
            var boldItalic = LoadBoldItalic();

            AddFallback(bold,       regular);
            AddFallback(italic,     regular);
            AddFallback(boldItalic, bold ?? regular);

            var medium = LoadMedium();
            AddFallback(medium, regular);

            AssetDatabase.SaveAssets();
        }

        static void AddFallback(TMP_FontAsset target, TMP_FontAsset fallback)
        {
            if (target == null || fallback == null) return;
            target.fallbackFontAssetTable ??= new List<TMP_FontAsset>();
            if (!target.fallbackFontAssetTable.Contains(fallback))
            {
                target.fallbackFontAssetTable.Add(fallback);
                EditorUtility.SetDirty(target);
            }
        }
    }
}
