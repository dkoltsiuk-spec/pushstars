using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class AvatarCollectionSceneSetup
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        private static Sprite Art(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Resources/AvatarCollection/" + name + ".png");
        private static Sprite Profile(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/ProfileSettings/" + name + ".png");

        public static void RepairLightning()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).First();
            foreach (var bolt in screen.Overlay.GetComponentsInChildren<AvatarLightningGraphic>(true))
            {
                if (bolt.GetComponent<CanvasRenderer>() == null) bolt.gameObject.AddComponent<CanvasRenderer>();
                bolt.SetAllDirty();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Tools/Push Stars/UI/Install Avatar Collection")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before installing Avatar Collection.");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var roots = scene.GetRootGameObjects();
            if (roots.SelectMany(r => r.GetComponentsInChildren<AvatarCollectionScreen>(true)).Any())
            { Debug.Log("[AvatarCollection] Already installed; preserving authored UI."); return; }
            Directory.CreateDirectory("Library/AvatarCollectionBackup");
            File.Copy(ScenePath, "Library/AvatarCollectionBackup/Main-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            var target = roots.SelectMany(r => r.GetComponentsInChildren<CharacterTurntable>(true)).First(t => t.name == "CharacterImage");
            var canvas = target.GetComponentInParent<Canvas>().rootCanvas;
            var roster = roots.SelectMany(r => r.GetComponentsInChildren<CharacterRoster>(true)).First();
            var screen = canvas.gameObject.AddComponent<AvatarCollectionScreen>();
            screen.Roster = roster;
            var overlay = UiBuilder.Image((RectTransform)canvas.transform, "AvatarCollection", new Color32(26, 71, 211, 255));
            overlay.raycastTarget = true;
            UiBuilder.Stretch(overlay.rectTransform);
            screen.Overlay = overlay.gameObject;
            for (int row = 0; row < 8; row++) for (int col = 0; col < 4; col++)
            {
                var bolt = UiBuilder.Rect(overlay.rectTransform, "Lightning").gameObject.AddComponent<AvatarLightningGraphic>();
                bolt.color = new Color(.65f, .8f, 1, .06f);
                bolt.raycastTarget = false;
                var rect = bolt.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2((col + .3f + row % 2 * .4f) / 4f, row / 7f);
                rect.sizeDelta = new Vector2(65, 95);
                rect.localRotation = Quaternion.Euler(0, 0, -12);
            }
            var safe = UiBuilder.Rect(overlay.rectTransform, "SafeArea");
            UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var art = UiBuilder.Rect(safe, "Art");
            art.anchorMin = art.anchorMax = art.pivot = new Vector2(.5f, 1);
            art.sizeDelta = new Vector2(390, 720);
            screen.Art = art;
            screen.Back = Button(art, "Back", Profile("Group 549"), "", 20, 48, 57, 44);
            screen.Home = Button(art, "Home", Profile("Group 550"), "", 313, 48, 57, 44);
            Label(art, "Title", "AVATARS", 90, 49, 210, 44, 25);
            screen.Tabs = new[] {
                Button(art, "All", Profile("Group 556"), "ALL", 20, 103, 82, 42),
                Button(art, "Opened", Profile("Group 557"), "OPENED", 114, 103, 111, 42),
                Button(art, "Premium", Profile("Group 557"), "PREMIUM", 237, 103, 133, 42) };
            screen.TabOn = Profile("Group 556"); screen.TabOff = Profile("Group 557");
            screen.Purple = Art("CardPurple"); screen.Gold = Art("CardGold");
            screen.PurpleFooter = Art("FooterPurple"); screen.GoldFooter = Art("FooterGold");
            var grid = UiBuilder.Rect(art, "Cards");
            Place(grid, 18, 172, 354, 450);
            screen.Grid = grid;
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(166, 208);
            layout.spacing = new Vector2(20, 22);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount; layout.constraintCount = 2;
            screen.Cards = new Button[4]; screen.InfoButtons = new Button[4];
            screen.Plates = new Image[4]; screen.Footers = new Image[4]; screen.Actions = new TextMeshProUGUI[4];
            string[] names = { "SONIC", "MADAM ENGRY", "FIGHTER", "ROBOT" };
            string[] portraits = { "Sonic", "MainWoman", "MainMan", "Robot" };
            for (int i = 0; i < 4; i++)
            {
                var card = Button(grid, "Card" + i, screen.Purple, "", 0, 0, 166, 208);
                screen.Cards[i] = card; screen.Plates[i] = card.image;
                var rt = (RectTransform)card.transform;
                if (Art(portraits[i]) != null)
                {
                    var portrait = Pic(rt, "Portrait", Art(portraits[i]), 6, 29, 154, 151);
                    portrait.preserveAspect = true;
                }
                else Label(rt, "Unknown", "?", 20, 40, 126, 118, 80);
                Label(rt, "Name", names[i], 10, 5, 143, 27, 14);
                screen.Footers[i] = Pic(rt, "Footer", screen.PurpleFooter, 5, 163, 156, 38);
                screen.Actions[i] = Label(rt, "Action", i == 0 || i == 3 ? "PREVIEW" : "SELECT", 7, 164, 150, 35, 14);
                screen.InfoButtons[i] = Button(rt, "Info", Art("Info"), "i", 144, -6, 33, 33);
            }
            screen.Empty = Label(art, "PremiumEmpty", "PREMIUM AVATARS\n<size=16>Coming soon</size>", 25, 230, 340, 130, 24);
            screen.Empty.gameObject.SetActive(false);
            var info = UiBuilder.Image(art, "AvatarInfo", new Color(0, 0, .1f, .9f));
            UiBuilder.Stretch(info.rectTransform);
            info.raycastTarget = true;
            screen.InfoPanel = info.gameObject;
            screen.InfoTitle = Label(info.rectTransform, "Title", "", 25, 210, 340, 50, 27);
            screen.InfoBody = Label(info.rectTransform, "Body", "", 35, 276, 320, 160, 19);
            screen.InfoClose = Button(info.rectTransform, "Close", Profile("Group 556"), "OK", 135, 462, 120, 46);
            info.gameObject.SetActive(false);
            target.gameObject.AddComponent<AvatarCollectionTap>().Screen = screen;
            target.GetComponent<RawImage>().raycastTarget = true;
            overlay.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[AvatarCollection] Installed four cards and home tap; saved Main scene.");
        }

        private static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
        }
        private static Image Pic(RectTransform parent, string name, Sprite sprite, float x, float y, float w, float h)
        {
            var image = UiBuilder.Image(parent, name, Color.white); image.sprite = sprite;
            Place(image.rectTransform, x, y, w, h); return image;
        }
        private static TextMeshProUGUI Label(RectTransform parent, string name, string value, float x, float y, float w, float h, float size)
        {
            var text = UiBuilder.Text(parent, name, Color.white, value, size, FontStyles.Bold | FontStyles.Italic);
            text.enableWordWrapping = false;
            Place(text.rectTransform, x, y, w, h); return text;
        }
        private static Button Button(RectTransform parent, string name, Sprite sprite, string text, float x, float y, float w, float h)
        {
            var image = Pic(parent, name, sprite, x, y, w, h); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            if (!string.IsNullOrEmpty(text)) Label(image.rectTransform, "Label", text, 3, 1, w - 6, h - 2, 16);
            return button;
        }
    }
}
