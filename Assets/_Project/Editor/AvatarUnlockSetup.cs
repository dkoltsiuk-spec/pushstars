using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TextCore;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class AvatarUnlockSetup
    {
        [MenuItem("Tools/Push Stars/UI/Install Avatar Cards and Prices")]
        public static void Run()
            => Install(false);

        public static void ResumeInterruptedInstall() => Install(true);

        private static void Install(bool resume)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/_Project/Scenes/Main.unity" || (scene.isDirty && !resume))
                throw new InvalidOperationException("Open the saved Main scene outside Play Mode first.");
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(scene.path, "Library/AvatarCollectionBackup/Main-before-unlocks-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            foreach (string name in new[] { "RobotHead", "GladiatorHead" })
            {
                string path = "Assets/_Project/Resources/AvatarCollection/" + name + ".png";
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
                importer.maxTextureSize = 256; importer.SaveAndReimport();
            }
            const string catalogPath = "Assets/_Project/Resources/AvatarCatalog.asset";
            if (AssetDatabase.LoadAssetAtPath<AvatarCatalog>(catalogPath) == null)
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<AvatarCatalog>(), catalogPath);
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            var currencySprites = CurrencySprites();
            for (int i = 0; i < screen.Cards.Length; i++)
            {
                var card = (RectTransform)screen.Cards[i].transform;
                var view = card.GetComponent<AvatarUnlockView>() ?? card.gameObject.AddComponent<AvatarUnlockView>();
                BuildBar(view, card, 5, 140, 155, 24, 54, screen.Actions[i]);
                screen.Actions[i].spriteAsset = currencySprites;
                view.Refresh(AvatarCatalog.At(i));
                screen.Actions[i].fontSize = 15;
                screen.Actions[i].enableAutoSizing = true;
                screen.Actions[i].fontSizeMin = 11;
                screen.Actions[i].fontSizeMax = 15;
                screen.Actions[i].transform.SetAsLastSibling();
                screen.InfoButtons[i].transform.SetAsLastSibling();
            }
            var page = screen.PreviewPage;
            var pageRect = (RectTransform)page.transform;
            page.UnlockView = page.GetComponent<AvatarUnlockView>() ?? page.gameObject.AddComponent<AvatarUnlockView>();
            BuildBar(page.UnlockView, pageRect, 45, 560, 300, 32, 64, screen.Actions[0]);
            page.UnlockView.ProgressRoot.SetActive(false);
            page.Status.fontSize = 11;
            page.Status.spriteAsset = currencySprites;
            page.ActionText.spriteAsset = currencySprites;
            page.Status.enableWordWrapping = true;
            page.Status.rectTransform.sizeDelta = new Vector2(340, 36);
            ((RectTransform)page.Action.transform).sizeDelta = new Vector2(260, 54);
            page.Action.image.preserveAspect = false;
            page.ActionText.enableAutoSizing = true;
            page.ActionText.fontSizeMin = 14; page.ActionText.fontSizeMax = 19;
            page.ActionText.textWrappingMode = TextWrappingModes.NoWrap;
            UiBuilder.Stretch(page.ActionText.rectTransform, 8, 4, 8, 4);
            var balanceTransform = screen.Art.Find("AvatarAuraBalance");
            var balance = balanceTransform != null ? balanceTransform.GetComponent<TextMeshProUGUI>() :
                UiBuilder.Text(screen.Art, "AvatarAuraBalance", new Color32(232, 214, 255, 255), "0 AURA", 12, FontStyles.Bold);
            Place(balance.rectTransform, 235, 78, 135, 21);
            balance.alignment = TextAlignmentOptions.Right;
            balance.spriteAsset = currencySprites;
            screen.Balance = balance;
            foreach (var pill in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Where(t => t.name == "AuraPill"))
            {
                var label = pill.GetComponentInChildren<TextMeshProUGUI>(true);
                if (label == null) continue;
                var wallet = pill.GetComponent<AvatarAuraBalance>() ?? pill.gameObject.AddComponent<AvatarAuraBalance>();
                wallet.Label = label;
                label.text = CaseRewards.AuraBalance.ToString("N0");
            }
            var settings = new SerializedObject(screen.Roster);
            settings.FindProperty("_robotPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Robot/Robot.prefab");
            settings.ApplyModifiedPropertiesWithoutUndo();
            screen.RefreshSelection();
            EditorUtility.SetDirty(screen); EditorUtility.SetDirty(page); EditorUtility.SetDirty(screen.Roster);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            InstallCaseReward();
            Debug.Log("[AvatarUnlock] Installed hero cards, slanted progress and Aura pricing.");
        }

        private static TMP_SpriteAsset CurrencySprites()
        {
            const string path = "Assets/_Project/Resources/AvatarCollection/CurrencySprites.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(path);
            var sprite = Resources.Load<PushStarsTheme>("PushStarsTheme").IconAura;
            bool create = asset == null;
            if (create) asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = "CurrencySprites";
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_Version").stringValue = "1.1.0";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            asset.spriteSheet = sprite.texture;
            var rect = sprite.textureRect;
            var glyph = new TMP_SpriteGlyph(0,
                new GlyphMetrics(rect.width, rect.height, 0, rect.height * .88f, rect.width),
                new GlyphRect((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height), 1, 0, sprite);
            asset.spriteGlyphTable.Clear(); asset.spriteCharacterTable.Clear();
            asset.spriteGlyphTable.Add(glyph);
            asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, asset, glyph) { name = "aura", scale = 1.6f });
            if (create) AssetDatabase.CreateAsset(asset, path);
            var material = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().FirstOrDefault();
            if (material == null)
            {
                material = new Material(Shader.Find("TextMeshPro/Sprite")) { name = "CurrencySprites Material" };
                AssetDatabase.AddObjectToAsset(material, asset);
            }
            asset.material = material;
            asset.material.mainTexture = sprite.texture;
            asset.UpdateLookupTables();
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void InstallCaseReward()
        {
            const string path = "Assets/_Project/Scenes/CaseReward.unity";
            var existing = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            bool alreadyLoaded = existing.isLoaded;
            if (existing.isLoaded && existing.isDirty) throw new InvalidOperationException("Save CaseReward before installing its hero-card reward row.");
            var scene = existing.isLoaded ? existing : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var reward = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PushStars.Fight.RewardScreen>(true)).First();
                foreach (var label in reward.GetComponentsInChildren<TextMeshProUGUI>(true))
                    if (label.text == "GEMS") label.text = "REWARDS";
                var serialized = new SerializedObject(reward);
                var prize = serialized.FindProperty("_prizeUi");
                var note = (TextMeshProUGUI)prize.FindPropertyRelative("Note").objectReferenceValue;
                var parent = (RectTransform)note.transform.parent;
                var old = parent.Find("HeroCardIcon");
                var icon = old != null ? old.GetComponent<Image>() : UiBuilder.Image(parent, "HeroCardIcon", Color.white);
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
                icon.rectTransform.pivot = new Vector2(0, .5f);
                icon.rectTransform.anchoredPosition = Vector2.zero;
                icon.rectTransform.sizeDelta = new Vector2(50, 50);
                icon.preserveAspect = true; icon.gameObject.SetActive(false);
                UiBuilder.Stretch(note.rectTransform, 54, 0, 0, 0);
                note.textWrappingMode = TextWrappingModes.Normal;
                prize.FindPropertyRelative("AvatarCardIcon").objectReferenceValue = icon;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (!alreadyLoaded) EditorSceneManager.CloseScene(scene, true); }
        }

        private static void BuildBar(AvatarUnlockView view, RectTransform parent, float x, float y, float width, float height, float headSize, TextMeshProUGUI reference)
        {
            if (view.ProgressRoot != null) Object.DestroyImmediate(view.ProgressRoot);
            var root = UiBuilder.Rect(parent, "HeroCards");
            Place(root, x, y, width, height);
            view.ProgressRoot = root.gameObject;
            var barRect = UiBuilder.Rect(root, "SlantedProgress");
            Place(barRect, 0, 0, width, height);
            view.Bar = barRect.gameObject.AddComponent<AvatarShardBar>();
            view.Bar.raycastTarget = false;
            var head = UiBuilder.Image(root, "HeroHead", Color.white);
            head.rectTransform.anchorMin = head.rectTransform.anchorMax = new Vector2(0, 1);
            head.rectTransform.pivot = new Vector2(.5f, .5f);
            head.rectTransform.anchoredPosition = new Vector2(7, -height * .05f);
            head.rectTransform.sizeDelta = Vector2.one * headSize;
            head.rectTransform.localRotation = Quaternion.Euler(0, 0, 12);
            head.preserveAspect = true; view.Head = head;
            var count = UiBuilder.Text(root, "CardCount", Color.white, "0 / 60", height > 24 ? 16 : 12, FontStyles.Bold | FontStyles.Italic);
            count.font = reference.font;
            count.fontSharedMaterial = reference.fontSharedMaterial;
            Place(count.rectTransform, headSize * .45f, 0, width - headSize * .45f - 4, height);
            count.enableWordWrapping = false;
            view.Count = count;
        }

        private static void Place(RectTransform rect, float x, float y, float w, float h)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
        }
    }
}
