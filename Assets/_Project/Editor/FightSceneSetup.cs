using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using PushStars.CV;
using PushStars.Fight;
using PushStars.UI;

namespace PushStars.Editor
{
    /// <summary>
    /// Builds <c>Fight.unity</c> — the boss-duel screen (phase 08.9): full-screen camera feed
    /// (UGUI <see cref="CameraFeedView"/>, so the HUD canvas draws over it), the CV stack
    /// (<see cref="PushupSession"/> on a MediaPipe source when the plugin is compiled, else
    /// <see cref="MockPoseSource"/> for editor runs), the design-system HUD, and the result
    /// overlay. Everything is created and wired from code — same rule as the CV test scene:
    /// the build never depends on stale serialized references.
    ///
    /// Menu: Tools → Push Stars → Build Fight Screen. CI: <see cref="BuildScript.PrepareForUBA"/>
    /// regenerates the scene fresh before every cloud build.
    /// </summary>
    public static class FightSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Fight.unity";
        const float REF_W = 390f;
        const float REF_H = 844f;

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

            // ── Display camera: clears to black; everything visible is UI ────────────────────────
            var camGO = new GameObject("DisplayClearCamera", typeof(Camera), typeof(AudioListener));
            var cam = camGO.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;
            cam.depth = -100f;

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            // ── CV stack ─────────────────────────────────────────────────────────────────────────
            var cvGO = new GameObject("CV");
            MonoBehaviour pose = CreatePoseSource(cvGO, out bool usedMediaPipe);
            var session = cvGO.AddComponent<PushupSession>();
            var sessionSO = new SerializedObject(session);
            sessionSO.FindProperty("_poseSourceBehaviour").objectReferenceValue = pose;
            sessionSO.FindProperty("_logReps").boolValue = true;
            sessionSO.ApplyModifiedPropertiesWithoutUndo();

            // ── Canvas ───────────────────────────────────────────────────────────────────────────
            var canvasGO = new GameObject("FightCanvas", typeof(RectTransform));
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REF_W, REF_H);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // Solid dark base so the screen reads as designed even before the camera is up
            // (and always, in the editor with the mock source).
            var baseBg = MakeImage((RectTransform)canvasGO.transform, "BaseBackground", AppColors.BgDark);
            Stretch(baseBg.rectTransform, 0, 0, 0, 0);
            baseBg.raycastTarget = false;

            // ── Camera feed (UGUI, cover-scaled + rotated by CameraFeedView at runtime) ──────────
            var feedArea = MakeRect((RectTransform)canvasGO.transform, "CameraFeedArea");
            Stretch(feedArea, 0, 0, 0, 0);
            var feedImgGO = new GameObject("CameraImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            feedImgGO.transform.SetParent(feedArea, false);
            var feedImg = feedImgGO.GetComponent<RawImage>();
            feedImg.raycastTarget = false;
            var feedRT = feedImg.rectTransform;
            feedRT.anchorMin = feedRT.anchorMax = feedRT.pivot = new Vector2(0.5f, 0.5f);

            var feedView = feedArea.gameObject.AddComponent<CameraFeedView>();
            var feedSO = new SerializedObject(feedView);
            feedSO.FindProperty("_feedBehaviour").objectReferenceValue = pose;
            feedSO.FindProperty("_image").objectReferenceValue = feedImg;
            feedSO.ApplyModifiedPropertiesWithoutUndo();

            // Soft dark scrim over the feed so HUD text stays readable on any lighting.
            var scrim = MakeImage((RectTransform)canvasGO.transform, "Scrim", new Color(0f, 0f, 0f, 0.25f));
            Stretch(scrim.rectTransform, 0, 0, 0, 0);
            scrim.raycastTarget = false;

            // ── Safe area + HUD ──────────────────────────────────────────────────────────────────
            var safe = MakeRect((RectTransform)canvasGO.transform, "SafeArea");
            Stretch(safe, 0, 0, 0, 0);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Score row (top): [ТЫ + reps + form]  [timer]  [boss name + reps]
            var scoreRow = MakeRect(safe, "ScoreRow");
            Anchor(scoreRow, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            scoreRow.anchoredPosition = new Vector2(0f, -8f);
            scoreRow.sizeDelta = new Vector2(-24f, 110f);

            var youLabel = MakeTMP(scoreRow, "YouLabel", AppColors.TextSecondary, "ТЫ", 14, FontStyles.Bold);
            Anchor(youLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            youLabel.rectTransform.anchoredPosition = new Vector2(8f, 0f);
            youLabel.rectTransform.sizeDelta = new Vector2(120f, 20f);
            youLabel.alignment = TextAlignmentOptions.Left;

            var playerReps = MakeTMP(scoreRow, "PlayerReps", AppColors.AccentYellow, "0", 60, FontStyles.Bold);
            Anchor(playerReps.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            playerReps.rectTransform.anchoredPosition = new Vector2(8f, -20f);
            playerReps.rectTransform.sizeDelta = new Vector2(140f, 64f);
            playerReps.alignment = TextAlignmentOptions.Left;

            var form = MakeTMP(scoreRow, "Form", AppColors.TextSecondary, "FORM —", 13, FontStyles.Bold);
            Anchor(form.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            form.rectTransform.anchoredPosition = new Vector2(8f, -86f);
            form.rectTransform.sizeDelta = new Vector2(140f, 18f);
            form.alignment = TextAlignmentOptions.Left;

            var timer = MakeTMP(scoreRow, "Timer", AppColors.TextPrimary, "1:00", 30, FontStyles.Bold);
            Anchor(timer.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            timer.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            timer.rectTransform.sizeDelta = new Vector2(120f, 36f);

            var bossName = MakeTMP(scoreRow, "BossName", AppColors.TextSecondary, "БОСС", 14, FontStyles.Bold);
            Anchor(bossName.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            bossName.rectTransform.anchoredPosition = new Vector2(-8f, 0f);
            bossName.rectTransform.sizeDelta = new Vector2(160f, 20f);
            bossName.alignment = TextAlignmentOptions.Right;

            var bossReps = MakeTMP(scoreRow, "BossReps", AppColors.TextPrimary, "0", 60, FontStyles.Bold);
            Anchor(bossReps.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            bossReps.rectTransform.anchoredPosition = new Vector2(-8f, -20f);
            bossReps.rectTransform.sizeDelta = new Vector2(140f, 64f);
            bossReps.alignment = TextAlignmentOptions.Right;

            // Guidance banner (lower third, same slot the debug HUD proved on device).
            var bannerRoot = MakeRect(safe, "Banner");
            Anchor(bannerRoot, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            bannerRoot.anchoredPosition = new Vector2(0f, 150f);
            bannerRoot.sizeDelta = new Vector2(360f, 84f);
            var bannerBg = MakeImage(bannerRoot, "Bg", new Color(0f, 0f, 0f, 0.55f));
            Stretch(bannerBg.rectTransform, 0, 0, 0, 0);
            bannerBg.raycastTarget = false;
            var bannerText = MakeTMP(bannerRoot, "Text", new Color(1f, 0.75f, 0.2f), "ВСТАНЬ В ПЛАНКУ ПЕРЕД КАМЕРОЙ", 20, FontStyles.Bold);
            Stretch(bannerText.rectTransform, 10, 8, 10, 8);
            bannerText.enableWordWrapping = true;

            // Countdown (center, hidden until armed).
            var countdown = MakeTMP(safe, "Countdown", AppColors.AccentYellow, "3", 110, FontStyles.Bold);
            Anchor(countdown.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            countdown.rectTransform.anchoredPosition = new Vector2(0f, 40f);
            countdown.rectTransform.sizeDelta = new Vector2(380f, 130f);
            countdown.gameObject.SetActive(false);

            // Small ВЫЙТИ (top center, below the score row — leaves both rep corners clean).
            var exitBtn = MakeSimpleButton(safe, "ExitFight", "ВЫЙТИ",
                new Color(0f, 0f, 0f, 0.45f), AppColors.TextSecondary, 14, out var exitText);
            var exitRT = (RectTransform)exitBtn.transform;
            Anchor(exitRT, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            exitRT.anchoredPosition = new Vector2(0f, -122f);
            exitRT.sizeDelta = new Vector2(96f, 34f);

            // ── Result overlay (hidden until the duel ends) ──────────────────────────────────────
            var resultRoot = MakeRect((RectTransform)canvasGO.transform, "ResultOverlay");
            Stretch(resultRoot, 0, 0, 0, 0);
            var resultBg = MakeImage(resultRoot, "Bg", AppColors.BgDark);
            Stretch(resultBg.rectTransform, 0, 0, 0, 0);
            resultBg.raycastTarget = true; // swallow taps under the overlay

            var resultSafe = MakeRect(resultRoot, "SafeArea");
            Stretch(resultSafe, 0, 0, 0, 0);
            resultSafe.gameObject.AddComponent<SafeAreaFitter>();

            var resultTitle = MakeTMP(resultSafe, "Title", AppColors.AccentLime, "ПОБЕДА!", 46, FontStyles.Bold);
            Anchor(resultTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            resultTitle.rectTransform.anchoredPosition = new Vector2(0f, 150f);
            resultTitle.rectTransform.sizeDelta = new Vector2(360f, 60f);

            var resultScore = MakeTMP(resultSafe, "Score", AppColors.TextPrimary, "ТЫ  0 : 0  БОСС", 24, FontStyles.Bold);
            Anchor(resultScore.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            resultScore.rectTransform.anchoredPosition = new Vector2(0f, 80f);
            resultScore.rectTransform.sizeDelta = new Vector2(360f, 36f);

            var resultXp = MakeTMP(resultSafe, "Xp", AppColors.AccentLime, "+0 XP", 28, FontStyles.Bold);
            Anchor(resultXp.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            resultXp.rectTransform.anchoredPosition = new Vector2(0f, 30f);
            resultXp.rectTransform.sizeDelta = new Vector2(360f, 36f);

            var continueBtn = MakeSimpleButton(resultSafe, "Continue", "ДАЛЕЕ",
                AppColors.BtnPrimaryBg, AppColors.BtnPrimaryFg, 20, out _);
            var contRT = (RectTransform)continueBtn.transform;
            Anchor(contRT, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            contRT.anchoredPosition = new Vector2(0f, 110f);
            contRT.sizeDelta = new Vector2(320f, 60f);

            resultRoot.gameObject.SetActive(false);

            // ── Behaviours + wiring ──────────────────────────────────────────────────────────────
            var hud = canvasGO.AddComponent<FightHud>();
            var hudSO = new SerializedObject(hud);
            hudSO.FindProperty("_playerReps").objectReferenceValue = playerReps;
            hudSO.FindProperty("_form").objectReferenceValue = form;
            hudSO.FindProperty("_timer").objectReferenceValue = timer;
            hudSO.FindProperty("_bossName").objectReferenceValue = bossName;
            hudSO.FindProperty("_bossReps").objectReferenceValue = bossReps;
            hudSO.FindProperty("_bannerRoot").objectReferenceValue = bannerRoot.gameObject;
            hudSO.FindProperty("_bannerText").objectReferenceValue = bannerText;
            hudSO.FindProperty("_countdown").objectReferenceValue = countdown;
            hudSO.FindProperty("_session").objectReferenceValue = session;
            hudSO.ApplyModifiedPropertiesWithoutUndo();

            var result = canvasGO.AddComponent<FightResultScreen>();
            var resultSO = new SerializedObject(result);
            resultSO.FindProperty("_root").objectReferenceValue = resultRoot.gameObject;
            resultSO.FindProperty("_title").objectReferenceValue = resultTitle;
            resultSO.FindProperty("_score").objectReferenceValue = resultScore;
            resultSO.FindProperty("_xp").objectReferenceValue = resultXp;
            resultSO.FindProperty("_continueButton").objectReferenceValue = continueBtn;
            resultSO.ApplyModifiedPropertiesWithoutUndo();

            var opponentGO = new GameObject("Opponent");
            var opponent = opponentGO.AddComponent<BossOpponent>();

            var controllerGO = new GameObject("FightController");
            var controller = controllerGO.AddComponent<FightController>();
            var ctrlSO = new SerializedObject(controller);
            ctrlSO.FindProperty("_session").objectReferenceValue = session;
            ctrlSO.FindProperty("_opponent").objectReferenceValue = opponent;
            ctrlSO.FindProperty("_hud").objectReferenceValue = hud;
            ctrlSO.FindProperty("_result").objectReferenceValue = result;
            ctrlSO.FindProperty("_exitButton").objectReferenceValue = exitBtn;
            ctrlSO.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuildSettings(ScenePath);
            Debug.Log($"[FightSceneSetup] Fight.unity built (pose source: {(usedMediaPipe ? "MediaPipe" : "Mock")}).");
            return usedMediaPipe;
        }

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

        // ── UI helpers (mirrors MainVsScreenSetup's idioms) ──────────────────────────────────────
        static RectTransform MakeRect(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image MakeImage(RectTransform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        static TextMeshProUGUI MakeTMP(RectTransform parent, string name, Color color, string text,
                                       float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.color = color;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;

            var rubik = FontSetup.Resolve(style, out var remaining);
            if (rubik != null) { tmp.font = rubik; tmp.fontStyle = remaining; }
            return tmp;
        }

        static Button MakeSimpleButton(RectTransform parent, string name, string label,
                                       Color bg, Color fg, float fontSize, out TextMeshProUGUI text)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = bg;

            text = MakeTMP((RectTransform)go.transform, "Label", fg, label, fontSize, FontStyles.Bold);
            Stretch(text.rectTransform, 4, 4, 4, 4);
            return go.GetComponent<Button>();
        }

        static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
        }

        static void EnsureSceneInBuildSettings(string scenePath)
        {
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path == scenePath) return;
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes)
            {
                new EditorBuildSettingsScene(scenePath, true),
            };
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
