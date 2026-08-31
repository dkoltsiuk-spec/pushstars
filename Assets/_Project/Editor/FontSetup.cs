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

        // Sampling quality: 90 pt, 1024×1024 atlas, dynamic population (render mode below).
        // Padding is the SDF gradient's room to spread, and everything drawn OUTSIDE the glyph —
        // the outline and the drop shadow — has to fit inside it. 17 is well above TMP's usual
        // 10 % of the sampling size because a fully external outline costs twice what a centred
        // one does, and the shadow needs room under it; the two together spend ~11 px. In atlas
        // terms this changes nothing: at 90 pt both 13 and 17 fit 8 glyphs per row.
        private const int SamplingSize = 90;
        private const int Padding      = 17;
        private const int AtlasSize    = 1024;

        // SDF32, not TMP's default SDFAA. SDFAA does not compute a distance field at all — it
        // estimates one from the antialiased coverage bitmap, which is only meaningful within a
        // texel or two of the glyph edge. Past that the values go flat and wobbly. The style
        // below reads the field ~6 texels out for the outline and ~10 for the shadow, i.e. deep
        // in the useless range, which is what turned the letters mushy. SDF32 supersamples 32×
        // and gives true distances across the whole padding. It is slower to bake — a one-time
        // editor cost, since the atlas is dynamic and the glyphs persist once rendered.
        private const GlyphRenderMode RenderMode = GlyphRenderMode.SDF32;

        // ── Text style: black keyline + hard black drop copy ──────────────────────
        // Every label in the game carries a black outline and a hard black duplicate
        // dropped a couple of pixels — cartoon-sticker lettering. Both come out of the SDF
        // material (outline + underlay) rather than a second GameObject per label: one draw
        // call instead of two, and the style travels with the font wherever it is used.
        //
        // The outline must sit ENTIRELY OUTSIDE the letters. TMP does not do that by default:
        // the SDF shader centres the band on the glyph edge, pushing the silhouette out by
        // `outline` while pulling the face in by the same amount, so half of it eats the letter
        // and the text comes out spindly. Reading the shader:
        //
        //     weight  = (WeightNormal/4 + _FaceDilate) * ratioA * 0.5
        //     bias    = (0.5 - weight) * scale - 0.5
        //     outline = _OutlineWidth * ratioA * 0.5 * scale
        //     face edge = bias + outline,  silhouette edge = bias - outline
        //
        // Setting _FaceDilate ≈ _OutlineWidth cancels the inward half (the face edge lands back on
        // the undilated glyph edge), leaving the band outside. FaceDilate is therefore not a
        // boldness knob: it has to stay next to OutlineWidth or the outline creeps back into the
        // letter. Below it sits a hair under, which lets a sliver bite in — deliberate, matched
        // against the Figma comp by eye.
        //
        // These are the values dialled in on the Rubik Bold material in the Inspector and then
        // captured here, so a rebuild reproduces them and the other four weights match. Units are
        // TMP's normalised ones, where 1 spans the font's whole SDF gradient (Padding + 1 atlas
        // pixels); ShaderUtilities.UpdateShaderRatios rescales them again against the padding
        // actually available. Everything below fits inside Padding 17 with room to spare — the
        // outline reaches ~4.2 texels out, the shadow ~9.8 — so raise Padding before raising these.
        private const float OutlineWidth  = 0.256f;
        private const float FaceDilate    = 0.24f;  // ≈ OutlineWidth: holds the outline outside
        private const float ShadowOffsetX = -0.11f; // slight leftward cast
        private const float ShadowOffsetY = -1f;    // negative = downward
        private const float ShadowDilate  = 0.67f;  // fattens the drop copy so it clears the outline

        // Small labels get the same style at half strength, as a separate material preset — see
        // EnsureSmallPreset for why one keyline cannot cover both sizes. The scale multiplies the
        // whole set, so the proportions tuned above are preserved, only lighter.
        private const float  SmallScale        = 0.5f;
        private const float  SmallTextMaxSize  = 15f;   // pt; above this a label keeps the full keyline
        private const string SmallPresetSuffix = " Outline S";

        // Narrows the antialiasing band on every edge — face, outline and shadow alike. A vector
        // stroke in Figma has no AA band to speak of, so TMP at 0 always reads a touch softer
        // beside it. Small values only: push this far and the edges start to crawl at small
        // point sizes, which is worse than the softness it buys.
        private const float Sharpness = 0.15f;

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
            ApplyTextStyle();

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

        // ── Text style ────────────────────────────────────────────────────────────

        /// <summary>
        /// Stamps the outline + drop-shadow style onto every Rubik font asset's material.
        /// Separate from <see cref="Run"/> on purpose: re-running the generator deletes and
        /// recreates the font assets (new GUIDs, prefabs to re-link), whereas this only edits
        /// the materials already in place — so restyling costs nothing.
        /// Menu: Tools → Push Stars → Apply Text Style
        /// </summary>
        [MenuItem("Tools/Push Stars/Apply Text Style", priority = 205)]
        public static void ApplyTextStyle()
        {
            var paths = new[] { RegularAsset, MediumAsset, BoldAsset,
                                ItalicAsset, BoldItalicAsset };
            int styled = 0;

            foreach (var path in paths)
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (fa == null || fa.material == null) continue;
                EnsureAtlasSettings(fa);
                StyleMaterial(fa.material);
                EnsureSmallPreset(fa);
                EditorUtility.SetDirty(fa.material);
                EditorUtility.SetDirty(fa);
                styled++;
            }

            if (styled == 0)
            {
                Debug.LogWarning("[FontSetup] No Rubik font assets found — run Setup Rubik Font first.");
                return;
            }

            MakeRubikTheTMPDefault();

            AssetDatabase.SaveAssets();
            Debug.Log($"[FontSetup] ✓ Outline + drop shadow applied to {styled} Rubik material(s).");
        }

        /// <summary>
        /// Brings an existing font asset's atlas padding and render mode up to the values above,
        /// in place — edited through SerializedObject and cleared rather than regenerated, so the
        /// asset keeps its GUID and every prefab and scene pointing at it stays wired. The atlas
        /// is dynamic, so the glyphs simply re-render with the new settings when next asked for.
        /// </summary>
        static void EnsureAtlasSettings(TMP_FontAsset fa)
        {
            // The shader reads the gradient span from the material, not the asset, so it is
            // re-stated every run — UpdateShaderRatios divides by it moments later.
            fa.material.SetFloat("_GradientScale", Padding + 1);

            var so      = new SerializedObject(fa);
            var padding = so.FindProperty("m_AtlasPadding");
            var mode    = so.FindProperty("m_AtlasRenderMode");
            if (padding == null || mode == null) return;

            bool changed = padding.intValue != Padding || mode.intValue != (int)RenderMode;
            if (!changed) return;

            padding.intValue = Padding;
            mode.intValue    = (int)RenderMode;
            so.ApplyModifiedPropertiesWithoutUndo();

            fa.ClearFontAssetData();          // drop glyphs baked with the old settings
            fa.ReadFontAssetDefinition();

            Debug.Log($"[FontSetup] {fa.name}: padding → {Padding}, render mode → {RenderMode}; " +
                      "glyphs cleared for re-bake.");
        }

        /// <summary>Black keyline plus a hard black copy offset downward, on one SDF material.
        /// The mobile TMP shader gates both behind shader features, so the keywords matter as
        /// much as the values — without them the numbers are set and nothing renders.</summary>
        public static void StyleMaterial(Material mat, float scale = 1f)
        {
            if (mat == null || !mat.HasProperty("_OutlineWidth")) return;
            ShaderUtilities.GetShaderPropertyIDs(); // UpdateShaderRatios reads the cached IDs

            mat.EnableKeyword("OUTLINE_ON");
            mat.SetColor("_OutlineColor",    Color.black);
            mat.SetFloat("_OutlineWidth",    OutlineWidth * scale);
            mat.SetFloat("_OutlineSoftness", 0f);
            // Not boldness — this is what moves the band off the letter and onto its outside.
            mat.SetFloat("_FaceDilate",      FaceDilate * scale);

            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor",    Color.black);
            mat.SetFloat("_UnderlayOffsetX",  ShadowOffsetX * scale);
            mat.SetFloat("_UnderlayOffsetY",  ShadowOffsetY * scale);
            mat.SetFloat("_UnderlayDilate",   ShadowDilate  * scale);
            mat.SetFloat("_UnderlaySoftness", 0f); // 0 = the hard edge; anything above blurs it

            if (mat.HasProperty("_Sharpness")) mat.SetFloat("_Sharpness", Sharpness);

            // Rescales outline/underlay into the range the atlas padding can hold.
            ShaderUtilities.UpdateShaderRatios(mat);
        }

        /// <summary>
        /// The same style at <see cref="SmallScale"/>, as a material preset beside the font asset.
        ///
        /// One keyline cannot serve both a 20 pt headline and a 12 pt label. The width is a
        /// fraction of an em, so it is proportionally identical at both — but so are the gaps
        /// between letters, and at 12 pt what survives the dilation falls under the width of the
        /// antialiasing band. The letters fuse, the word reads as one fat blob and the outline
        /// stops reading as a stroke at all. Scaling the whole style down for small text is what
        /// makes it *look* like the headline treatment, rather than measure like it.
        /// </summary>
        static Material EnsureSmallPreset(TMP_FontAsset fa)
        {
            string path = SmallPresetPath(fa);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(fa.material) { name = fa.name + SmallPresetSuffix };
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.shader != fa.material.shader) mat.shader = fa.material.shader;

            // A preset has to keep sampling the font's own atlas, and read the same gradient span,
            // or TMP treats it as a different font and the metrics go wrong.
            mat.SetTexture("_MainTex",       fa.material.GetTexture("_MainTex"));
            mat.SetFloat("_GradientScale",   fa.material.GetFloat("_GradientScale"));
            mat.SetFloat("_TextureWidth",    fa.material.GetFloat("_TextureWidth"));
            mat.SetFloat("_TextureHeight",   fa.material.GetFloat("_TextureHeight"));

            StyleMaterial(mat, SmallScale);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static string SmallPresetPath(TMP_FontAsset fa) => $"{FontsDir}/{fa.name}{SmallPresetSuffix}.mat";

        /// <summary>Puts a label on the light-keyline preset when it is small enough to need it.
        /// Call it right after assigning the font; above the threshold it does nothing and the
        /// label keeps the font's own material.</summary>
        public static void ApplyOutlineFor(TMP_Text tmp, float size)
        {
            if (tmp == null || tmp.font == null || size > SmallTextMaxSize) return;
            var preset = AssetDatabase.LoadAssetAtPath<Material>(SmallPresetPath(tmp.font));
            if (preset != null) tmp.fontSharedMaterial = preset;
        }

        /// <summary>Points TMP's own default font asset at Rubik, so text created outside the
        /// builders (a hand-added label, a package prefab) comes out in Rubik instead of
        /// LiberationSans — and therefore carries the outline style too.</summary>
        static void MakeRubikTheTMPDefault()
        {
            var settings = Resources.Load<TMP_Settings>("TMP Settings");
            var regular  = LoadRegular();
            if (settings == null || regular == null) return;

            var so   = new SerializedObject(settings);
            var prop = so.FindProperty("m_defaultFontAsset");
            if (prop == null || prop.objectReferenceValue == regular) return;

            prop.objectReferenceValue = regular;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[FontSetup] ✓ TMP default font asset → Rubik TMP.");
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
                RenderMode, AtlasSize, AtlasSize,
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
