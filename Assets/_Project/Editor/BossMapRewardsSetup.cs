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
    /// live <see cref="BossMapController.RewardNode"/>s; the ice island shows its five golems, gems and
    /// chest as a non-interactive COMING SOON preview until its models are supplied. Idempotent:
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
                    "Forest: bosses 1-3, gem islet, bosses 4-5, bouncing chest. Ice: five golems, gems and chest (COMING SOON).\n");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static void Install(BossMapController c)
        {
            var forest = (RectTransform)c.MapContent.Find("ForestChapter");
            var slots = PathSlots(ForestFirst, BossCatalog.Chapters[0]);
            for (int i = 0; i < c.Nodes.Length && i < slots.Bosses.Count; i++)
                ((RectTransform)c.Nodes[i].Button.transform).anchoredPosition = slots.Bosses[i];

            var gems = RewardRoot(forest, "GemIslet", slots.Gems);
            var platform = Adopt(forest, gems, "RewardPlatform", new Vector2(0, -26), new Vector2(164, 164), false);
            var gem = Adopt(forest, gems, "GemReward", new Vector2(0, -12), new Vector2(64, 64), true);
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
            c.Rewards = new[]
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

            BuildIcePreview(c);
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
            var area = root.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
            area.color = Color.clear; area.raycastTarget = true;
            var button = root.GetComponent<Button>() ?? root.gameObject.AddComponent<Button>();
            button.targetGraphic = area; button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(button.onClick, i);
            var bounce = icon.GetComponent<RewardBounce>() ?? icon.gameObject.AddComponent<RewardBounce>();
            bounce.Height = hop;
            EditorUtility.SetDirty(bounce);
            return new BossMapController.RewardNode
            {
                ChapterId = chapter, Kind = kind, Button = button, Platform = platform, Icon = icon, Bounce = bounce
            };
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
