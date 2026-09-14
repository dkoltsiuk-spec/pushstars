using System;
using System.IO;
using System.Linq;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class GoblinForestSetup
    {
        public const string Folder = "Assets/_Project/UI/Sprites/GoblinForest/";
        private const string ScenePath = "Assets/_Project/Scenes/Fight.unity";

        [MenuItem("Tools/Push Stars/Boss/Install Goblin Forest")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("Save the authored Fight scene before installing the forest.");
            Directory.CreateDirectory("output/goblin-forest");
            File.Copy(ScenePath, "output/goblin-forest/Fight-before-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".unity");
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossCombatScreen>(true)).Single();
                Apply(screen);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static void Apply(BossCombatScreen screen)
        {
            if (screen.Preparation) return;
            var content = screen.Content;
            var forest = screen.Root.GetComponent<GoblinForestPresentation>();
            if (forest == null) forest = screen.Root.AddComponent<GoblinForestPresentation>();
            screen.Forest = forest;
            forest.OriginalDecor = content.Cast<Transform>().Where(t => t.name.StartsWith("Pattern", StringComparison.Ordinal))
                .Select(t => t.gameObject).Concat(new[] {screen.Root.transform.Find("Backdrop").gameObject}).ToArray();

            var matte = Node(screen.Root.transform, "GoblinForestMatte", 0, 0, 0, 0);
            matte.anchorMin = Vector2.zero; matte.anchorMax = Vector2.one; matte.offsetMin = matte.offsetMax = Vector2.zero;
            var fill = matte.GetComponent<Image>() ?? matte.gameObject.AddComponent<Image>();
            fill.color = new Color32(12, 18, 17, 255); fill.raycastTarget = false;
            matte.SetAsFirstSibling(); forest.Matte = matte.gameObject;

            var background = Node(content, "GoblinForestBackground", 0, 0, 390, 844);
            var oldImage = background.GetComponent<RawImage>();
            if (oldImage) UnityEngine.Object.DestroyImmediate(oldImage);
            var image = background.GetComponent<ForestBackdropGraphic>() ?? background.gameObject.AddComponent<ForestBackdropGraphic>();
            image.Texture = Load("background", false);
            ConfigureOpaqueRows(image);
            image.raycastTarget = false;
            background.SetAsFirstSibling();
            forest.Background = background.gameObject;

            var foreground = Node(content, "GoblinForestForeground", 0, 0, 390, 844);
            if (foreground.GetComponent<RectMask2D>() == null) foreground.gameObject.AddComponent<RectMask2D>();
            // Above fighter images and their shadows, below all HP, labels and clickable controls.
            foreground.SetSiblingIndex(Mathf.Max(screen.PlayerPortrait.transform.GetSiblingIndex(), screen.BossPortrait.transform.GetSiblingIndex()) + 1);
            forest.Foreground = foreground.gameObject;
            Prop(foreground, "GrassLeft", "grass-left", -150, -367, 198, 183, false, false, 8, .95f, .15f, .1f, .78f);
            Prop(foreground, "GrassRight", "grass-right", 154, -367, 202, 196, true, false, 8.5f, .84f, 2.4f, .1f, .78f);
            Prop(foreground, "BranchTopRight", "branch", 104, 349, 230, 82, true, true, 4.2f, .68f, 1.3f, 0, .85f);
            Prop(foreground, "FernStoneMiddleLeft", "fern-stone", -141, 54, 155, 84, false, false, 2.1f, .54f, 3.8f, .3f, .87f);
            forest.LeftGrass = (RectTransform)foreground.Find("GrassLeft");
            forest.RightGrass = (RectTransform)foreground.Find("GrassRight");
            forest.Branch = (RectTransform)foreground.Find("BranchTopRight");
            forest.FernStone = (RectTransform)foreground.Find("FernStoneMiddleLeft");
            var particles = Node(foreground, "LeavesAndPetals", 0, 0, 0, 0);
            particles.anchorMin = Vector2.zero; particles.anchorMax = Vector2.one;
            particles.offsetMin = particles.offsetMax = Vector2.zero;
            var effects = particles.GetComponent<ForestParticleEffects>() ?? particles.gameObject.AddComponent<ForestParticleEffects>();
            string[] leafFiles = {"leaf-1", "leaf-2", "leaf-3", "leaf-4"};
            string[] petalFiles = {"petal-blue", "petal-cream", "flower"};
            effects.Leaves = leafFiles.Select(f => Load(f, true)).ToArray();
            effects.LeafUvs = leafFiles.Select(VisibleRect).ToArray();
            effects.Petals = petalFiles.Select(f => Load(f, true)).ToArray();
            effects.PetalUvs = petalFiles.Select(VisibleRect).ToArray();
            effects.ParticleMaterial = DefringeMaterial();
            forest.Effects = effects;
            forest.Configure(true);
            EditorUtility.SetDirty(screen);
        }

        private static void Prop(Transform parent, string name, string file, float x, float y, float w, float h,
            bool mirror, bool pinRight, float amplitude, float speed, float phase, float fixedBase, float brightness)
        {
            var rect = Node(parent, name, x, y, w, h);
            var g = rect.GetComponent<ForestWindGraphic>() ?? rect.gameObject.AddComponent<ForestWindGraphic>();
            g.Texture = Load(file, true); g.Uv = VisibleRect(file);
            g.material = DefringeMaterial();
            g.Mirror = mirror; g.PinRight = pinRight; g.Amplitude = amplitude; g.Speed = speed; g.Phase = phase;
            g.FixedBase = fixedBase; g.color = new Color(brightness, brightness, brightness, 1); g.raycastTarget = false;
            g.SetAllDirty();
        }

        private static Material DefringeMaterial()
        {
            const string path = Folder + "ForestDefringe.mat";
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/_Project/Art/Shaders/ForestDefringe.shader");
            if (shader == null) throw new InvalidOperationException("Forest defringe shader missing");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.SetFloat("_FringePixels", 1.35f); EditorUtility.SetDirty(material);
            return material;
        }

        // Conservative row bounds prevent sampling the transparent phone-shaped export corners.
        // Only those corners extend their nearest opaque edge; the central artwork and divider retain their UVs.
        private static void ConfigureOpaqueRows(ForestBackdropGraphic graphic)
        {
            var source = new Texture2D(2, 2);
            try
            {
                source.LoadImage(File.ReadAllBytes(Folder + "background.png"));
                var pixels = source.GetPixels32(); int w = source.width, h = source.height;
                const int bands = 64;
                var bounds = new Vector2[bands];
                float minX = 1, maxX = 0;
                for (int band = 0; band < bands; band++)
                {
                    int left = 0, right = w - 1;
                    int start = Mathf.FloorToInt(band * (h - 1f) / bands);
                    int end = Mathf.CeilToInt((band + 1) * (h - 1f) / bands);
                    for (int y = start; y <= end; y++)
                    {
                        int lo = 0, hi = w - 1;
                        while (lo < w && pixels[y * w + lo].a < 254) lo++;
                        while (hi >= 0 && pixels[y * w + hi].a < 254) hi--;
                        if (hi - lo < 8) throw new InvalidOperationException("Background contains an empty scanline");
                        left = Math.Max(left, lo + 2); right = Math.Min(right, hi - 2);
                    }
                    bounds[band] = new Vector2((left + .5f) / w, (right + .5f) / w);
                    minX = Mathf.Min(minX, bounds[band].x); maxX = Mathf.Max(maxX, bounds[band].y);
                }
                graphic.SourceX = new Vector2(minX, maxX);
                graphic.OpaqueRows = new Vector2[bands + 1];
                for (int i = 0; i <= bands; i++)
                {
                    var a = bounds[Mathf.Max(0, i - 1)]; var b = bounds[Mathf.Min(bands - 1, i)];
                    graphic.OpaqueRows[i] = new Vector2(Mathf.Max(a.x, b.x), Mathf.Min(a.y, b.y));
                }
                graphic.SetAllDirty();
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }

        private static Texture2D Load(string name, bool alpha)
        {
            string path = Folder + name + ".png";
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new FileNotFoundException(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = alpha;
            importer.mipmapEnabled = false; importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = name == "background" ? 4096 : 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Crop transparent export padding via UVs; the supplied PNGs remain byte-for-byte intact.
        private static Rect VisibleRect(string name)
        {
            var texture = new Texture2D(2, 2);
            try
            {
                texture.LoadImage(File.ReadAllBytes(Folder + name + ".png"));
                int w = texture.width, h = texture.height, left = w, bottom = h, right = -1, top = -1;
                var pixels = texture.GetPixels32();
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                    if (pixels[y * w + x].a > 20)
                    {left = Math.Min(left, x); right = Math.Max(right, x); bottom = Math.Min(bottom, y); top = Math.Max(top, y);}
                if (right < left) throw new InvalidOperationException("Empty foreground: " + name);
                return new Rect(left / (float)w, bottom / (float)h, (right - left + 1f) / w, (top - bottom + 1f) / h);
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
        }

        private static RectTransform Node(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = parent.Find(name) as RectTransform;
            if (rect == null)
            {
                rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(parent, false); rect.gameObject.layer = 5;
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(x, y); rect.sizeDelta = new Vector2(w, h);
            return rect;
        }
    }
}
