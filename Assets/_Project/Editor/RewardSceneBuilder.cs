using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.Fight;
using PushStars.UI;
using PushStars.UI.Layout;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>One-time authoring/migration builder. Saved scenes contain every UI object;
    /// the game never calls this builder or reconstructs these scenes during navigation.</summary>
    public static class RewardSceneBuilder
    {
        public const string ScenesDirectory = "Assets/_Project/Scenes";
        private static readonly Color Gold = new Color32(255, 214, 17, 255);
        private static readonly Color Muted = new Color32(180, 184, 209, 255);
        private static readonly Color Lime = new Color32(123, 255, 75, 255);
        private static readonly Color Violet = new Color32(209, 163, 255, 255);
        private static readonly string[] CaseResources = { "CaseCommon", "CaseRare", "CaseEpic", "CaseLegendary" };
        private static PushStarsTheme _theme;
        private static RectTransform _composition;
        private static ScreenLayoutRoot _layout;
        private static RewardScreen _screen;

        public static void BuildAll()
        {
            Directory.CreateDirectory(ScenesDirectory);
            _theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            Build(FightScreen.RewardSummary, "reward-summary", FightRewardBackdrop.Style.Summary);
            Build(FightScreen.CaseAward, "case-award", FightRewardBackdrop.Style.Summary);
            Build(FightScreen.CaseOpening, "case-opening", FightRewardBackdrop.Style.Case);
            Build(FightScreen.CaseReward, "case-reward", FightRewardBackdrop.Style.Gems);
            AssetDatabase.SaveAssets();
            Debug.Log("[RewardSceneBuilder] Four independent, serialized reward scenes saved.");
        }

        private static void Build(FightScreen kind, string layoutId, FightRewardBackdrop.Style appearance)
        {
            string path = ScenesDirectory + "/" + kind + ".unity";
            if (File.Exists(path)) return;
            var previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            try
            {
            var root = new GameObject(kind.ToString());
            _screen = root.AddComponent<RewardScreen>();
            _screen.Configure(kind);
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390, 844);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var backdrop = FullRect("Backdrop", canvasObject.transform).gameObject.AddComponent<FightRewardBackdrop>();
            backdrop.Appearance = appearance;
            backdrop.raycastTarget = true;
            backdrop.SetVerticesDirty();
            AddPattern(canvasObject.transform, appearance);
            var safe = FullRect("SafeArea", canvasObject.transform);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            _composition = Rect("Composition", safe, 0, 0, 390, 844);
            _composition.anchorMin = _composition.anchorMax = new Vector2(0.5f, 0.5f);
            _composition.anchoredPosition = Vector2.zero;
            _layout = _composition.gameObject.AddComponent<ScreenLayoutRoot>();
            _layout.Configure(layoutId, false);
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.transform.SetParent(root.transform, false);
            switch (kind)
            {
                case FightScreen.RewardSummary: BuildSummary(); break;
                case FightScreen.CaseAward: BuildCase(true); break;
                case FightScreen.CaseOpening: BuildCase(false); break;
                case FightScreen.CaseReward: BuildPrize(); break;
            }
            // Migrate a pre-existing saved composition once. From here the scene itself owns
            // the RectTransforms, so neither old PlayerPrefs nor the catalog can overwrite it.
            _layout.ApplySavedLayout();
            _layout.MarkSceneAuthored();
            _layout.ShowEditButton = false;
            FightPresentationSceneBuilder.PersistTextMaterials(root);
            EditorUtility.SetDirty(_screen);
            EditorUtility.SetDirty(_layout);
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not save " + path);
            var registered = EditorBuildSettings.scenes.ToList();
            if (!registered.Any(item => item.path == path))
            {
                registered.Add(new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = registered.ToArray();
            }
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        private static void BuildSummary()
        {
            var ui = _screen.SummaryUi;
            Label("Title", Group("header", 195, 105, 354, 90), "ТРЕНИРОВКА\nЗАВЕРШЕНА!", 29, Gold,
                FightTypography.Role.Title, TextAlignmentOptions.Left);
            Label("Subtitle", Group("subtitle", 195, 163, 352, 24), "Становишься сильнее с каждым разом", 12.5f,
                Color.white, FightTypography.Role.Subtitle, TextAlignmentOptions.Left);
            ui.PlayerName = Label("PlayerName", Group("player-name", 195, 199, 352, 24), "BEASTCORE_DEV", 14,
                Gold, FightTypography.Role.Label, TextAlignmentOptions.Left);
            ui.Trophies = RewardRow("trophies", "КУБКИ", _theme != null ? _theme.IconCup : null,
                258, Gold, "+21", out ui.TrophyContent, out _);
            ui.EnergyXp = RewardRow("energy-xp", "ОПЫТ", _theme != null ? _theme.IconXP : null,
                310, Gold, "+570", out ui.XpContent, out _);
            ui.Aura = RewardRow("aura", "АУРА", _theme != null ? _theme.IconAura : null,
                362, Violet, "+32", out ui.AuraContent, out var aura);
            ui.AuraGroup = aura.gameObject;

            var portrait = Group("portrait", 264, 489, 215, 443);
            var shadow = Icon("GroundShadow", Rect("ShadowBox", portrait, 107.5f, 422, 132, 24),
                _theme != null ? _theme.GroundShadow : null);
            shadow.color = new Color(0, 0, 0, 0.42f);
            ui.Portrait = FullRect("Character", portrait).gameObject.AddComponent<RawImage>();
            ui.Portrait.raycastTarget = false;
            ui.MalePortrait = AssetDatabase.LoadAssetAtPath<Texture2D>(FightPresentationSceneBuilder.StandingPortraitPath);
            ui.FemalePortrait = AssetDatabase.LoadAssetAtPath<Texture2D>(FightPresentationSceneBuilder.StandingFemalePortraitPath);
            ui.Portrait.texture = CharacterRoster.SavedGender == CharacterGender.Female ? ui.FemalePortrait : ui.MalePortrait;
            if (ui.Portrait.texture == null)
                throw new InvalidOperationException("Build the presentation scenes and standing portrait before the reward scenes.");
            var reps = Group("reps", 77, 466, 116, 98);
            Label("Caption", Rect("CaptionBox", reps, 58, 13, 116, 26), "ВСЕГО ПОВТОРОВ", 11, Muted,
                FightTypography.Role.Caption, TextAlignmentOptions.Left);
            ui.TotalReps = Label("Count", Rect("CountBox", reps, 58, 65, 116, 70), "57", 57,
                Color.white, FightTypography.Role.Value, TextAlignmentOptions.Left);
            var technique = Group("technique", 77, 565, 116, 65);
            Label("Caption", Rect("CaptionBox", technique, 58, 12, 116, 24), "ТЕХНИКА", 12, Muted,
                FightTypography.Role.Caption, TextAlignmentOptions.Left);
            ui.Technique = Label("Value", Rect("ValueBox", technique, 58, 44, 116, 40), "92%", 25,
                Lime, FightTypography.Role.Value, TextAlignmentOptions.Left);
            ui.CaseAwardButton = ActionButton(Group("case-award-button", 94, 673, 150, 48),
                "ПОЛУЧЕН КЕЙС", _screen.InspectCaseAward, out var awardLabel);
            awardLabel.fontSize = 13; awardLabel.fontSizeMax = 13; awardLabel.fontSizeMin = 10;
            ActionButton(Group("continue-button", 195, 775, 230, 60), "НА ГЛАВНУЮ", _screen.Home, out _);
        }

        private static void BuildCase(bool receipt)
        {
            var ui = _screen.CaseUi;
            Label("Title", Group("header", 195, 111, 340, 46), receipt ? "ПОЛУЧЕН КЕЙС!" : "ТВОЙ КЕЙС", 28,
                receipt ? Gold : Color.white, FightTypography.Role.Heading);
            ui.Source = Label("Source", Group("queue", 195, 151, 340, 24), "КЕЙС ЗА ТРЕНИРОВКУ", 12,
                Muted, FightTypography.Role.Caption);
            ui.Rarity = Label("Rarity", Group("rarity", 195, 224, 340, 40), "ОБЫЧНЫЙ", 24,
                RewardScreen.RarityColor(CaseRarity.Common), FightTypography.Role.Value);
            var stars = Group("stars", 195, 271, 180, 40);
            ui.StarsContent = FullRect("AnimatedStars", stars);
            var allStars = new List<RewardStarGraphic>();
            ui.RarityStarGroups = new GameObject[4];
            // Centered rows are separate authored variants; runtime only toggles visibility.
            for (int rarityIndex = 0; rarityIndex < 4; rarityIndex++)
            {
                var row = FullRect("Rarity" + rarityIndex, ui.StarsContent);
                ui.RarityStarGroups[rarityIndex] = row.gameObject;
                for (int starIndex = 0; starIndex <= rarityIndex; starIndex++)
                {
                    var star = Rect("Star" + (starIndex + 1), row, 90 + (starIndex - rarityIndex * 0.5f) * 38, 20, 38, 38);
                    var graphic = star.gameObject.AddComponent<RewardStarGraphic>();
                    graphic.color = Gold; graphic.raycastTarget = false; allStars.Add(graphic);
                }
                row.gameObject.SetActive(rarityIndex == 0);
            }
            ui.Stars = allStars.ToArray();
            var chest = Group("case", 195, 438, 306, 285);
            ui.Glow = Glow(chest, RewardScreen.RarityColor(CaseRarity.Common), 380);
            ui.Content = FullRect("AnimatedCase", chest);
            ui.RarityArtwork = CaseResources.Select(name => Resources.Load<Sprite>("Rewards/" + name)).ToArray();
            ui.Artwork = Icon("CaseArt", ui.Content, ui.RarityArtwork[0]);
            var hit = chest.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            ui.TapButton = chest.gameObject.AddComponent<Button>();
            ui.TapButton.targetGraphic = hit; ui.TapButton.transition = Selectable.Transition.None;
            UnityEventTools.AddPersistentListener(ui.TapButton.onClick, _screen.TapCase);
            ui.Status = Label("Message", Group("upgrade-message", 195, 612, 352, 37),
                receipt ? "КЕЙС СОХРАНЁН В ИНВЕНТАРЕ" : "КАЖДЫЙ ТАП — ШАНС НА УЛУЧШЕНИЕ", 17,
                Gold, FightTypography.Role.Label);
            if (!receipt)
            {
                var attempts = Group("attempts", 195, 651, 94, 18);
                ui.TapPips = new Image[3];
                for (int i = 0; i < 3; i++)
                {
                    var pip = Rect("Tap" + (i + 1), attempts, 21 + i * 26, 9, 14, 14);
                    ui.TapPips[i] = pip.gameObject.AddComponent<Image>();
                    ui.TapPips[i].sprite = _theme != null ? _theme.CircleShape : null;
                    ui.TapPips[i].color = new Color(1, 1, 1, 0.2f); ui.TapPips[i].raycastTarget = false;
                }
            }
            ui.Hint = Label("Hint", Group("hint", 195, 704, 340, 52),
                receipt ? "Открой сейчас или вернись к нему позже" : "Нажимай на кейс — каждый тап\nдаёт шанс повысить редкость",
                15, Color.white, FightTypography.Role.Caption);
            ui.ActionButton = ActionButton(Group("open-button", 195, 781, 228, 57),
                receipt ? "ОТКРЫТЬ СЕЙЧАС" : "УЛУЧШИТЬ  1/3", receipt ? (UnityAction)_screen.OpenNow : _screen.TapCase,
                out ui.ActionLabel);
            HomeButton();
        }

        private static void BuildPrize()
        {
            var ui = _screen.PrizeUi;
            Label("Title", Group("header", 195, 185, 350, 48), "КРИСТАЛЛЫ", 30,
                Color.white, FightTypography.Role.Heading);
            ui.Rarity = Label("Rarity", Group("rarity", 195, 231, 340, 26), "РЕДКИЙ КЕЙС", 13,
                new Color(1, 1, 1, 0.72f), FightTypography.Role.Caption);
            var prize = Group("gems", 195, 430, 245, 230);
            ui.Glow = Glow(prize, new Color(0.8f, 1, 0.1f), 410);
            ui.Content = FullRect("AnimatedGems", prize);
            Icon("GemsArt", ui.Content, Resources.Load<Sprite>("Rewards/Gems"));
            ui.Amount = Label("Amount", Group("amount", 195, 576, 270, 65), "×100", 46,
                Color.white, FightTypography.Role.Value);
            ui.Note = Label("Note", Group("receipt", 195, 682, 346, 42), "Кристаллы пополнят твой баланс", 14,
                Color.white, FightTypography.Role.Caption);
            ui.ClaimButton = ActionButton(Group("claim-button", 195, 780, 252, 60), "ЗАБРАТЬ И ДОМОЙ", _screen.ClaimPrize,
                out ui.ClaimLabel);
            HomeButton();
        }

        private static void HomeButton()
        {
            var button = ActionButton(Group("home-button", 74, 49, 126, 42), "ДОМОЙ", _screen.Home, out var label);
            label.fontSize = 14; label.fontSizeMax = 14; label.fontSizeMin = 12;
        }

        private static TextMeshProUGUI RewardRow(string id, string caption, Sprite sprite, float y, Color tint,
            string initial, out RectTransform content, out RectTransform group)
        {
            group = Group(id, 86, y, 134, 45);
            content = FullRect("AnimatedValue", group);
            Icon("Icon", Rect("IconBox", content, 18, 21, 32, 35), sprite);
            var value = Label("Value", Rect("ValueBox", content, 91, 16, 90, 35), initial, 27,
                tint, FightTypography.Role.Value, TextAlignmentOptions.Left);
            Label("Caption", Rect("CaptionBox", content, 90, 39, 90, 15), caption, 10,
                Muted, FightTypography.Role.Caption, TextAlignmentOptions.Left);
            return value;
        }

        private static void AddPattern(Transform parent, FightRewardBackdrop.Style appearance)
        {
            if (_theme == null || _theme.IconLightningBG == null) return;
            var pattern = FullRect("LightningPattern", parent);
            for (int row = 0; row < 9; row++)
                for (int col = 0; col < 5; col++)
                {
                    var bolt = Icon("Bolt", pattern, _theme.IconLightningBG);
                    bolt.rectTransform.anchorMin = bolt.rectTransform.anchorMax =
                        new Vector2((col + (row % 2) * 0.45f) / 4f, row / 8f);
                    bolt.rectTransform.anchoredPosition = Vector2.zero;
                    bolt.rectTransform.sizeDelta = new Vector2(70, 90);
                    bolt.color = new Color(1, 1, 1, appearance == FightRewardBackdrop.Style.Gems ? 0.07f : 0.025f);
                }
        }

        private static RectTransform Group(string id, float x, float y, float width, float height)
        {
            var rect = Rect(id, _composition, x, y, width, height);
            _layout.Register(id, rect); return rect;
        }
        private static TextMeshProUGUI Label(string name, RectTransform parent, string value, float size, Color tint,
            FightTypography.Role role, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var label = FullRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.text = value; label.fontSize = size; label.fontSizeMin = size * 0.68f; label.fontSizeMax = size;
            label.enableAutoSizing = true; label.enableWordWrapping = false; label.overflowMode = TextOverflowModes.Ellipsis;
            label.alignment = alignment; label.color = tint; label.raycastTarget = false;
            FightTypography.Apply(label, role); return label;
        }
        private static Button ActionButton(RectTransform parent, string caption, UnityAction action, out TextMeshProUGUI label)
        {
            var plate = parent.gameObject.AddComponent<Image>();
            plate.sprite = _theme != null ? _theme.BtnShape : null;
            plate.color = plate.sprite != null ? Color.white : Gold;
            plate.type = plate.sprite != null && plate.sprite.border.sqrMagnitude > 0 ? Image.Type.Sliced : Image.Type.Simple;
            var button = parent.gameObject.AddComponent<Button>(); button.targetGraphic = plate;
            UnityEventTools.AddPersistentListener(button.onClick, action);
            label = Label("Label", parent, caption, 19, Color.white, FightTypography.Role.Button);
            label.rectTransform.offsetMin = new Vector2(17, 4); label.rectTransform.offsetMax = new Vector2(-17, 0);
            return button;
        }
        private static Image Glow(RectTransform parent, Color tint, float size)
        {
            var rect = FullRect("Glow", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f); rect.sizeDelta = Vector2.one * size;
            var image = rect.gameObject.AddComponent<Image>(); image.sprite = _theme != null ? _theme.GlowRadial : null;
            tint.a = 0.34f; image.color = tint; image.raycastTarget = false; image.enabled = image.sprite != null;
            return image;
        }
        private static Image Icon(string name, Transform parent, Sprite sprite)
        {
            var image = FullRect(name, parent).gameObject.AddComponent<Image>();
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false; image.enabled = sprite != null;
            return image;
        }
        private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height); return rect;
        }
        private static RectTransform FullRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
        }
    }
}
