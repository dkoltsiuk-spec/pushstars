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
    /// <summary>Explicit authoring pass for the supplied result/summary/case mockups.
    /// Changes existing scene objects and retains their serialized actions and data bindings.</summary>
    public static class RewardMockupAlignment
    {
        private static readonly Color Gold = new Color32(255, 214, 0, 255);
        private static readonly Color Muted = new Color32(161, 164, 184, 255);
        private static PushStarsTheme Theme => Resources.Load<PushStarsTheme>("PushStarsTheme");

        [MenuItem("Tools/Push Stars/Align Reward Screens To Mockups")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            // Refuse to discard edits in an open scene.
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scene edits first.");
            try
            {
                Edit("FightResults", AlignResults);
                Edit("RewardSummary", AlignSummary);
                Edit("CaseOpening", AlignCase);
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
            Debug.Log("[RewardMockupAlignment] Three authored scenes aligned to reference compositions.");
        }

        private static void Edit(string name, Action<Scene> action)
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Project/Scenes/" + name + ".unity");
            action(scene);
            EnsureDisplayCamera(scene);
            foreach (var root in scene.GetRootGameObjects()) FightPresentationSceneBuilder.PersistTextMaterials(root);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static RectTransform Find(Scene scene, string name) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<RectTransform>(true)).First(t => t.name == name);

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static RectTransform Child(Transform parent, string name, float x, float y, float w, float h)
        {
            var rt = parent.Find(name) as RectTransform;
            if (rt == null)
            {
                rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                rt.SetParent(parent, false);
            }
            Place(rt, x, y, w, h);
            return rt;
        }

        private static TextMeshProUGUI Style(RectTransform rt, float size, Color tint,
            FightTypography.Role role, TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var text = rt.GetComponent<TextMeshProUGUI>();
            if (text == null) text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            FightTypography.Apply(text, role);
            text.fontSize = text.fontSizeMax = size; text.fontSizeMin = size * .72f;
            text.enableAutoSizing = true; text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = align; text.color = tint; text.raycastTarget = false;
            return text;
        }

        private static void Text(Scene scene, string name, float x, float y, float w, float h,
            float size, Color color, FightTypography.Role role, string value = null,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var rt = Find(scene, name); Place(rt, x, y, w, h);
            var text = Style(rt, size, color, role, align);
            if (value != null) text.text = value;
        }

        private static Image Icon(Transform parent, string name, Sprite sprite, float x, float y, float w, float h)
        {
            var rt = Child(parent, name, x, y, w, h);
            var image = rt.GetComponent<Image>() ?? rt.gameObject.AddComponent<Image>();
            image.sprite = sprite; image.preserveAspect = true; image.raycastTarget = false;
            return image;
        }

        private static void Button(RectTransform rt, bool home)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/TrainingScreen/button-yellow.png");
            var face = rt.GetComponent<Image>();
            face.sprite = sprite; face.type = Image.Type.Simple; face.color = Color.white;
            var shadow = Icon(rt, "MockupShadow", sprite, rt.rect.width / 2, rt.rect.height / 2 + 5,
                rt.rect.width, rt.rect.height);
            shadow.preserveAspect = false;
            shadow.color = new Color32(8, 9, 17, 255);
            shadow.transform.SetAsFirstSibling();
            // The parent face draws before children, so put a second face over the shadow.
            var top = Icon(rt, "MockupFace", sprite, rt.rect.width / 2, rt.rect.height / 2,
                rt.rect.width, rt.rect.height);
            top.preserveAspect = false;
            top.transform.SetSiblingIndex(1);
            var label = rt.GetComponentsInChildren<TextMeshProUGUI>(true).First();
            Style(label.rectTransform, 18, Color.white, FightTypography.Role.Button, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(home ? 31 : 8, 2);
            label.rectTransform.offsetMax = new Vector2(-8, 0);
            label.transform.SetAsLastSibling();
            if (home) Icon(rt, "HomeIcon", Theme.IconHouse, 30, rt.rect.height / 2, 24, 24);
        }

        private static void AlignResults(Scene scene)
        {
            // One reference composition shared by both portraits and both stat columns.
            var duel = Find(scene, "DuelLayout");
            var safe = duel.Find("SafeArea") as RectTransform;
            var composition = duel.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "MockupComposition")
                ?? Child(safe, "MockupComposition", 0, 0, 390, 844);
            composition.SetParent(safe, false);
            Place(composition, 0, 0, 390, 844);
            composition.anchorMin = composition.anchorMax = new Vector2(.5f, .5f);
            composition.anchoredPosition = Vector2.zero;
            var compositionFitter = composition.GetComponent<RewardCompositionFitter>();
            if (compositionFitter == null) compositionFitter = composition.gameObject.AddComponent<RewardCompositionFitter>();
            // Keep NEXT within the safe area on shorter phone displays.
            compositionFitter.WidthBias = 0f;
            EditorUtility.SetDirty(compositionFitter);
            foreach (Transform child in safe.Cast<Transform>().ToArray()) if (child != composition) child.SetParent(composition, false);
            foreach (string name in new[] { "OpponentPortrait", "PlayerPortrait" }) Find(scene, name).SetParent(composition, false);
            var canvas = duel.GetComponentInParent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(390, 844);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            Find(scene, "Home").gameObject.SetActive(false);

            Place(Find(scene, "OpponentPortrait"), 274, 193, 200, 348);
            Place(Find(scene, "PlayerPortrait"), 106, 590, 218, 366);
            foreach (var item in new[] { ("Opponent", 274f, 355f), ("Player", 106f, 756f) })
            {
                var shadow = Icon(composition, item.Item1 + "GroundShadow", Theme.GroundShadow,
                    item.Item2, item.Item3, 180, 26);
                shadow.color = new Color(0, 0, .15f, .4f);
                shadow.transform.SetSiblingIndex(Find(scene, item.Item1 + "Portrait").GetSiblingIndex());
            }
            foreach (string name in new[] { "OpponentNamePlate", "PlayerNamePlate" })
            {
                var plate = Find(scene, name); var img = plate.GetComponent<Image>();
                if (img != null) img.enabled = false;
            }
            Place(Find(scene, "OpponentNamePlate"), 111, 99, 208, 49);
            Place(Find(scene, "PlayerNamePlate"), 267, 490, 236, 83);
            var opponent = Style(Find(scene, "OpponentName"), 37, Gold, FightTypography.Role.Title);
            var player = Style(Find(scene, "PlayerName"), 35, Gold, FightTypography.Role.Title);
            foreach (var text in new[] { opponent, player })
            {
                Stretch(text.rectTransform);
                Comic(text);
            }
            player.enableWordWrapping = true;
            player.text = player.text.Replace("\n", "").Replace("_", "_\n");
            Icon(composition, "OpponentFlag", Theme.FlagGermany, 27, 141, 27, 20);
            Icon(composition, "PlayerFlag", Theme.FlagMoldova, 263, 539, 27, 20);
            Icon(composition, "OpponentAura", Theme.IconAura, 55, 141, 23, 23);
            Icon(composition, "OpponentLevel", Theme.IconLevel, 81, 141, 23, 23);
            Icon(composition, "PlayerAura", Theme.IconAura, 290, 539, 23, 23);
            Icon(composition, "PlayerLevel", Theme.IconLevel, 316, 539, 23, 23);

            Text(scene, "OpponentReps", 85, 207, 145, 120, 124, new Color32(255,29,24,255), FightTypography.Role.Value);
            Text(scene, "PlayerReps", 303, 582, 150, 122, 124, new Color32(91,239,0,255), FightTypography.Role.Value,
                align: TextAlignmentOptions.Right);
            Text(scene, "OpponentResultFormCaption", 83, 273, 122, 21, 15, Muted, FightTypography.Role.Label, "FORM");
            Text(scene, "OpponentResultForm", 83, 297, 122, 32, 27, Color.green, FightTypography.Role.Label);
            Text(scene, "OpponentResultTempoCaption", 83, 324, 122, 21, 15, Muted, FightTypography.Role.Label, "TEMPO");
            Text(scene, "OpponentResultTempo", 83, 350, 122, 32, 27, Color.red, FightTypography.Role.Label);
            Text(scene, "PlayerResultFormCaption", 329, 652, 76, 21, 15, Muted, FightTypography.Role.Label, "FORM", TextAlignmentOptions.Right);
            Text(scene, "PlayerResultForm", 329, 678, 76, 32, 27, Color.red, FightTypography.Role.Label, align: TextAlignmentOptions.Right);
            Text(scene, "PlayerResultTempoCaption", 329, 707, 76, 21, 15, Muted, FightTypography.Role.Label, "TEMPO", TextAlignmentOptions.Right);
            Text(scene, "PlayerResultTempo", 329, 735, 76, 32, 27, Color.green, FightTypography.Role.Label, align: TextAlignmentOptions.Right);
            // Keep the authored lightning seam, but remove its old label panel.
            var banner = Find(scene, "BannerPlate");
            Place(banner, 195, 407, 440, 139);
            var bannerImage = banner.GetComponent<Image>();
            if (bannerImage != null) { bannerImage.color = Color.white; bannerImage.preserveAspect = false; }
            var title = Style(Find(scene, "Banner"), 49, Color.white, FightTypography.Role.Title, TextAlignmentOptions.Center);
            title.text = "WINNER"; Comic(title);
            title.rectTransform.localRotation = Quaternion.Euler(0, 0, 4);
            foreach (string name in new[] { "OpponentResultFormCaption", "OpponentResultTempoCaption", "PlayerResultFormCaption", "PlayerResultTempoCaption" })
            {
                var caption = Find(scene, name).GetComponent<TextMeshProUGUI>();
                caption.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik TMP.asset");
                caption.fontSharedMaterial = caption.font.material;
                caption.fontStyle = FontStyles.Normal;
            }
            var next = Find(scene, "Continue"); next.SetParent(composition, false);
            Place(next, 195, 770, 126, 56); Button(next, false);
            DuelResultEntrance.ApplyLayout(composition);
            // Re-evaluate the edit-mode body crop against the newly authored portrait boxes.
            var results = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightResultScreen>(true)).Single();
            results.MarkSceneAuthored(AssetDatabase.LoadAssetAtPath<Texture2D>(FightPresentationSceneBuilder.StandingPortraitPath));
        }

        private static void AlignSummary(Scene scene)
        {
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single();
            RewardSummaryStageSetup.Attach(screen);
            var composition = Find(scene, "Composition");
            if (composition.GetComponent<RewardCompositionFitter>() == null) composition.gameObject.AddComponent<RewardCompositionFitter>();
            Place(Find(scene, "header"), 195, 103, 350, 33);
            var title = Style(Find(scene, "Title"), 23, Gold, FightTypography.Role.Title);
            title.text = "TRAINING IS COMPLETED!!!";
            Place(Find(scene, "subtitle"), 195, 137, 350, 28);
            Style(Find(scene, "Subtitle"), 20, Color.white, FightTypography.Role.Subtitle).text = "Best training brooo!";
            Place(Find(scene, "player-name"), 210, 167, 321, 21);
            Style(screen.SummaryUi.PlayerName.rectTransform, 12, Gold, FightTypography.Role.Label);
            Icon(composition, "PlayerFlag", Theme.FlagMoldova, 30, 167, 24, 18);
            string[] groups = { "trophies", "energy-xp", "aura" };
            for (int i = 0; i < groups.Length; i++)
            {
                var group = Find(scene, groups[i]); Place(group, 77, 211 + i * 47, 119, 39);
                var caption = group.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "Caption");
                caption.gameObject.SetActive(false);
                var icon = group.GetComponentsInChildren<Image>(true).First();
                Place((RectTransform)icon.transform.parent, 20, 20, 38, 37);
                var value = group.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "Value");
                Place((RectTransform)value.transform.parent, 84, 20, 83, 37);
                Style(value.rectTransform, 24, i == 2 ? new Color32(198,143,255,255) : Gold, FightTypography.Role.Value);
            }
            Place(Find(scene, "portrait"), 256, 479, 214, 486);
            var shadow = Find(scene, "ShadowBox"); Place(shadow, 107, 470, 181, 31);
            // Fit the static portrait without changing its aspect ratio.
            var portrait = screen.SummaryUi.Portrait;
            if (portrait.texture != null)
            {
                float width = portrait.rectTransform.rect.width / portrait.rectTransform.rect.height * portrait.texture.height / portrait.texture.width;
                portrait.uvRect = new Rect((1 - width) / 2, 0, width, 1);
            }
            Place(Find(scene, "reps"), 76, 402, 113, 91);
            var reps = Find(scene, "reps");
            Style(reps.GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "Caption").rectTransform,
                15, Muted, FightTypography.Role.Caption);
            Style(screen.SummaryUi.TotalReps.rectTransform, 61, Color.white, FightTypography.Role.Value);
            Place(Find(scene, "technique"), 83, 505, 128, 66);
            var technique = Find(scene, "technique").GetComponentsInChildren<TextMeshProUGUI>(true).First(t => t.name == "Caption");
            Style(technique.rectTransform, 15, Muted, FightTypography.Role.Caption).text = "TECHNIQUE";
            Style(screen.SummaryUi.Technique.rectTransform, 23, Color.green, FightTypography.Role.Value);
            // The inventory remains reachable from Home; the mockup has one primary action.
            if (screen.SummaryUi.CaseAwardButton != null)
            {
                screen.SummaryUi.CaseAwardButton.gameObject.SetActive(false);
                screen.SummaryUi.CaseAwardButton = null;
            }
            var home = Find(scene, "continue-button"); Place(home, 195, 771, 138, 52); Button(home, true); RewardSummaryRedesign.Redesign(screen); // current Brawl-style layout builds on this pass
            EditorUtility.SetDirty(screen);
        }

        private static void AlignCase(Scene scene)
        {
            var screen = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RewardScreen>(true)).Single();
            var composition = Find(scene, "Composition");
            if (composition.GetComponent<RewardCompositionFitter>() == null) composition.gameObject.AddComponent<RewardCompositionFitter>();
            foreach (string name in new[] { "header", "queue", "rarity", "attempts", "open-button", "home-button" })
                Find(scene, name).gameObject.SetActive(false);
            Place(Find(scene, "stars"), 195, 193, 180, 45);
            foreach (var row in screen.CaseUi.RarityStarGroups)
            {
                var stars = row.GetComponentsInChildren<RewardStarGraphic>(true);
                for (int i = 0; i < stars.Length; i++) Place(stars[i].rectTransform, 90 + (i - (stars.Length - 1) / 2f) * 47, 22.5f, 46, 46);
            }
            var chest = Find(scene, "case"); Place(chest, 195, 409, 285, 265);
            screen.CaseUi.Glow.rectTransform.sizeDelta = new Vector2(380, 460);
            var rays = Child(chest, "Rays", 142.5f, 132.5f, 390, 470);
            if (rays.GetComponent<CanvasRenderer>() == null) rays.gameObject.AddComponent<CanvasRenderer>();
            var graphic = rays.GetComponent<CaseTapGraphic>() ?? rays.gameObject.AddComponent<CaseTapGraphic>();
            graphic.Rays = true; graphic.raycastTarget = false; rays.SetAsFirstSibling();
            RewardCaseAttentionSetup.Attach(screen);
            Place(Find(scene, "hint"), 195, 764, 340, 35);
            Style(screen.CaseUi.Hint.rectTransform, 20, Color.white, FightTypography.Role.Subtitle, TextAlignmentOptions.Center).text = "Tap to open case";
            Place(Find(scene, "upgrade-message"), 195, 615, 350, 30);
            screen.CaseUi.Status.text = "";
            EditorUtility.SetDirty(screen);
        }

        private static void EnsureDisplayCamera(Scene scene)
        {
            if (scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true))
                .Any(c => c.enabled && c.targetTexture == null && c.cullingMask == 0)) return;
            var camera = new GameObject("DisplayClearCamera", typeof(Camera)).GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(20, 23, 29, 255);
            camera.cullingMask = 0; camera.depth = -100; camera.nearClipPlane = .1f;
        }

        private static void Comic(TextMeshProUGUI text)
        {
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik Black TMP.asset");
            text.fontSharedMaterial = new Material(text.font.material) { name = "Reward Comic Keyline" };
            text.fontStyle = FontStyles.Italic;
            text.outlineWidth = .16f; text.outlineColor = Color.black;
            text.fontMaterial.SetFloat("_FaceDilate", .16f);
            text.UpdateMeshPadding();
        }
    }
}
