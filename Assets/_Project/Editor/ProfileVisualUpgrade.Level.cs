using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>The level block, full width under the avatar row, in a card-game style: a tilted
    /// white tag (owner art) with the level biting into a flat black-rimmed bar, two-tone orange-yellow fill, a big
    /// outlined XP count across the bar, "N XP to level N+1" above it.</summary>
    public static partial class ProfileVisualUpgrade
    {
        static readonly string[] LevelParts = { "XpCaption", "Xp", "XpNext", "XpTrack", "XpFillArea", "XpFill", "LevelBadge", "Level" };
        const int ArtScale = 4; // the level sprites are drawn at 4× their canvas size
        // The full-width block needs its own row: everything from the stats down moves by this much.
        const float LevelRowGrow = 20, BelowLevelRow = 165;
        static readonly Color32 Ink = new Color32(0, 0, 0, 255);

        [MenuItem("Push Stars/UI/Rebuild Profile Level Block")]
        public static void RebuildLevelBlock()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Main.unity") throw new InvalidOperationException("Open Main first.");
            var d = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ProfileDashboard>(true)).First();
            var page = d.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "AthleteDashboard");
            _black = Font("Rubik Black"); _regular = Font("Rubik Medium");
            _title = Material(_black, "Title", .20f, .025f); _body = Material(_regular, "Body", 0, 0); _caption = Material(_black, "Caption", .12f, 0);
            Undo.RecordObject(d, "Profile level block");
            BuildLevelBlock(page, d);
            // Pages built before the block went full width still have the stats right under it.
            if (page.Find("RepsIcon") is RectTransform reps && reps.anchoredPosition.y > -180 - LevelRowGrow + .5f)
                MakeRoomForLevelRow(page, d);
            d.Refresh();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }

        static void BuildLevelBlock(RectTransform page, ProfileDashboard d)
        {
            foreach (var old in page.Cast<Transform>().Where(t => LevelParts.Contains(t.name)).ToArray())
                Undo.DestroyObjectImmediate(old.gameObject);

            Text(page, "XpCaption", "LEVEL PROGRESS", 66, 118, 150, 16, 10, true, Muted);
            d.XpNext = Text(page, "XpNext", "", 216, 118, 158, 16, 10, true, Gold);
            d.XpNext.alignment = TextAlignmentOptions.Right;

            // Track runs under the tag so the bar reads as hanging off it; its right end lines up
            // with the panels below (16 + 358). The fill starts where the tag ends.
            var track = Pic(page, "XpTrack", Art("level-track", 336, 32, 7, true, Track), 38, 138, 336, 32);
            Sliced(track);
            var area = Rect(page, "XpFillArea", 52, 141, 319, 26);
            var fill = Pic(area, "XpFill", Art("level-fill", 319, 26, 4, true, Fill), 0, 0, 0, 0);
            Sliced(fill);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(.66f, 1);
            fill.rectTransform.pivot = new Vector2(0, .5f); fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
            d.XpFill = fill;
            d.Xp = Text(page, "Xp", "", 52, 136, 319, 34, 19, true);
            d.Xp.fontSharedMaterial = ValueMaterial(); d.Xp.alignment = TextAlignmentOptions.Center;
            d.Xp.characterSpacing = -1;

            // The owner's tag art comes already tilted; the level sits on its card below the clip,
            // turned to the same angle.
            var tag = Pic(page, "LevelBadge", TagSprite(), 6, 108, 51, 67);
            d.Level = Text(tag.transform, "Level", "", 0, 0, 34, 32, 22, true, new Color32(20, 20, 28, 255));
            var levelRect = d.Level.rectTransform;
            levelRect.pivot = new Vector2(.5f, .5f); levelRect.anchoredPosition = new Vector2(27, -45);
            levelRect.localRotation = Quaternion.Euler(0, 0, 10);
            d.Level.fontSharedMaterial = _caption; d.Level.alignment = TextAlignmentOptions.Center;
            d.Level.lineSpacing = -30; // pulls the small "LVL" down onto the number
        }

        /// <summary>Moves every page row below the level block down by <see cref="LevelRowGrow"/>,
        /// with the history start and the page height.</summary>
        static void MakeRoomForLevelRow(RectTransform page, ProfileDashboard d)
        {
            foreach (RectTransform child in page)
                if (-child.anchoredPosition.y >= BelowLevelRow)
                {
                    Undo.RecordObject(child, "Make room for level row");
                    child.anchoredPosition -= new Vector2(0, LevelRowGrow);
                }
            Undo.RecordObject(page, "Make room for level row");
            page.sizeDelta += new Vector2(0, LevelRowGrow);
            d.HistoryTop += LevelRowGrow;
        }

        static void Sliced(Image image)
        {
            image.preserveAspect = false; image.type = Image.Type.Sliced; image.fillCenter = true;
            image.pixelsPerUnitMultiplier = ArtScale;
        }

        // XP count: black rim plus a hard drop shadow.
        static Material ValueMaterial()
        {
            var mat = Material(_black, "LevelValue", .2f, .04f);
            mat.EnableKeyword("UNDERLAY_ON");
            mat.SetColor("_UnderlayColor", Color.black);
            mat.SetFloat("_UnderlayOffsetX", 0); mat.SetFloat("_UnderlayOffsetY", -.8f);
            mat.SetFloat("_UnderlayDilate", .2f); mat.SetFloat("_UnderlaySoftness", 0);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Flat slate groove inside a thick black rim, a touch darker under the top edge.
        static Color32 Track(float x, float y, float inset)
        {
            if (inset < 3f) return Ink;
            var c = (Color)new Color32(43, 49, 82, 255);
            if (y < 9f) c = Color.Lerp(new Color32(29, 33, 60, 255), c, (y - 3f) / 6f);
            return c;
        }

        // Two-tone orange-yellow: bright yellow upper half with a light top line, orange lower half.
        static Color32 Fill(float x, float y, float inset)
        {
            if (y < 2.2f) return new Color32(255, 236, 140, 255);
            return y < 12.5f ? new Color32(255, 204, 38, 255) : new Color32(247, 150, 14, 255);
        }

        static Sprite TagSprite()
        {
            const string path = ArtPath + "lvl-icon.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Signed distance to a rounded rectangle (negative inside), canvas units.</summary>
        static float RoundRect(float x, float y, float x0, float y0, float x1, float y1, float r)
        {
            var half = new Vector2((x1 - x0) * .5f, (y1 - y0) * .5f);
            var q = new Vector2(Mathf.Abs(x - (x0 + x1) * .5f), Mathf.Abs(y - (y0 + y1) * .5f)) - half + new Vector2(r, r);
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - r;
        }

        /// <summary>Writes a sprite at <see cref="ArtScale"/>× its canvas size. With
        /// <paramref name="clip"/> the art is cut to a rounded rectangle of corner
        /// <paramref name="radius"/> and sliced on it; <paramref name="paint"/> gets canvas-unit x,
        /// y from the top and the distance in from that edge.</summary>
        static Sprite Art(string name, int w, int h, float radius, bool clip, Func<float, float, float, Color32> paint)
        {
            string path = ArtPath + name + ".png";
            int pw = w * ArtScale, ph = h * ArtScale;
            var texture = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            var pixels = new Color32[pw * ph];
            for (int py = 0; py < ph; py++)
            for (int px = 0; px < pw; px++)
            {
                float x = (px + .5f) / ArtScale, y = h - (py + .5f) / ArtScale;
                float inset = clip ? -RoundRect(x, y, 0, 0, w, h, radius) : 0;
                var c = paint(x, y, Mathf.Max(0, inset));
                if (clip) c.a = (byte)(c.a * Mathf.Clamp01(inset * ArtScale + .5f)); // one-pixel antialiased edge
                pixels[py * pw + px] = c;
            }
            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            int border = clip ? Mathf.CeilToInt(radius * ArtScale) + 4 : 0;
            importer.spriteBorder = new Vector4(border, border, border, border);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
