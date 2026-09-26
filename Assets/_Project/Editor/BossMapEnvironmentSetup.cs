using System;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class BossMapEnvironmentSetup
    {
        [MenuItem("Tools/Push Stars/Main/Install Boss Map Biomes")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(AuthoredScenes.MainPath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(AuthoredScenes.MainPath, OpenSceneMode.Additive);
            try
            {
                var controller = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BossMapController>(true)).Single();
                Install(controller);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        public static void Install(BossMapController c)
        {
            var forest = (RectTransform)c.MapContent.Find("ForestChapter");
            var forestBiome = forest.GetComponent<BossMapBiome>() ?? forest.gameObject.AddComponent<BossMapBiome>();
            forestBiome.LightAnchors = new[] { (RectTransform)forest.Find("ForestIsland") }
                .Concat(c.Nodes.Select(n => (RectTransform)n.Button.transform)).ToArray();
            var ice = c.MapContent.Find("IceChapter") as RectTransform;
            if (ice == null)
            {
                foreach (string asset in new[] { "ice-island", "ice-platform", "ice-face" })
                {
                    string path = BossMapSceneSetup.Art + asset + ".png";
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.maxTextureSize = 2048;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.SaveAndReimport();
                }
                ice = Rect(c.MapContent, "IceChapter", Vector2.zero, new Vector2(390, 740));
                ice.anchorMin = ice.anchorMax = ice.pivot = new Vector2(.5f, 0);
                Picture(ice, "IceIsland", "ice-island", new Vector2(0, 240), new Vector2(325, 340));
                Picture(ice, "IcePlatform", "ice-platform", new Vector2(55, 494), new Vector2(174, 174));
                var disc = Picture(ice, "IceDisc", "node-locked", new Vector2(55, 531), new Vector2(94, 98));
                disc.color = new Color(.7f, .9f, 1);
                Picture(ice, "IceFace", "ice-face", new Vector2(55, 534), new Vector2(71, 71));
                var label = Rect(ice, "ComingSoon", new Vector2(0, 657), new Vector2(340, 68)).gameObject.AddComponent<TextMeshProUGUI>();
                label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.BoldAsset);
                label.fontSharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/UI/Sprites/ModeSelection/ModeOutline.mat");
                label.text = "ICE ISLAND\n<size=18>COMING SOON</size>";
                label.fontSize = 27; label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
            }
            var iceBiome = ice.GetComponent<BossMapBiome>() ?? ice.gameObject.AddComponent<BossMapBiome>();
            if (ice.Find("IceBoss1") == null) ice.sizeDelta = new Vector2(390, 740); // BossMapRewardsSetup sizes the full path
            iceBiome.Background = new Color(.34f, .61f, .78f);
            iceBiome.Mist = new Color(.74f, .9f, .98f);
            iceBiome.Light = new Color(.8f, .97f, 1);
            iceBiome.LightAnchors = ice.Cast<Transform>()
                .Where(t => t.name == "IceIsland" || t.name == "IceFace" || t.name.StartsWith("IceBoss"))
                .Cast<RectTransform>().ToArray();
            var oldBackground = c.Background.GetComponent<Image>();
            if (oldBackground != null) oldBackground.enabled = false;
            foreach (Transform child in c.Background.transform)
                if (child.name == "Skull") child.gameObject.SetActive(false);
            var backdropRect = c.Background.transform.Find("ProceduralAtmosphere") as RectTransform;
            if (backdropRect == null) backdropRect = Rect(c.Background.transform, "ProceduralAtmosphere", Vector2.zero, Vector2.zero);
            backdropRect.anchorMin = Vector2.zero; backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = backdropRect.offsetMax = Vector2.zero;
            var backdrop = backdropRect.GetComponent<BossMapBackdrop>() ?? backdropRect.gameObject.AddComponent<BossMapBackdrop>();
            backdrop.Owner = c; backdrop.raycastTarget = false;
            backdrop.Biomes = c.MapContent.Cast<Transform>().Select(t => t.GetComponent<BossMapBiome>()).Where(b => b != null).ToArray();
            EditorUtility.SetDirty(forestBiome); EditorUtility.SetDirty(iceBiome); EditorUtility.SetDirty(backdrop);
        }

        public static void InstallAndValidate()
        {
            Run();
            BossMapValidation.Run();
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.gameObject.layer = 5;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }

        private static Image Picture(Transform parent, string name, string asset, Vector2 position, Vector2 size)
        {
            var image = Rect(parent, name, position, size).gameObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(BossMapSceneSetup.Art + asset + ".png");
            image.preserveAspect = true; image.raycastTarget = false;
            return image;
        }
    }
}
