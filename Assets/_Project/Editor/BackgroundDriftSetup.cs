using System;
using System.Linq;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>
    /// Turns the static icon wallpapers of the profile (fists and dumbbells) and the settings
    /// (gears) into a lattice of real icons that <see cref="FloatingPattern"/> floats upward.
    /// The profile's baked wallpaper is split once into a clean gradient plus the two icons.
    /// </summary>
    public static class BackgroundDriftSetup
    {
        const string Art = "Assets/_Project/UI/Sprites/ProfileBrawl/";
        const string LayerName = "FloatingIcons";

        [MenuItem("Push Stars/UI/Animate Background Patterns")]
        public static void ApplyToMain()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Main.unity") throw new InvalidOperationException("Open Main first.");
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RectTransform>(true)).ToArray();
            foreach (var bg in all.Where(t => t.name == "ProfileBackground")) InstallProfile(bg);
            foreach (var overlay in all.Where(t => t.name == "SettingsOverlay")) InstallSettings(overlay);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>Gradient stays put; fists and dumbbells float up over it.</summary>
        public static void InstallProfile(RectTransform background)
        {
            var pattern = background.Find("Pattern")?.GetComponent<Image>();
            if (pattern == null) return;
            Undo.RecordObject(pattern, "Profile gradient");
            pattern.sprite = Sprite("profile-gradient.png");
            pattern.preserveAspect = false; pattern.type = Image.Type.Simple;
            var fitter = pattern.GetComponent<AspectRatioFitter>();
            if (fitter != null) Undo.DestroyObjectImmediate(fitter);
            var rt = pattern.rectTransform;
            Undo.RecordObject(rt, "Profile gradient");
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

            var fist = Sprite("pattern-fist.png");
            var dumbbell = Sprite("pattern-dumbbell.png");
            var layer = Layer(background, pattern.transform.GetSiblingIndex() + 1, speed: 9, spin: 0, wobble: 3);
            // Staggered like the old wallpaper: fists and dumbbells alternate along the diagonals.
            Lattice(layer, 135, 80, 14, 5, (row, col) => (row + col) % 2 == 0
                ? (fist, new Vector2(60, 60)) : (dumbbell, new Vector2(46, 42)), new Color(.6f, .75f, 1f, .07f));
        }

        /// <summary>The faint gear wallpaper floats up, each gear turning slowly.</summary>
        public static void InstallSettings(RectTransform overlay)
        {
            var old = overlay.Cast<Transform>().Where(t => t.name == "GearPattern").ToArray();
            var gear = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/ProfileSettings/Group 553.png");
            int index = old.Length > 0 ? old.Min(t => t.GetSiblingIndex()) : 0;
            foreach (var g in old) Undo.DestroyObjectImmediate(g.gameObject);
            var layer = Layer(overlay, index, speed: 8, spin: 10, wobble: 0);
            Lattice(layer, 100, 112, 10, 5, (row, col) => (gear, new Vector2(77, 77)), new Color(.55f, .7f, 1f, .035f));
        }

        static RectTransform Layer(RectTransform parent, int index, float speed, float spin, float wobble)
        {
            var existing = parent.Find(LayerName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            var go = new GameObject(LayerName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Floating background icons");
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.SetSiblingIndex(Mathf.Min(index, parent.childCount - 1));
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var drift = go.AddComponent<FloatingPattern>();
            drift.Speed = speed; drift.Spin = spin; drift.Wobble = wobble;
            return rt;
        }

        /// <summary>Rows × cols of icons centred on the layer, odd rows shifted half a step; the
        /// lattice is rows × rowStep tall, which becomes the wrap period.</summary>
        static void Lattice(RectTransform layer, float colStep, float rowStep, int rows, int cols,
            Func<int, int, (Sprite sprite, Vector2 size)> pick, Color tint)
        {
            var drift = layer.GetComponent<FloatingPattern>();
            drift.Period = rows * rowStep;
            for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                var (sprite, size) = pick(row, col);
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                icon.layer = 5;
                var rt = (RectTransform)icon.transform;
                rt.SetParent(layer, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
                rt.sizeDelta = size;
                rt.anchoredPosition = new Vector2((col - (cols - 1) * .5f) * colStep + (row % 2) * colStep * .5f,
                    drift.Period * .5f - (row + .5f) * rowStep);
                rt.localEulerAngles = new Vector3(0, 0, (row * 37 + col * 53) % 360 * (sprite.name.Contains("553") ? 1 : 0));
                var image = icon.GetComponent<Image>();
                image.sprite = sprite; image.color = tint; image.raycastTarget = false; image.preserveAspect = true;
            }
        }

        static Sprite Sprite(string file)
        {
            string path = Art + file;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
