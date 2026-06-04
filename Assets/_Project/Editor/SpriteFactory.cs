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
    /// All sprites are saved as 9-slice tinted-white textures so they can be coloured at runtime.
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
        static void SaveRoundedRect(string name, int w, int h, int radius)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px  = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color(1, 1, 1, RoundedAlpha(x, y, w, h, radius));
            tex.SetPixels(px);
            tex.Apply();
            SaveAsSprite(name, tex, new Vector4(radius, radius, radius, radius));
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
        static void SaveAsSprite(string name, Texture2D tex, Vector4 border)
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
            importer.wrapMode            = TextureWrapMode.Clamp;

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
