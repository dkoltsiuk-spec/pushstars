using System.IO;
using UnityEditor;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Generates procedural UI sprite atlases used by the design system:
    ///   • pill_24.png         — rounded rectangle with 24 px corner radius (full pill buttons)
    ///   • pill_16.png         — rounded rectangle with 16 px corner radius (chips, smaller pills)
    ///   • pill_12.png         — rounded rectangle with 12 px corner radius (top-bar mini pills)
    ///   • circle_128.png      — anti-aliased filled circle (nav buttons, VS badge)
    ///   • dashed_ring_512.png — rotating dashed ring (matchmaking screen)
    ///   • pill_bar_track.png  — loading bar's black trough
    ///   • pill_bar_fill.png   — loading bar's gold fill, gradient baked in
    ///   • arrow_right.png     — the arrow a "read on" link needs and Rubik has no glyph for
    ///   • dash_tile.png       — one dash and its gap, tiled to draw a dashed measure line
    /// All sprites are saved as 9-slice tinted-white textures so they can be coloured at runtime —
    /// except the bar fill, whose gradient is the art itself and is drawn at tint white.
    /// Menu: Tools → Push Stars → Generate UI Sprites
    /// </summary>
    public static class SpriteFactory
    {
        public const string SpritesDir = "Assets/_Project/UI/Sprites";

        [MenuItem("Tools/Push Stars/Generate UI Sprites", priority = 200)]
        public static void GenerateAll()
        {
            EnsureDirs();

            SaveRoundedRect("pill_24", 128, 64, 24);
            SaveRoundedRect("pill_16",  96, 48, 16);
            SaveRoundedRect("pill_12",  64, 32, 12);
            SaveRoundedRect("pill_capsule", 128, 56, 28); // true capsule for 56 px-tall buttons
            SaveRoundedRect("card_history", 256, 48, 24); // 24 px = half-height → very round corners
            SaveCircle     ("circle_128", 128);
            SaveDashedRing ("dashed_ring_512", 512, outerR: 244, thickness: 14, dashes: 36);
            SaveRadialGlow ("glow_radial", 256);       // stage glow behind the character
            SaveSoftEllipse("ground_shadow", 256, 96); // contact shadow under his feet
            BuildBarSprites();                        // loading bar: trough + the gold in it
            BuildLinkArrow();                         // the arrow "read on" links point with
            BuildDashSprite();                        // one dash + its gap, tiled along a measure

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SpriteFactory] ✓ Generated UI sprites in " + SpritesDir);
        }

        static void EnsureDirs()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/UI"))
                AssetDatabase.CreateFolder("Assets/_Project", "UI");
            if (!AssetDatabase.IsValidFolder(SpritesDir))
                AssetDatabase.CreateFolder("Assets/_Project/UI", "Sprites");
        }

        // ── Rounded rectangle ─────────────────────────────────────────────────────
        /// <param name="ppu">Sprite pixels per UI unit. Above 100 the sprite is drawn smaller than
        /// its pixel count — the way to bake a corner at 3× the density the canvas asks for, so it
        /// stays a curve instead of a staircase on a Retina panel. The 9-slice border is divided by
        /// the same number, so a 3× sprite needs a 3× border to keep the corner it was drawn with.</param>
        /// <param name="border">Overrides the default all-round border. Zero on an edge means that
        /// edge is not sliced at all: the texture stretches across it, which is how a baked gradient
        /// survives — a sliced middle band would repeat one row of it instead.</param>
        static void SaveRoundedRect(string name, int w, int h, int radius,
                                    float ppu = 100f, Vector4? border = null, Gradient ramp = null)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px  = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                // Bottom row is t = 0, so a ramp is authored the way it is read on screen.
                Color rgb = ramp == null ? Color.white : ramp.Evaluate(h == 1 ? 0f : y / (float)(h - 1));
                for (int x = 0; x < w; x++)
                    px[y * w + x] = new Color(rgb.r, rgb.g, rgb.b, RoundedAlpha(x, y, w, h, radius));
            }
            tex.SetPixels(px);
            tex.Apply();
            SaveAsSprite(name, tex, border ?? new Vector4(radius, radius, radius, radius), ppu);
        }

        static float RoundedAlpha(int x, int y, int w, int h, int r)
        {
            int cx = (x < r) ? r : (x >= w - r ? w - 1 - r : x);
            int cy = (y < r) ? r : (y >= h - r ? h - 1 - r : y);
            float dx = x - cx;
            float dy = y - cy;
            float d  = Mathf.Sqrt(dx * dx + dy * dy);
            return Mathf.Clamp01(r + 0.5f - d);
        }

        // ── Loading bar ───────────────────────────────────────────────────────────
        // Both halves of the boot screen's progress bar. Their shape lives here, in one place,
        // because they only read as a single object if they agree about it — and drawn at 3× the
        // canvas density (ppu 300), because they sit dead centre of the first thing the app ever
        // shows, where a stepped corner is the first thing anyone sees.

        /// <summary>Height of the trough, in the canvas's 390×844 reference units.</summary>
        public const float BarHeight = 38f;

        /// <summary>
        /// Corner radius of the trough. The one number that decides how round the whole bar looks:
        /// the gold inside takes this minus <see cref="BarPadding"/>, so the two curves stay
        /// concentric at any value. 8 is a soft rectangle, 12 matches the comp, and half the height
        /// (19) turns both into capsules.
        /// </summary>
        public const float BarRadius = 12f;

        /// <summary>How much trough is left showing around the gold, on every side.</summary>
        public const float BarPadding = 5f;

        /// <summary>Everything is drawn at this multiple of the reference units and read back at
        /// the matching ppu, so a 12-unit corner is 36 real pixels of curve on a 3× panel.</summary>
        private const int BarScale = 3;

        /// <summary>
        /// Draws the bar's two sprites from the constants above.
        ///
        /// <para>Always redraws, rather than skipping files already on disk: the shape is decided
        /// by numbers in code, and a PNG left over from an earlier radius would quietly outrank
        /// them — the bar would keep its old corners with nothing to say why. Cheap enough to be
        /// unconditional; both textures are a few kilobytes of flat colour.</para>
        ///
        /// <para>The boot screen calls this itself instead of assuming somebody ran Generate UI
        /// Sprites first: it is the one screen that has to come out right on a fresh clone, before
        /// anyone has opened a menu.</para>
        /// </summary>
        public static void BuildBarSprites()
        {
            EnsureDirs();

            int th = Mathf.RoundToInt(BarHeight * BarScale);
            int tr = Mathf.RoundToInt(BarRadius * BarScale);
            SaveBarTrack("pill_bar_track", th * 8, th, tr);

            // Concentric with the trough: a shape inset by the padding keeps its curves parallel to
            // the outer ones only if its radius drops by exactly that much. Equal radii are what
            // made the gold read as a capsule sitting inside a rectangle.
            int fh = Mathf.RoundToInt((BarHeight - 2f * BarPadding) * BarScale);
            int fr = Mathf.Clamp(Mathf.RoundToInt((BarRadius - BarPadding) * BarScale), 1, fh / 2);
            SaveBarFill("pill_bar_fill", fh * 5, fh, fr);

            AssetDatabase.SaveAssets();
        }

        /// <summary>The trough: a flat rounded rectangle, tinted near-black in the scene.</summary>
        static void SaveBarTrack(string name, int w, int h, int radius)
            => SaveRoundedRect(name, w, h, radius, ppu: 100f * BarScale);

        /// <summary>
        /// The gold that fills the trough, with its vertical gradient baked into the pixels — deep
        /// orange along the bottom, bright rim along the top — so the bar is one draw call and one
        /// asset rather than a stack of tinted strips faking a ramp.
        ///
        /// <para>Sliced left and right only. The bar grows by width, so the caps must hold their
        /// radius while the middle stretches; the vertical axis is never sliced because a middle
        /// band would resample one row of the gradient over the whole height and flatten it.</para>
        /// </summary>
        static void SaveBarFill(string name, int w, int h, int radius)
        {
            var gold = new Gradient();
            gold.SetKeys(new[]
            {
                new GradientColorKey(new Color32(228, 131,  12, 255), 0.00f),
                new GradientColorKey(new Color32(247, 168,  28, 255), 0.28f),
                new GradientColorKey(new Color32(255, 201,  51, 255), 0.58f),
                new GradientColorKey(new Color32(255, 224, 102, 255), 0.82f),
                new GradientColorKey(new Color32(255, 242, 168, 255), 1.00f),
            }, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });

            SaveRoundedRect(name, w, h, radius, ppu: 100f * BarScale,
                            border: new Vector4(radius, 0, radius, 0), ramp: gold);
        }

        // ── Read-on arrow ─────────────────────────────────────────────────────────
        /// <summary>Draws the read-on arrow. Public and unconditional for the same reason
        /// <see cref="BuildBarSprites"/> is: a screen must not depend on somebody having opened a
        /// menu before it, and the shape is decided by numbers in code either way.</summary>
        public static void BuildLinkArrow()
        {
            EnsureDirs();
            SaveArrow("arrow_right", 96, 56, shaft: 6f, head: 30f);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// A plain right-pointing arrow: shaft, then a triangular head.
        ///
        /// <para>Drawn rather than typed because Rubik has no U+2192 — and neither does anything
        /// in its fallback chain, which is Rubik again in other weights. Setting an arrow in the
        /// label would put a missing-glyph box on the screen, and the box is silent: nothing warns
        /// that a character was asked for and not found.</para>
        ///
        /// <para>White, so callers tint it. Drawn at ppu 300 like the rest of the @3x art.</para>
        /// </summary>
        static void SaveArrow(string name, int w, int h, float shaft, float head)
        {
            // The head is a pair of diagonals, and a diagonal resolved one pixel at a time is a
            // staircase at every size a link would use this at. Sampled 16× per pixel instead.
            const int Samples = 4;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px  = new Color[w * h];
            float cy     = (h - 1) * 0.5f;
            float tipX   = w - 0.5f;
            float headX  = w - head;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < Samples; sy++)
                for (int sx = 0; sx < Samples; sx++)
                {
                    float fx = x + (sx + 0.5f) / Samples;
                    float fy = y + (sy + 0.5f) / Samples;
                    float dy = Mathf.Abs(fy - cy);

                    // Shaft runs a little into the head so the two never part company.
                    bool inShaft = fx <= headX + 1f && dy <= shaft * 0.5f;
                    bool inHead  = fx >= headX && fx <= tipX &&
                                   dy <= Mathf.Lerp(head * 0.5f, 0f, Mathf.InverseLerp(headX, tipX, fx));
                    if (inShaft || inHead) hits++;
                }
                px[y * w + x] = new Color(1, 1, 1, hits / (float)(Samples * Samples));
            }

            tex.SetPixels(px);
            tex.Apply();
            SaveAsSprite(name, tex, Vector4.zero, ppu: 300f);
        }

        // ── Dashed measure line ───────────────────────────────────────────────────
        /// <summary>Draws the dash tile. Public and unconditional for the same reason the bar and
        /// the arrow are: a screen must not depend on somebody having opened a menu first.</summary>
        public static void BuildDashSprite()
        {
            EnsureDirs();
            SaveDash("dash_tile", 60, 15, dash: 36);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// One dash and the gap after it, drawn once and repeated by <c>Image.Type.Tiled</c>.
        ///
        /// <para>A dashed line has to survive being any length — it spans whatever gap the layout
        /// leaves between two pictures — and a stretched sprite of a whole line would stretch its
        /// dashes with it, longer on a wide phone than a narrow one. Tiling repeats a fixed dash
        /// instead, so only their number changes.</para>
        ///
        /// <para>The texture wraps rather than clamps, which lets UGUI draw the whole run as one
        /// quad with scrolled UVs instead of one quad per dash.</para>
        /// </summary>
        static void SaveDash(string name, int w, int h, int dash)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px  = new Color[w * h];
            float radius = (h - 1) * 0.5f;   // round caps: the dashes read as strokes, not bricks

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = x < dash ? RoundedAlpha(x, y, dash, h, Mathf.RoundToInt(radius)) : 0f;
                px[y * w + x] = new Color(1, 1, 1, a);
            }

            tex.SetPixels(px);
            tex.Apply();
            SaveAsSprite(name, tex, Vector4.zero, ppu: 300f, wrap: TextureWrapMode.Repeat);
        }

        // ── Filled anti-aliased circle ────────────────────────────────────────────
        static void SaveCircle(string name, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px  = new Color[size * size];
            float r = size * 0.5f - 1f;
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(r - d + 0.5f);
                px[y * size + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels(px);
            tex.Apply();
            SaveAsSprite(name, tex, Vector4.zero);
        }

        // ── Radial glow ───────────────────────────────────────────────────────────
        // White with a smooth alpha falloff from the centre, so one sprite serves every
        // glow on the screen — the hue comes from Image.color. Squashing the RectTransform
        // turns it into the tall oval the main screen puts behind the character.
        // The falloff is smoothstep² rather than linear: a linear ramp leaves a visible
        // disc edge on a flat dark background.
        static void SaveRadialGlow(string name, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px  = new Color[size * size];
            float c = (size - 1) * 0.5f;
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / r, dy = (y - c) / r;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d));
                px[y * size + x] = new Color(1, 1, 1, a * a);
            }
            tex.SetPixels(px);
            tex.Apply();
            SaveAsSprite(name, tex, Vector4.zero);
        }

        // ── Soft contact shadow ───────────────────────────────────────────────────
        // Flat ellipse, densest at the centre. Drawn in black at low alpha it reads as the
        // ground contact that stops the character from floating over the background.
        static void SaveSoftEllipse(string name, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px  = new Color[w * h];
            float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x - cx) / (w * 0.5f), dy = (y - cy) / (h * 0.5f);
                float d  = Mathf.Sqrt(dx * dx + dy * dy);
                float a  = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d));
                px[y * w + x] = new Color(1, 1, 1, a);
            }
            tex.SetPixels(px);
            tex.Apply();
            SaveAsSprite(name, tex, Vector4.zero);
        }

        // ── Dashed ring (matchmaking screen) ──────────────────────────────────────
        static void SaveDashedRing(string name, int size, float outerR, float thickness, int dashes)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px  = new Color[size * size];
            float c       = (size - 1) * 0.5f;
            float innerR  = outerR - thickness;
            float anglePer  = 2f * Mathf.PI / dashes;
            float halfDash  = anglePer * 0.30f; // 60 % dash, 40 % gap

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c;
                float d  = Mathf.Sqrt(dx * dx + dy * dy);

                float aRing = Mathf.Clamp01(outerR - d + 0.5f) *
                              Mathf.Clamp01(d - innerR + 0.5f);
                if (aRing <= 0)
                {
                    px[y * size + x] = Color.clear;
                    continue;
                }

                float ang = Mathf.Atan2(dy, dx);
                if (ang < 0) ang += 2f * Mathf.PI;
                float local = (ang % anglePer) - anglePer * 0.5f;
                float aDash = Mathf.Abs(local) < halfDash ? 1f : 0f;

                px[y * size + x] = new Color(1, 1, 1, aRing * aDash);
            }
            tex.SetPixels(px);
            tex.Apply();
            SaveAsSprite(name, tex, Vector4.zero);
        }

        // ── PNG export with sliced-sprite import settings ─────────────────────────
        static void SaveAsSprite(string name, Texture2D tex, Vector4 border, float ppu = 100f,
                                 TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            string path = $"{SpritesDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled       = false;
            importer.filterMode          = FilterMode.Bilinear;
            importer.wrapMode            = wrap;
            importer.spritePixelsPerUnit = ppu;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteBorder                     = border;
            settings.spriteMeshType                   = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);

            importer.SaveAndReimport();
        }
    }
}
