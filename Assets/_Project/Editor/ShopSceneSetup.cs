using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class ShopSceneSetup
    {
        private const string ArtPath = "Assets/_Project/UI/Sprites/Shop/";
        private static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + name + ".png");
        private static Sprite Avatar(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/AvatarCollection/" + name + ".png");
        private static Sprite Profile(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/ProfileSettings/" + name + ".png");

        [MenuItem("Tools/Push Stars/UI/Install Shop")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Main.unity" || scene.isDirty)
                throw new InvalidOperationException("Open and save Main before installing Shop.");
            var collection = Object.FindFirstObjectByType<AvatarCollectionScreen>();
            if (collection == null) throw new InvalidOperationException("Install the avatar collection first.");
            if (collection.GetComponent<ShopScreen>() != null) { Debug.Log("[Shop] Already installed."); return; }
            Directory.CreateDirectory("Library/ShopBackup");
            File.Copy(scene.path, "Library/ShopBackup/Main-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            ImportArt();
            var screen = Undo.AddComponent<ShopScreen>(collection.gameObject);
            screen.Collection = collection;
            screen.Entry = collection.GetComponentsInChildren<Button>(true).First(b => b.name == "ShopTile");
            Tactile(screen.Entry.gameObject);
            var root = UiBuilder.Image((RectTransform)collection.transform, "Shop", new Color32(25, 74, 224, 255));
            root.raycastTarget = true; UiBuilder.Stretch(root.rectTransform);
            screen.Overlay = root.gameObject;
            var pattern = collection.Overlay.transform.Find("HomeLightningPattern");
            if (pattern != null) Object.Instantiate(pattern.gameObject, root.transform, false).SetActive(true);
            var safe = UiBuilder.Rect(root.rectTransform, "SafeArea");
            UiBuilder.Stretch(safe); safe.gameObject.AddComponent<SafeAreaFitter>();
            var art = UiBuilder.Rect(safe, "Art");
            art.anchorMin = art.anchorMax = art.pivot = new Vector2(.5f, 1);
            art.sizeDelta = new Vector2(390, 780); screen.Art = art;
            screen.Back = Button(art, "Back", Profile("Group 549"), "", 20, 30, 57, 44);
            screen.Home = Button(art, "Home", Profile("Group 550"), "", 313, 30, 57, 44);
            Text(art, "Title", "SHOP", 90, 31, 210, 44, 25);
            screen.Balance = Text(art, "GemBalance", "0", 263, 85, 85, 23, 13);
            screen.Balance.alignment = TextAlignmentOptions.Right;
            Pic(art, "GemIcon", AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/gem.png"), 352, 86, 18, 20).preserveAspect = true;

            var viewport = UiBuilder.Rect(art, "Viewport");
            Place(viewport, 0, 128, 390, 560); screen.Viewport = viewport;
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.viewport = viewport; screen.Scroll = scroll;
            var content = UiBuilder.Rect(viewport, "Content");
            Place(content, 0, 0, 390, 505); screen.Content = scroll.content = content;
            Section(content, "SPECIAL OFFERS", 8, true);

            var offer = Object.Instantiate(collection.Cards[0], content, false);
            offer.name = "SonicOffer"; offer.onClick = new Button.ButtonClickedEvent();
            offer.gameObject.SetActive(true); Place((RectTransform)offer.transform, 20, 52, 166, 235);
            screen.Offers = new[] { offer };
            var info = offer.transform.Find("Info").GetComponent<Button>();
            info.onClick = new Button.ButtonClickedEvent(); screen.Info = new[] { info };
            var unlock = offer.GetComponent<AvatarUnlockView>();
            if (unlock != null) { Object.DestroyImmediate(unlock.ProgressRoot); Object.DestroyImmediate(unlock); }
            var lockIcon = offer.transform.Find("LockIcon");
            if (lockIcon != null) Object.DestroyImmediate(lockIcon.gameObject);
            foreach (var surface in offer.GetComponentsInChildren<AvatarCardSurface>(true))
            { surface.SetLocked(false); surface.SetSelected(false); }
            var preview = offer.GetComponentInChildren<AvatarCardPreview>(true);
            preview.Slot = 20; preview.Tint = Color.white;
            preview.Image.color = Color.white;
            Place(preview.Image.rectTransform, 6, 31, 154, 161);
            var action = offer.transform.Find("Action").GetComponent<TextMeshProUGUI>();
            Place(action.rectTransform, 2, 190, 162, 39); action.text = "$5.99";
            screen.OfferActions = new[] { action };

            Section(content, "COINS AND VALUES", 325, false);
            screen.Packs = new Button[3]; screen.PackSprites = new Sprite[3];
            for (int i = 0; i < 3; i++)
            {
                float x = 20 + i * 122;
                var pack = Button(content, "Gems" + ShopScreen.GemAmounts[i], null, "", x, 373, 106, 120);
                pack.image.color = Color.clear; screen.Packs[i] = pack;
                GemSurface((RectTransform)pack.transform);
                var sprite = Art("Gems" + ShopScreen.GemAmounts[i] + "Clean"); screen.PackSprites[i] = sprite;
                var gems = Pic((RectTransform)pack.transform, "Gems", sprite, 8, 28, 90, 60);
                gems.preserveAspect = true; gems.material = GemMaterial();
                var count = Text((RectTransform)pack.transform, "Amount", ShopScreen.GemAmounts[i].ToString(), 7, 3, 88, 29, 21);
                count.alignment = TextAlignmentOptions.Right;
                count.color = new Color32(226, 255, 168, 255);
                Text((RectTransform)pack.transform, "Price", ShopScreen.GemPrices[i], 2, 92, 102, 23, 16);
            }
            screen.Okay = Button(art, "Okay", Art("Okay"), "OK", 140, 714, 110, 54);

            var dialog = UiBuilder.Image(art, "PackDialog", new Color(0, .02f, .15f, .85f));
            UiBuilder.Stretch(dialog.rectTransform); dialog.raycastTarget = true; screen.PackDialog = dialog.gameObject;
            var panel = UiBuilder.Image(dialog.rectTransform, "Panel", new Color32(32, 63, 164, 255));
            panel.rectTransform.anchorMin = panel.rectTransform.anchorMax = panel.rectTransform.pivot = new Vector2(.5f, .5f);
            panel.rectTransform.sizeDelta = new Vector2(332, 365); Tactile(panel.gameObject);
            screen.PackTitle = Text(panel.rectTransform, "Title", "30 GEMS", 15, 20, 302, 36, 23);
            screen.PackImage = Pic(panel.rectTransform, "Gems", screen.PackSprites[0], 90, 69, 152, 110);
            screen.PackImage.preserveAspect = true;
            screen.PackImage.material = GemMaterial();
            screen.PackPrice = Text(panel.rectTransform, "Price", "$1.99", 15, 185, 302, 29, 21);
            Text(panel.rectTransform, "Availability", "COMING SOON", 15, 230, 302, 28, 17);
            var note = Text(panel.rectTransform, "Description", "Purchases are not available yet.", 15, 261, 302, 24, 12);
            note.fontSharedMaterial = note.font.material;
            screen.DialogClose = Button(panel.rectTransform, "Close", Art("Okay"), "OK", 111, 299, 110, 49);
            dialog.gameObject.SetActive(false); root.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Shop] Installed one Sonic offer, three gem packs, shared avatar preview and lobby entry.");
        }

        private static void ImportArt()
        {
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(ArtPath, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        private static void GemSurface(RectTransform parent)
        {
            var rect = UiBuilder.Rect(parent, "CardSurface"); UiBuilder.Stretch(rect);
            var surface = rect.gameObject.AddComponent<ShopGemCardSurface>();
            surface.raycastTarget = false; rect.SetAsFirstSibling();
        }

        private static Material GemMaterial()
        {
            const string path = ArtPath + "GemSprite.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("PushStars/UI Shop Gem Sprite"));
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        [MenuItem("Tools/Push Stars/UI/Repair Shop Cards")]
        public static void RepairCards()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            var screen = Object.FindFirstObjectByType<ShopScreen>();
            if (screen == null) throw new InvalidOperationException("Open Main with Shop installed.");
            ImportArt();
            foreach (var pack in screen.Packs)
            {
                foreach (var name in new[] { "GlowCard", "Footer" })
                {
                    var child = pack.transform.Find(name);
                    if (child != null) Undo.DestroyObjectImmediate(child.gameObject);
                }
                if (pack.transform.Find("CardSurface") == null) GemSurface((RectTransform)pack.transform);
            }
            for (int i = 0; i < screen.Packs.Length; i++)
            {
                var gems = screen.Packs[i].transform.Find("Gems").GetComponent<Image>();
                var sprite = Art("Gems" + ShopScreen.GemAmounts[i] + "Clean");
                if (sprite == null) continue;
                gems.sprite = screen.PackSprites[i] = sprite;
                gems.material = GemMaterial(); Place(gems.rectTransform, 8, 28, 90, 60);
            }
            screen.PackImage.material = GemMaterial();
            screen.PackImage.sprite = screen.PackSprites[0];
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(screen.gameObject.scene);
            EditorSceneManager.SaveScene(screen.gameObject.scene);
        }

        private static void Section(RectTransform parent, string title, float y, bool yellow)
        {
            var rect = UiBuilder.Rect(parent, title); Place(rect, 23, y, 344, 31);
            var bar = rect.gameObject.AddComponent<ShopSectionGraphic>(); bar.raycastTarget = false;
            var text = Text(bar.rectTransform, "Label", title, 12, 1, 326, 28, 17);
            text.alignment = TextAlignmentOptions.Left;
            if (yellow) text.color = new Color32(255, 224, 0, 255);
        }
        private static void Tactile(GameObject go) { if (go.GetComponent<UiTactile>() == null) go.AddComponent<UiTactile>(); }
        private static void Place(RectTransform rect, float x, float y, float w, float h)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }
        private static Image Pic(RectTransform parent, string name, Sprite sprite, float x, float y, float w, float h)
        { var image = UiBuilder.Image(parent, name, Color.white); image.sprite = sprite; Place(image.rectTransform, x, y, w, h); return image; }
        private static TextMeshProUGUI Text(RectTransform parent, string name, string value, float x, float y, float w, float h, float size)
        { var text = UiBuilder.Text(parent, name, Color.white, value, size, FontStyles.Bold); text.textWrappingMode = TextWrappingModes.NoWrap; Place(text.rectTransform, x, y, w, h); return text; }
        private static Button Button(RectTransform parent, string name, Sprite sprite, string value, float x, float y, float w, float h)
        {
            var image = Pic(parent, name, sprite, x, y, w, h); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; Tactile(image.gameObject);
            if (value.Length > 0) Text(image.rectTransform, "Label", value, 3, 0, w - 6, h - 5, 20);
            return button;
        }
    }
}
