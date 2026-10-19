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
    /// <summary>
    /// Installs an island's battle layers next to the forest ones: its PvP arena split into boss/player
    /// halves, a procedural ledge on the seam, and calm ambient particles with chip bursts on boss hits.
    /// The ice island gets the ice temple with snow (tools/art/paint_ice_particles.py); the lava island the
    /// lava forge with rising embers (tools/art/paint_lava_particles.py).
    /// </summary>
    public static class ArenaIslandSetup
    {
        public sealed class Island
        {
            public string Chapter, Name, ArenaId, Folder, ParticlesNode;
            public Color32 Matte;
            public Rect TopSource, BottomSource;
            public float TopPivot, BottomPivot;
            public Color TopTint, BottomTint;
            /// <summary>Ambient sprites, then burst sprites; the last burst sprite is the ground-burst accent.</summary>
            public string[] Ambient, Bursts;
            public Action<IslandLedgeGraphic> Ledge;
            public Action<ForestParticleEffects> Particles;
            public string FirstSprite => Folder + Ambient[0] + ".png";
        }

        public static readonly Island Ice = new Island
        {
            Chapter = "ice", Name = "Ice", ArenaId = "ice-temple", Folder = "Assets/_Project/UI/Sprites/IceIsland/",
            ParticlesNode = "SnowAndIceChips", Matte = new Color32(10, 24, 44, 255),
            // Boss half: the arch with the floor line a quarter up; player half: the near floor.
            TopSource = new Rect(0, .266f, 1, .518f), TopPivot = .25f, BottomSource = new Rect(0, 0, 1, .51f), BottomPivot = .1f,
            TopTint = new Color(.8f, .87f, .96f), BottomTint = new Color(.8f, .86f, .95f),
            // Mostly soft dots with the odd crystal flake.
            Ambient = new[] {"snow-dot", "snow-dot", "snowflake"}, Bursts = new[] {"ice-chip-1", "ice-chip-2", "ice-chunk"},
            Ledge = ledge => { },
            Particles = fx =>
            {
                fx.AmbientMax = 16; fx.AmbientSeed = 10;
                fx.AmbientInterval = new Vector2(.45f, .8f); fx.AmbientSize = new Vector2(5, 10);
                fx.AmbientFall = new Vector2(20, 34); fx.AmbientSpin = 20; fx.AmbientTint = 1;
                fx.AmbientTumble = false; fx.AmbientRise = false;
            },
        };

        public static readonly Island Lava = new Island
        {
            Chapter = "lava", Name = "Lava", ArenaId = "lava-forge", Folder = "Assets/_Project/UI/Sprites/LavaIsland/",
            ParticlesNode = "EmbersAndBasaltChips", Matte = new Color32(34, 12, 10, 255),
            // Same template as the ice temple: the glowing gate behind the boss, the near floor under the player.
            TopSource = new Rect(0, .266f, 1, .518f), TopPivot = .25f, BottomSource = new Rect(0, 0, 1, .51f), BottomPivot = .1f,
            // The forge glows close to the magma golems' own orange; dimmed so the boss reads in front of it.
            TopTint = new Color(.72f, .6f, .58f), BottomTint = new Color(.74f, .66f, .68f),
            // Few, small embers; a grey ash flake now and then.
            Ambient = new[] {"ember-dot", "ember-dot", "ash-flake"}, Bursts = new[] {"basalt-chip-1", "basalt-chip-2", "basalt-chunk"},
            // Cooled basalt crust over a glowing magma seam with short molten drips.
            Ledge = ledge =>
            {
                ledge.Cap = new Color32(118, 90, 82, 255); ledge.CapShade = new Color32(56, 38, 38, 255);
                ledge.BandTop = new Color32(255, 196, 82, 255); ledge.BandBottom = new Color32(196, 52, 20, 255);
                ledge.Shadow = new Color32(24, 6, 4, 140); ledge.CapDepth = .5f;
            },
            Particles = fx =>
            {
                fx.AmbientMax = 12; fx.AmbientSeed = 8;
                fx.AmbientInterval = new Vector2(.5f, .9f); fx.AmbientSize = new Vector2(4, 8);
                fx.AmbientFall = new Vector2(16, 28); fx.AmbientSpin = 15; fx.AmbientTint = 1;
                fx.AmbientTumble = false; fx.AmbientRise = true;
            },
        };

        public static readonly Island[] Islands = {Ice, Lava};

        /// <summary>Seam just above the player HP bar, as a fraction of the 844-unit content height.</summary>
        public const float Seam = .5f + 6f / 844;
        private const string ScenePath = "Assets/_Project/Scenes/Fight.unity";

        [MenuItem("Tools/Push Stars/Boss/Install Ice Island")]
        public static void RunIce() => Run(Ice);

        [MenuItem("Tools/Push Stars/Boss/Install Lava Island")]
        public static void RunLava() => Run(Lava);

        public static void Run(params Island[] islands)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("Save the authored Fight scene before installing an island.");
            Directory.CreateDirectory("output/arena-islands");
            File.Copy(ScenePath, "output/arena-islands/Fight-before-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + ".unity");
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossCombatScreen>(true)).Single();
                foreach (var island in islands) Apply(screen, island);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static ArenaIslandPresentation Presentation(BossCombatScreen screen, Island island) =>
            island == Ice ? screen.Ice : island == Lava ? screen.Lava : throw new ArgumentException(island.Chapter);

        public static void Apply(BossCombatScreen screen, Island island)
        {
            if (screen.Preparation) return;
            var content = screen.Content;
            var presentation = Presentation(screen, island);
            if (presentation == null) presentation = screen.Root.AddComponent<ArenaIslandPresentation>();
            if (island == Ice) screen.Ice = presentation; else screen.Lava = presentation;
            presentation.ArenaId = island.ArenaId;
            presentation.OriginalDecor = content.Cast<Transform>().Where(t => t.name.StartsWith("Pattern", StringComparison.Ordinal))
                .Select(t => t.gameObject).Concat(new[] {screen.Root.transform.Find("Backdrop").gameObject}).ToArray();

            var matte = Node(screen.Root.transform, island.Name + "IslandMatte", 0, 0, 0, 0);
            matte.anchorMin = Vector2.zero; matte.anchorMax = Vector2.one; matte.offsetMin = matte.offsetMax = Vector2.zero;
            var fill = matte.GetComponent<Image>();
            if (fill == null) fill = matte.gameObject.AddComponent<Image>();
            fill.color = island.Matte; fill.raycastTarget = false;
            matte.SetAsFirstSibling(); presentation.Matte = matte.gameObject;

            var background = Node(content, island.Name + "IslandBackground", 0, 0, 390, 844);
            var backdrop = background.GetComponent<SplitArenaGraphic>();
            if (backdrop == null) backdrop = background.gameObject.AddComponent<SplitArenaGraphic>();
            backdrop.Seam = Seam; backdrop.raycastTarget = false; backdrop.Sprite = null;
            backdrop.TopSource = island.TopSource; backdrop.TopPivot = island.TopPivot;
            backdrop.BottomSource = island.BottomSource; backdrop.BottomPivot = island.BottomPivot;
            backdrop.TopTint = island.TopTint; backdrop.BottomTint = island.BottomTint;
            background.SetAsFirstSibling();
            presentation.Background = background.gameObject; presentation.Backdrop = backdrop;

            var ledgeRect = Node(background, island.Name + "Ledge", 0, 0, 0, 16);
            ledgeRect.anchorMin = new Vector2(0, Seam); ledgeRect.anchorMax = new Vector2(1, Seam);
            ledgeRect.sizeDelta = new Vector2(0, 16); ledgeRect.anchoredPosition = Vector2.zero;
            // Re-added on every install so the scene picks up the graphic's current palette defaults.
            var oldLedge = ledgeRect.GetComponent<IslandLedgeGraphic>();
            if (oldLedge != null) UnityEngine.Object.DestroyImmediate(oldLedge);
            var ledge = ledgeRect.gameObject.AddComponent<IslandLedgeGraphic>();
            island.Ledge(ledge);
            ledge.raycastTarget = false; ledge.SetAllDirty();

            var foreground = Node(content, island.Name + "IslandForeground", 0, 0, 390, 844);
            if (foreground.GetComponent<RectMask2D>() == null) foreground.gameObject.AddComponent<RectMask2D>();
            // Above fighter images and their shadows, below all HP, labels and clickable controls.
            foreground.SetSiblingIndex(Mathf.Max(screen.PlayerPortrait.transform.GetSiblingIndex(), screen.BossPortrait.transform.GetSiblingIndex()) + 1);
            presentation.Foreground = foreground.gameObject;
            var particles = Node(foreground, island.ParticlesNode, 0, 0, 0, 0);
            particles.anchorMin = Vector2.zero; particles.anchorMax = Vector2.one;
            particles.offsetMin = particles.offsetMax = Vector2.zero;
            var effects = particles.GetComponent<ForestParticleEffects>();
            if (effects == null) effects = particles.gameObject.AddComponent<ForestParticleEffects>();
            effects.Leaves = island.Ambient.Select(f => Load(island, f)).ToArray();
            effects.LeafUvs = island.Ambient.Select(f => new Rect(0, 0, 1, 1)).ToArray();
            effects.Petals = island.Bursts.Select(f => Load(island, f)).ToArray();
            effects.PetalUvs = island.Bursts.Select(f => new Rect(0, 0, 1, 1)).ToArray();
            effects.ParticleMaterial = null;
            island.Particles(effects);
            presentation.Effects = effects;
            // Saved switched off; BossCombatScreen turns it on for this island's bosses. The shared decor is
            // left to the forest layers' saved state.
            background.gameObject.SetActive(false); foreground.gameObject.SetActive(false); matte.gameObject.SetActive(false);
            EditorUtility.SetDirty(screen);
        }

        private static Texture2D Load(Island island, string name)
        {
            string path = island.Folder + name + ".png";
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new FileNotFoundException(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false; importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 64;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
