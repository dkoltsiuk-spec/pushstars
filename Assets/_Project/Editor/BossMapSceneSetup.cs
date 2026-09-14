using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class BossMapSceneSetup
    {
        public const string Art = "Assets/_Project/UI/Sprites/BossMap/";
        [MenuItem("Tools/Push Stars/Main/Refine Boss FIGHT Labels")]
        public static void UpdateFightLabels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetSceneByPath(AuthoredScenes.MainPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(AuthoredScenes.MainPath, OpenSceneMode.Additive);
            var c = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossMapController>(true)).Single();
            PolishFightLabels(c);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }

        [MenuItem("Tools/Push Stars/Main/Enlarge Boss Island and Animate OK")]
        public static void UpdateIslandAndShine()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetSceneByPath(AuthoredScenes.MainPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(AuthoredScenes.MainPath, OpenSceneMode.Additive);
            var c = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossMapController>(true)).Single();
            EnlargeIslandAndAnimateShine(c);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
            File.WriteAllText("output/boss-map/island-shine.txt", "Map island enlarged to 325 x 367.5. OK shine: 0.6s sweep every 3s.\n");
        }
        [MenuItem("Tools/Push Stars/Main/Add Boss Island and Map")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetSceneByPath(AuthoredScenes.MainPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(AuthoredScenes.MainPath, OpenSceneMode.Additive);
            Directory.CreateDirectory("output/boss-map");
            File.Copy(AuthoredScenes.MainPath, "output/boss-map/Main-before-boss-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            Install(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
            File.WriteAllText("output/boss-map/install.txt", "Boss island and map installed in Main.\n");
        }

        public static BossMapController Install(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var existing = roots.SelectMany(r => r.GetComponentsInChildren<BossMapController>(true)).FirstOrDefault();
            if (existing != null) { Polish(existing); return existing; }
            ImportArt();
            var battle = roots.SelectMany(r => r.GetComponentsInChildren<Button>(true)).Single(b => b.name == "BattleButton");
            var panel = (RectTransform)battle.transform.parent.parent;
            var mirror = panel.GetComponentInParent<DeviceSimulatorMirrorFix>().transform;
            var c = Rect(panel, "BossMapController", 0, 0, 0, 0).gameObject.AddComponent<BossMapController>();
            Undo.RegisterCreatedObjectUndo(c.gameObject, "Add boss island and map");
            c.Battle = roots.SelectMany(r => r.GetComponentsInChildren<SearchOpponentController>(true)).Single();
            c.FriendDuel = roots.SelectMany(r => r.GetComponentsInChildren<FriendDuelController>(true)).FirstOrDefault();
            c.Hint = panel.GetComponentInChildren<Toast>(true);
            c.BottomNav = mirror.GetComponentsInChildren<Transform>(true).Single(t => t.name == "BottomNav").gameObject;
            c.CharacterDecor = new[] { "CharacterArea", "GlowHalo", "GlowCore", "GroundShadow", "GenderSwitch", "FriendSlot", "PlusSlot" }
                .SelectMany(name => panel.Cast<Transform>().Where(t => t.name == name)).Select(t => t.gameObject).ToArray();
            c.HomeOnly = new[] { "ActionRow", "ShopTile", "SpareSlot" }.Select(name => panel.Find(name).gameObject).ToArray();
            var bg = Picture(mirror, "BossBackground", "background", 0, 0, 0, 0);
            Stretch(bg.rectTransform); bg.preserveAspect = false;
            bg.transform.SetSiblingIndex(panel.parent.GetSiblingIndex());
            c.Background = bg.gameObject;
            var skull = AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/UI/Sprites/ModeSelection/skull-pattern.png").OfType<Sprite>().FirstOrDefault();
            if (skull != null)
                for (int row = 0; row < 8; row++) for (int col = 0; col < 4; col++)
                {
                    var tile = Picture(bg.transform, "Skull", skull, -155 + col * 110 + (row % 2) * 40, -420 + row * 125, 94, 94);
                    tile.color = new Color(.65f, .75f, .3f, .06f);
                    tile.rectTransform.localRotation = Quaternion.Euler(0, 0, -18);
                }
            var home = Rect(panel, "BossHome", 0, 0, 0, 0); Stretch(home); c.Home = home.gameObject;
            home.SetSiblingIndex(0);
            var composition = Rect(home, "IslandComposition", 0, 430, 350, 405);
            composition.anchorMin = composition.anchorMax = new Vector2(.5f, 0);
            c.HomeComposition = composition;
            var shadow = Picture(composition, "IslandShadow", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/ground_shadow.png"), 0, -124, 240, 38);
            shadow.color = new Color(0, 0, 0, .38f); shadow.preserveAspect = false;
            c.Island = Clickable(Picture(composition, "ForestIsland", "forest-island", 0, 21, 290, 330));
            var progress = Rect(composition, "IslandProgress", 0, -173, 302, 50);
            c.Nodes = new BossMapController.Node[3];
            for (int i = 0; i < 3; i++)
            {
                var plate = Picture(progress, "BossProgress" + (i + 1), "progress-locked", -119 + 53 * i, 0, 51, 45);
                var face = Picture(plate.transform, "Goblin", "goblin-locked", 0, 7, 42, 42);
                c.Nodes[i] = new BossMapController.Node { BossIndex = i, ProgressPlate = plate, ProgressFace = face };
            }
            Picture(progress, "Milestone", "progress-fill", 20, 0, 15, 42);
            Picture(progress, "RewardStep", "progress-locked", 57, 0, 51, 45);
            Picture(progress, "ChestStep", "progress-locked", 110, 0, 51, 45);
            var bubble = Clickable(Picture(progress, "RewardBubble", "reward-bubble", 22, 57, 62, 60));
            Picture(bubble.transform, "Gem", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/gem.png"), 0, 4, 36, 36);
            UnityEventTools.AddPersistentListener(bubble.onClick, c.ShowRewardHint);
            var map = Rect(panel, "BossMap", 0, 0, 0, 0); Stretch(map); c.Map = map.gameObject;
            c.MapGroup = map.gameObject.AddComponent<CanvasGroup>();
            var viewport = Rect(map, "MapViewport", 0, 0, 0, 0); Stretch(viewport);
            viewport.offsetMin = new Vector2(0, 84); viewport.offsetMax = new Vector2(0, -65);
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            c.Scroll = viewport.gameObject.AddComponent<ScrollRect>();
            c.Scroll.horizontal = false; c.Scroll.vertical = true;
            c.Scroll.movementType = ScrollRect.MovementType.Clamped;
            c.Scroll.scrollSensitivity = 30; c.Scroll.decelerationRate = .09f;
            c.Scroll.viewport = viewport;
            var content = Rect(viewport, "Chapters", 0, 0, 0, 1070);
            content.anchorMin = new Vector2(0, 0); content.anchorMax = new Vector2(1, 0); content.pivot = new Vector2(.5f, 0);
            c.MapContent = content; c.Scroll.content = content;
            // Add future chapter rects under Chapters. Runtime stacks them upward automatically.
            var chapter = Rect(content, "ForestChapter", 0, 0, 390, 1070);
            chapter.anchorMin = chapter.anchorMax = chapter.pivot = new Vector2(.5f, 0);
            Picture(chapter, "IslandShadow", shadow.sprite, 0, 32, 250, 32).color = new Color(0, 0, 0, .4f);
            Picture(chapter, "ForestIsland", "forest-island", 0, 166, 260, 294);
            float[] x = { -67, 72, -73 };
            for (int i = 0; i < c.Nodes.Length; i++)
            {
                var node = c.Nodes[i];
                var root = Rect(chapter, "Boss" + (i + 1), x[i], 420 + 135 * i, 142, 148);
                var area = root.gameObject.AddComponent<Image>(); area.color = Color.clear;
                node.Button = Clickable(area);
                node.Platform = Picture(root, "Platform", "platform-locked", 0, -26, 164, 164);
                node.Disc = Picture(root, "Disc", "node-locked", 0, 5, 94, 98);
                node.Face = Picture(root, "Goblin", "goblin-locked", 0, 8, 66, 66);
                node.Fight = CaptionButton(root, "Fight", "FIGHT", "fight-button", 0, -78, 111, 53);
            }
            Picture(chapter, "RewardPlatform", "platform-locked", 60, 805, 146, 146);
            var reward = Clickable(Picture(chapter, "GemReward", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/gem.png"), 60, 833, 56, 56));
            reward.GetComponent<Image>().color = new Color(.6f, .65f, .55f, 1);
            UnityEventTools.AddPersistentListener(reward.onClick, c.ShowRewardHint);
            var chest = Clickable(Picture(chapter, "IslandChest", "chest", -28, 970, 108, 116));
            UnityEventTools.AddPersistentListener(chest.onClick, c.ShowRewardHint);
            foreach (RectTransform child in chapter) child.anchorMin = child.anchorMax = new Vector2(.5f, 0);
            c.Close = CaptionButton(map, "CloseMap", "OK", "button", 0, 41, 111, 56);
            ((RectTransform)c.Close.transform).anchorMin = ((RectTransform)c.Close.transform).anchorMax = new Vector2(.5f, 0);
            var stripes = Picture(c.Close.transform, "Stripes", "button-stripes", 27, 2, 34, 45); stripes.transform.SetAsFirstSibling();
            // Currency HUD remains live and is never duplicated, including on the map.
            panel.Find("TrophyPill").SetAsLastSibling(); panel.Find("HudGroup").SetAsLastSibling();
            c.Hint.transform.SetAsLastSibling();
            c.Grass = Sprite("platform"); c.Stone = Sprite("platform-locked");
            c.Complete = Sprite("node-complete"); c.Current = Sprite("node-current"); c.Locked = Sprite("node-locked");
            c.Goblin = Sprite("goblin"); c.LockedGoblin = Sprite("goblin-locked");
            c.ProgressCurrent = Sprite("progress-current"); c.ProgressLocked = Sprite("progress-locked");
            home.gameObject.SetActive(false); map.gameObject.SetActive(false); bg.gameObject.SetActive(false);
            Polish(c);
            EditorUtility.SetDirty(c);
            return c;
        }

        private static void Polish(BossMapController c)
        {
            var panel = c.transform.parent;
            c.ResponsiveControls = new[] { "ActionRow", "TrophyPill", "HudGroup" }.Select(n => (RectTransform)panel.Find(n)).ToArray();
            foreach (var tile in c.Background.GetComponentsInChildren<Image>(true))
                if (tile.name == "Skull") tile.color = new Color(.65f, .75f, .3f, .06f);
            var chapter = c.MapContent.Find("ForestChapter");
            var island = (RectTransform)chapter.Find("ForestIsland");
            island.anchoredPosition = new Vector2(0, 166); island.sizeDelta = new Vector2(260, 294);
            foreach (var node in c.Nodes)
            {
                node.Platform.rectTransform.sizeDelta = new Vector2(164, 164);
                var r = (RectTransform)node.Button.transform;
                r.anchoredPosition = new Vector2(r.anchoredPosition.x, 420 + node.BossIndex * 135);
            }
            var rewardPlatform = (RectTransform)chapter.Find("RewardPlatform");
            rewardPlatform.sizeDelta = new Vector2(146, 146); rewardPlatform.anchoredPosition = new Vector2(60, 805);
            ((RectTransform)chapter.Find("GemReward")).anchoredPosition = new Vector2(60, 833);
            ((RectTransform)chapter.Find("IslandChest")).anchoredPosition = new Vector2(-28, 970);
            ((RectTransform)chapter).sizeDelta = new Vector2(390, 1070);
            EnlargeIslandAndAnimateShine(c);
            PolishFightLabels(c);
            EditorUtility.SetDirty(c);
        }

        public static void PolishFightLabels(BossMapController c)
        {
            const string path = Art + "FightLabel.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/UI/Sprites/ModeSelection/ModeOutline.mat"));
                material.name = "FightLabel";
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_OutlineWidth", .18f);
            material.SetFloat("_UnderlayDilate", .5f);
            EditorUtility.SetDirty(material);
            foreach (var node in c.Nodes)
            {
                var label = node.Fight.GetComponentInChildren<TextMeshProUGUI>(true);
                Undo.RecordObject(label, "Refine boss FIGHT text");
                label.fontSize = 21;
                label.fontSharedMaterial = material;
                label.UpdateMeshPadding();
                EditorUtility.SetDirty(label);
            }
            AssetDatabase.SaveAssets();
        }

        private static void EnlargeIslandAndAnimateShine(BossMapController c)
        {
            var chapter = (RectTransform)c.MapContent.Find("ForestChapter");
            var island = (RectTransform)chapter.Find("ForestIsland");
            const float width = 325, height = 367.5f;
            float extraHeight = height - island.sizeDelta.y;
            island.sizeDelta = new Vector2(width, height);
            island.anchoredPosition += Vector2.up * (extraHeight * .5f);
            // Preserve the island's bottom clearance and the gap to the first FIGHT button.
            foreach (RectTransform child in chapter)
                if (child != island && child.name != "IslandShadow") child.anchoredPosition += Vector2.up * extraHeight;
            chapter.sizeDelta += Vector2.up * extraHeight;
            ((RectTransform)chapter.Find("IslandShadow")).sizeDelta = new Vector2(300, 38);

            var close = (RectTransform)c.Close.transform;
            var viewport = close.Find("ShineViewport") as RectTransform;
            if (viewport == null)
            {
                viewport = Rect(close, "ShineViewport", 0, 0, 0, 0);
                Stretch(viewport);
                viewport.offsetMin = new Vector2(9, 10); viewport.offsetMax = new Vector2(-9, -7);
                viewport.gameObject.AddComponent<RectMask2D>();
                viewport.SetAsFirstSibling();
            }
            var stripes = close.Find("Stripes") as RectTransform ?? viewport.Find("Stripes") as RectTransform;
            if (stripes == null) stripes = Picture(viewport, "Stripes", "button-stripes", 0, 0, 34, 45).rectTransform;
            stripes.SetParent(viewport, false);
            stripes.anchoredPosition = Vector2.zero;
            var shine = viewport.GetComponent<ButtonShineSweep>();
            if (shine == null) shine = viewport.gameObject.AddComponent<ButtonShineSweep>();
            shine.Stripes = stripes.GetComponent<Image>(); shine.Stripes.raycastTarget = false;
            shine.Interval = 3; shine.Duration = .6f; shine.Stripes.enabled = false;
            EditorUtility.SetDirty(shine);

            foreach (var node in c.Nodes)
            {
                var pulse = node.Button.transform.Find("ActiveBossPulse") as RectTransform;
                if (pulse == null) pulse = Rect(node.Button.transform, "ActiveBossPulse", 0, 5, 152, 158);
                pulse.SetAsLastSibling();
                pulse.SetSiblingIndex(node.Disc.transform.GetSiblingIndex());
                if (pulse.GetComponent<CanvasRenderer>() == null) pulse.gameObject.AddComponent<CanvasRenderer>();
                var rings = pulse.GetComponent<BossPulseRings>();
                if (rings == null) rings = pulse.gameObject.AddComponent<BossPulseRings>();
                rings.ActiveMarker = node.Fight.gameObject;
                rings.color = new Color(1, .85f, .03f, .65f);
                rings.raycastTarget = false;
                EditorUtility.SetDirty(rings);
            }
        }

        private static void ImportArt()
        {
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(Art, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }
        private static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + name + ".png");
        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false); r.gameObject.layer = 5;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f, .5f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h); return r;
        }
        private static void Stretch(RectTransform r)
        { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }
        private static Image Picture(Transform parent, string name, string art, float x, float y, float w, float h)
            => Picture(parent, name, Sprite(art), x, y, w, h);
        private static Image Picture(Transform parent, string name, Sprite sprite, float x, float y, float w, float h)
        {
            var image = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false; return image;
        }
        private static Button Clickable(Image image)
        {
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            button.navigation = new Navigation { mode = Navigation.Mode.None }; return button;
        }
        private static Button CaptionButton(Transform parent, string name, string caption, string art, float x, float y, float w, float h)
        {
            var b = Clickable(Picture(parent, name, art, x, y, w, h));
            var label = Rect(b.transform, "Label", 0, 1, w - 12, h - 8).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.BoldAsset);
            label.fontSharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/UI/Sprites/ModeSelection/ModeOutline.mat");
            label.text = caption; label.fontSize = 23; label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false; return b;
        }
    }
}
