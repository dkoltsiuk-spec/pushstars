using System;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class FriendDuelSceneSetup
    {
        private static readonly Color Gold = new Color32(255, 200, 24, 255);
        private static readonly Color Surface = new Color32(33, 38, 77, 255);
        private static TMP_FontAsset Font => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.BoldAsset);
        private static Sprite Pill => AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFactory.SpritesDir + "/pill_16.png");

        [MenuItem("Tools/Push Stars/Main/Add Friend Duel")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != AuthoredScenes.MainPath) throw new InvalidOperationException("Open Main first.");
            Install(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        public static void Install(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var existing = roots.SelectMany(r => r.GetComponentsInChildren<FriendDuelController>(true)).FirstOrDefault();
            if (existing != null) { Polish(existing); InstallPresentation(existing, scene); return; }
            var battle = roots.SelectMany(r => r.GetComponentsInChildren<Button>(true)).Single(b => b.name == "BattleButton");
            var panel = (RectTransform)battle.transform.parent.parent;
            var oldSlots = panel.Cast<Transform>().Where(t => t.name == "PlusSlot").ToArray();
            Sprite plus = oldSlots.Select(t => t.GetComponent<Image>()?.sprite).FirstOrDefault(s => s != null);
            foreach (var slot in oldSlots) Undo.DestroyObjectImmediate(slot.gameObject);
            var controller = Rect(panel, "FriendDuelController", 0, 0, 0, 0).gameObject.AddComponent<FriendDuelController>();
            Undo.RegisterCreatedObjectUndo(controller.gameObject, "Add friend duel controller");
            controller.BattleButton = battle;
            controller.ModeButton = battle.transform.parent.Find("PvpButton").GetComponent<Button>();
            controller.ExerciseButton = battle.transform.parent.Find("PushupButton").GetComponent<Button>();
            controller.ModeSelector = roots.SelectMany(r => r.GetComponentsInChildren<ModeSelectionController>(true)).Single();

            var entry = Rect(panel, "FriendSlot", 0, 0, 102, 100);
            entry.anchorMin = entry.anchorMax = entry.pivot = new Vector2(.5f, .5f);
            entry.anchoredPosition = new Vector2(-118, 110);
            Undo.RegisterCreatedObjectUndo(entry.gameObject, "Add single friend slot");
            controller.Slot = Button(entry, "HitArea", "", 0, 0, 102, 100, Color.clear);
            controller.PlusIcon = Picture(controller.Slot.transform, "PlusIcon", plus, Color.white, 29, 4, 44, 44);
            controller.PlusIcon.preserveAspect = true;
            if (plus == null) Text(controller.PlusIcon.transform, "Plus", "+", 32, 0, 0, 44, 44, TextAlignmentOptions.Center);
            var badge = Picture(controller.Slot.transform, "FriendBadge", Pill, new Color32(93, 78, 180, 255), 26, 0, 50, 50);
            badge.type = Image.Type.Sliced;
            Text(badge.transform, "Initial", "A", 28, 0, 0, 50, 50, TextAlignmentOptions.Center);
            controller.FriendBadge = badge.gameObject; badge.gameObject.SetActive(false);
            controller.SlotName = Text(controller.Slot.transform, "FriendName", "С другом", 13, 0, 56, 102, 23, TextAlignmentOptions.Center);
            controller.SlotName.richText = false; controller.SlotName.overflowMode = TextOverflowModes.Ellipsis;
            controller.SlotStatus = Text(controller.Slot.transform, "FriendStatus", "", 10, 0, 80, 102, 18, TextAlignmentOptions.Center);
            controller.SlotStatus.color = new Color32(156, 239, 164, 255);
            controller.BattleCaption = Text(battle.transform, "FriendBattleCaption", "", 11, 0, 85, 163, 21, TextAlignmentOptions.Center);
            controller.BattleCaption.richText = false; controller.BattleCaption.overflowMode = TextOverflowModes.Ellipsis;

            var canvas = battle.GetComponentInParent<Canvas>().rootCanvas;
            var mirror = canvas.GetComponentInChildren<DeviceSimulatorMirrorFix>(true);
            var overlay = Rect(mirror != null ? mirror.transform : canvas.transform, "FriendDuelOverlay", 0, 0, 0, 0);
            Stretch(overlay); controller.Overlay = overlay.gameObject;
            Undo.RegisterCreatedObjectUndo(overlay.gameObject, "Add friend duel windows");
            var dim = Picture(overlay, "Dimmer", null, new Color(0, 0, 0, .78f), 0, 0, 0, 0);
            Stretch(dim.rectTransform); controller.Backdrop = Clickable(dim);
            var sheet = Picture(overlay, "FriendSheet", Pill, new Color32(22, 25, 53, 255), 0, 0, 390, 680);
            sheet.type = Image.Type.Sliced; sheet.raycastTarget = true;
            var rt = sheet.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, 0);
            rt.anchoredPosition = Vector2.zero; controller.Sheet = rt;
            Picture(rt, "Handle", Pill, new Color(1, 1, 1, .2f), 170, 12, 50, 4).type = Image.Type.Sliced;
            controller.Title = Text(rt, "Title", "ДУЭЛЬ С ДРУГОМ", 24, 24, 41, 296, 34);
            controller.Title.enableAutoSizing = true; controller.Title.fontSizeMin = 20;
            controller.Close = Button(rt, "Close", "×", 335, 32, 42, 46, Color.clear);
            controller.Subtitle = Text(rt, "Subtitle", "Брось вызов знакомому. Один на один.", 15, 24, 86, 342, 46);
            controller.Subtitle.color = new Color32(198, 202, 224, 255);
            controller.Body = Text(rt, "Body", "", 17, 24, 144, 342, 225);
            controller.Body.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.RegularAsset);
            controller.Body.textWrappingMode = TextWrappingModes.Normal;
            controller.Body.alignment = TextAlignmentOptions.TopLeft;
            controller.Body.lineSpacing = 2;
            controller.Message = Text(rt, "Message", "", 13, 24, 376, 342, 37);
            controller.Message.textWrappingMode = TextWrappingModes.Normal;
            controller.Message.color = Gold;
            var code = Picture(rt, "CodeCard", Pill, Surface, 24, 145, 342, 80);
            code.type = Image.Type.Sliced; controller.CodeGroup = code.gameObject;
            controller.CodeLabel = Text(code.transform, "Code", "", 39, 14, 6, 314, 66, TextAlignmentOptions.Center);
            controller.CodeLabel.color = Gold; controller.CodeLabel.characterSpacing = 7;
            var inputCard = Picture(rt, "CodeInputCard", Pill, Surface, 24, 145, 342, 84);
            inputCard.type = Image.Type.Sliced; inputCard.raycastTarget = true;
            controller.InputGroup = inputCard.gameObject;
            var input = inputCard.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect(inputCard.transform, "TextArea", 14, 9, 314, 66);
            viewport.gameObject.AddComponent<RectMask2D>();
            var inputText = Text(viewport, "InputText", "", 34, 0, 0, 314, 66, TextAlignmentOptions.Center);
            inputText.richText = false;
            var placeholder = Text(viewport, "Placeholder", "000 000", 34, 0, 0, 314, 66, TextAlignmentOptions.Center);
            placeholder.color = new Color(1, 1, 1, .3f);
            input.textViewport = viewport; input.textComponent = inputText; input.placeholder = placeholder;
            input.targetGraphic = inputCard; input.characterLimit = 12; input.keyboardType = TouchScreenKeyboardType.NumberPad;
            input.lineType = TMP_InputField.LineType.SingleLine; controller.CodeInput = input;
            var players = Rect(rt, "Players", 24, 144, 342, 166); controller.PlayersGroup = players.gameObject;
            Player(players, "Me", 0, out controller.MeLabel, out controller.MeStatus);
            Player(players, "Friend", 86, out controller.FriendLabel, out controller.FriendStatus);

            controller.Primary = Button(rt, "Primary", "ПРИГЛАСИТЬ ДРУГА", 24, 418, 342, 52, Gold);
            controller.PrimaryLabel = controller.Primary.GetComponentInChildren<TextMeshProUGUI>();
            controller.PrimaryLabel.color = new Color32(27, 25, 40, 255);
            controller.Secondary = Button(rt, "Secondary", "ВВЕСТИ КОД", 24, 481, 342, 46, Surface);
            controller.SecondaryLabel = controller.Secondary.GetComponentInChildren<TextMeshProUGUI>();
            controller.Tertiary = Button(rt, "Tertiary", "ВЫЙТИ ИЗ КОМНАТЫ", 24, 538, 342, 32, Color.clear);
            controller.TertiaryLabel = controller.Tertiary.GetComponentInChildren<TextMeshProUGUI>();
            controller.TertiaryLabel.color = new Color32(255, 148, 151, 255); controller.TertiaryLabel.fontSize = 13;
            controller.Help = Button(rt, "Help", "Как это работает?", 24, 575, 342, 32, Color.clear);
            controller.Help.GetComponentInChildren<TextMeshProUGUI>().fontSize = 13;
            controller.DemoAction = Button(rt, "DemoAdvance", "Демо: друг вошёл", 24, 615, 166, 28, Surface);
            controller.DemoLabel = controller.DemoAction.GetComponentInChildren<TextMeshProUGUI>(); controller.DemoLabel.fontSize = 10;
            controller.DemoConnection = Button(rt, "DemoConnection", "Демо: обрыв связи", 200, 615, 166, 28, Surface);
            controller.DemoConnection.GetComponentInChildren<TextMeshProUGUI>().fontSize = 10;
            var preview = Text(rt, "PreviewBadge", "ПРЕДПРОСМОТР · БЕЗ СЕТИ", 9, 24, 651, 342, 16, TextAlignmentOptions.Center);
            preview.color = Gold; controller.PreviewBadge = preview.gameObject;
            // This label is enabled only by Editor preview; device builds never display fake room state.
            preview.gameObject.SetActive(false);
            code.gameObject.SetActive(false); inputCard.gameObject.SetActive(false); players.gameObject.SetActive(false);
            controller.Tertiary.gameObject.SetActive(false);
            controller.DemoAction.gameObject.SetActive(false); controller.DemoConnection.gameObject.SetActive(false);
            overlay.gameObject.SetActive(false);
            var search = roots.SelectMany(r => r.GetComponentsInChildren<SearchOpponentController>(true)).Single();
            var so = new SerializedObject(search); so.FindProperty("_friendDuel").objectReferenceValue = controller;
            so.ApplyModifiedPropertiesWithoutUndo();
            Polish(controller);
            InstallPresentation(controller, scene);
            EditorUtility.SetDirty(controller);
        }

        private static void InstallPresentation(FriendDuelController c, Scene scene)
        {
            c.ExerciseSettings = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BattleSettingsController>(true)).Single();
            c.ModeHint = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Toast>(true)).First();
            c.InactiveModeMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/UI/Materials/UiDrained.mat");
            var p = c.Presentation;
            if (p == null)
            {
                p = c.gameObject.AddComponent<FriendDuelPresentation>(); c.Presentation = p;
                var stage = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterStage>(true)).Single();
                var so = new SerializedObject(stage);
                p.PlayerImage = (RawImage)so.FindProperty("_targetImage").objectReferenceValue;
                p.SourceCamera = (Camera)so.FindProperty("_stageCamera").objectReferenceValue;
                p.RimLight = (Light)so.FindProperty("_rimLight").objectReferenceValue;
                p.MalePrefab = MainCharacterSetup.LoadCharacterPrefab(CharacterGender.Male);
                p.FemalePrefab = MainCharacterSetup.LoadCharacterPrefab(CharacterGender.Female);
                var friendStage = new GameObject("FriendCharacterStage3D");
                SceneManager.MoveGameObjectToScene(friendStage, scene);
                friendStage.transform.position = new Vector3(100, 0, 0);
                p.FriendRoot = new GameObject("FriendAvatarRoot").transform;
                p.FriendRoot.SetParent(friendStage.transform, false);
                p.FriendRoot.localPosition = stage.AvatarRoot.position;
                p.FriendRoot.localRotation = stage.AvatarRoot.rotation;
                p.FriendRoot.gameObject.layer = stage.AvatarRoot.gameObject.layer;
                p.FriendCamera = new GameObject("FriendStageCamera").AddComponent<Camera>();
                p.FriendCamera.transform.SetParent(friendStage.transform, false);
                p.FriendCamera.transform.localPosition = p.SourceCamera.transform.position;
                p.FriendCamera.transform.localRotation = p.SourceCamera.transform.rotation;
                p.FriendCamera.enabled = false;
                var player = p.PlayerImage.rectTransform;
                var portrait = Rect(player.parent, "FriendCharacterImage", 0, 0, 264, 470);
                portrait.anchorMin = player.anchorMin; portrait.anchorMax = player.anchorMax; portrait.pivot = player.pivot;
                portrait.sizeDelta = player.sizeDelta; portrait.anchoredPosition = player.anchoredPosition;
                portrait.localScale = player.localScale; portrait.SetSiblingIndex(player.GetSiblingIndex());
                p.FriendImage = portrait.gameObject.AddComponent<RawImage>();
                p.FriendImage.color = Color.clear; p.FriendImage.raycastTarget = false;
                portrait.gameObject.SetActive(false);
                Undo.RegisterCreatedObjectUndo(friendStage, "Add independent friend character stage");
                Undo.RegisterCreatedObjectUndo(portrait.gameObject, "Add friend character portrait");
            }
            p.FriendImage.raycastTarget = false;
            p.PlayerOffset = 0f; p.PairScale = 1f; p.FriendOffset = -108f; p.FriendScale = .82f; p.FriendDepth = 30f;
            p.PlayerShadowOffset = new Vector2(-10f, 5f); p.PlayerShadowSize = new Vector2(168f, 40f);
            var sourceStage = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterStage>(true)).Single();
            p.PlayerRoot = sourceStage.AvatarRoot;
            p.PlayerShadow = c.transform.parent.Find("GroundShadow").GetComponent<Image>();
            var shadowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFactory.SpritesDir + "/circle_128.png");
            if (shadowSprite != null) p.PlayerShadow.sprite = shadowSprite;
            p.PlayerShadow.rectTransform.sizeDelta = new Vector2(168, 40);
            p.PlayerShadow.color = new Color(0, 0, 0, .5f);
            if (p.FriendShadow == null)
            {
                p.FriendShadow = Object.Instantiate(p.PlayerShadow, p.PlayerShadow.transform.parent);
                p.FriendShadow.name = "FriendGroundShadow";
                p.FriendShadow.transform.SetSiblingIndex(p.PlayerShadow.transform.GetSiblingIndex() + 1);
                Undo.RegisterCreatedObjectUndo(p.FriendShadow.gameObject, "Add shadow under friend");
            }
            p.FriendShadow.raycastTarget = false; p.FriendShadow.gameObject.SetActive(false);
            p.FriendShadow.sprite = p.PlayerShadow.sprite;
            EditorUtility.SetDirty(p.PlayerShadow); EditorUtility.SetDirty(p.PlayerShadow.rectTransform);
            var oldPortraitButton = p.FriendImage.GetComponent<Button>();
            if (oldPortraitButton != null) Undo.DestroyObjectImmediate(oldPortraitButton);
            var hit = p.FriendImage.transform.Find("FriendHitTarget");
            if (hit == null)
            {
                var image = Picture(p.FriendImage.transform, "FriendHitTarget", null, Color.clear, 0, 0, 142, 390);
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = new Vector2(.5f, .5f);
                image.rectTransform.anchoredPosition = Vector2.zero;
                c.FriendAvatarButton = Clickable(image);
                c.FriendAvatarButton.transition = Selectable.Transition.None;
            }
            else c.FriendAvatarButton = hit.GetComponent<Button>();
            p.BattleTitle = c.BattleButton.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t != c.BattleCaption).rectTransform;
            p.BattleCaption = c.BattleCaption.rectTransform;
            p.BattleCaption.anchorMin = p.BattleCaption.anchorMax = new Vector2(.5f, 0);
            p.BattleCaption.pivot = new Vector2(.5f, 0);
            p.BattleCaption.anchoredPosition = new Vector2(0, 17);
            p.BattleCaption.sizeDelta = new Vector2(141, 18);
            c.BattleCaption.fontSize = c.BattleCaption.fontSizeMax = 12;
            c.BattleCaption.enableAutoSizing = true; c.BattleCaption.fontSizeMin = 9;
            EditorUtility.SetDirty(p); EditorUtility.SetDirty(c); EditorUtility.SetDirty(p.BattleCaption); EditorUtility.SetDirty(c.BattleCaption);
        }

        private static void Polish(FriendDuelController c)
        {
            var bodyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/UI/Sprites/ModeSelection/ModeBody.mat");
            foreach (var label in c.Overlay.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (label.font == Font) label.fontSharedMaterial = bodyMaterial;
                label.fontSizeMax = label.fontSize;
                EditorUtility.SetDirty(label);
            }
            c.Title.fontSizeMax = 24;
            c.PrimaryLabel.fontSizeMax = c.SecondaryLabel.fontSizeMax = 17;
            c.TertiaryLabel.fontSizeMax = 13;
            c.Help.GetComponentInChildren<TextMeshProUGUI>().fontSizeMax = 13;
            c.DemoLabel.fontSizeMax = 10;
            c.DemoConnection.GetComponentInChildren<TextMeshProUGUI>().fontSizeMax = 10;
            c.Close.GetComponentInChildren<TextMeshProUGUI>().fontSizeMax = 26;
            c.MeLabel.fontSizeMax = c.FriendLabel.fontSizeMax = 20;
        }

        private static void Player(RectTransform parent, string name, float top, out TextMeshProUGUI label, out TextMeshProUGUI status)
        {
            var card = Picture(parent, name, Pill, Surface, 0, top, 342, 76); card.type = Image.Type.Sliced;
            label = Text(card.transform, "Name", name, 20, 18, 10, 306, 30);
            label.richText = false; label.overflowMode = TextOverflowModes.Ellipsis;
            label.enableAutoSizing = true; label.fontSizeMin = 12;
            status = Text(card.transform, "Status", "В комнате", 13, 18, 44, 306, 23);
            status.color = new Color32(132, 240, 161, 255);
        }

        private static RectTransform Rect(Transform parent, string name, float x, float top, float w, float h)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false); rt.gameObject.layer = 5;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -top); rt.sizeDelta = new Vector2(w, h); return rt;
        }
        private static void Stretch(RectTransform rt)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        private static Image Picture(Transform parent, string name, Sprite sprite, Color color, float x, float y, float w, float h)
        { var i = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>(); i.sprite = sprite; i.color = color; i.raycastTarget = false; return i; }
        private static Button Clickable(Image image)
        {
            image.raycastTarget = true; var b = image.gameObject.AddComponent<Button>(); b.targetGraphic = image;
            b.navigation = new Navigation { mode = Navigation.Mode.None }; return b;
        }
        private static Button Button(Transform parent, string name, string value, float x, float y, float w, float h, Color color)
        {
            var image = Picture(parent, name, Pill, color, x, y, w, h); image.type = Image.Type.Sliced;
            var b = Clickable(image); var label = Text(image.transform, "Label", value, 17, 8, 0, w - 16, h, TextAlignmentOptions.Center);
            label.enableAutoSizing = true; label.fontSizeMin = 10; return b;
        }
        private static TextMeshProUGUI Text(Transform parent, string name, string value, float size, float x, float y, float w, float h, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var t = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font; t.fontSize = t.fontSizeMax = size; t.text = value; t.color = Color.white; t.alignment = align;
            t.raycastTarget = false; t.textWrappingMode = TextWrappingModes.NoWrap; return t;
        }
    }
}
