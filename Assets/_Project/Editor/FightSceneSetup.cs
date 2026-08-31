using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PushStars.CV;
using PushStars.Fight;
using PushStars.UI;

namespace PushStars.Editor
{
    /// <summary>
    /// Builds <c>Fight.unity</c> — the one screen every 60-second set runs on: the onboarding level
    /// test, a duel against the player's own ghost, and the scripted boss ladder. Which of the three
    /// it is comes from <c>FightRequest</c> at load time, so there is a single scene to build,
    /// verify and keep working on device.
    ///
    /// <para><b>The screen shows the character, not the camera.</b> The webcam still runs — it is
    /// what the pose detector reads — but nothing renders it. The player watches their own 3D body
    /// doing the reps: a stage camera renders it, alone on the Character layer, into a render
    /// texture that fills the screen under the HUD. This is the product's core promise (no live
    /// video, ever) and it is also what makes the screen legible from a plank: one big figure and
    /// two numbers instead of a shaky wide-angle view of a room.</para>
    ///
    /// <para>The IMGUI tuning HUD is still in the scene, disabled, behind a corner button — an
    /// on-device CV failure is otherwise invisible now that there is no camera feed to look at.</para>
    ///
    /// Menu: Tools ▸ Push Stars ▸ Build Fight Screen. CI: <see cref="BuildScript.PrepareForUBA"/>
    /// regenerates the scene fresh before every cloud build.
    /// </summary>
    public static class FightSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Fight.unity";
        const string CharacterLayer = "Character";

        [MenuItem("Tools/Push Stars/Build Fight Screen", priority = 21)]
        public static void Build()
        {
            bool mediapipe = BuildFightScene();
            EditorUtility.DisplayDialog("Push Stars — Fight Screen",
                "Fight.unity built and wired.\n\n" +
                (mediapipe
                    ? "Pose source: MediaPipePoseSource (plugin compiled)."
                    : "Pose source: MockPoseSource (MediaPipe define off) — Play simulates ~40rpm pushups.") +
                "\n\nThe scene was added to Build Settings.",
                "OK");
        }

        /// <summary>Regenerates the scene from scratch and saves it. Returns true when the REAL
        /// MediaPipe pose source was wired (CI requires it; the mock is an editor convenience).</summary>
        public static bool BuildFightScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int charLayer = UiBuilder.EnsureLayer(CharacterLayer);

            var displayCam = UiBuilder.ClearCamera();
            displayCam.cullingMask = 0; // everything on screen is UI; the body arrives via the RT
            UiBuilder.EventSystem();

            // ── CV stack ─────────────────────────────────────────────────────────────────────────
            var cvGO = new GameObject("CV");
            MonoBehaviour pose = CreatePoseSource(cvGO, out bool usedMediaPipe);
            var session = cvGO.AddComponent<PushupSession>();
            var sessionSO = new SerializedObject(session);
            UiBuilder.Set(sessionSO, "_poseSourceBehaviour", pose);
            sessionSO.FindProperty("_logReps").boolValue = true;
            sessionSO.ApplyModifiedPropertiesWithoutUndo();

            // The full tuning HUD from testCV, disabled. With no camera feed on screen, a dead
            // model or a failing armer has no other tell — the corner button brings STATUS,
            // POSE fps and the armer reject reason back over the fight UI in one tap.
            var debugHud = cvGO.AddComponent<PushupDebugHud>();
            var dbgSO = new SerializedObject(debugHud);
            UiBuilder.Set(dbgSO, "_session", session);
            // The fight HUD already beeps/buzzes — mute the debug copy so sounds don't double.
            dbgSO.FindProperty("_repSound").boolValue = false;
            dbgSO.FindProperty("_bottomTickSound").boolValue = false;
            dbgSO.FindProperty("_rejectBuzzSound").boolValue = false;
            dbgSO.ApplyModifiedPropertiesWithoutUndo();
            debugHud.enabled = false;

            // The camera preview with the detected skeleton drawn over it. Without a feed on
            // screen there is no way to tell "the phone is pointed at the ceiling" from "the model
            // cannot see you" — both read as TRACK: Lost. Its live controls stay on so the frame
            // rotation can be corrected on the device instead of through another build.
            var preview = CreateCameraPreview(cvGO, pose);

            // ── 3D stage: the character, alone on its own layer, rendered into a texture ─────────
            var stage = BuildAvatarStage(charLayer, out var stageCamera, out var avatarRoot);

            // ── Canvas ───────────────────────────────────────────────────────────────────────────
            UiBuilder.Canvas("FightCanvas", out var canvasRoot);
            canvasRoot.gameObject.AddComponent<ThemeInitializer>();

            var baseBg = UiBuilder.Image(canvasRoot, "BaseBackground", AppColors.BgDark);
            UiBuilder.Stretch(baseBg.rectTransform);

            var floorGlow = LoadSprite("Assets/_Project/UI/Sprites/glow_radial.png");
            if (floorGlow != null)
            {
                var glow = UiBuilder.Image(canvasRoot, "FloorGlow", new Color(0.28f, 0.36f, 0.9f, 0.30f));
                glow.sprite = floorGlow;
                UiBuilder.Place(glow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(760f, 760f));
            }

            // The character's render texture, full screen, under the HUD.
            var avatarImageGO = new GameObject("AvatarImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            avatarImageGO.transform.SetParent(canvasRoot, false);
            var avatarImage = avatarImageGO.GetComponent<RawImage>();
            avatarImage.raycastTarget = false;
            avatarImage.color = new Color(1f, 1f, 1f, 0.06f); // faint in edit mode; CharacterStage sets white on Play
            UiBuilder.Stretch(avatarImage.rectTransform);

            var stageSO = new SerializedObject(stage);
            UiBuilder.Set(stageSO, "_stageCamera", stageCamera);
            UiBuilder.Set(stageSO, "_avatarRoot", avatarRoot);
            UiBuilder.Set(stageSO, "_targetImage", avatarImage);
            stageSO.FindProperty("_width").intValue = 1080;
            stageSO.FindProperty("_height").intValue = 1920;
            stageSO.ApplyModifiedPropertiesWithoutUndo();

            // ── Safe area + HUD ──────────────────────────────────────────────────────────────────
            var safe = UiBuilder.Rect(canvasRoot, "SafeArea");
            UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var hudRefs = BuildHud(safe, out var exitBtn, out var exitLabel, out var debugBtn);

            // ── Result overlay (hidden until the set ends) ───────────────────────────────────────
            var resultRefs = BuildResultOverlay(canvasRoot);

            // ── Behaviours + wiring ──────────────────────────────────────────────────────────────
            var hud = canvasRoot.gameObject.AddComponent<FightHud>();
            var hudSO = new SerializedObject(hud);
            UiBuilder.Set(hudSO, "_playerReps", hudRefs.PlayerReps);
            UiBuilder.Set(hudSO, "_form", hudRefs.Form);
            UiBuilder.Set(hudSO, "_timer", hudRefs.Timer);
            UiBuilder.Set(hudSO, "_bossName", hudRefs.OpponentName);
            UiBuilder.Set(hudSO, "_bossReps", hudRefs.OpponentReps);
            UiBuilder.Set(hudSO, "_bannerRoot", hudRefs.BannerRoot);
            UiBuilder.Set(hudSO, "_bannerText", hudRefs.BannerText);
            UiBuilder.Set(hudSO, "_countdown", hudRefs.Countdown);
            UiBuilder.Set(hudSO, "_session", session);
            hudSO.ApplyModifiedPropertiesWithoutUndo();

            var result = canvasRoot.gameObject.AddComponent<FightResultScreen>();
            var resultSO = new SerializedObject(result);
            UiBuilder.Set(resultSO, "_root", resultRefs.Root);
            UiBuilder.Set(resultSO, "_title", resultRefs.Title);
            UiBuilder.Set(resultSO, "_subtitle", resultRefs.Subtitle);
            UiBuilder.Set(resultSO, "_score", resultRefs.Score);
            UiBuilder.Set(resultSO, "_rewards", resultRefs.Rewards);
            UiBuilder.Set(resultSO, "_note", resultRefs.Note);
            UiBuilder.Set(resultSO, "_continueButton", resultRefs.Continue);
            UiBuilder.Set(resultSO, "_continueLabel", resultRefs.ContinueLabel);
            UiBuilder.Set(resultSO, "_secondaryButton", resultRefs.Secondary);
            UiBuilder.Set(resultSO, "_secondaryLabel", resultRefs.SecondaryLabel);
            resultSO.ApplyModifiedPropertiesWithoutUndo();

            // ── Opponents: both feeds live in the scene, the controller picks one ────────────────
            var opponentGO = new GameObject("Opponents");
            var boss = opponentGO.AddComponent<BossOpponent>();
            var ghost = opponentGO.AddComponent<GhostOpponent>();

            // ── Avatar driver + body ────────────────────────────────────────────────────────────
            var driver = cvGO.AddComponent<PushupAvatarDriver>();
            var driverSO = new SerializedObject(driver);
            UiBuilder.Set(driverSO, "_session", session);
            driverSO.ApplyModifiedPropertiesWithoutUndo(); // the Animator arrives at runtime

            var avatar = cvGO.AddComponent<FightAvatar>();
            var avatarSO = new SerializedObject(avatar);
            UiBuilder.Set(avatarSO, "_session", session);
            UiBuilder.Set(avatarSO, "_driver", driver);
            UiBuilder.Set(avatarSO, "_stageCamera", stageCamera);
            UiBuilder.Set(avatarSO, "_avatarRoot", avatarRoot);
            UiBuilder.Set(avatarSO, "_malePrefab", MainCharacterSetup.LoadCharacterPrefab(CharacterGender.Male));
            UiBuilder.Set(avatarSO, "_femalePrefab", MainCharacterSetup.LoadCharacterPrefab(CharacterGender.Female));
            UiBuilder.Set(avatarSO, "_fightController", AvatarOverlayTestSetup.EnsurePushupController());
            avatarSO.ApplyModifiedPropertiesWithoutUndo();

            var controllerGO = new GameObject("FightController");
            var controller = controllerGO.AddComponent<FightController>();
            var ctrlSO = new SerializedObject(controller);
            UiBuilder.Set(ctrlSO, "_session", session);
            UiBuilder.Set(ctrlSO, "_boss", boss);
            UiBuilder.Set(ctrlSO, "_ghost", ghost);
            UiBuilder.Set(ctrlSO, "_hud", hud);
            UiBuilder.Set(ctrlSO, "_result", result);
            UiBuilder.Set(ctrlSO, "_exitButton", exitBtn);
            UiBuilder.Set(ctrlSO, "_exitLabel", exitLabel);
            var panels = preview != null
                ? new Behaviour[] { debugHud, preview }
                : new Behaviour[] { debugHud };
            UiBuilder.SetArray(ctrlSO, "_debugPanels", panels);
            UiBuilder.Set(ctrlSO, "_debugButton", debugBtn);
            ctrlSO.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            UiBuilder.EnsureSceneInBuildSettings(ScenePath);
            Debug.Log($"[FightSceneSetup] Fight.unity built (pose source: {(usedMediaPipe ? "MediaPipe" : "Mock")}).");
            return usedMediaPipe;
        }

        // ── 3D stage ────────────────────────────────────────────────────────────────────────────

        /// <summary>Camera + lights on the character layer. The body itself is instantiated at
        /// runtime by <see cref="FightAvatar"/> (the player's saved gender decides which one), and
        /// the camera is repositioned every frame to frame it — nothing here is authored for a
        /// specific pose.</summary>
        static CharacterStage BuildAvatarStage(int charLayer, out Camera stageCamera, out Transform avatarRoot)
        {
            var stageGO = new GameObject("AvatarStage3D");
            var stage = stageGO.AddComponent<CharacterStage>();

            avatarRoot = new GameObject("AvatarRoot").transform;
            avatarRoot.SetParent(stageGO.transform, false);
            avatarRoot.gameObject.layer = charLayer;
            // No 180° flip here (unlike the menu stage): FightAvatar puts its camera on +Z, which
            // is the side a Unity character already faces.

            var camGO = new GameObject("StageCamera");
            camGO.transform.SetParent(stageGO.transform, false);
            camGO.transform.localPosition = new Vector3(1.0f, 1.2f, 2.9f);
            camGO.transform.LookAt(stageGO.transform.position + new Vector3(0f, 0.8f, 0f));
            stageCamera = camGO.AddComponent<Camera>();
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent: the UI shows through
            stageCamera.fieldOfView = 40f;
            stageCamera.nearClipPlane = 0.05f;
            stageCamera.farClipPlane = 40f;
            stageCamera.cullingMask = 1 << charLayer;
            stageCamera.useOcclusionCulling = false;
            stageCamera.allowMSAA = true;

            var keyGO = new GameObject("KeyLight");
            keyGO.transform.SetParent(stageGO.transform, false);
            keyGO.transform.rotation = Quaternion.Euler(38f, 200f, 0f);
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1f, 0.97f, 0.9f);
            key.cullingMask = 1 << charLayer;

            var fillGO = new GameObject("FillLight");
            fillGO.transform.SetParent(stageGO.transform, false);
            fillGO.transform.rotation = Quaternion.Euler(12f, 140f, 0f);
            var fill = fillGO.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.5f;
            fill.color = new Color(0.8f, 0.86f, 1f);
            fill.cullingMask = 1 << charLayer;

            return stage;
        }

        // ── HUD ─────────────────────────────────────────────────────────────────────────────────

        struct HudRefs
        {
            public TextMeshProUGUI PlayerReps, Form, Timer, OpponentName, OpponentReps, BannerText, Countdown;
            public GameObject BannerRoot;
        }

        static HudRefs BuildHud(RectTransform safe, out Button exitBtn, out TextMeshProUGUI exitLabel,
                                out Button debugBtn)
        {
            var refs = new HudRefs();

            // Score row (top): [ТЫ + reps + form]  [timer]  [opponent + reps]
            var scoreRow = UiBuilder.Rect(safe, "ScoreRow");
            UiBuilder.Anchor(scoreRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            scoreRow.anchoredPosition = new Vector2(0f, -8f);
            scoreRow.sizeDelta = new Vector2(-24f, 110f);

            var youLabel = UiBuilder.Text(scoreRow, "YouLabel", AppColors.TextSecondary, "ТЫ", 14,
                                          FontStyles.Bold, TextAlignmentOptions.Left);
            UiBuilder.Place(youLabel.rectTransform, new Vector2(0f, 1f), new Vector2(8f, 0f), new Vector2(120f, 20f));

            refs.PlayerReps = UiBuilder.Text(scoreRow, "PlayerReps", AppColors.AccentYellow, "0", 60,
                                             FontStyles.Bold, TextAlignmentOptions.Left);
            UiBuilder.Place(refs.PlayerReps.rectTransform, new Vector2(0f, 1f), new Vector2(8f, -20f), new Vector2(140f, 64f));

            refs.Form = UiBuilder.Text(scoreRow, "Form", AppColors.TextSecondary, "FORM —", 13,
                                       FontStyles.Bold, TextAlignmentOptions.Left);
            UiBuilder.Place(refs.Form.rectTransform, new Vector2(0f, 1f), new Vector2(8f, -86f), new Vector2(140f, 18f));

            refs.Timer = UiBuilder.Text(scoreRow, "Timer", AppColors.TextPrimary, "1:00", 30, FontStyles.Bold);
            UiBuilder.Place(refs.Timer.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(120f, 36f));

            refs.OpponentName = UiBuilder.Text(scoreRow, "OpponentName", AppColors.TextSecondary, "СОПЕРНИК", 14,
                                               FontStyles.Bold, TextAlignmentOptions.Right);
            UiBuilder.Place(refs.OpponentName.rectTransform, new Vector2(1f, 1f), new Vector2(-8f, 0f), new Vector2(180f, 20f));

            refs.OpponentReps = UiBuilder.Text(scoreRow, "OpponentReps", AppColors.TextPrimary, "0", 60,
                                               FontStyles.Bold, TextAlignmentOptions.Right);
            UiBuilder.Place(refs.OpponentReps.rectTransform, new Vector2(1f, 1f), new Vector2(-8f, -20f), new Vector2(140f, 64f));

            // Guidance banner (lower third, same slot the debug HUD proved on device).
            var bannerRoot = UiBuilder.Rect(safe, "Banner");
            UiBuilder.Place(bannerRoot, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(360f, 84f));
            var bannerBg = UiBuilder.Image(bannerRoot, "Bg", new Color(0f, 0f, 0f, 0.62f));
            UiBuilder.Stretch(bannerBg.rectTransform);
            refs.BannerText = UiBuilder.Text(bannerRoot, "Text", new Color(1f, 0.75f, 0.2f),
                                             "ВСТАНЬ В ПЛАНКУ ПЕРЕД КАМЕРОЙ", 20, FontStyles.Bold);
            UiBuilder.Stretch(refs.BannerText.rectTransform, 10, 8, 10, 8);
            refs.BannerRoot = bannerRoot.gameObject;

            // Countdown (center, hidden until armed).
            refs.Countdown = UiBuilder.Text(safe, "Countdown", AppColors.AccentYellow, "3", 110, FontStyles.Bold);
            UiBuilder.Place(refs.Countdown.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(380f, 130f));
            refs.Countdown.gameObject.SetActive(false);

            // Small ВЫЙТИ (top centre, below the score row — leaves both rep corners clean).
            exitBtn = UiBuilder.Button(safe, "ExitFight", "ВЫЙТИ", new Color(0f, 0f, 0f, 0.45f),
                                       AppColors.TextSecondary, 14, out exitLabel);
            UiBuilder.Place((RectTransform)exitBtn.transform, new Vector2(0.5f, 1f), new Vector2(0f, -122f), new Vector2(96f, 34f));

            // Diagnostics toggle: deliberately small and unlabelled-looking, bottom-left.
            debugBtn = UiBuilder.Button(safe, "DebugToggle", "•••", new Color(1f, 1f, 1f, 0.06f),
                                        new Color(1f, 1f, 1f, 0.35f), 16, out _);
            UiBuilder.Place((RectTransform)debugBtn.transform, new Vector2(0f, 0f), new Vector2(14f, 14f), new Vector2(46f, 34f));

            return refs;
        }

        // ── Result overlay ──────────────────────────────────────────────────────────────────────

        struct ResultRefs
        {
            public GameObject Root;
            public TextMeshProUGUI Title, Subtitle, Score, Rewards, Note, ContinueLabel, SecondaryLabel;
            public Button Continue, Secondary;
        }

        static ResultRefs BuildResultOverlay(RectTransform canvasRoot)
        {
            var refs = new ResultRefs();

            var root = UiBuilder.Rect(canvasRoot, "ResultOverlay");
            UiBuilder.Stretch(root);
            var bg = UiBuilder.Image(root, "Bg", AppColors.BgDark);
            UiBuilder.Stretch(bg.rectTransform);
            bg.raycastTarget = true; // swallow taps under the overlay

            var safe = UiBuilder.Rect(root, "SafeArea");
            UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            refs.Title = UiBuilder.Text(safe, "Title", AppColors.TextSecondary, "ТВОЙ УРОВЕНЬ", 22, FontStyles.Bold);
            UiBuilder.PlaceWide(refs.Title.rectTransform, 0.5f, 232f, 30f);

            refs.Subtitle = UiBuilder.Text(safe, "Subtitle", AppColors.AccentYellow, "АТЛЕТ", 52, FontStyles.Bold);
            UiBuilder.PlaceWide(refs.Subtitle.rectTransform, 0.5f, 176f, 66f);

            refs.Score = UiBuilder.Text(safe, "Score", AppColors.TextPrimary, "0 отжиманий за 60 секунд", 18, FontStyles.Bold);
            UiBuilder.PlaceWide(refs.Score.rectTransform, 0.5f, 122f, 28f);

            refs.Rewards = UiBuilder.Text(safe, "Rewards", AppColors.AccentLime, "+0 XP", 26, FontStyles.Bold);
            UiBuilder.PlaceWide(refs.Rewards.rectTransform, 0.5f, 70f, 34f);

            refs.Note = UiBuilder.Text(safe, "Note", new Color(1f, 1f, 1f, 0.55f), "", 15, FontStyles.Normal);
            UiBuilder.PlaceWide(refs.Note.rectTransform, 0.5f, -10f, 80f, 34f);

            refs.Continue = UiBuilder.Button(safe, "Continue", "ПРОДОЛЖИТЬ",
                                             AppColors.BtnPrimaryBg, AppColors.BtnPrimaryFg, 20, out refs.ContinueLabel);
            UiBuilder.PlaceWide((RectTransform)refs.Continue.transform, 0f, 150f, 60f, 34f);

            refs.Secondary = UiBuilder.Button(safe, "Secondary", "ПРОПУСТИТЬ",
                                              new Color(1f, 1f, 1f, 0.06f), AppColors.TextSecondary, 16,
                                              out refs.SecondaryLabel);
            UiBuilder.PlaceWide((RectTransform)refs.Secondary.transform, 0f, 84f, 48f, 34f);
            refs.Secondary.gameObject.SetActive(false);

            root.gameObject.SetActive(false);
            refs.Root = root.gameObject;
            return refs;
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────

        /// <summary>MediaPipe adapter when the define/plugin compiled it, else the editor mock.
        /// Resolved by reflection so this file compiles either way (same trick as CvTestSceneSetup).</summary>
        static MonoBehaviour CreatePoseSource(GameObject go, out bool usedMediaPipe)
        {
            var poseType = Type.GetType("PushStars.CV.MediaPipePoseSource, PushStars.CV.MediaPipe");
            if (poseType != null)
            {
                usedMediaPipe = true;
                return (MonoBehaviour)go.AddComponent(poseType);
            }
            usedMediaPipe = false;
            return go.AddComponent<MockPoseSource>();
        }

        /// <summary>Adds <c>WebCamPreview</c> when the MediaPipe assembly compiled it. Resolved by
        /// reflection for the same reason the pose source is: this file has to build with the
        /// plugin absent.</summary>
        static Behaviour CreateCameraPreview(GameObject go, MonoBehaviour source)
        {
            var type = Type.GetType("PushStars.CV.WebCamPreview, PushStars.CV.MediaPipe");
            if (type == null) return null;

            var preview = (Behaviour)go.AddComponent(type);
            var so = new SerializedObject(preview);
            so.FindProperty("_source").objectReferenceValue = source;
            so.FindProperty("_showSkeleton").boolValue = true;
            so.FindProperty("_showControls").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            preview.enabled = false;
            return preview;
        }

        static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }
}
