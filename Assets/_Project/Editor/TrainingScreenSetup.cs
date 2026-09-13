using System;
using System.IO;
using System.Linq;
using PushStars.Fight;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    public static class TrainingScreenSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Training.unity";
        private const string Art = "Assets/_Project/UI/Sprites/TrainingScreen/";
        private const string Sprites = "Assets/_Project/UI/Sprites/";
        private const string TrainingBackgroundSprite = Sprites + "bg_training_home.png";
        private static TMP_FontAsset _font;
        private static Material _outline, _body;
        [MenuItem("Tools/Push Stars/Create Training Screen")]
        public static void Run()
        {
            foreach (var path in Directory.GetFiles(Art, "*.png"))
            {
                AssetDatabase.ImportAsset(path.Replace('\\', '/'));
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            }
            if (!File.Exists(ScenePath))
            {
                if (!AssetDatabase.CopyAsset("Assets/_Project/Scenes/Fight.unity", ScenePath)) throw new Exception("Cannot create training scene.");
            }
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var controller = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightController>(true)).Single();
                if (!scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TrainingScreen>(true)).Any()) Build(controller, scene);
                foreach (var glyph in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TrainingHomeGlyph>(true)))
                    if (glyph.GetComponent<CanvasRenderer>() == null) glyph.gameObject.AddComponent<CanvasRenderer>();
                EditorSceneManager.SaveScene(scene);
                if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
                    EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
                AssetDatabase.SaveAssets();
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        private static void Build(FightController controller, Scene scene)
        {
            _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.BoldAsset);
            _outline = AssetDatabase.LoadAssetAtPath<Material>(Sprites + "ModeSelection/ModeOutline.mat");
            _body = AssetDatabase.LoadAssetAtPath<Material>(Sprites + "ModeSelection/ModeBody.mat");
            var hud = new SerializedObject(controller).FindProperty("_hud").objectReferenceValue as FightHud;
            var canvas = hud.GetComponent<Canvas>();
            canvas.gameObject.layer = 5;
            var playerHalf = new SerializedObject(hud).FindProperty("_playerHalf").objectReferenceValue as RectTransform;
            var source = playerHalf.GetComponentInChildren<RawImage>(true);
            var avatar = playerHalf.GetComponentInChildren<FightAvatar>(true);
            if (avatar == null) avatar = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true)).First();
            var restTexture = FightPresentationSceneBuilder.EnsureTrainingRestPortrait(avatar);
            var femaleRest = FightPresentationSceneBuilder.EnsureTrainingRestPortrait(avatar, true);
            var pushupTexture = FightPresentationSceneBuilder.EnsureTrainingPushupPortrait(avatar);
            var femalePushup = FightPresentationSceneBuilder.EnsureTrainingPushupPortrait(avatar, true);
            var avatarSettings = new SerializedObject(avatar);
            avatarSettings.FindProperty("_viewDirection").vector3Value = new Vector3(0, .65f, 1);
            avatarSettings.FindProperty("_faceBias").floatValue = .3f;
            avatarSettings.ApplyModifiedPropertiesWithoutUndo();
            var stage = avatar.StageCamera.GetComponentInParent<PushStars.UI.CharacterStage>();
            if (stage != null)
            {
                var stageSettings = new SerializedObject(stage); stageSettings.FindProperty("_width").intValue = 768; stageSettings.FindProperty("_height").intValue = 768; stageSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            // Existing HUD remains a data sink and owns the live camera surface. The training UI covers it.
            foreach (Transform child in canvas.transform)
            {
                var hidden = child.GetComponent<CanvasGroup>();
                if (hidden == null) hidden = child.gameObject.AddComponent<CanvasGroup>();
                hidden.alpha = 0; hidden.blocksRaycasts = false; hidden.interactable = false;
            }
            var screen = canvas.gameObject.AddComponent<TrainingScreen>();
            var root = Rect(canvas.transform, "TrainingScreen"); Stretch(root);
            var background = Image(root, "Background", SpriteImporter.Load(TrainingBackgroundSprite)); Stretch(background.rectTransform);
            var decor = Rect(root, "LightningPattern"); Stretch(decor);
            var bolts = new RectTransform[40];
            for (int i = 0; i < bolts.Length; i++)
            {
                var bolt = Image(decor, "Bolt" + i, Sprite(Sprites + "icon_lightning_BG.png"));
                bolt.color = new Color(1, 1, 1, .28f);
                Place(bolt.rectTransform, -60 + i % 5 * 105 + (i / 5 % 2) * 30, i / 5 * 160 - 100, 83, 110);
                bolts[i] = bolt.rectTransform;
            }
            var content = Rect(root, "Content"); content.anchorMin = content.anchorMax = content.pivot = new Vector2(.5f, .5f); content.sizeDelta = new Vector2(390, 844);
            // A design canvas scales uniformly, preserving the composition on phones and short windows.
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390, 844); scaler.matchWidthOrHeight = 1;
            var loading = Panel(content, "Loading");
            Label(loading, "LoadingLabel", "LOADING…", 28, 20, 390, 350, 48, Color.white, true).fontStyle = FontStyles.Italic;
            var exercise = Panel(content, "Exercise");
            var topBolt = Image(exercise, "TopAccent", Sprite(Sprites + "bolt_corner_top.png")); Place(topBolt.rectTransform, 0, 0, 195, 295);
            var bottomBolt = Image(exercise, "BottomAccent", Sprite(Sprites + "bolt_corner_bottom.png")); Place(bottomBolt.rectTransform, 170, 565, 220, 279);
            Label(exercise, "SetCaption", "SET:", 13, 24, 140, 95, 22, new Color32(145, 166, 180, 255));
            var set = Label(exercise, "Set", "1/3", 32, 24, 164, 100, 45, Color.white);
            Label(exercise, "TechniqueCaption", "TECHNIQUE:", 13, 254, 140, 112, 22, new Color32(145, 166, 180, 255));
            var technique = Label(exercise, "Technique", "92%", 30, 270, 164, 96, 44, Color.green); technique.alignment = TextAlignmentOptions.Right;
            var reps = Label(exercise, "Reps", "18", 114, 70, 198, 250, 155, new Color32(255, 220, 0, 255), true);
            var counterMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "TrainingCounter.mat");
            bool newCounterMaterial = counterMaterial == null;
            if (newCounterMaterial) counterMaterial = new Material(_outline) { name = "TrainingCounter" };
            counterMaterial.SetFloat("_OutlineWidth", .13f); counterMaterial.SetFloat("_FaceDilate", .08f);
            counterMaterial.SetFloat("_UnderlayDilate", .3f); counterMaterial.SetFloat("_UnderlayOffsetY", -.45f);
            ShaderUtilities.UpdateShaderRatios(counterMaterial);
            if (newCounterMaterial) AssetDatabase.CreateAsset(counterMaterial, Art + "TrainingCounter.mat");
            else EditorUtility.SetDirty(counterMaterial);
            reps.fontSharedMaterial = counterMaterial;
            var floor = Image(exercise, "Floor", Sprite(Sprites + "circle_128.png")); floor.color = new Color32(25, 29, 42, 255); Place(floor.rectTransform, 52, 661, 286, 37);
            var live = Raw(exercise, "LiveCharacter", pushupTexture, 20, 350, 350, 350);
            var hint = Label(exercise, "Guidance", "", 16, 20, 700, 350, 40, Color.white, true);
            var clock = Image(exercise, "TimerIcon", Sprite(Sprites + "time.png")); Place(clock.rectTransform, 22, 768, 26, 28); clock.color = Color.white;
            var timer = Label(exercise, "SetTimer", "00:45", 20, 57, 766, 92, 32, Color.white);
            var next = Button(exercise, "Next", "NEXT", true, 151, 749, 100, 49);
            var rest = Panel(content, "Rest");
            var watch = Image(rest, "RestWatch", Sprite(Art + "stopwatch.png")); Place(watch.rectTransform, 35, 228, 320, 330); watch.color = new Color(1, 1, 1, .45f);
            var restPortrait = Raw(rest, "RestingCharacter", restTexture, 25, 268, 345, 272);
            var sleeps = new RectTransform[3];
            for (int i = 0; i < sleeps.Length; i++) { var z = Label(rest, "Sleep" + i, "Z", 44 - i * 11, 85 + i * 32, 176 + i * 35, 56, 65, Color.white, true); z.rectTransform.localRotation = Quaternion.Euler(0, 0, i % 2 == 0 ? -17 : 15); sleeps[i] = z.rectTransform; }
            var restTimer = Label(rest, "RestTimer", "00:45", 32, 120, 553, 150, 48, Color.white, true);
            var track = Image(rest, "ProgressTrack", Sprite(Sprites + "pill_24.png")); track.color = new Color32(27, 30, 46, 255); Place(track.rectTransform, 37, 613, 316, 33);
            var fill = Image(track.transform, "ProgressFill", Sprite(Sprites + "pill_24.png")); Stretch(fill.rectTransform); fill.color = new Color32(255, 207, 0, 255); fill.type = UnityEngine.UI.Image.Type.Filled; fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; fill.fillAmount = .5f;
            var steps = new Image[10]; var stepLabels = new TextMeshProUGUI[10];
            var dumbbell = Sprite(Sprites + "ModeSelection/training-icon.png");
            for (int i = 0; i < steps.Length; i++)
            {
                var circle = Image(track.transform, "Step" + i, Sprite(Sprites + "circle_128.png")); circle.color = new Color32(255, 209, 0, 255);
                circle.rectTransform.anchorMin = circle.rectTransform.anchorMax = new Vector2(i / 2f, .5f); circle.rectTransform.sizeDelta = new Vector2(39, 39);
                steps[i] = Image(circle.transform, "Icon", i == 0 ? Sprite(Art + "check.png") : dumbbell); Stretch(steps[i].rectTransform); steps[i].rectTransform.offsetMin = new Vector2(4, 4); steps[i].rectTransform.offsetMax = new Vector2(-4, -4);
                stepLabels[i] = Label(circle.transform, "Number", "", 11, 0, 36, 39, 18, Color.white, true); circle.gameObject.SetActive(i < 3);
            }
            var addRest = Button(rest, "AddRest", "+15 sec", false, 20, 719, 163, 66);
            var startSet = Button(rest, "StartSet", "START SET", true, 207, 719, 163, 66);
            var elapsed = Label(rest, "Elapsed", "01:25", 18, 135, 792, 120, 30, Color.white, true);
            var results = Panel(content, "Results");
            var resultAccent = Image(results, "ResultsAccent", Sprite(Sprites + "bolt_corner_top.png")); Place(resultAccent.rectTransform, 0, 0, 240, 325);
            Label(results, "Completed", "TRAINING IS COMPLETED!!!", 22, 23, 91, 350, 45, new Color32(255, 216, 0, 255)).fontStyle = FontStyles.Italic;
            Label(results, "Congrats", "Best training brooo!", 18, 23, 133, 330, 32, Color.white).fontStyle = FontStyles.Italic;
            var xpIcon = Image(results, "XpIcon", Sprite(Sprites + "exp_lightning.png")); Place(xpIcon.rectTransform, 23, 174, 28, 32);
            var xp = Label(results, "Xp", "+570", 21, 58, 176, 130, 33, Color.yellow);
            Label(results, "XpCaption", "XP EARNED", 11, 25, 211, 155, 24, new Color32(145, 166, 180, 255));
            var totalSets = Label(results, "TotalSets", "TOTAL REPS • 3 SETS", 13, 24, 300, 210, 28, new Color32(145, 166, 180, 255));
            var totalReps = Label(results, "TotalReps", "57", 61, 24, 321, 145, 87, Color.white);
            totalReps.fontSharedMaterial = counterMaterial;
            Label(results, "TotalTechniqueCaption", "TECHNIQUE", 13, 24, 415, 132, 24, new Color32(145, 166, 180, 255));
            var totalTechnique = Label(results, "TotalTechnique", "92%", 24, 24, 441, 110, 41, Color.green);
            var resultPortrait = Raw(results, "ResultCharacter", AssetDatabase.LoadAssetAtPath<Texture2D>(FightPresentationSceneBuilder.StandingPortraitPath), 169, 241, 208, 471);
            var more = Button(results, "MoreSet", "<color=#FFDB00>+1</color> more set", false, 20, 750, 163, 64);
            var home = Button(results, "Home", "HOME", true, 207, 750, 163, 64);
            var house = Rect(home.transform, "HomeIcon"); Place(house, 22, 17, 29, 29); house.gameObject.AddComponent<TrainingHomeGlyph>().raycastTarget = false;
            var homeLabel = home.GetComponentInChildren<TextMeshProUGUI>(); Place(homeLabel.rectTransform, 44, -3, 110, 64);
            var header = Panel(content, "Header");
            var exit = Button(header, "Exit", "X", false, 22, 73, 51, 43); exit.GetComponent<Image>().sprite = Sprite(Sprites + "btn_close.png");
            var modeButton = Button(header, "Mode", "PUSHUP", true, 137, 69, 116, 48); modeButton.enabled = false;
            var pause = Button(header, "Pause", "Ⅱ", false, 315, 73, 53, 43); pause.GetComponent<Image>().sprite = Sprite(Sprites + "btn_pause.png");
            var pauseCurtain = Panel(content, "Paused"); var shade = Image(pauseCurtain, "Shade", null); Stretch(shade.rectTransform); shade.color = new Color(0, 0, 0, .82f);
            Label(pauseCurtain, "PauseTitle", "PAUSED", 44, 30, 322, 330, 72, Color.white, true);
            var resume = Button(pauseCurtain, "Resume", "RESUME", true, 105, 424, 180, 63);
            pauseCurtain.gameObject.SetActive(false);
            var so = new SerializedObject(screen);
            Set(so, "_content", content);
            Set(so, "_loading", loading.gameObject); Set(so, "_exercise", exercise.gameObject); Set(so, "_rest", rest.gameObject); Set(so, "_results", results.gameObject); Set(so, "_header", header.gameObject); Set(so, "_pauseCurtain", pauseCurtain.gameObject);
            Set(so, "_mode", modeButton.GetComponentInChildren<TextMeshProUGUI>()); Set(so, "_pauseLabel", pause.GetComponentInChildren<TextMeshProUGUI>());
            Set(so, "_set", set); Set(so, "_reps", reps); Set(so, "_technique", technique); Set(so, "_timer", timer); Set(so, "_hint", hint); Set(so, "_restTimer", restTimer); Set(so, "_elapsed", elapsed);
            Set(so, "_totalReps", totalReps); Set(so, "_totalSets", totalSets); Set(so, "_totalTechnique", totalTechnique); Set(so, "_xp", xp);
            Set(so, "_exit", exit); Set(so, "_pause", pause); Set(so, "_next", next); Set(so, "_addRest", addRest); Set(so, "_startSet", startSet); Set(so, "_more", more); Set(so, "_home", home); Set(so, "_resume", resume);
            Set(so, "_livePortrait", live); Set(so, "_sourcePortrait", source); Set(so, "_pushupPreview", pushupTexture); Set(so, "_restPortrait", restPortrait.rectTransform);
            Set(so, "_femalePushup", femalePushup); Set(so, "_femaleRest", femaleRest); Set(so, "_femaleStanding", AssetDatabase.LoadAssetAtPath<Texture2D>(FightPresentationSceneBuilder.StandingFemalePortraitPath)); Set(so, "_resultPortrait", resultPortrait);
            Set(so, "_progressFill", fill); Set(so, "_check", Sprite(Art + "check.png")); Set(so, "_dumbbell", dumbbell);
            Array(so, "_steps", steps); Array(so, "_stepLabels", stepLabels); Array(so, "_bolts", bolts); Array(so, "_sleepLetters", sleeps); so.ApplyModifiedPropertiesWithoutUndo();
            loading.gameObject.SetActive(false); rest.gameObject.SetActive(false); results.gameObject.SetActive(false);
            FightPresentationSceneBuilder.PersistTextMaterials(canvas.gameObject);
            TrainingMeasurementLayout.Apply(scene);
        }
        private static RectTransform Panel(Transform parent, string name) { var rt = Rect(parent, name); Stretch(rt); return rt; }
        private static RectTransform Rect(Transform parent, string name) { var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false); rt.gameObject.layer = 5; return rt; }
        private static void Stretch(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }
        private static void Place(RectTransform rt, float x, float y, float w, float h) { rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); }
        private static Sprite Sprite(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        private static Image Image(Transform parent, string name, Sprite sprite) { var image = Rect(parent, name).gameObject.AddComponent<Image>(); image.sprite = sprite; image.raycastTarget = false; return image; }
        private static RawImage Raw(Transform parent, string name, Texture texture, float x, float y, float w, float h) { var raw = Rect(parent, name).gameObject.AddComponent<RawImage>(); raw.texture = texture; raw.raycastTarget = false; Place(raw.rectTransform, x, y, w, h); return raw; }
        private static TextMeshProUGUI Label(Transform parent, string name, string text, float size, float x, float y, float w, float h, Color color, bool center = false)
        { var label = Rect(parent, name).gameObject.AddComponent<TextMeshProUGUI>(); Place(label.rectTransform, x, y, w, h); label.font = _font; label.fontSharedMaterial = size < 19 ? _body : _outline; label.fontSize = size; label.text = text; label.color = color; label.raycastTarget = false; label.enableWordWrapping = false; label.alignment = center ? TextAlignmentOptions.Center : TextAlignmentOptions.Left; return label; }
        private static Button Button(Transform parent, string name, string text, bool yellow, float x, float y, float w, float h)
        { var image = Image(parent, name, Sprite(Art + (yellow ? "button-yellow.png" : "button-muted.png"))); Place(image.rectTransform, x, y, w, h); image.raycastTarget = true; var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.navigation = new Navigation { mode = Navigation.Mode.None }; Label(image.transform, name + "Text", text, 19, 0, -3, w, h, Color.white, true); return button; }
        private static void Set(SerializedObject so, string key, UnityEngine.Object value) => so.FindProperty(key).objectReferenceValue = value;
        private static void Array<T>(SerializedObject so, string key, T[] values) where T : UnityEngine.Object { var array = so.FindProperty(key); array.arraySize = values.Length; for (int i = 0; i < values.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
    }
}
