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
    /// <summary>Upgrades the authored onboarding in place, retaining its character stages.</summary>
    public static class GuidedOnboardingSetup
    {
        private const string Art = "Assets/_Project/UI/Sprites/Onboarding/";

        [MenuItem("Tools/Push Stars/Onboarding/Apply Coach Onboarding")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            ImportArt();
            var scene = SceneManager.GetSceneByPath(OnboardingSceneSetup.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene = EditorSceneManager.OpenScene(OnboardingSceneSetup.ScenePath);
            var cards = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GenderChoiceCard>(true))
                .OrderBy(c => (int)c.Gender).ToArray();
            if (cards.Length != 2) throw new InvalidOperationException("Onboarding needs its two authored character stages.");
            // Detach the reusable cards before replacing the presentation during an editor iteration.
            foreach (var card in cards) card.transform.SetParent(null, true);
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "CoachOnboardingCanvas" || root.name == "OnboardingAssessment") Object.DestroyImmediate(root);
                else if (root.name == "OnboardingCanvas") Object.DestroyImmediate(root);
            }
            var stages = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterStage>(true))
                .OrderBy(c => c.name == "Stage_Male" ? 0 : 1).ToArray();
            if (stages.Length != 2) throw new InvalidOperationException("Exactly two selection stages are required.");
            for (int i = 0; i < stages.Length; i++)
            {
                stages[i].gameObject.SetActive(true);
                cards[i].Portrait.texture = stages[i].StageCamera.targetTexture;
                stages[i].StageCamera.Render();
            }

            var workout = CopyAssessment(scene);
            var hud = workout.GetComponentInChildren<FightHud>(true);
            var hudSettings = new SerializedObject(hud);
            var assessmentTitle = hudSettings.FindProperty("_soloCaption").objectReferenceValue as TextMeshProUGUI;
            assessmentTitle.enableWordWrapping = false;
            assessmentTitle.enableAutoSizing = true;
            assessmentTitle.fontSizeMin = 12;
            assessmentTitle.fontSizeMax = 17;
            var player = workout.GetComponentsInChildren<FightAvatar>(true).Single(a => a.name == "CV");
            var fightCanvas = workout.GetComponentInChildren<Canvas>(true);
            var workoutGroup = Group(fightCanvas.gameObject, 0);
            fightCanvas.sortingOrder = 0;
            var unusedPlayerStage = workout.transform.Find("PlayerStage3D").gameObject;
            unusedPlayerStage.SetActive(false);
            workout.transform.Find("Opponents").gameObject.SetActive(false);
            workout.transform.Find("GhostStage3D").gameObject.SetActive(false);
            var backdrop = hud.GetComponentsInChildren<Image>(true)
                .FirstOrDefault(i => i.name == "Backdrop" && i.transform.parent.name == "LevelTestScenery");
            if (backdrop == null || backdrop.sprite == null) throw new InvalidOperationException("Missing authored assessment backdrop.");

            var canvas = UiBuilder.Canvas("CoachOnboardingCanvas", out var rootRect);
            canvas.sortingOrder = 10;
            var selectionBackground = Panel(rootRect, "SelectionBackground", 1);
            var blue = UiBuilder.Image((RectTransform)selectionBackground.transform, "Blue", new Color32(31, 39, 72, 255));
            UiBuilder.Stretch(blue.rectTransform);
            var theme = Resources.Load<PushStarsTheme>("PushStarsTheme");
            if (theme != null && theme.IconLightningBG != null)
            {
                var pattern = LightningField.Build((RectTransform)selectionBackground.transform, theme.IconLightningBG);
                var patternGroup = Group(pattern.gameObject, .7f);
                patternGroup.blocksRaycasts = false;
            }
            var assessmentBackground = Panel(rootRect, "AssessmentBackground", 0);
            var arena = UiBuilder.Image((RectTransform)assessmentBackground.transform, "Backdrop", Color.white);
            arena.sprite = backdrop.sprite;
            UiBuilder.Stretch(arena.rectTransform);
            var arenaAspect = arena.gameObject.AddComponent<AspectRatioFitter>();
            arenaAspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            arenaAspect.aspectRatio = backdrop.sprite.rect.width / backdrop.sprite.rect.height;

            var safe = UiBuilder.Rect(rootRect, "SafeArea"); UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            var selection = Panel(safe, "CharacterSelection", 1);
            var title = UiBuilder.Text((RectTransform)selection.transform, "Title", Color.white, "CHOOSE YOUR HERO", 16);
            UiBuilder.Place(title.rectTransform, new Vector2(.5f, 1), new Vector2(0, -25), new Vector2(290, 24));
            for (int i = 0; i < cards.Length; i++) ConfigureCard(cards[i], (RectTransform)selection.transform, i, stages[i]);
            var hero = UiBuilder.Rect(safe, "PersistentHero"); UiBuilder.Stretch(hero);
            var target = UiBuilder.Rect(safe, "AssessmentHeroTarget");
            UiBuilder.Place(target, new Vector2(.5f, .53f), Vector2.zero, new Vector2(235, 470));
            target.pivot = new Vector2(.5f, .5f);

            var previewHud = BuildPreviewHud(safe);
            var wash = Panel(rootRect, "CoachBlueWash", 0);
            var dark = UiBuilder.Image((RectTransform)wash.transform, "Dim", new Color32(8, 19, 65, 65));
            UiBuilder.Stretch(dark.rectTransform);

            // The coach reaches the bottom edge; the dialogue stays inside the device safe area.
            var coachLayer = Panel(rootRect, "Coach", 0);
            var coach = Picture((RectTransform)coachLayer.transform, "Trainer", "coach-welcome.png");
            UiBuilder.Place(coach.rectTransform, new Vector2(.5f, 0), new Vector2(-58, -22), new Vector2(356, 316));
            coach.preserveAspect = true;
            var dialogueSafe = UiBuilder.Rect(rootRect, "DialogueSafeArea"); UiBuilder.Stretch(dialogueSafe);
            dialogueSafe.gameObject.AddComponent<SafeAreaFitter>();
            var bubbleImage = Picture(dialogueSafe, "SpeechBubble", "coach-bubble.png");
            UiBuilder.Place(bubbleImage.rectTransform, new Vector2(.5f, 0), new Vector2(82, 122), new Vector2(200, 158));
            var bubble = Group(bubbleImage.gameObject, 0);
            var speech = UiBuilder.Text(bubbleImage.rectTransform, "Speech", new Color32(46, 51, 69, 255),
                "Hey, I'm your coach!\nChoose your hero.\nI'll show you the ropes.", 13, FontStyles.Bold);
            speech.fontSharedMaterial = speech.font.material;
            speech.lineSpacing = 3;
            UiBuilder.Place(speech.rectTransform, new Vector2(.5f, 1), new Vector2(0, -17), new Vector2(176, 78));
            var action = ActionButton(bubbleImage.rectTransform, "CoachAction", "LET'S GO", out var actionLabel);
            UiBuilder.Place((RectTransform)action.transform, new Vector2(.5f, 0), new Vector2(0, 43), new Vector2(172, 34));

            var placement = BuildPlacement(bubbleImage.rectTransform, out var placementPhone);
            placement.SetActive(false);
            var next = ActionButton(dialogueSafe, "Next", "NEXT", out _);
            next.GetComponent<Image>().sprite = Sprite("next-button.png");
            next.GetComponent<Image>().type = Image.Type.Simple;
            UiBuilder.Place((RectTransform)next.transform, new Vector2(.5f, 0), new Vector2(0, 42), new Vector2(136, 44));
            var back = UiBuilder.Button(dialogueSafe, "Back", "BACK", new Color(0, 0, 0, 0), Color.white, 12, out _);
            UiBuilder.Place((RectTransform)back.transform, new Vector2(0, 1), new Vector2(12, -8), new Vector2(72, 44));
            var skip = UiBuilder.Button(dialogueSafe, "NotNow", "NOT NOW", new Color(0, 0, 0, 0), new Color32(211, 224, 255, 255), 12, out _);
            UiBuilder.Place((RectTransform)skip.transform, new Vector2(1, 0), new Vector2(-20, 38), new Vector2(102, 44));
            var stepLabel = UiBuilder.Text(dialogueSafe, "StepLabel", new Color32(169, 195, 255, 255), "01 / CHOOSE YOUR HERO", 9);
            UiBuilder.Place(stepLabel.rectTransform, new Vector2(.5f, 0), new Vector2(100, 12), new Vector2(150, 18));
            back.gameObject.SetActive(false); skip.gameObject.SetActive(false); action.gameObject.SetActive(false);
            coachLayer.transform.SetAsLastSibling();
            // A foreground wash softens the coach's lower edge, with controls above it.
            var washArt = Picture((RectTransform)coachLayer.transform, "ForegroundBlueWash", "coach-blue-wash.png");
            washArt.rectTransform.anchorMin = new Vector2(0, 0);
            washArt.rectTransform.anchorMax = new Vector2(1, 0);
            washArt.rectTransform.pivot = new Vector2(.5f, 0);
            washArt.rectTransform.sizeDelta = new Vector2(0, 280);
            washArt.color = new Color(1, 1, 1, .85f);
            var controls = UiBuilder.Rect(rootRect, "ForegroundControls"); UiBuilder.Stretch(controls);
            controls.gameObject.AddComponent<SafeAreaFitter>();
            next.transform.SetParent(controls, false);
            controls.SetSiblingIndex(coachLayer.transform.GetSiblingIndex());
            stepLabel.transform.SetParent(controls, false);
            UiBuilder.Place(stepLabel.rectTransform, new Vector2(.5f, 0), new Vector2(0, 8), new Vector2(240, 18));
            var hintSafe = UiBuilder.Rect(rootRect, "HintSafeArea"); UiBuilder.Stretch(hintSafe);
            hintSafe.gameObject.AddComponent<SafeAreaFitter>();
            stepLabel.transform.SetParent(hintSafe, false);
            var dismissSurface = UiBuilder.Button(rootRect, "DismissCoach", "", Color.clear, Color.clear, 1, out _);
            UiBuilder.Stretch((RectTransform)dismissSurface.transform);

            var guide = rootRect.gameObject.AddComponent<GuidedOnboardingController>();
            var so = new SerializedObject(guide);
            Set(so, "_heroLayer", hero); Set(so, "_selection", selection);
            Set(so, "_selectionBackground", selectionBackground); Set(so, "_assessmentBackground", assessmentBackground);
            Set(so, "_assessmentHud", previewHud); Set(so, "_assessmentTarget", target);
            Set(so, "_next", next); Set(so, "_back", back); Set(so, "_skip", skip);
            Set(so, "_dismissSurface", dismissSurface);
            Set(so, "_wash", wash); Set(so, "_coachGroup", coachLayer); Set(so, "_coach", coach);
            Set(so, "_bubble", bubble); Set(so, "_speech", speech); Set(so, "_action", action);
            Set(so, "_actionLabel", actionLabel); Set(so, "_placement", placement);
            Set(so, "_placementPhone", placementPhone); Set(so, "_stepLabel", stepLabel);
            Set(so, "_workout", workout); Set(so, "_workoutCanvas", workoutGroup);
            Set(so, "_player", player); Set(so, "_hud", hud); Set(so, "_unusedPlayerStage", unusedPlayerStage);
            UiBuilder.SetArray(so, "_cards", cards); UiBuilder.SetArray(so, "_stages", stages);
            UiBuilder.SetArray(so, "_coachPoses", new Object[] { Sprite("coach-welcome.png"), Sprite("coach-assessment.png"), Sprite("coach-phone.png") });
            so.ApplyModifiedPropertiesWithoutUndo();
            // Saved scene previews the first composition, while Awake starts its entrance from zero.
            foreach (var text in rootRect.GetComponentsInChildren<TextMeshProUGUI>(true)) StyleCoachText(text);
            coachLayer.alpha = bubble.alpha = wash.alpha = 1;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            UiBuilder.EnsureSceneInBuildSettings(scene.path);
            AssetDatabase.SaveAssets();
            Debug.Log("[Onboarding] Coach onboarding saved with the user's ten original exports and embedded assessment.");
        }

        private static void ConfigureCard(GenderChoiceCard card, RectTransform parent, int index, CharacterStage stage)
        {
            card.transform.SetParent(parent, false);
            var rect = (RectTransform)card.transform;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            UiBuilder.Place(rect, new Vector2(.5f, 1), new Vector2(index == 0 ? -91 : 91, -64), new Vector2(172, 400));
            var portrait = card.Portrait.rectTransform;
            UiBuilder.Place(portrait, new Vector2(.5f, 0), new Vector2(0, 58), new Vector2(203, 406));
            var glow = rect.Find("Glow").GetComponent<Image>(); glow.sprite = Sprite("selection-glow.png");
            UiBuilder.Place(glow.rectTransform, new Vector2(.5f, 0), new Vector2(0, 72), new Vector2(256, 332));
            var ring = rect.Find("DotRing").GetComponent<Image>();
            foreach (Transform child in ring.transform) child.gameObject.SetActive(false);
            UiBuilder.Place(ring.rectTransform, new Vector2(.5f, 0), new Vector2(0, 13), new Vector2(32, 36));
            ring.type = Image.Type.Simple;
            ring.preserveAspect = true;
            var oldShadow = rect.Find("GroundShadow");
            if (oldShadow != null) Object.DestroyImmediate(oldShadow.gameObject);
            var shadow = new GameObject("GroundShadow", typeof(RectTransform), typeof(HardEllipseGraphic)).GetComponent<HardEllipseGraphic>();
            shadow.transform.SetParent(rect, false);
            shadow.color = new Color(0, 0, 0, .34f); shadow.raycastTarget = false;
            var contact = portrait.GetComponent<AvatarContactShadow>();
            if (contact == null) contact = portrait.gameObject.AddComponent<AvatarContactShadow>();
            contact.Bind(stage, shadow.rectTransform);
            var so = new SerializedObject(card);
            Set(so, "_selectedSprite", Sprite("selection-on.png")); Set(so, "_unselectedSprite", Sprite("selection-off.png"));
            so.FindProperty("_unselectedScale").floatValue = .88f;
            so.ApplyModifiedPropertiesWithoutUndo();
            card.SetSelected(index == 0);
        }

        private static CanvasGroup BuildPreviewHud(RectTransform parent)
        {
            var panel = Panel(parent, "AssessmentPreviewHUD", 0); var rect = (RectTransform)panel.transform;
            var badge = Picture(rect, "AssessmentBadge", "coach-button.png");
            badge.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/onb_btn_allow.png");
            UiBuilder.Place(badge.rectTransform, new Vector2(.5f, 1), new Vector2(0, -58), new Vector2(140, 46));
            var caption = UiBuilder.Text(badge.rectTransform, "Caption", new Color32(22, 27, 38, 255), "ASSESSMENT", 14);
            UiBuilder.Stretch(caption.rectTransform, 4, 4, 4, 4);
            var tempo = UiBuilder.Text(rect, "Tempo", Color.white, "<size=11><color=#9BADBC>TEMPO</color></size>\n--", 28, FontStyles.Bold, TextAlignmentOptions.Left);
            UiBuilder.Place(tempo.rectTransform, new Vector2(0, 1), new Vector2(22, -114), new Vector2(100, 62));
            var form = UiBuilder.Text(rect, "Technique", Color.white, "<size=11><color=#9BADBC>TECHNIQUE</color></size>\n--", 28, FontStyles.Bold, TextAlignmentOptions.Right);
            UiBuilder.Place(form.rectTransform, new Vector2(1, 1), new Vector2(-22, -114), new Vector2(100, 62));
            return panel;
        }

        private static GameObject BuildPlacement(RectTransform bubble, out RectTransform phone)
        {
            var panel = UiBuilder.Rect(bubble, "PlacementDiagram");
            UiBuilder.Place(panel, new Vector2(.5f, 1), new Vector2(0, -50), new Vector2(306, 136));
            var bg = Picture(panel, "BluePanel", "placement-panel.png"); UiBuilder.Stretch(bg.rectTransform);
            var person = UiBuilder.Image(panel, "PushupPosition", Color.white);
            person.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/onb_place_person.png");
            person.preserveAspect = true;
            UiBuilder.Place(person.rectTransform, new Vector2(.5f, .5f), new Vector2(-38, 0), new Vector2(178, 85));
            var device = UiBuilder.Image(panel, "Phone", Color.white);
            device.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/onb_place_phone.png");
            device.preserveAspect = true; phone = device.rectTransform;
            UiBuilder.Place(phone, new Vector2(1, .5f), new Vector2(-17, -7), new Vector2(50, 77));
            var distance = UiBuilder.Text(panel, "Distance", new Color32(158, 220, 255, 255), "1.5 - 2 m", 13);
            UiBuilder.Place(distance.rectTransform, new Vector2(.5f, 0), new Vector2(0, 9), new Vector2(180, 22));
            var hint = UiBuilder.Text(bubble, "PlacementHint", new Color32(46, 51, 69, 255),
                "Prop it upright, facing you.\nKeep your whole body in view.", 12);
            UiBuilder.Place(hint.rectTransform, new Vector2(.5f, 1), new Vector2(0, -192), new Vector2(314, 28));
            hint.transform.SetParent(panel, true);
            return panel.gameObject;
        }

        private static GameObject CopyAssessment(Scene target)
        {
            var source = EditorSceneManager.OpenScene("Assets/_Project/Scenes/Fight.unity", OpenSceneMode.Additive);
            var bundle = new GameObject("AssessmentSource"); SceneManager.MoveGameObjectToScene(bundle, source);
            bundle.SetActive(false);
            try
            {
                foreach (var root in source.GetRootGameObjects())
                    if (root != bundle && root.name != "DisplayClearCamera" && root.name != "EventSystem") root.transform.SetParent(bundle.transform, true);
                // Clone as a single tree so all session, HUD, driver and camera references are remapped.
                var copy = Object.Instantiate(bundle); copy.name = "OnboardingAssessment";
                SceneManager.MoveGameObjectToScene(copy, target); return copy;
            }
            finally { EditorSceneManager.CloseScene(source, true); }
        }

        private static void ImportArt()
        {
            AssetDatabase.Refresh();
            foreach (var path in Directory.GetFiles(Art, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 100;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }
        }
        private static Sprite Sprite(string file) => AssetDatabase.LoadAssetAtPath<Sprite>(Art + file);
        private static void StyleCoachText(TextMeshProUGUI text)
        {
            string path = "Assets/_Project/UI/Materials/Coach-" + text.font.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(text.font.material);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetFloat("_OutlineWidth", 0);
            material.SetFloat("_FaceDilate", 0);
            material.SetFloat("_OutlineSoftness", 0);
            material.SetFloat("_WeightNormal", 0);
            material.SetFloat("_WeightBold", 0);
            material.SetFloat("_Sharpness", .15f);
            material.DisableKeyword("UNDERLAY_ON"); material.DisableKeyword("UNDERLAY_INNER");
            material.SetColor("_FaceColor", Color.white);
            ShaderUtilities.UpdateShaderRatios(material);
            EditorUtility.SetDirty(material);
            text.fontSharedMaterial = material;
            text.UpdateMeshPadding();
        }
        private static Image Picture(RectTransform parent, string name, string file)
        { var image = UiBuilder.Image(parent, name, Color.white); image.sprite = Sprite(file); return image; }
        private static CanvasGroup Group(GameObject go, float alpha)
        { var group = go.GetComponent<CanvasGroup>(); if (group == null) group = go.AddComponent<CanvasGroup>(); group.alpha = alpha; group.interactable = group.blocksRaycasts = false; return group; }
        private static CanvasGroup Panel(RectTransform parent, string name, float alpha)
        { var rect = UiBuilder.Rect(parent, name); UiBuilder.Stretch(rect); return Group(rect.gameObject, alpha); }
        private static Button ActionButton(RectTransform parent, string name, string text, out TextMeshProUGUI label)
        {
            var button = UiBuilder.Button(parent, name, text, Color.white, new Color32(20, 24, 35, 255), 14, out label);
            button.GetComponent<Image>().sprite = Sprite("coach-button.png");
            label.fontSharedMaterial = label.font.material;
            return button;
        }
        private static void Set(SerializedObject so, string key, Object value) => so.FindProperty(key).objectReferenceValue = value;
    }
}
