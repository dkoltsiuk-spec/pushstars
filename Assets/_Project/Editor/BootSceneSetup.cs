using System.IO;
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
    /// Builds <c>Boot.unity</c> — the first thing the app draws: the poster art, the percentage and
    /// the bar it belongs to. <see cref="AppBootstrap"/> sits on the same canvas and drives both,
    /// then routes to the intro, the level test or the main screen.
    ///
    /// <para>Boot used to be a bare GameObject with a bootstrap on it, which meant a black screen
    /// for as long as Firebase took to answer — indistinguishable from a crash on a cold launch.
    /// It then spelled the wordmark out in text over a flat background; the artwork now carries the
    /// logo, the arena and both fighters, so the screen draws the art and stays out of its way.</para>
    ///
    /// Menu: Tools ▸ Push Stars ▸ Build Boot Screen. Everything is created from code, so the build
    /// never depends on stale serialized references.
    /// </summary>
    public static class BootSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Boot.unity";
        private const string SpritesDir = "Assets/_Project/UI/Sprites/";
        private const string ArtSprite = SpritesDir + "loading_bg.png";

        [MenuItem("Tools/Push Stars/Build Boot Screen", priority = 4)]
        public static void Build()
        {
            if (File.Exists(ScenePath)) { AuthoredScenes.Open(ScenePath); return; }
            BuildScene();
            EditorUtility.DisplayDialog("Push Stars — Boot",
                "Boot.unity built: loading screen + AppBootstrap routing.\n\n" +
                "First launch → Onboarding, intro done but no level test → Fight (level test), " +
                "otherwise → Main.", "OK");
        }

        public static void BuildScene()
        {
            if (AuthoredScenes.PreserveExisting(ScenePath)) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            UiBuilder.ClearCamera();
            UiBuilder.EventSystem();
            UiBuilder.Canvas("BootCanvas", out var root);

            // Under the art, never instead of it: on an aspect the poster cannot cover, the crop
            // takes from the long axis, and this is what any sliver left over reads as.
            var bg = UiBuilder.Image(root, "Background", AppColors.BgDark);
            UiBuilder.Stretch(bg.rectTransform);

            var art = SpriteImporter.Load(ArtSprite);
            if (art != null)
            {
                // The art is a finished poster — logo, arena and both fighters are painted into it —
                // so it is placed like a photograph rather than a panel: EnvelopeParent scales it up
                // until it covers the screen and lets the overflow crop off whichever axis is long.
                // Stretching it to fit instead would squash the fighters on every device whose
                // aspect is not the reference one.
                var poster = UiBuilder.Image(root, "Art", Color.white);
                poster.sprite = art;
                var fitter = poster.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fitter.aspectRatio = art.rect.width / art.rect.height;
            }

            var safe = UiBuilder.Rect(root, "SafeArea");
            UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var percent = UiBuilder.Text(safe, "Percent", Color.white, "0%", 44f, FontStyles.Bold);
            ConfigurePercent(percent);
            var progress = UiBuilder.Rect(safe, "ProgressTrack").gameObject.AddComponent<LoadingProgressGraphic>();
            ConfigureProgress(progress);
            // ── Behaviours ──────────────────────────────────────────────────────────────────────
            var group = root.gameObject.AddComponent<CanvasGroup>();

            var loading = root.gameObject.AddComponent<LoadingScreen>();
            var loadingSO = new SerializedObject(loading);
            UiBuilder.Set(loadingSO, "_progress", progress);
            UiBuilder.Set(loadingSO, "_percent", percent);
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
            Debug.Log($"[BootSceneSetup] Boot.unity built (art: {(art != null ? "loading_bg" : "missing")}).");
        }

        /// <summary>Updates only the loading UI in an existing authored Boot scene.</summary>
        public static void ApplyReferenceLayout(UnityEngine.SceneManagement.Scene scene)
        {
            LoadingScreen loading = null;
            foreach (var root in scene.GetRootGameObjects())
                if ((loading = root.GetComponentInChildren<LoadingScreen>(true)) != null) break;
            if (loading == null) throw new System.InvalidOperationException("Boot LoadingScreen is missing.");
            var safe = (RectTransform)loading.transform.Find("SafeArea");
            foreach (string name in new[] { "Status", "Version", "Wordmark", "Tagline" })
            {
                var extra = safe.Find(name);
                if (extra != null) Object.DestroyImmediate(extra.gameObject);
            }
            var percent = safe.Find("Percent").GetComponent<TextMeshProUGUI>();
            ConfigurePercent(percent);
            var track = (RectTransform)safe.Find("ProgressTrack");
            for (int i = track.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(track.GetChild(i).gameObject);
            var oldImage = track.GetComponent<Image>();
            if (oldImage != null) Object.DestroyImmediate(oldImage);
            var progress = track.GetComponent<LoadingProgressGraphic>();
            if (progress == null) progress = track.gameObject.AddComponent<LoadingProgressGraphic>();
            ConfigureProgress(progress);
            var data = new SerializedObject(loading);
            UiBuilder.Set(data, "_progress", progress);
            UiBuilder.Set(data, "_percent", percent);
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void ConfigureProgress(LoadingProgressGraphic progress)
        {
            progress.color = Color.white;
            progress.raycastTarget = false;
            progress.Progress = 0f;
            UiBuilder.Place(progress.rectTransform, new Vector2(.5f, 0f),
                new Vector2(0f, 45f), new Vector2(340f, 46f));
            progress.rectTransform.pivot = new Vector2(.5f, .5f);
        }

        private static void ConfigurePercent(TextMeshProUGUI percent)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontSetup.BoldAsset);
            if (font != null) { percent.font = font; percent.fontStyle = FontStyles.Normal; }
            percent.color = Color.white;
            percent.fontSize = 44f;
            percent.enableAutoSizing = false;
            percent.enableWordWrapping = false;
            percent.text = "0%";
            percent.raycastTarget = false;
            UiBuilder.Place(percent.rectTransform, new Vector2(.5f, 0f),
                new Vector2(0f, 108f), new Vector2(260f, 56f));
            percent.rectTransform.pivot = new Vector2(.5f, .5f);
        }
    }
}
