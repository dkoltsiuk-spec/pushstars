using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PushStars.App;
using PushStars.UI;

namespace PushStars.Editor
{
    /// <summary>
    /// Builds <c>Boot.unity</c> — the first thing the app draws: wordmark, progress bar, and the
    /// line saying what startup is doing. <see cref="AppBootstrap"/> sits on the same canvas and
    /// drives both, then routes to the intro, the level test or the main screen.
    ///
    /// <para>Boot used to be a bare GameObject with a bootstrap on it, which meant a black screen
    /// for as long as Firebase took to answer — indistinguishable from a crash on a cold launch.</para>
    ///
    /// Menu: Tools ▸ Push Stars ▸ Build Boot Screen. Everything is created from code, so the build
    /// never depends on stale serialized references.
    /// </summary>
    public static class BootSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Boot.unity";
        private const string GlowSprite = "Assets/_Project/UI/Sprites/glow_radial.png";
        private const string PillSprite = "Assets/_Project/UI/Sprites/pill_capsule.png";

        [MenuItem("Tools/Push Stars/Build Boot Screen", priority = 4)]
        public static void Build()
        {
            BuildScene();
            EditorUtility.DisplayDialog("Push Stars — Boot",
                "Boot.unity built: loading screen + AppBootstrap routing.\n\n" +
                "First launch → Onboarding, intro done but no level test → Fight (level test), " +
                "otherwise → Main.", "OK");
        }

        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            UiBuilder.ClearCamera();
            UiBuilder.EventSystem();
            UiBuilder.Canvas("BootCanvas", out var root);

            var bg = UiBuilder.Image(root, "Background", AppColors.BgDark);
            UiBuilder.Stretch(bg.rectTransform);

            // Soft glow behind the wordmark so the screen is not a flat black rectangle.
            var glow = AssetDatabase.LoadAssetAtPath<Sprite>(GlowSprite);
            if (glow != null)
            {
                var halo = UiBuilder.Image(root, "Glow", new Color(0.28f, 0.36f, 0.9f, 0.42f));
                halo.sprite = glow;
                UiBuilder.Place(halo.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(620f, 620f));
            }

            var safe = UiBuilder.Rect(root, "SafeArea");
            UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // ── Wordmark ────────────────────────────────────────────────────────────────────────
            var word = UiBuilder.Text(safe, "Wordmark", AppColors.TextPrimary, "PUSH STARS", 52, FontStyles.Bold);
            word.characterSpacing = 6f;
            UiBuilder.Place(word.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 100f), new Vector2(360f, 70f));

            var tagline = UiBuilder.Text(safe, "Tagline", AppColors.TextSecondary,
                                         "ОТЖИМАНИЯ · ДУЭЛИ · РЕЙТИНГ", 13, FontStyles.Bold);
            tagline.characterSpacing = 4f;
            UiBuilder.Place(tagline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 58f), new Vector2(360f, 20f));

            // ── Progress bar ────────────────────────────────────────────────────────────────────
            var pill = AssetDatabase.LoadAssetAtPath<Sprite>(PillSprite);

            var track = UiBuilder.Image(safe, "ProgressTrack", new Color(1f, 1f, 1f, 0.10f));
            if (pill != null) { track.sprite = pill; track.type = Image.Type.Sliced; }
            UiBuilder.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 148f), new Vector2(240f, 6f));

            var fill = UiBuilder.Image(track.rectTransform, "Fill", AppColors.AccentYellow);
            UiBuilder.Stretch(fill.rectTransform);
            if (pill != null) fill.sprite = pill;
            // Filled + Sliced are exclusive; a filled bar needs the plain type.
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;

            var status = UiBuilder.Text(safe, "Status", AppColors.TextSecondary, "Запуск…", 13, FontStyles.Normal);
            UiBuilder.Place(status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 116f), new Vector2(320f, 20f));

            var version = UiBuilder.Text(safe, "Version", new Color(1f, 1f, 1f, 0.25f), "v0.1", 11, FontStyles.Normal);
            UiBuilder.Place(version.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(200f, 16f));

            // ── Behaviours ──────────────────────────────────────────────────────────────────────
            var group = root.gameObject.AddComponent<CanvasGroup>();

            var loading = root.gameObject.AddComponent<LoadingScreen>();
            var loadingSO = new SerializedObject(loading);
            UiBuilder.Set(loadingSO, "_progressFill", fill);
            UiBuilder.Set(loadingSO, "_status", status);
            UiBuilder.Set(loadingSO, "_version", version);
            UiBuilder.Set(loadingSO, "_group", group);
            loadingSO.ApplyModifiedPropertiesWithoutUndo();

            var bootstrapGO = new GameObject("AppBootstrap");
            var bootstrap = bootstrapGO.AddComponent<AppBootstrap>();
            var bootSO = new SerializedObject(bootstrap);
            bootSO.FindProperty("_mainSceneName").stringValue = "Main";
            UiBuilder.Set(bootSO, "_loading", loading);
            bootSO.ApplyModifiedPropertiesWithoutUndo();

            // The theme asset is what makes AppColors match the design system at runtime; without
            // it every colour above falls back to the compiled defaults.
            bootstrapGO.AddComponent<ThemeInitializer>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            UiBuilder.EnsureSceneInBuildSettings(ScenePath);
            Debug.Log("[BootSceneSetup] Boot.unity built.");
        }
    }
}
