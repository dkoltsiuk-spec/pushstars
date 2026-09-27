using System;
using System.IO;
using System.Linq;
using PushStars.Fight;
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
    /// <summary>The one screen after a fight, built from the Brawl Stars / Clash Royale research
    /// (docs: the old Results card + this summary said the same thing twice):
    /// a saturated tone that says who won with a faint lightning brand pattern, VICTORY! top left
    /// with trophies and the daily-streak bonus under it, the hero big in a looping win pose with the
    /// opponent muted behind, the score as the big number, XP, and one button bottom right.
    /// Edits the authored scene in place (old objects are left inactive for the legacy tools),
    /// so bindings, routes and the idle stage survive. Re-runnable. RewardScreen fills and
    /// stages it at runtime.</summary>
    public static class RewardSummaryRedesign
    {
        private const string ScenePath = "Assets/_Project/Scenes/RewardSummary.unity";
        private const string LocationSprite = "Assets/_Project/UI/Sprites/IMG_0916.PNG";
        private static readonly Color Gold = new Color32(255, 214, 17, 255);
        private static readonly Color Muted = new Color(1, 1, 1, .72f);
        private static readonly Color Ink = new Color32(8, 9, 24, 255);
        private static readonly Color Track = new Color(0, .05f, .25f, .35f);
        private static PushStarsTheme Theme => Resources.Load<PushStarsTheme>("PushStarsTheme");

        [MenuItem("Tools/Push Stars/Rewards/Build One-Screen Fight Result")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty) throw new InvalidOperationException("Save RewardSummary edits first.");
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single();
                Redesign(screen);
                FightPresentationSceneBuilder.PersistTextMaterials(screen.gameObject);
                EditorUtility.SetDirty(screen);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + ScenePath);
            }
            finally
            {
                if (opened) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            Debug.Log("[RewardSummaryRedesign] One-screen fight result authored.");
        }

        internal static void Redesign(RewardScreen screen)
        {
            var ui = screen.SummaryUi;
            var composition = Find(screen, "Composition");

            // Earlier passes' groups stay in the scene, inactive, so the legacy alignment tool
            // still finds its objects; nothing below reads them.
            foreach (var old in new[] { "summary-set", "summary-cards", "summary-level", "reps", "technique", "aura",
                                        "trophies", "energy-xp", "subtitle", "player-name" })
            {
                var found = composition.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == old);
                if (found != null) found.gameObject.SetActive(false);
            }

            // ── Background: saturated tone (tinted per outcome at runtime) + faint brand pattern ──
            var backdrop = Find(screen, "Backdrop");
            var tone = Child(backdrop.parent, "Tone", 0, 0, 0, 0);
            tone.anchorMin = Vector2.zero; tone.anchorMax = Vector2.one; tone.offsetMin = tone.offsetMax = Vector2.zero;
            var flat = tone.GetComponent<Image>();
            if (flat != null) Object.DestroyImmediate(flat);
            var bg = Graphic<RewardToneBackdrop>(tone);
            bg.Top = new Color32(30, 74, 206, 255); bg.Middle = new Color32(56, 126, 252, 255);
            bg.Bottom = new Color32(28, 66, 196, 255); bg.MiddleAt = .55f;
            bg.Light = new Color32(196, 228, 255, 105); bg.LightCenter = new Vector2(.45f, .5f); bg.LightRadius = .8f;
            bg.Floor = new Color(0, 0, 0, 0); bg.BandAlpha = 0;
            bg.Vignette = new Color32(10, 26, 110, 120); bg.VignetteStart = .6f;
            bg.SetVerticesDirty();
            tone.SetSiblingIndex(backdrop.GetSiblingIndex() + 1);
            ui.Backdrop = bg;
            // The fight's location behind everything (Clash Royale keeps its arena on the result
            // screen). There is one location today — the main screen's shark arena — so it is
            // authored here; the outcome tone lies over it half-transparent to keep text legible.
            var location = Child(backdrop.parent, "Location", 0, 0, 0, 0);
            location.anchorMin = location.anchorMax = new Vector2(.5f, .5f);
            location.pivot = new Vector2(.5f, .5f); location.anchoredPosition = Vector2.zero;
            var locationImage = Ensure<Image>(location.gameObject);
            locationImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LocationSprite);
            locationImage.raycastTarget = false; locationImage.color = Color.white;
            var cover = Ensure<AspectRatioFitter>(location.gameObject);
            cover.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            if (locationImage.sprite != null) cover.aspectRatio = locationImage.sprite.rect.width / locationImage.sprite.rect.height;
            location.SetSiblingIndex(backdrop.GetSiblingIndex() + 1);
            tone.SetSiblingIndex(location.GetSiblingIndex() + 1);
            var veil = Ensure<CanvasGroup>(tone.gameObject);
            veil.alpha = .20f; veil.interactable = veil.blocksRaycasts = false;
            var pattern = screen.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "LightningPattern");
            if (pattern != null) pattern.gameObject.SetActive(false);

            // ── Title, top left ──
            var header = Group(composition, "summary-header", 195, 84, 342, 54);
            var title = Find(screen, "header"); title.SetParent(header, false); Place(title, 171, 27, 342, 54);
            title.gameObject.SetActive(true);
            ui.Title = Style(Find(screen, "Title"), 46, Color.white, FightTypography.Role.Title, TextAlignmentOptions.Left);
            ui.Title.text = "YOU WIN!";
            Keyline(ui.Title, .22f, -.8f);
            ui.Header = header;

            BuildTrophyRewards(screen);

            // ── Stage: the hero big and slightly left, the opponent muted behind on the right ──
            var stage = Group(composition, "summary-stage", 195, 405, 390, 480);
            foreach (var leftover in new[] { "Rays", "PodiumSide", "PodiumRim", "PodiumTop" })
            {
                var found = stage.Find(leftover);
                if (found != null) Object.DestroyImmediate(found.gameObject);
            }
            ui.Rays = null;
            var glow = stage.Find("Glow");
            if (glow != null) glow.gameObject.SetActive(false);
            var opponentShadow = Child(stage, "OpponentShadow", 305, 374, 76, 13);
            Graphic<HardEllipseGraphic>(opponentShadow).color = new Color(0, .025f, .1f, .32f);
            ui.OpponentShadow = opponentShadow.gameObject;
            var opponent = Child(stage, "OpponentCharacter", 305, 225, 300, 340);
            var opponentImage = Ensure<RawImage>(opponent.gameObject);
            opponentImage.raycastTarget = false;
            opponentImage.color = RewardScreen.OpponentTint;
            ui.OpponentPortrait = opponentImage;
            AttachOpponentStage(screen, opponentImage);
            var portrait = Find(screen, "portrait"); portrait.SetParent(stage, false); Place(portrait, 185, 212, 540, 560);
            var shadow = Find(screen, "ShadowBox"); Place(shadow, 270, 529, 126, 19);
            var shadowImage = shadow.GetComponentInChildren<Image>(true);
            if (shadowImage != null) Object.DestroyImmediate(shadowImage);
            Graphic<HardEllipseGraphic>(Find(screen, "GroundShadow")).color = new Color(0, .025f, .1f, .38f);
            opponent.SetAsFirstSibling();
            opponentShadow.SetAsFirstSibling();
            portrait.SetAsLastSibling();
            ui.Stage = stage;

            // ── Score: 21 : 18 [NEW RECORD], and a caption line: 92% FORM   VS OSKAT009 ──
            var score = Group(composition, "result-score", 195, 674, 342, 76);
            var scoreRow = Child(score, "Row", 171, 24, 342, 50);
            Row(scoreRow.gameObject, TextAnchor.LowerLeft, 6);
            ui.TotalReps = Number(scoreRow, "Mine", "21", 54, Color.white, 0);
            ui.ScoreSeparator = Number(scoreRow, "Separator", ":", 32, Muted, 1);
            ui.OpponentReps = Number(scoreRow, "Theirs", "18", 34, Muted, 2);
            var badge = Child(scoreRow, "RecordBadge", 0, 0, 104, 22);
            Plate(badge.gameObject, Gold, 11);
            var badgeLayout = Ensure<LayoutElement>(badge.gameObject);
            badgeLayout.preferredWidth = 104; badgeLayout.preferredHeight = 22;
            Style(Child(badge, "Label", 52, 11, 100, 18), 11, Ink, FightTypography.Role.Label).text = "NEW RECORD";
            badge.SetSiblingIndex(3);
            ui.RecordBadge = badge;
            var captionRow = Child(score, "Caption", 171, 62, 342, 18);
            Row(captionRow.gameObject, TextAnchor.MiddleLeft, 6);
            ui.Technique = Word(captionRow, "Form", "92%", Color.white, 0);
            Word(captionRow, "FormLabel", "FORM", Muted, 1);
            ui.OpponentName = Word(captionRow, "Opponent", "VS OSKAT009", Muted, 2);
            var formLayout = Ensure<LayoutElement>(ui.OpponentName.gameObject);
            formLayout.minWidth = 0;
            ui.TechniqueStars = new RewardStarGraphic[0];
            ui.RepsPanel = score;

            // ── XP: level badge, bar, +570 XP ──
            var xp = Group(composition, "result-xp", 195, 732, 342, 48);
            var levelBadge = Child(xp, "LevelBadge", 19, 24, 38, 50);
            var oldBadge = levelBadge.GetComponent<RewardPlateGraphic>();
            if (oldBadge != null) Object.DestroyImmediate(oldBadge);
            var badgeImage = Ensure<Image>(levelBadge.gameObject);
            badgeImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/ProfileSettings/Group 532.png");
            badgeImage.preserveAspect = true; badgeImage.raycastTarget = false;
            Style(Child(levelBadge, "Label", 21, 11, 30, 12), 10, new Color32(255, 216, 140, 255), FightTypography.Role.Label).text = "LVL";
            ui.Level = Style(Child(levelBadge, "Value", 21, 28, 30, 27), 23, Color.white, FightTypography.Role.Value);
            ui.Level.text = "4";
            var xpTrack = Child(xp, "Track", 142, 20, 260, 27);
            Plate(xpTrack.gameObject, new Color32(25, 46, 107, 255), 13);
            var trackPlate = xpTrack.GetComponent<RewardPlateGraphic>();
            trackPlate.BorderWidth = 1; trackPlate.Border = new Color32(50, 42, 30, 255);
            ui.LevelFill = Fill(xpTrack, .62f);
            ui.LevelFill.offsetMin = new Vector2(1, 1); ui.LevelFill.offsetMax = new Vector2(-1, -1);
            var fillPlate = ui.LevelFill.GetComponent<RewardPlateGraphic>();
            fillPlate.Top = new Color32(255, 159, 0, 255); fillPlate.Bottom = new Color32(255, 218, 100, 255);
            fillPlate.Radius = 11;
            var xpValue = Child(xp, "AnimatedValue", 311, 20, 64, 24);
            ui.EnergyXp = Style(Child(xpValue, "Value", 32, 12, 64, 24), 16, Color.white, FightTypography.Role.Value, TextAlignmentOptions.Right);
            ui.EnergyXp.text = "+570 XP";
            ui.XpContent = xpValue;
            ui.LevelProgress = Style(Child(xp, "Progress", 152, 20, 218, 25), 17, Color.white, FightTypography.Role.Value);
            ui.LevelProgress.text = "620/1000";
            ui.LevelUp = Style(Child(xp, "LevelUp", 158, 42, 220, 14), 10, Gold, FightTypography.Role.Label);
            ui.LevelUp.text = "";
            levelBadge.SetAsLastSibling();
            ui.LevelGroup = xp; ui.LevelBadge = levelBadge;

            // ── Home action, centred along the bottom ──
            ConfigureHomeButton(screen);

            foreach (var step in new[] { header, ui.Cards, stage, score, xp })
                if (step.GetComponent<CanvasGroup>() == null) step.gameObject.AddComponent<CanvasGroup>();
            stage.SetSiblingIndex(0);
            ui.Continue.SetAsLastSibling();
            UseUprightTypography(screen);
        }

        public static void ConfigureHomeButton(RewardScreen screen)
        {
            var ui = screen.SummaryUi;
            var button = Find(screen, "continue-button");
            Place(button, 195, 801, 170, 65.2f);
            var buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/TrainingScreen/result-home-button.png");
            var buttonImage = button.GetComponent<Image>();
            buttonImage.sprite = buttonSprite;
            buttonImage.type = Image.Type.Simple; buttonImage.preserveAspect = true;
            foreach (string name in new[] { "MockupFace", "MockupShadow" })
            {
                // The supplied artwork already includes its border and shadow.
                var duplicate = button.Find(name);
                if (duplicate != null) duplicate.gameObject.SetActive(false);
            }
            ui.ContinueIcon = Icon(button, "HomeIcon", Theme.IconHouse, 47, 32, 30, 30);
            ui.ContinueLabel = button.GetComponentInChildren<TextMeshProUGUI>(true);
            Place(ui.ContinueLabel.rectTransform, 102, 32, 76, 34);
            Style(ui.ContinueLabel.rectTransform, 22, Color.white, FightTypography.Role.Label).text = "HOME";
            ui.ContinueLabel.fontSizeMin = 12;
            var labelMaterial = new Material(ui.ContinueLabel.fontSharedMaterial) { name = "Result Home Label" };
            labelMaterial.SetColor("_OutlineColor", Color.black);
            labelMaterial.SetFloat("_OutlineWidth", .075f);
            labelMaterial.SetFloat("_FaceDilate", .025f);
            labelMaterial.DisableKeyword("UNDERLAY_ON");
            ui.ContinueLabel.fontSharedMaterial = labelMaterial;
            ui.ContinueLabel.fontStyle = FontStyles.Normal;
            ui.ContinueLabel.UpdateMeshPadding();
            ui.ContinueIcon.transform.SetAsLastSibling();
            ui.ContinueLabel.transform.SetAsLastSibling();
            if (button.GetComponent<CanvasGroup>() == null) button.gameObject.AddComponent<CanvasGroup>();
            ui.Continue = button;

        }

        /// <summary>Updates only the trophy row, preserving the rest of the authored composition.</summary>
        public static void BuildTrophyRewards(RewardScreen screen)
        {
            var ui = screen.SummaryUi;
            var trophies = Group(Find(screen, "Composition"), "result-trophies", 195, 132, 342, 40);
            foreach (var name in new[] { "LeagueTrack", "League" })
            {
                var old = trophies.Find(name);
                if (old != null) Object.DestroyImmediate(old.gameObject);
            }
            var trophyContent = Child(trophies, "AnimatedValue", 55, 20, 110, 40);
            Icon(trophyContent, "Icon", Theme.IconCup, 20, 20, 40, 40);
            ui.Trophies = Style(Child(trophyContent, "Value", 80, 20, 60, 34), 27, Color.white, FightTypography.Role.Value, TextAlignmentOptions.Left);
            ui.Trophies.text = "+20";
            // Match the reference: the flame overlaps a short black wedge; the tilted
            // trophy and bonus sit above its right edge, outside the backing plate.
            var bonus = Child(trophies, "StreakBonus", 260, 20, 164, 44);
            var oldPlate = bonus.GetComponent<RewardPlateGraphic>();
            if (oldPlate != null) Object.DestroyImmediate(oldPlate);
            var backing = Child(bonus, "Backing", 62, 29, 92, 31);
            Plate(backing.gameObject, Color.black, 3);
            backing.GetComponent<RewardPlateGraphic>().Slant = 12;
            backing.SetAsFirstSibling();
            Icon(bonus, "Flame", Theme.IconStreak, 20, 26, 47, 57);
            ui.StreakDays = Style(Child(bonus, "Days", 67, 29, 34, 32), 26, new Color32(255, 218, 0, 255), FightTypography.Role.Value);
            ui.StreakDays.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik Black TMP.asset");
            ui.StreakDays.fontSharedMaterial = ui.StreakDays.font.material;
            ui.StreakDays.fontStyle = FontStyles.Normal;
            ui.StreakDays.text = "2";
            var cup = Icon(bonus, "Trophy", Theme.IconCup, 107, 15, 40, 40);
            cup.rectTransform.localRotation = Quaternion.Euler(0, 0, 8);
            var outline = Ensure<Outline>(cup.gameObject);
            outline.effectColor = Color.black; outline.effectDistance = new Vector2(1.5f, -1.5f);
            ui.StreakBonusTrophies = Style(Child(bonus, "Bonus", 145, 8, 48, 36), 31, new Color32(255, 212, 86, 255), FightTypography.Role.Value);
            Keyline(ui.StreakBonusTrophies, .2f, -.25f);
            ui.StreakBonusTrophies.rectTransform.localRotation = Quaternion.Euler(0, 0, 15);
            ui.StreakBonusTrophies.text = "+1";
            ui.StreakBonusGroup = bonus.gameObject;
            ui.TrophyContent = trophyContent; ui.TrophyGroup = trophies.gameObject; ui.Cards = trophies;
            var serialized = new SerializedObject(screen);
            serialized.FindProperty("_sampleSummary.StreakDays").intValue = 2;
            serialized.FindProperty("_sampleSummary.StreakBonusTrophies").intValue = 1;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void UseUprightTypography(RewardScreen screen)
        {
            foreach (var text in screen.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                // Italic font assets contain slanted glyphs even with the style flag cleared.
                if (text.font != null && text.font.name.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0)
                    FightTypography.Apply(text, FightTypography.Role.Label);
                text.fontStyle &= ~FontStyles.Italic;
                text.UpdateMeshPadding();
            }
        }

        /// <summary>Copies the opponent's stage (ghost silhouette, or the boss in a boss fight)
        /// from FightResults, the way <see cref="RewardSummaryStageSetup"/> copies the player's.</summary>
        private static void AttachOpponentStage(RewardScreen screen, RawImage target)
        {
            var ui = screen.SummaryUi;
            if (ui.Opponent != null)
            {
                var existing = new SerializedObject(ui.Opponent.GetComponentInParent<CharacterStage>());
                existing.FindProperty("_targetImage").objectReferenceValue = target;
                existing.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
            string sourcePath = FightPresentationSceneBuilder.ResultsPath;
            var source = SceneManager.GetSceneByPath(sourcePath);
            bool opened = !source.IsValid() || !source.isLoaded;
            if (opened) source = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
            try
            {
                var rival = source.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true))
                    .First(a => new SerializedObject(a).FindProperty("_opponentStage").boolValue);
                var clone = Object.Instantiate(rival.StageCamera.GetComponentInParent<CharacterStage>().gameObject);
                clone.name = "SummaryOpponentStage";
                SceneManager.MoveGameObjectToScene(clone, screen.gameObject.scene);
                var stage = clone.GetComponent<CharacterStage>();
                var serialized = new SerializedObject(stage);
                serialized.FindProperty("_targetImage").objectReferenceValue = target;
                serialized.FindProperty("_idleBob").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                stage.StageCamera.targetTexture = null;
                stage.StageCamera.enabled = false;
                ui.Opponent = clone.GetComponentInChildren<FightAvatar>(true);
            }
            finally { if (opened) EditorSceneManager.CloseScene(source, true); }
        }

        private static TextMeshProUGUI Number(RectTransform row, string name, string sample, float size, Color tint, int index)
        {
            var text = Style(Child(row, name, 0, 0, 0, 50), size, tint, FightTypography.Role.Value, TextAlignmentOptions.BottomLeft);
            text.text = sample; text.enableAutoSizing = false; text.transform.SetSiblingIndex(index);
            if (name == "Mine") Keyline(text, .2f, -.7f);
            return text;
        }

        private static TextMeshProUGUI Word(RectTransform row, string name, string sample, Color tint, int index)
        {
            var text = Style(Child(row, name, 0, 0, 0, 18), 13, tint, FightTypography.Role.Label, TextAlignmentOptions.Left);
            text.text = sample; text.enableAutoSizing = false; text.transform.SetSiblingIndex(index);
            return text;
        }

        private static RectTransform Fill(RectTransform track, float progress)
        {
            var fill = track.Find("Fill") as RectTransform;
            if (fill == null) { fill = new GameObject("Fill", typeof(RectTransform)).GetComponent<RectTransform>(); fill.SetParent(track, false); }
            fill.anchorMin = Vector2.zero; fill.anchorMax = new Vector2(progress, 1);
            fill.pivot = new Vector2(0, .5f); fill.localScale = Vector3.one; fill.localRotation = Quaternion.identity;
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            Plate(fill.gameObject, Gold, 5);
            return fill;
        }

        private static void Row(GameObject go, TextAnchor anchor, float spacing)
        {
            var row = Ensure<HorizontalLayoutGroup>(go.gameObject);
            row.childAlignment = anchor; row.spacing = spacing;
            row.childControlWidth = true; row.childControlHeight = false;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
        }

        // ── Helpers ──

        private static T Ensure<T>(GameObject go) where T : Component
        {
            var found = go.GetComponent<T>();
            return found != null ? found : go.AddComponent<T>();
        }

        private static RectTransform Find(Component root, string name) =>
            root.GetComponentsInChildren<RectTransform>(true).First(t => t.name == name && t != root.transform);

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        private static RectTransform Child(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null)
            {
                rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                rt.SetParent(parent, false);
            }
            rt.gameObject.SetActive(true);
            Place(rt, x, y, w, h);
            return rt;
        }

        private static RectTransform Group(Transform composition, string name, float x, float y, float w, float h) =>
            Child(composition, name, x, y, w, h);

        private static Image Icon(Transform parent, string name, Sprite sprite, float x, float y, float w, float h)
        {
            var rt = Child(parent, name, x, y, w, h);
            var image = Ensure<Image>(rt.gameObject);
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
            return image;
        }

        private static T Graphic<T>(RectTransform rt) where T : Graphic
        {
            if (rt.GetComponent<CanvasRenderer>() == null) rt.gameObject.AddComponent<CanvasRenderer>();
            var graphic = Ensure<T>(rt.gameObject);
            graphic.raycastTarget = false;
            return graphic;
        }

        /// <summary>A flat rounded fill: no keyline, shadow or gloss.</summary>
        private static void Plate(GameObject go, Color tint, float radius)
        {
            var plate = Graphic<RewardPlateGraphic>((RectTransform)go.transform);
            plate.Top = plate.Bottom = tint; plate.Border = Ink; plate.Shadow = Color.clear;
            plate.Radius = radius; plate.BorderWidth = 0; plate.ShadowOffset = 0; plate.Gloss = 0;
            plate.SetVerticesDirty();
        }

        private static TextMeshProUGUI Style(RectTransform rt, float size, Color tint,
            FightTypography.Role role, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var text = Ensure<TextMeshProUGUI>(rt.gameObject);
            FightTypography.Apply(text, role);
            text.fontSize = text.fontSizeMax = size; text.fontSizeMin = size * .72f;
            text.enableAutoSizing = true; text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = align; text.color = tint; text.raycastTarget = false;
            return text;
        }

        /// <summary>The heavy comic keyline of the result banners: black outline plus a hard drop.</summary>
        private static void Keyline(TextMeshProUGUI text, float outline, float drop)
        {
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik Black TMP.asset");
            var material = new Material(text.font.material) { name = "Result Keyline " + text.name };
            material.SetColor("_OutlineColor", Color.black);
            material.SetFloat("_OutlineWidth", outline);
            material.SetFloat("_FaceDilate", outline);
            material.SetColor("_UnderlayColor", Color.black);
            material.SetFloat("_UnderlayOffsetY", drop);
            material.SetFloat("_UnderlayDilate", outline);
            material.SetFloat("_UnderlaySoftness", 0);
            material.EnableKeyword("UNDERLAY_ON");
            text.fontSharedMaterial = material;
            text.fontStyle = FontStyles.Normal;
            text.UpdateMeshPadding();
        }
    }
}
