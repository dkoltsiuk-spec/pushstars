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
    /// <para><b>The screen is split between the two fighters</b> — opponent above in red, player
    /// below in blue — each half showing that fighter's own body, rep count, FORM and tempo, with
    /// the clock on the seam. A level test hides the upper half rather than parking a zero there.</para>
    ///
    /// <para><b>Bodies, not camera.</b> The webcam still runs — it is what the pose detector reads —
    /// but nothing renders it. Each half is a stage camera filming a character alone on its own
    /// layer into a render texture. That is the product's promise (no live video, ever) and it is
    /// what makes the screen readable from a plank: two figures and two numbers instead of a shaky
    /// wide-angle view of a room.</para>
    ///
    /// <para>The IMGUI tuning HUD and the camera preview are still in the scene, disabled, behind a
    /// corner button — an on-device CV failure is otherwise invisible with no feed to look at.</para>
    ///
    /// Menu: Tools ▸ Push Stars ▸ Build Fight Screen. CI: <see cref="BuildScript.PrepareForUBA"/>
    /// regenerates the scene fresh before every cloud build.
    /// </summary>
    public static class FightSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Fight.unity";
        const string PlayerLayer = "Character";
        const string GhostLayer = "CharacterGhost";
        const string GlowSprite = "Assets/_Project/UI/Sprites/glow_radial.png";
        const string PillSprite = "Assets/_Project/UI/Sprites/pill_capsule.png";
        const string CupSprite = "Assets/_Project/UI/Sprites/cup_.png";
        const string VsCoinSprite = "Assets/_Project/UI/Sprites/VS_for_serching.png";
        /// <summary>The arena backdrop: pink-red top, periwinkle bottom, a jagged seam baked in at
        /// roughly the same height as <see cref="SeamY"/>. Sized 796×1716 — a near-exact match for
        /// this project's 390×844 reference canvas — so it is meant to be shown at native aspect,
        /// full-bleed, not tiled or cropped.</summary>
        const string FightBgSprite = "Assets/_Project/UI/Sprites/bg_fight.png";
        /// <summary>The jagged ribbon banner the verdict sits on. 811×296 (aspect 2.74) — placed at
        /// that same aspect so its torn edges render as drawn, not stretched.</summary>
        const string BannerWinSprite = "Assets/_Project/UI/Sprites/bg_winner.png";

        /// <summary>Fraction of the screen the opponent's half occupies. Slightly under half: the
        /// player's own body is the one they are steering, and it gets the larger stage.</summary>
        const float SeamY = 0.48f;

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

            // Two layers, not one: each stage camera must see its own body and only its own.
            int playerLayer = UiBuilder.EnsureLayer(PlayerLayer);
            int ghostLayer = UiBuilder.EnsureLayer(GhostLayer);

            var displayCam = UiBuilder.ClearCamera();
            displayCam.cullingMask = 0; // everything on screen is UI; the bodies arrive via RTs
            UiBuilder.EventSystem();

            // ── CV stack ─────────────────────────────────────────────────────────────────────────
            var cvGO = new GameObject("CV");
            MonoBehaviour pose = CreatePoseSource(cvGO, out bool usedMediaPipe);
            var session = cvGO.AddComponent<PushupSession>();
            var sessionSO = new SerializedObject(session);
            UiBuilder.Set(sessionSO, "_poseSourceBehaviour", pose);
            sessionSO.FindProperty("_logReps").boolValue = true;
            sessionSO.ApplyModifiedPropertiesWithoutUndo();

            var debugHud = cvGO.AddComponent<PushupDebugHud>();
            var dbgSO = new SerializedObject(debugHud);
            UiBuilder.Set(dbgSO, "_session", session);
            // The fight HUD already beeps/buzzes — mute the debug copy so sounds don't double.
            dbgSO.FindProperty("_repSound").boolValue = false;
            dbgSO.FindProperty("_bottomTickSound").boolValue = false;
            dbgSO.FindProperty("_rejectBuzzSound").boolValue = false;
            dbgSO.ApplyModifiedPropertiesWithoutUndo();
            debugHud.enabled = false;

            var preview = CreateCameraPreview(cvGO, pose);

            // ── Opponents ────────────────────────────────────────────────────────────────────────
            var opponentGO = new GameObject("Opponents");
            var boss = opponentGO.AddComponent<BossOpponent>();
            var ghost = opponentGO.AddComponent<GhostOpponent>();

            // ── 3D stages: one body per half, each alone on its layer ────────────────────────────
            var playerStage = BuildAvatarStage("PlayerStage3D", playerLayer, Vector3.zero,
                                               out var playerCam, out var playerRoot);
            var ghostStage = BuildAvatarStage("GhostStage3D", ghostLayer, new Vector3(0f, 0f, 200f),
                                              out var ghostCam, out var ghostRoot);

            // ── Canvas ───────────────────────────────────────────────────────────────────────────
            UiBuilder.Canvas("FightCanvas", out var canvasRoot);
            canvasRoot.gameObject.AddComponent<ThemeInitializer>();

            // Real art now, not the flat ArenaRed/ArenaBlue rectangles + a bare tilted bar that
            // used to stand in for an arena backdrop. AppColors.BgDark is only the fallback for a
            // checkout where the sprite hasn't been imported yet — the scene must never draw
            // literally nothing.
            var baseBg = UiBuilder.Image(canvasRoot, "BaseBackground", AppColors.BgDark);
            UiBuilder.Stretch(baseBg.rectTransform);
            var fightBgSprite = LoadSprite(FightBgSprite);
            if (fightBgSprite != null) { baseBg.sprite = fightBgSprite; baseBg.color = Color.white; }

            var opponentHalf = BuildHalf(canvasRoot, "OpponentHalf", SeamY, 1f, out var opponentAvatarImage);
            var playerHalf = BuildHalf(canvasRoot, "PlayerHalf", 0f, SeamY, out var playerAvatarImage);

            WireStage(playerStage, playerCam, playerRoot, playerAvatarImage);
            WireStage(ghostStage, ghostCam, ghostRoot, opponentAvatarImage);

            // ── Safe area + HUD ──────────────────────────────────────────────────────────────────
            var safe = UiBuilder.Rect(canvasRoot, "SafeArea");
            UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var hudRefs = BuildHud(safe, out var exitBtn, out var exitLabel, out var debugBtn);

            // ── Overlays ─────────────────────────────────────────────────────────────────────────
            var readyRefs = BuildReadyOverlay(canvasRoot);
            var resultRefs = BuildResultOverlay(canvasRoot);

            // ── Behaviours + wiring ──────────────────────────────────────────────────────────────
            var hud = canvasRoot.gameObject.AddComponent<FightHud>();
            var hudSO = new SerializedObject(hud);
            UiBuilder.Set(hudSO, "_opponentPanel", hudRefs.OpponentPanel);
            UiBuilder.Set(hudSO, "_opponentName", hudRefs.OpponentName);
            UiBuilder.Set(hudSO, "_opponentReps", hudRefs.OpponentReps);
            UiBuilder.Set(hudSO, "_opponentForm", hudRefs.OpponentForm);
            UiBuilder.Set(hudSO, "_opponentTempo", hudRefs.OpponentTempo);
            UiBuilder.Set(hudSO, "_playerPanel", hudRefs.PlayerPanel);
            UiBuilder.Set(hudSO, "_playerName", hudRefs.PlayerName);
            UiBuilder.Set(hudSO, "_playerReps", hudRefs.PlayerReps);
            UiBuilder.Set(hudSO, "_playerForm", hudRefs.PlayerForm);
            UiBuilder.Set(hudSO, "_playerTempo", hudRefs.PlayerTempo);
            UiBuilder.Set(hudSO, "_timer", hudRefs.Timer);
            UiBuilder.Set(hudSO, "_bannerRoot", hudRefs.BannerRoot);
            UiBuilder.Set(hudSO, "_bannerText", hudRefs.BannerText);
            UiBuilder.Set(hudSO, "_countdown", hudRefs.Countdown);
            UiBuilder.Set(hudSO, "_session", session);
            hudSO.ApplyModifiedPropertiesWithoutUndo();

            var readyPanel = canvasRoot.gameObject.AddComponent<DuelReadyPanel>();
            var readySO = new SerializedObject(readyPanel);
            UiBuilder.Set(readySO, "_root", readyRefs.Root);
            UiBuilder.Set(readySO, "_opponentName", readyRefs.OpponentName);
            UiBuilder.Set(readySO, "_opponentTrophies", readyRefs.OpponentTrophies);
            UiBuilder.Set(readySO, "_opponentBest", readyRefs.OpponentBest);
            UiBuilder.Set(readySO, "_opponentWinRate", readyRefs.OpponentWinRate);
            // The card's own crops mirror the exact bodies already rendering behind it — one stage
            // each, shown twice, rather than a second camera per fighter to keep in sync.
            UiBuilder.Set(readySO, "_opponentAvatarImage", readyRefs.OpponentAvatarImage);
            UiBuilder.Set(readySO, "_opponentAvatarSource", opponentAvatarImage);
            UiBuilder.Set(readySO, "_playerName", readyRefs.PlayerName);
            UiBuilder.Set(readySO, "_playerTrophies", readyRefs.PlayerTrophies);
            UiBuilder.Set(readySO, "_playerBest", readyRefs.PlayerBest);
            UiBuilder.Set(readySO, "_playerWinRate", readyRefs.PlayerWinRate);
            UiBuilder.Set(readySO, "_playerAvatarImage", readyRefs.PlayerAvatarImage);
            UiBuilder.Set(readySO, "_playerAvatarSource", playerAvatarImage);
            UiBuilder.Set(readySO, "_readyButton", readyRefs.ReadyButton);
            readySO.ApplyModifiedPropertiesWithoutUndo();

            var result = canvasRoot.gameObject.AddComponent<FightResultScreen>();
            var resultSO = new SerializedObject(result);
            UiBuilder.Set(resultSO, "_root", resultRefs.Root);
            UiBuilder.Set(resultSO, "_duelLayout", resultRefs.DuelLayout);
            UiBuilder.Set(resultSO, "_banner", resultRefs.Banner);
            UiBuilder.Set(resultSO, "_opponentName", resultRefs.OpponentName);
            UiBuilder.Set(resultSO, "_opponentReps", resultRefs.OpponentReps);
            UiBuilder.Set(resultSO, "_opponentForm", resultRefs.OpponentForm);
            UiBuilder.Set(resultSO, "_opponentTempo", resultRefs.OpponentTempo);
            // Same crops-of-the-live-stage trick as the ready card, pointed at the same two source
            // images the duel HUD renders full-size.
            UiBuilder.Set(resultSO, "_opponentAvatarImage", resultRefs.OpponentAvatarImage);
            UiBuilder.Set(resultSO, "_opponentAvatarSource", opponentAvatarImage);
            UiBuilder.Set(resultSO, "_playerName", resultRefs.PlayerName);
            UiBuilder.Set(resultSO, "_playerReps", resultRefs.PlayerReps);
            UiBuilder.Set(resultSO, "_playerForm", resultRefs.PlayerForm);
            UiBuilder.Set(resultSO, "_playerTempo", resultRefs.PlayerTempo);
            UiBuilder.Set(resultSO, "_playerAvatarImage", resultRefs.PlayerAvatarImage);
            UiBuilder.Set(resultSO, "_playerAvatarSource", playerAvatarImage);
            UiBuilder.Set(resultSO, "_duelRewards", resultRefs.DuelRewards);
            UiBuilder.Set(resultSO, "_duelNote", resultRefs.DuelNote);
            UiBuilder.Set(resultSO, "_levelTestLayout", resultRefs.TestLayout);
            UiBuilder.Set(resultSO, "_testTitle", resultRefs.TestTitle);
            UiBuilder.Set(resultSO, "_testTier", resultRefs.TestTier);
            UiBuilder.Set(resultSO, "_testScore", resultRefs.TestScore);
            UiBuilder.Set(resultSO, "_testRewards", resultRefs.TestRewards);
            UiBuilder.Set(resultSO, "_testNote", resultRefs.TestNote);
            UiBuilder.Set(resultSO, "_continueButton", resultRefs.Continue);
            UiBuilder.Set(resultSO, "_continueLabel", resultRefs.ContinueLabel);
            UiBuilder.Set(resultSO, "_secondaryButton", resultRefs.Secondary);
            UiBuilder.Set(resultSO, "_secondaryLabel", resultRefs.SecondaryLabel);
            resultSO.ApplyModifiedPropertiesWithoutUndo();

            // ── Avatar drivers + bodies ─────────────────────────────────────────────────────────
            var playerDriver = cvGO.AddComponent<PushupAvatarDriver>();
            var playerDriverSO = new SerializedObject(playerDriver);
            UiBuilder.Set(playerDriverSO, "_session", session);
            playerDriverSO.ApplyModifiedPropertiesWithoutUndo(); // the Animator arrives at runtime

            var ghostDriver = opponentGO.AddComponent<GhostAvatarDriver>();
            var ghostDriverSO = new SerializedObject(ghostDriver);
            UiBuilder.Set(ghostDriverSO, "_ghost", ghost);
            ghostDriverSO.ApplyModifiedPropertiesWithoutUndo();

            var fightController = AvatarOverlayTestSetup.EnsurePushupController();
            var male = MainCharacterSetup.LoadCharacterPrefab(CharacterGender.Male);
            var female = MainCharacterSetup.LoadCharacterPrefab(CharacterGender.Female);

            AddAvatar(cvGO, playerDriver, playerCam, playerRoot, male, female, fightController, shadow: false);
            AddAvatar(opponentGO, ghostDriver, ghostCam, ghostRoot, male, female, fightController, shadow: true);

            var controllerGO = new GameObject("FightController");
            var controller = controllerGO.AddComponent<FightController>();
            var ctrlSO = new SerializedObject(controller);
            UiBuilder.Set(ctrlSO, "_session", session);
            UiBuilder.Set(ctrlSO, "_boss", boss);
            UiBuilder.Set(ctrlSO, "_ghost", ghost);
            UiBuilder.Set(ctrlSO, "_hud", hud);
            UiBuilder.Set(ctrlSO, "_result", result);
            UiBuilder.Set(ctrlSO, "_readyPanel", readyPanel);
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

        // ── 3D stages ───────────────────────────────────────────────────────────────────────────

        /// <summary>Camera + lights for one body, on its own layer and parked at its own spot. The
        /// body itself is instantiated at runtime by <see cref="FightAvatar"/> (which body depends
        /// on a saved preference) and the camera is repositioned every frame to frame it — nothing
        /// here is authored for a specific pose.</summary>
        static CharacterStage BuildAvatarStage(string name, int layer, Vector3 origin,
                                               out Camera stageCamera, out Transform avatarRoot)
        {
            var stageGO = new GameObject(name);
            stageGO.transform.position = origin;
            var stage = stageGO.AddComponent<CharacterStage>();

            avatarRoot = new GameObject("AvatarRoot").transform;
            avatarRoot.SetParent(stageGO.transform, false);
            avatarRoot.gameObject.layer = layer;
            // No 180° flip (unlike the menu stage): FightAvatar puts its camera on +Z, which is the
            // side a Unity character already faces.

            var camGO = new GameObject("StageCamera");
            camGO.transform.SetParent(stageGO.transform, false);
            camGO.transform.localPosition = new Vector3(1.0f, 1.2f, 2.9f);
            camGO.transform.LookAt(origin + new Vector3(0f, 0.8f, 0f));
            stageCamera = camGO.AddComponent<Camera>();
            stageCamera.clearFlags = CameraClearFlags.SolidColor;
            stageCamera.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent: the half shows through
            stageCamera.fieldOfView = 40f;
            stageCamera.nearClipPlane = 0.05f;
            stageCamera.farClipPlane = 40f;
            stageCamera.cullingMask = 1 << layer;
            stageCamera.useOcclusionCulling = false;
            stageCamera.allowMSAA = true;

            // Colour and intensity are shared with the menu stage so the same body reads the same
            // in both; the angles are this screen's own, because its camera stands on the opposite
            // side and the menu's angles would light the figure from behind.
            AddLight(stageGO.transform, "KeyLight", Quaternion.Euler(38f, 200f, 0f),
                     CharacterLighting.KeyIntensity, CharacterLighting.KeyColor, layer);
            AddLight(stageGO.transform, "FillLight", Quaternion.Euler(12f, 140f, 0f),
                     CharacterLighting.FillIntensity, CharacterLighting.FillColor, layer);
            return stage;
        }

        static void AddLight(Transform parent, string name, Quaternion rotation, float intensity,
                             Color color, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.rotation = rotation;
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.cullingMask = 1 << layer;
        }

        static void WireStage(CharacterStage stage, Camera cam, Transform root, RawImage target)
        {
            var so = new SerializedObject(stage);
            UiBuilder.Set(so, "_stageCamera", cam);
            UiBuilder.Set(so, "_avatarRoot", root);
            UiBuilder.Set(so, "_targetImage", target);
            so.FindProperty("_width").intValue = 1080;
            so.FindProperty("_height").intValue = 1080;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void AddAvatar(GameObject host, MonoBehaviour driver, Camera cam, Transform root,
                              GameObject male, GameObject female,
                              UnityEditor.Animations.AnimatorController controller, bool shadow)
        {
            var avatar = host.AddComponent<FightAvatar>();
            var so = new SerializedObject(avatar);
            UiBuilder.Set(so, "_driverBehaviour", driver);
            UiBuilder.Set(so, "_stageCamera", cam);
            UiBuilder.Set(so, "_avatarRoot", root);
            UiBuilder.Set(so, "_malePrefab", male);
            UiBuilder.Set(so, "_femalePrefab", female);
            UiBuilder.Set(so, "_fightController", controller);
            so.FindProperty("_shadow").boolValue = shadow;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Halves ──────────────────────────────────────────────────────────────────────────────

        /// <summary>One fighter's band of the screen: a tinted panel, a glow behind where the body
        /// stands, and the RawImage its stage renders into.</summary>
        /// <summary>One fighter's band of the screen — just a clipping region for their avatar
        /// render now. Its own flat fill and radial glow are gone: <c>bg_fight.png</c> sits behind
        /// the whole canvas and already carries the colour and the mood for both halves at once.</summary>
        static RectTransform BuildHalf(RectTransform parent, string name,
                                       float fromY, float toY, out RawImage avatarImage)
        {
            var half = UiBuilder.Rect(parent, name);
            half.anchorMin = new Vector2(0f, fromY);
            half.anchorMax = new Vector2(1f, toY);
            half.offsetMin = Vector2.zero;
            half.offsetMax = Vector2.zero;

            // Faint tint in edit mode (nothing rendered into it yet); CharacterStage sets it to
            // white once the stage camera actually has a texture to show, at Play.
            avatarImage = UiBuilder.RawImage(half, "AvatarImage", new Color(1f, 1f, 1f, 0.06f));
            UiBuilder.Stretch(avatarImage.rectTransform);
            return half;
        }

        // ── HUD ─────────────────────────────────────────────────────────────────────────────────

        struct HudRefs
        {
            public GameObject OpponentPanel, PlayerPanel, BannerRoot;
            public TextMeshProUGUI OpponentName, OpponentReps, OpponentForm, OpponentTempo;
            public TextMeshProUGUI PlayerName, PlayerReps, PlayerForm, PlayerTempo;
            public TextMeshProUGUI Timer, BannerText, Countdown;
        }

        static HudRefs BuildHud(RectTransform safe, out Button exitBtn, out TextMeshProUGUI exitLabel,
                                out Button debugBtn)
        {
            var refs = new HudRefs();

            // Opponent scoreboard: name and count on the left, stats on the right.
            var opp = UiBuilder.Rect(safe, "OpponentScore");
            UiBuilder.Stretch(opp);
            refs.OpponentPanel = opp.gameObject;
            refs.OpponentName = NameBadge(opp, "OpponentName", "СОПЕРНИК", new Vector2(0f, 1f),
                                          new Vector2(14f, -14f));
            refs.OpponentReps = UiBuilder.Text(opp, "OpponentReps", AppColors.TextPrimary, "0", 68,
                                               FontStyles.Bold, TextAlignmentOptions.Left);
            UiBuilder.Place(refs.OpponentReps.rectTransform, new Vector2(0f, 1f),
                            new Vector2(14f, -46f), new Vector2(180f, 82f));
            refs.OpponentForm = StatBlock(opp, "OpponentForm", "FORM", new Vector2(1f, 1f),
                                          new Vector2(-14f, -50f));
            refs.OpponentTempo = StatBlock(opp, "OpponentTempo", "ТЕМП", new Vector2(1f, 1f),
                                           new Vector2(-14f, -110f));

            // Player scoreboard: same side-assignment as the opponent's (name+count share a side,
            // stats take the other), but pulled up to sit just under the seam instead of down by
            // the bottom edge. That is the comp's actual layout, and it is not just cosmetic: both
            // fighters' reps end up close together, so comparing "18 vs 21" costs one glance instead
            // of a trip down the whole screen.
            var mine = UiBuilder.Rect(safe, "PlayerScore");
            UiBuilder.Stretch(mine);
            refs.PlayerPanel = mine.gameObject;
            refs.PlayerName = NameBadge(mine, "PlayerName", "ТЫ", new Vector2(0f, 0f),
                                        new Vector2(14f, 380f));
            refs.PlayerReps = UiBuilder.Text(mine, "PlayerReps", AppColors.AccentYellow, "0", 68,
                                             FontStyles.Bold, TextAlignmentOptions.Right);
            UiBuilder.Place(refs.PlayerReps.rectTransform, new Vector2(1f, 0f),
                            new Vector2(-14f, 292f), new Vector2(180f, 82f));
            refs.PlayerForm = StatBlock(mine, "PlayerForm", "FORM", new Vector2(0f, 0f),
                                        new Vector2(14f, 344f));
            refs.PlayerTempo = StatBlock(mine, "PlayerTempo", "ТЕМП", new Vector2(0f, 0f),
                                         new Vector2(14f, 288f));

            // Clock on the seam: both fighters are readable without leaving it.
            var timerPlate = UiBuilder.Image(safe, "TimerPlate", new Color(0f, 0f, 0f, 0.55f));
            var pill = LoadSprite(PillSprite);
            if (pill != null) { timerPlate.sprite = pill; timerPlate.type = Image.Type.Sliced; }
            UiBuilder.Place(timerPlate.rectTransform, new Vector2(0.5f, SeamY), Vector2.zero,
                            new Vector2(104f, 38f));
            refs.Timer = UiBuilder.Text(timerPlate.rectTransform, "Timer", AppColors.TextPrimary,
                                        "1:00", 24, FontStyles.Bold);
            UiBuilder.Stretch(refs.Timer.rectTransform, 4, 2, 4, 2);

            // Guidance banner, in the player's half — it is always about what THEY should fix.
            var bannerRoot = UiBuilder.Rect(safe, "Banner");
            UiBuilder.Place(bannerRoot, new Vector2(0.5f, 0f), new Vector2(0f, 196f),
                            new Vector2(360f, 78f));
            var bannerBg = UiBuilder.Image(bannerRoot, "Bg", new Color(0f, 0f, 0f, 0.62f));
            UiBuilder.Stretch(bannerBg.rectTransform);
            refs.BannerText = UiBuilder.Text(bannerRoot, "Text", new Color(1f, 0.75f, 0.2f),
                                             "ВСТАНЬ В ПЛАНКУ ПЕРЕД КАМЕРОЙ", 18, FontStyles.Bold);
            UiBuilder.Stretch(refs.BannerText.rectTransform, 10, 6, 10, 6);
            refs.BannerRoot = bannerRoot.gameObject;

            refs.Countdown = UiBuilder.Text(safe, "Countdown", AppColors.AccentYellow, "3", 110, FontStyles.Bold);
            UiBuilder.Place(refs.Countdown.rectTransform, new Vector2(0.5f, SeamY),
                            new Vector2(0f, 0f), new Vector2(380f, 130f));
            refs.Countdown.gameObject.SetActive(false);

            exitBtn = UiBuilder.Button(safe, "ExitFight", "ВЫЙТИ", new Color(0f, 0f, 0f, 0.45f),
                                       AppColors.TextSecondary, 13, out exitLabel);
            UiBuilder.Place((RectTransform)exitBtn.transform, new Vector2(0.5f, 1f),
                            new Vector2(0f, -14f), new Vector2(88f, 30f));

            debugBtn = UiBuilder.Button(safe, "DebugToggle", "•••", new Color(1f, 1f, 1f, 0.06f),
                                        new Color(1f, 1f, 1f, 0.35f), 16, out _);
            UiBuilder.Place((RectTransform)debugBtn.transform, new Vector2(0f, 0f),
                            new Vector2(14f, 14f), new Vector2(46f, 34f));
            return refs;
        }

        /// <summary>Dark pill with the fighter's name — the badge from the comp.</summary>
        static TextMeshProUGUI NameBadge(RectTransform parent, string name, string text,
                                         Vector2 anchor, Vector2 position)
        {
            var plate = UiBuilder.Image(parent, name + "Plate", new Color(0f, 0f, 0f, 0.55f));
            var pill = LoadSprite(PillSprite);
            if (pill != null) { plate.sprite = pill; plate.type = Image.Type.Sliced; }
            UiBuilder.Place(plate.rectTransform, anchor, position, new Vector2(180f, 26f));

            var label = UiBuilder.Text(plate.rectTransform, name, AppColors.TextPrimary, text, 14,
                                       FontStyles.Bold | FontStyles.Italic);
            UiBuilder.Stretch(label.rectTransform, 8, 2, 8, 2);
            return label;
        }

        /// <summary>A small caption over a value — FORM 85, ТЕМП 1.8с. Returns the value label.</summary>
        static TextMeshProUGUI StatBlock(RectTransform parent, string name, string caption,
                                        Vector2 anchor, Vector2 position)
        {
            var align = anchor.x > 0.5f ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;

            var cap = UiBuilder.Text(parent, name + "Caption", AppColors.TextSecondary, caption, 11,
                                     FontStyles.Bold, align);
            UiBuilder.Place(cap.rectTransform, anchor, position, new Vector2(120f, 14f));

            var value = UiBuilder.Text(parent, name, AppColors.TextPrimary, "—", 26,
                                       FontStyles.Bold, align);
            UiBuilder.Place(value.rectTransform, anchor, position + new Vector2(0f, -18f),
                            new Vector2(120f, 30f));
            return value;
        }

        // ── Ready overlay ───────────────────────────────────────────────────────────────────────

        struct ReadyRefs
        {
            public GameObject Root;
            public TextMeshProUGUI OpponentName, OpponentTrophies, OpponentBest, OpponentWinRate;
            public TextMeshProUGUI PlayerName, PlayerTrophies, PlayerBest, PlayerWinRate;
            public RawImage OpponentAvatarImage, PlayerAvatarImage;
            public Button ReadyButton;
        }

        /// <summary>The pre-duel card: each fighter's portrait in their own corner, stats stacked
        /// beside them, a VS medal on the seam between. Its own background is just a transparent
        /// tap-blocker — the arena art (<c>bg_fight.png</c>) already sits behind it on the canvas,
        /// so Ready, the live duel and the result share one backdrop instead of three improvised
        /// ones. The two portraits are crops of the SAME render targets the duel HUD shows
        /// full-size (<c>DuelReadyPanel</c> points a RawImage at one already rendering elsewhere),
        /// so nothing here costs a second camera per fighter.</summary>
        static ReadyRefs BuildReadyOverlay(RectTransform canvasRoot)
        {
            var refs = new ReadyRefs();

            var root = UiBuilder.Rect(canvasRoot, "ReadyOverlay");
            UiBuilder.Stretch(root);

            // No background of its own: bg_fight.png already sits on the canvas behind this overlay
            // (FightCanvas's own BaseBackground) and the two glows this used to improvise were only
            // ever standing in for exactly that art. A fully transparent hit-blocker still has to
            // exist here, or taps fall through to the HUD underneath.
            var tapBlock = UiBuilder.Image(root, "TapBlock", Color.clear);
            UiBuilder.Stretch(tapBlock.rectTransform);
            tapBlock.raycastTarget = true;

            // Faint oversized "VS" wallpaper — two copies, tilted, standing in for a tiled pattern
            // at a fraction of the cost of actually tiling one.
            AddWatermark(root, "Watermark1", new Vector2(-70f, 140f), 150f, -18f);
            AddWatermark(root, "Watermark2", new Vector2(80f, -220f), 130f, -18f);

            var safe = UiBuilder.Rect(root, "SafeArea");
            UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // ── Portraits — full-bleed on root, not safe: this is backdrop art, not content, and
            // it is meant to run to the edges the way the comp's does. ──
            refs.OpponentAvatarImage = UiBuilder.RawImage(root, "OpponentPortrait", Color.white);
            UiBuilder.Place(refs.OpponentAvatarImage.rectTransform, new Vector2(1f, 1f),
                            new Vector2(-2f, -60f), new Vector2(196f, 300f));

            refs.PlayerAvatarImage = UiBuilder.RawImage(root, "PlayerPortrait", Color.white);
            UiBuilder.Place(refs.PlayerAvatarImage.rectTransform, new Vector2(0f, 0f),
                            new Vector2(2f, 4f), new Vector2(196f, 300f));

            // ── Opponent block, top-left — kept clear of the portrait by stopping at x≈190 ──
            refs.OpponentName = UiBuilder.Text(safe, "OpponentName", AppColors.AccentYellow,
                                               "СОПЕРНИК", 24, FontStyles.Bold | FontStyles.Italic,
                                               TextAlignmentOptions.Left);
            UiBuilder.Place(refs.OpponentName.rectTransform, new Vector2(0f, 1f),
                            new Vector2(18f, -32f), new Vector2(170f, 34f));
            refs.OpponentTrophies = TrophyRow(safe, "OpponentTrophies", new Vector2(0f, 1f),
                                              new Vector2(18f, -80f));
            refs.OpponentBest = CardStat(safe, "OpponentBest", "МАКС. ОТЖИМАНИЙ", new Vector2(0f, 1f),
                                         new Vector2(18f, -128f));
            refs.OpponentWinRate = CardStat(safe, "OpponentWinRate", "ПОБЕД", new Vector2(0f, 1f),
                                            new Vector2(18f, -196f));

            // ── VS medal, on the seam between the two portraits ──
            var vsSprite = LoadSprite(VsCoinSprite);
            var vs = UiBuilder.Image(safe, "VsMedal", Color.white);
            if (vsSprite != null) vs.sprite = vsSprite;
            else { vs.color = AppColors.AccentYellow; vs.sprite = LoadSprite(PillSprite); } // never blank
            UiBuilder.Place(vs.rectTransform, new Vector2(0.5f, SeamY), new Vector2(0f, 0f),
                            new Vector2(108f, 108f));

            // ── Player block, bottom-right — mirrors the opponent's, right-aligned ──
            refs.PlayerName = UiBuilder.Text(safe, "PlayerName", AppColors.AccentYellow, "ТЫ", 24,
                                             FontStyles.Bold | FontStyles.Italic, TextAlignmentOptions.Right);
            UiBuilder.Place(refs.PlayerName.rectTransform, new Vector2(1f, 0f),
                            new Vector2(-18f, 284f), new Vector2(170f, 34f));
            refs.PlayerTrophies = TrophyRow(safe, "PlayerTrophies", new Vector2(1f, 0f),
                                            new Vector2(-18f, 240f));
            refs.PlayerBest = CardStat(safe, "PlayerBest", "МАКС. ОТЖИМАНИЙ", new Vector2(1f, 0f),
                                       new Vector2(-18f, 196f));
            refs.PlayerWinRate = CardStat(safe, "PlayerWinRate", "ПОБЕД", new Vector2(1f, 0f),
                                          new Vector2(-18f, 140f));

            refs.ReadyButton = UiBuilder.Button(safe, "Ready", "ГОТОВ", AppColors.AccentYellow,
                                                new Color32(24, 20, 8, 255), 22, out _);
            UiBuilder.PlaceWide((RectTransform)refs.ReadyButton.transform, 0f, 44f, 60f, 40f);

            root.gameObject.SetActive(false);
            refs.Root = root.gameObject;
            return refs;
        }

        /// <summary>One huge, near-invisible "VS", tilted — a stand-in for a tiled background
        /// pattern that costs one TMP object instead of a real tiling texture.</summary>
        static void AddWatermark(RectTransform parent, string name, Vector2 position, float size, float rotationDeg)
        {
            var label = UiBuilder.Text(parent, name, new Color(1f, 1f, 1f, 0.05f), "VS", size, FontStyles.Bold);
            UiBuilder.Place(label.rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(size * 3f, size * 1.4f));
            label.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotationDeg);
        }

        /// <summary>Cup icon beside a big number, icon always on the left of the number regardless
        /// of which side of the screen the row itself sits on. The row is a small fixed-width
        /// container anchored to that side, and icon/value are placed inside it in local space —
        /// which is what keeps "icon, then number" true whichever way the container is pivoted.</summary>
        static TextMeshProUGUI TrophyRow(RectTransform parent, string name, Vector2 anchor, Vector2 position)
        {
            var row = UiBuilder.Rect(parent, name + "Row");
            UiBuilder.Place(row, anchor, position, new Vector2(150f, 34f));

            var icon = UiBuilder.Image(row, "Icon", Color.white);
            icon.sprite = LoadSprite(CupSprite);
            icon.preserveAspect = true;
            UiBuilder.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(28f, 28f));

            var value = UiBuilder.Text(row, name, AppColors.AccentYellow, "—", 26, FontStyles.Bold,
                                       TextAlignmentOptions.Left);
            UiBuilder.Place(value.rectTransform, new Vector2(0f, 0.5f), new Vector2(36f, 0f),
                            new Vector2(110f, 34f));
            return value;
        }

        /// <summary>Caption above a big value, as on the comp's fighter cards.</summary>
        static TextMeshProUGUI CardStat(RectTransform parent, string name, string caption,
                                        Vector2 anchor, Vector2 position)
        {
            var align = anchor.x > 0.5f ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;

            var cap = UiBuilder.Text(parent, name + "Caption", AppColors.TextSecondary, caption, 11,
                                     FontStyles.Bold, align);
            UiBuilder.Place(cap.rectTransform, anchor, position, new Vector2(170f, 14f));

            var value = UiBuilder.Text(parent, name, AppColors.TextPrimary, "—", 28,
                                       FontStyles.Bold, align);
            UiBuilder.Place(value.rectTransform, anchor, position + new Vector2(0f, -22f),
                            new Vector2(170f, 34f));
            return value;
        }

        // ── Result overlay ──────────────────────────────────────────────────────────────────────

        struct ResultRefs
        {
            public GameObject Root, DuelLayout, TestLayout;
            public TextMeshProUGUI Banner;
            public TextMeshProUGUI OpponentName, OpponentReps, OpponentForm, OpponentTempo;
            public RawImage OpponentAvatarImage, PlayerAvatarImage;
            public TextMeshProUGUI PlayerName, PlayerReps, PlayerForm, PlayerTempo;
            public TextMeshProUGUI DuelRewards, DuelNote;
            public TextMeshProUGUI TestTitle, TestTier, TestScore, TestRewards, TestNote;
            public TextMeshProUGUI ContinueLabel, SecondaryLabel;
            public Button Continue, Secondary;
        }

        static ResultRefs BuildResultOverlay(RectTransform canvasRoot)
        {
            var refs = new ResultRefs();

            var root = UiBuilder.Rect(canvasRoot, "ResultOverlay");
            UiBuilder.Stretch(root);
            // A light darkening, not a cover: bg_fight.png behind everything is what this screen
            // is shown over, and it should still read as the same arena, just a shade moodier for
            // the verdict.
            var scrim = UiBuilder.Image(root, "Scrim", new Color(0f, 0f, 0f, 0.22f));
            UiBuilder.Stretch(scrim.rectTransform);
            scrim.raycastTarget = true;

            // ── Duel layout: a trading card per fighter — portrait in one corner, name/reps/FORM/
            // TEMPO stacked in the other — mirroring the ready card's grammar exactly, because this
            // and that card answer the same kind of question ("how did each of us do") instead of
            // the live HUD's "glance at both at once" split-screen.
            var duel = UiBuilder.Rect(root, "DuelLayout");
            UiBuilder.Stretch(duel);
            refs.DuelLayout = duel.gameObject;

            refs.OpponentAvatarImage = UiBuilder.RawImage(duel, "OpponentPortrait", Color.white);
            UiBuilder.Place(refs.OpponentAvatarImage.rectTransform, new Vector2(1f, 1f),
                            new Vector2(-2f, -60f), new Vector2(196f, 300f));
            refs.PlayerAvatarImage = UiBuilder.RawImage(duel, "PlayerPortrait", Color.white);
            UiBuilder.Place(refs.PlayerAvatarImage.rectTransform, new Vector2(0f, 0f),
                            new Vector2(2f, 4f), new Vector2(196f, 300f));

            var duelSafe = UiBuilder.Rect(duel, "SafeArea");
            UiBuilder.Stretch(duelSafe);
            duelSafe.gameObject.AddComponent<SafeAreaFitter>();

            // Opponent column, top-left — kept clear of the portrait by stopping short of x≈190.
            refs.OpponentName = NameBadge(duelSafe, "OpponentName", "СОПЕРНИК", new Vector2(0f, 1f),
                                          new Vector2(18f, -32f));
            refs.OpponentReps = UiBuilder.Text(duelSafe, "OpponentReps", AppColors.TextPrimary, "0", 64,
                                               FontStyles.Bold, TextAlignmentOptions.Left);
            UiBuilder.Place(refs.OpponentReps.rectTransform, new Vector2(0f, 1f),
                            new Vector2(18f, -72f), new Vector2(170f, 82f));
            refs.OpponentForm = CardStat(duelSafe, "OpponentResultForm", "FORM", new Vector2(0f, 1f),
                                         new Vector2(18f, -166f));
            refs.OpponentTempo = CardStat(duelSafe, "OpponentResultTempo", "ТЕМП", new Vector2(0f, 1f),
                                          new Vector2(18f, -234f));

            // Verdict banner, on the seam, on the torn-ribbon art rather than a flat rectangle.
            var bannerSprite = LoadSprite(BannerWinSprite);
            var bannerPlate = UiBuilder.Image(duelSafe, "BannerPlate", Color.white);
            if (bannerSprite != null) { bannerPlate.sprite = bannerSprite; bannerPlate.preserveAspect = true; }
            else bannerPlate.color = AppColors.AccentBlue; // never blank if the asset is missing
            UiBuilder.Place(bannerPlate.rectTransform, new Vector2(0.5f, SeamY), Vector2.zero,
                            new Vector2(380f, 139f));
            bannerPlate.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -3f);
            refs.Banner = UiBuilder.Text(bannerPlate.rectTransform, "Banner", AppColors.TextPrimary,
                                         "ПОБЕДА", 30, FontStyles.Bold | FontStyles.Italic);
            UiBuilder.Stretch(refs.Banner.rectTransform, 16, 4, 16, 4);

            // Player column, bottom-right — mirrors the opponent's, right-aligned.
            refs.PlayerName = NameBadge(duelSafe, "PlayerName", "ТЫ", new Vector2(1f, 0f),
                                        new Vector2(-18f, 286f));
            refs.PlayerReps = UiBuilder.Text(duelSafe, "PlayerReps", AppColors.TextPrimary, "0", 64,
                                             FontStyles.Bold, TextAlignmentOptions.Right);
            UiBuilder.Place(refs.PlayerReps.rectTransform, new Vector2(1f, 0f),
                            new Vector2(-18f, 192f), new Vector2(170f, 82f));
            refs.PlayerForm = CardStat(duelSafe, "PlayerResultForm", "FORM", new Vector2(1f, 0f),
                                       new Vector2(-18f, 166f));
            refs.PlayerTempo = CardStat(duelSafe, "PlayerResultTempo", "ТЕМП", new Vector2(1f, 0f),
                                        new Vector2(-18f, 140f));

            refs.DuelRewards = UiBuilder.Text(duelSafe, "DuelRewards", AppColors.AccentLime, "+0 XP", 22,
                                              FontStyles.Bold);
            UiBuilder.PlaceWide(refs.DuelRewards.rectTransform, 0f, 110f, 28f);
            refs.DuelNote = UiBuilder.Text(duelSafe, "DuelNote", AppColors.AccentYellow, "", 13,
                                           FontStyles.Bold);
            UiBuilder.PlaceWide(refs.DuelNote.rectTransform, 0f, 84f, 20f);

            var safe = UiBuilder.Rect(root, "SafeArea");
            UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // ── Level-test layout ──────────────────────────────────────────────────────────────
            var test = UiBuilder.Rect(safe, "LevelTestLayout");
            UiBuilder.Stretch(test);
            refs.TestLayout = test.gameObject;

            refs.TestTitle = UiBuilder.Text(test, "Title", AppColors.TextSecondary, "ТВОЙ УРОВЕНЬ", 22, FontStyles.Bold);
            UiBuilder.PlaceWide(refs.TestTitle.rectTransform, 0.5f, 232f, 30f);
            refs.TestTier = UiBuilder.Text(test, "Tier", AppColors.AccentYellow, "АТЛЕТ", 52, FontStyles.Bold);
            UiBuilder.PlaceWide(refs.TestTier.rectTransform, 0.5f, 176f, 66f);
            refs.TestScore = UiBuilder.Text(test, "Score", AppColors.TextPrimary, "0 отжиманий за 60 секунд", 18, FontStyles.Bold);
            UiBuilder.PlaceWide(refs.TestScore.rectTransform, 0.5f, 122f, 28f);
            refs.TestRewards = UiBuilder.Text(test, "Rewards", AppColors.AccentLime, "+0 XP", 26, FontStyles.Bold);
            UiBuilder.PlaceWide(refs.TestRewards.rectTransform, 0.5f, 70f, 34f);
            refs.TestNote = UiBuilder.Text(test, "Note", new Color(1f, 1f, 1f, 0.55f), "", 15, FontStyles.Normal);
            UiBuilder.PlaceWide(refs.TestNote.rectTransform, 0.5f, -10f, 80f, 34f);

            // ── Shared actions ─────────────────────────────────────────────────────────────────
            refs.Continue = UiBuilder.Button(safe, "Continue", "ДАЛЕЕ", AppColors.AccentYellow,
                                             new Color32(24, 20, 8, 255), 20, out refs.ContinueLabel);
            UiBuilder.PlaceWide((RectTransform)refs.Continue.transform, 0f, 44f, 58f, 40f);

            refs.Secondary = UiBuilder.Button(safe, "Secondary", "ПРОПУСТИТЬ",
                                              new Color(1f, 1f, 1f, 0.06f), AppColors.TextSecondary, 16,
                                              out refs.SecondaryLabel);
            UiBuilder.PlaceWide((RectTransform)refs.Secondary.transform, 0f, 100f, 44f, 40f);
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
