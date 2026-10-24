using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Island path layout and collectibles. Every island reads bottom to top: boss 1–3, the gem
    /// islet, the remaining bosses, then the main boss's chest. The forest's gem islet and chest are
    /// live <see cref="BossMapController.RewardNode"/>s. Playable golem islands (ice, lava) get live
    /// nodes (forest node clones with the island face icon on island platforms) and their own gems and
    /// chest; an unplayable ice island stays a non-interactive COMING SOON preview. Idempotent:
    /// positions are absolute and objects are found by name before anything is created.
    /// </summary>
    public static class BossMapRewardsSetup
    {
        private const float Step = 135;
        private const float ForestFirst = 493.5f;
        private static readonly float[] ZigZag = { -67, 72, -73, 72, -73, 72, -73, 72 };

        [MenuItem("Tools/Push Stars/Main/Install Boss Map Rewards")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(AuthoredScenes.MainPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(AuthoredScenes.MainPath, OpenSceneMode.Additive);
            try
            {
                var c = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossMapController>(true)).Single();
                Install(c);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("output/boss-map");
                File.WriteAllText("output/boss-map/rewards-install.txt",
                    "Forest: bosses 1-3, gem islet, bosses 4-5, bouncing chest. Ice: five golems, gems and chest" +
                    (BossCatalog.FindChapter("ice").Playable ? " (live)." : " (COMING SOON).") +
                    (BossCatalog.FindChapter("lava").Playable ? " Lava: six golems, gems and chest (live).\n" : "\n"));
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static void Install(BossMapController c)
        {
            var forest = (RectTransform)c.MapContent.Find("ForestChapter");
            var slots = PathSlots(ForestFirst, BossCatalog.Chapters[0]);
            // Every island shows one shared face icon on its bosses; the fight HUD keeps the portraits.
            var forestFace = AssetDatabase.LoadAssetAtPath<Sprite>(BossMapSceneSetup.Art + "forest-face.png");
            for (int i = 0; i < c.Nodes.Length && i < slots.Bosses.Count; i++)
            {
                ((RectTransform)c.Nodes[i].Button.transform).anchoredPosition = slots.Bosses[i];
                c.Nodes[i].FaceArt = forestFace;
            }

            var gems = RewardRoot(forest, "GemIslet", slots.Gems);
            var platform = Adopt(forest, gems, "RewardPlatform", new Vector2(0, -26), new Vector2(164, 164), false);
            var gem = Adopt(forest, gems, "GemReward", new Vector2(0, -12), GemPileSize, true);
            gem.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GemPile); gem.preserveAspect = true;
            var chest = RewardRoot(forest, "ChestNode", slots.Chest);
            var chestPlatform = chest.Find("ChestPlatform") as RectTransform;
            if (chestPlatform == null)
            {
                chestPlatform = Object.Instantiate(platform.gameObject, chest).GetComponent<RectTransform>();
                chestPlatform.name = "ChestPlatform";
            }
            chestPlatform.SetAsFirstSibling();
            chestPlatform.anchoredPosition = new Vector2(0, -26); chestPlatform.sizeDelta = new Vector2(164, 164);
            var chestIcon = Adopt(forest, chest, "IslandChest", new Vector2(0, -36), new Vector2(104, 112), true);
            var rewards = new List<BossMapController.RewardNode>
            {
                Reward(c, "forest", BossMapController.RewardKind.Gems, gems, platform, gem, 9),
                Reward(c, "forest", BossMapController.RewardKind.Chest, chest, chestPlatform.GetComponent<Image>(), chestIcon, 16),
            };
            forest.sizeDelta = new Vector2(390, slots.Chest.y + 100);

            var bubble = c.Nodes[0].ProgressPlate.transform.parent.Find("RewardBubble") as RectTransform;
            if (bubble != null)
            {
                // Between the third and fourth plates of the home strip, where the islet sits on the path.
                bubble.anchoredPosition = new Vector2(28, 57);
                c.HomeGemBubble = bubble.gameObject;
            }

            var ice = BossCatalog.FindChapter("ice");
            if (ice.Playable) BuildIsland(c, rewards, ice, (RectTransform)c.MapContent.Find("IceChapter"), "Ice",
                AssetDatabase.LoadAssetAtPath<Sprite>(BossMapSceneSetup.Art + "ice-face.png"));
            else BuildIcePreview(c);
            var lava = BossCatalog.FindChapter("lava");
            if (lava.Playable) BuildIsland(c, rewards, lava, LavaChapter(c), "Lava",
                AssetDatabase.LoadAssetAtPath<Sprite>(BossMapSceneSetup.Art + "lava-face.png"));
            c.Rewards = rewards.ToArray();
            EnsureHomePlates(c);
            c.HomeIslands = new[] { "forest-island", "ice-island", "lava-island" }
                .Select(art => AssetDatabase.LoadAssetAtPath<Sprite>(BossMapSceneSetup.Art + art + ".png")).ToArray();
            c.RefreshProgress();
            EditorUtility.SetDirty(c);
        }

        private struct Slots { public List<Vector2> Bosses; public Vector2 Gems, Chest; }

        /// <summary>Zig-zag path: one step per boss, one for the gem islet after GemsAfterBoss,
        /// then the chest a step and a bit above the main boss.</summary>
        private static Slots PathSlots(float first, BossChapter chapter)
        {
            var slots = new Slots { Bosses = new List<Vector2>() };
            int step = 0;
            for (int i = 0; i < chapter.Bosses.Count; i++)
            {
                slots.Bosses.Add(new Vector2(ZigZag[step % ZigZag.Length], first + step * Step)); step++;
                if (i == chapter.GemsAfterBoss) { slots.Gems = new Vector2(ZigZag[step % ZigZag.Length], first + step * Step); step++; }
            }
            slots.Chest = new Vector2(ZigZag[step % ZigZag.Length], first + step * Step + 10);
            return slots;
        }

        private static RectTransform RewardRoot(RectTransform chapter, string name, Vector2 position)
        {
            var root = chapter.Find(name) as RectTransform;
            if (root == null)
            {
                root = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                root.SetParent(chapter, false); root.gameObject.layer = 5;
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Add boss map reward");
            }
            root.anchorMin = root.anchorMax = new Vector2(.5f, 0); root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = position; root.sizeDelta = new Vector2(142, 148); root.localScale = Vector3.one;
            return root;
        }

        /// <summary>Moves an existing chapter image under its reward root and strips its old
        /// "coming later" button; the root owns the tap from now on.</summary>
        private static Image Adopt(RectTransform chapter, RectTransform root, string name, Vector2 position, Vector2 size, bool grounded)
        {
            var rect = (root.Find(name) ?? chapter.Find(name)) as RectTransform;
            if (rect == null) throw new InvalidOperationException("Missing map object " + name);
            rect.SetParent(root, false);
            var button = rect.GetComponent<Button>();
            if (button != null) Object.DestroyImmediate(button);
            var image = rect.GetComponent<Image>();
            image.raycastTarget = false; image.color = Color.white;
            // Bouncing icons pivot on their bottom edge so the squash lands on the platform.
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, grounded ? 0 : .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one;
            return image;
        }

        private static BossMapController.RewardNode Reward(BossMapController c, string chapter, BossMapController.RewardKind kind,
            RectTransform root, Image platform, Image icon, float hop)
        {
            // Explicit null checks: `??` sees Unity's fake null for a missing component in the Editor.
            var area = root.GetComponent<Image>();
            if (area == null) area = root.gameObject.AddComponent<Image>();
            area.color = Color.clear; area.raycastTarget = true;
            var button = root.GetComponent<Button>();
            if (button == null) button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = area; button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            var bounce = icon.GetComponent<RewardBounce>();
            if (bounce == null) bounce = icon.gameObject.AddComponent<RewardBounce>();
            bounce.Height = hop;
            EditorUtility.SetDirty(bounce);
            return new BossMapController.RewardNode
            {
                ChapterId = chapter, Kind = kind, Button = button, Platform = platform, Icon = icon, Bounce = bounce,
                Amount = kind == BossMapController.RewardKind.Gems ? GemAmount(icon, chapter) : null
            };
        }

        /// <summary>A pile, not a single gem: the islet pays a handful at once.</summary>
        private const string GemPile = "Assets/_Project/Resources/Rewards/Gems.png";
        private static readonly Vector2 GemPileSize = new Vector2(96, 68);
        private const string KeylinePath = "Assets/_Project/UI/Fonts/Rubik Black TMP Map Keyline.mat";

        /// <summary>"x25" on the pile's lower right, white with a black keyline. A child of the
        /// pile, so it bounces and hides with it; the number itself is filled from the catalog.</summary>
        private static TextMeshProUGUI GemAmount(Image pile, string chapter)
        {
            var rect = pile.transform.Find("Amount") as RectTransform;
            if (rect == null)
            {
                rect = new GameObject("Amount", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
                rect.SetParent(pile.transform, false); rect.gameObject.layer = 5;
                Undo.RegisterCreatedObjectUndo(rect.gameObject, "Add gem amount");
            }
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(30, 10); rect.sizeDelta = new Vector2(76, 34); rect.localScale = Vector3.one;
            var label = rect.GetComponent<TextMeshProUGUI>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik Black TMP.asset");
            label.fontSharedMaterial = Keyline(label.font);
            label.fontSize = 27; label.fontStyle = FontStyles.Italic;
            label.alignment = TextAlignmentOptions.Center; label.textWrappingMode = TextWrappingModes.NoWrap;
            label.color = Color.white; label.raycastTarget = false;
            label.text = "x" + (BossCatalog.FindChapter(chapter)?.GemReward ?? 0);
            EditorUtility.SetDirty(label);
            return label;
        }

        /// <summary>Shared keyline material, saved as an asset so the scene never holds a
        /// material that only lived in memory.</summary>
        private static Material Keyline(TMP_FontAsset font)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(KeylinePath);
            if (material != null) return material;
            material = new Material(font.material) { name = "Rubik Black TMP Map Keyline" };
            material.EnableKeyword("OUTLINE_ON");
            material.SetFloat("_OutlineWidth", .22f);
            material.SetColor("_OutlineColor", Color.black);
            material.SetFloat("_FaceDilate", .2f);
            AssetDatabase.CreateAsset(material, KeylinePath);
            return material;
        }

        private static void BuildIcePreview(BossMapController c)
        {
            var ice = (RectTransform)c.MapContent.Find("IceChapter");
            if (ice == null) return;
            foreach (string obsolete in new[] { "IcePlatform", "IceDisc", "IceFace" })
            {
                var old = ice.Find(obsolete);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }
            var chapter = BossCatalog.FindChapter("ice");
            var island = (RectTransform)ice.Find("IceIsland");
            float first = island.anchoredPosition.y + island.sizeDelta.y * .5f + 107;
            var slots = PathSlots(first, chapter);
            var faceTint = new Color(.62f, .68f, .75f, 1);
            var anchors = new List<RectTransform> { island };
            for (int i = 0; i < slots.Bosses.Count; i++)
            {
                bool king = i == slots.Bosses.Count - 1;
                var node = Group(ice, "IceBoss" + (i + 1), slots.Bosses[i], new Vector2(142, 148));
                node.localScale = Vector3.one * (king ? 1.12f : 1);
                Picture(node, "Platform", "ice-platform", new Vector2(0, -26), new Vector2(164, 164), Color.white);
                Picture(node, "Disc", "node-locked", new Vector2(0, 5), new Vector2(94, 98), Color.white);
                Picture(node, "Face", "ice-face", new Vector2(0, 8), new Vector2(66, 66), faceTint);
                anchors.Add(node);
            }
            var gems = Group(ice, "IceGemIslet", slots.Gems, new Vector2(142, 148));
            Picture(gems, "Platform", "ice-platform", new Vector2(0, -26), new Vector2(164, 164), Color.white);
            var gem = Picture(gems, "Gem", null, new Vector2(0, 16), new Vector2(64, 64), new Color(.55f, .6f, .62f, 1));
            gem.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/gem.png");
            var chest = Group(ice, "IceChest", slots.Chest, new Vector2(142, 148));
            Picture(chest, "Platform", "ice-platform", new Vector2(0, -26), new Vector2(164, 164), Color.white).transform.SetAsFirstSibling();
            Picture(chest, "Chest", "chest", new Vector2(0, 20), new Vector2(104, 112), new Color(.55f, .6f, .66f, 1));

            var label = (RectTransform)ice.Find("ComingSoon");
            label.anchoredPosition = new Vector2(0, slots.Chest.y + 120);
            label.SetAsLastSibling();
            var text = label.GetComponent<TextMeshProUGUI>();
            text.text = "ICE ISLAND\n<size=18>5 GOLEMS · COMING SOON</size>";
            ice.sizeDelta = new Vector2(390, label.anchoredPosition.y + 90);
            var biome = ice.GetComponent<BossMapBiome>();
            if (biome != null) { biome.LightAnchors = anchors.ToArray(); EditorUtility.SetDirty(biome); }
            EditorUtility.SetDirty(text);
        }

        /// <summary>Live golem island ("Ice", "Lava"): nodes cloned from a forest node (platform,
        /// disc, face, FIGHT), the gem islet after golem 3 and the king's chest, all on the island's
        /// platform art. Objects are named prefix + Island/Boss1../GemIslet/Chest. Faces are the
        /// golem portraits unless the island passes one shared face.</summary>
        private static void BuildIsland(BossMapController c, List<BossMapController.RewardNode> rewards,
            BossChapter chapter, RectTransform ice, string prefix, Sprite face = null)
        {
            foreach (string obsolete in new[] { "IcePlatform", "IceDisc", "IceFace" })
            {
                var old = ice.Find(obsolete);
                if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            }
            int start = BossCatalog.LadderStart(chapter);
            var island = (RectTransform)ice.Find(prefix + "Island");
            float first = island.anchoredPosition.y + island.sizeDelta.y * .5f + 107;
            var slots = PathSlots(first, chapter);
            string platformName = prefix.ToLowerInvariant() + "-platform";
            var platformArt = AssetDatabase.LoadAssetAtPath<Sprite>(BossMapSceneSetup.Art + platformName + ".png");
            var template = c.Nodes.First(n => n.ProgressPlate != null).Button.gameObject;
            var nodes = c.Nodes.Where(n => !n.Button.transform.IsChildOf(ice)).ToList();
            var anchors = new List<RectTransform> { island };
            for (int i = 0; i < chapter.Bosses.Count; i++)
            {
                string name = prefix + "Boss" + (i + 1);
                var root = ice.Find(name) as RectTransform;
                if (root != null && root.Find("Fight") == null) { Undo.DestroyObjectImmediate(root.gameObject); root = null; }
                if (root == null)
                {
                    root = (RectTransform)Object.Instantiate(template, ice).transform;
                    root.name = name;
                    Undo.RegisterCreatedObjectUndo(root.gameObject, "Add golem node");
                }
                root.anchorMin = root.anchorMax = new Vector2(.5f, 0);
                root.anchoredPosition = slots.Bosses[i];
                root.localScale = Vector3.one * (i == chapter.Bosses.Count - 1 ? 1.12f : 1);
                var node = new BossMapController.Node
                {
                    BossIndex = start + i, Button = root.GetComponent<Button>(),
                    Platform = root.Find("Platform").GetComponent<Image>(),
                    Disc = root.Find("Disc").GetComponent<Image>(), Face = root.Find("Goblin").GetComponent<Image>(),
                    Fight = root.Find("Fight").GetComponent<Button>(),
                    FaceArt = face != null ? face : AssetDatabase.LoadAssetAtPath<Sprite>(GolemBossSetup.PortraitPath(chapter.Bosses[i].PrefabId)),
                    PlatformArt = platformArt
                };
                if (node.FaceArt == null)
                    throw new InvalidOperationException("Missing golem portrait for " + chapter.Bosses[i].Id + "; run the island's Import Golems first.");
                var pulse = root.GetComponentInChildren<BossPulseRings>(true);
                if (pulse != null) { pulse.ActiveMarker = node.Fight.gameObject; EditorUtility.SetDirty(pulse); }
                nodes.Add(node);
                anchors.Add(root);
            }
            c.Nodes = nodes.OrderBy(n => n.BossIndex).ToArray();

            var gems = Group(ice, prefix + "GemIslet", slots.Gems, new Vector2(142, 148));
            var gemPlatform = Picture(gems, "Platform", platformName, new Vector2(0, -26), new Vector2(164, 164), Color.white);
            var gem = Picture(gems, "Gem", null, new Vector2(0, -12), GemPileSize, Color.white);
            gem.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(GemPile);
            gem.rectTransform.pivot = new Vector2(.5f, 0);
            var chest = Group(ice, prefix + "Chest", slots.Chest, new Vector2(142, 148));
            var chestPlatform = Picture(chest, "Platform", platformName, new Vector2(0, -26), new Vector2(164, 164), Color.white);
            chestPlatform.transform.SetAsFirstSibling();
            var chestIcon = Picture(chest, "Chest", "chest", new Vector2(0, -36), new Vector2(104, 112), Color.white);
            chestIcon.rectTransform.pivot = new Vector2(.5f, 0);
            foreach (var (kind, root, platform, icon, hop) in new[]
            {
                (BossMapController.RewardKind.Gems, gems, gemPlatform, gem, 9f),
                (BossMapController.RewardKind.Chest, chest, chestPlatform, chestIcon, 16f)
            })
            {
                var reward = Reward(c, chapter.Id, kind, root, platform, icon, hop);
                reward.PlatformArt = platformArt;
                rewards.Add(reward);
                anchors.Add(root);
            }

            // The island is live: its COMING SOON title goes, and the chapter ends above the chest like the forest.
            var label = ice.Find("ComingSoon");
            if (label != null) label.gameObject.SetActive(false);
            ice.sizeDelta = new Vector2(390, slots.Chest.y + 100);
            var biome = ice.GetComponent<BossMapBiome>();
            if (biome != null) { biome.LightAnchors = anchors.ToArray(); EditorUtility.SetDirty(biome); }
        }

        /// <summary>Lava chapter above the ice one: the supplied lava island and a molten biome.</summary>
        private static RectTransform LavaChapter(BossMapController c)
        {
            var lava = c.MapContent.Find("LavaChapter") as RectTransform;
            if (lava == null)
            {
                foreach (string asset in new[] { "lava-island", "lava-platform", "lava-face" })
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(BossMapSceneSetup.Art + asset + ".png");
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.maxTextureSize = 2048;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.SaveAndReimport();
                }
                lava = Group((RectTransform)c.MapContent, "LavaChapter", Vector2.zero, new Vector2(390, 740));
                lava.anchorMin = lava.anchorMax = lava.pivot = new Vector2(.5f, 0);
            }
            lava.SetAsLastSibling();
            var island = Picture(lava, "LavaIsland", "lava-island", new Vector2(0, 240), new Vector2(325, 340), Color.white);
            island.rectTransform.anchorMin = island.rectTransform.anchorMax = new Vector2(.5f, 0);
            var biome = lava.GetComponent<BossMapBiome>();
            if (biome == null) biome = lava.gameObject.AddComponent<BossMapBiome>();
            biome.Background = new Color(.36f, .13f, .16f);
            biome.Mist = new Color(.95f, .46f, .2f);
            biome.Light = new Color(1f, .72f, .36f);
            EditorUtility.SetDirty(biome);
            var backdrop = c.Background.GetComponentInChildren<BossMapBackdrop>(true);
            if (backdrop != null)
            {
                backdrop.Biomes = c.MapContent.Cast<Transform>().Select(t => t.GetComponent<BossMapBiome>()).Where(b => b != null).ToArray();
                EditorUtility.SetDirty(backdrop);
            }
            return lava;
        }

        /// <summary>The home strip needs a plate per boss of the longest playable island; the
        /// forest nodes own five, the rest are clones of the first.</summary>
        private static void EnsureHomePlates(BossMapController c)
        {
            var owned = c.Nodes.Where(n => n.ProgressPlate != null).ToList();
            int need = BossCatalog.Chapters.Where(ch => ch.Playable).Max(ch => ch.Bosses.Count) - owned.Count;
            var strip = owned[0].ProgressPlate.transform.parent;
            var extra = new List<BossMapController.HomePlate>();
            for (int i = 0; i < need; i++)
            {
                string name = "BossProgress" + (owned.Count + i + 1);
                var plate = strip.Find(name)?.GetComponent<Image>();
                if (plate == null)
                {
                    plate = Object.Instantiate(owned[0].ProgressPlate, strip);
                    plate.name = name;
                    Undo.RegisterCreatedObjectUndo(plate.gameObject, "Add home plate");
                }
                extra.Add(new BossMapController.HomePlate { Plate = plate, Face = plate.transform.Find("Goblin").GetComponent<Image>() });
            }
            c.ExtraPlates = extra.ToArray();
        }

        private static RectTransform Group(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = parent.Find(name) as RectTransform;
            if (rect == null)
            {
                rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(parent, false); rect.gameObject.layer = 5;
                Undo.RegisterCreatedObjectUndo(rect.gameObject, "Add ice preview");
            }
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0); rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }

        private static Image Picture(RectTransform parent, string name, string art, Vector2 position, Vector2 size, Color color)
        {
            var rect = parent.Find(name) as RectTransform;
            if (rect == null)
            {
                rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
                rect.SetParent(parent, false); rect.gameObject.layer = 5;
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = rect.GetComponent<Image>();
            if (art != null) image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BossMapSceneSetup.Art + art + ".png");
            image.preserveAspect = true; image.raycastTarget = false; image.color = color;
            return image;
        }
    }
}
