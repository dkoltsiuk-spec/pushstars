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

        // Preference order, not alternatives: the exported bar art is used the moment it exists,
        // and the sprites SpriteFactory draws stand in until it does. Swapping in the real thing is
        // therefore a file drop — no rewiring, and nothing here to keep in sync with it.
        private static readonly string[] TrackSprites =
            { SpritesDir + "bar_track.png", SpritesDir + "pill_bar_track.png" };
        private static readonly string[] FillSprites =
            { SpritesDir + "bar_fill.png", SpritesDir + "pill_bar_fill.png" };

        // ── Layout, in the 390×844 reference units the canvas is authored against ───────────────
        // Everything under the art is measured from the bottom of the SAFE area, not the screen:
        // the bar clears the home indicator by design, and on a phone without one it simply sits
        // that much lower.
        private const float BarWidth = 320f;
        private const float BarY = 34f;

        // Height, corner radius and the rim around the gold are baked into the two sprites, so they
        // are read back from the tool that draws them rather than restated here — restated, the
        // trough would keep its rect while the art changed shape under it.
        private const float BarHeight = SpriteFactory.BarHeight;
        private const float BarPadding = SpriteFactory.BarPadding;
        private const float PercentY = 85f;
        private const float PercentSize = 44f;
        private const float StatusY = 122f;
        private const float VersionY = 8f;

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

            // ── Wordmark (fallback only) ────────────────────────────────────────────────────────
            // Spelled out in text only when the poster is missing, so a stripped or half-imported
            // project still launches into something branded instead of a black screen. With the art
            // present this would print a second wordmark over the painted one.
            if (art == null)
            {
                var word = UiBuilder.Text(safe, "Wordmark", AppColors.TextPrimary, "PUSH STARS", 52, FontStyles.Bold);
                word.characterSpacing = 6f;
                word.enableWordWrapping = false;
                word.enableAutoSizing = true;
                word.fontSizeMin = 28f;
                word.fontSizeMax = 52f;
                UiBuilder.Place(word.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 104f), new Vector2(340f, 72f));

                var tagline = UiBuilder.Text(safe, "Tagline", AppColors.TextSecondary,
                                             "ОТЖИМАНИЯ · ДУЭЛИ · РЕЙТИНГ", 13, FontStyles.Bold);
                tagline.characterSpacing = 4f;
                tagline.enableWordWrapping = false;
                UiBuilder.Place(tagline.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), new Vector2(360f, 20f));
            }

            // ── Percentage ──────────────────────────────────────────────────────────────────────
            // Carries the same black keyline as every other number in the game (UiBuilder.Text
            // applies it), which is also what keeps it readable over the brightest part of the art.
            var percent = UiBuilder.Text(safe, "Percent", AppColors.TextPrimary, "0%", PercentSize, FontStyles.Bold);
            percent.enableWordWrapping = false;
            UiBuilder.Place(percent.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, PercentY), new Vector2(260f, 56f));

            // ── Progress bar ────────────────────────────────────────────────────────────────────
            SpriteFactory.BuildBarSprites();
            var trackSprite = LoadFirst(TrackSprites);
            var fillSprite = LoadFirst(FillSprites);

            var track = UiBuilder.Image(safe, "ProgressTrack", new Color(0.04f, 0.04f, 0.06f, 0.95f));
            if (trackSprite != null) { track.sprite = trackSprite; track.type = Image.Type.Sliced; }
            UiBuilder.Place(track.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, BarY),
                            new Vector2(BarWidth, BarHeight));

            // The fill is measured against this rather than against the trough, so the black rim
            // stays an even width at both ends and the gold's own caps never sit on top of it.
            var fillArea = UiBuilder.Rect(track.rectTransform, "FillArea");
            UiBuilder.Stretch(fillArea, BarPadding, BarPadding, BarPadding, BarPadding);

            // Tint white: the gradient is painted into the sprite, and any other tint multiplies it.
            var fill = UiBuilder.Image(fillArea, "Fill", Color.white);
            if (fillSprite != null) { fill.sprite = fillSprite; fill.type = Image.Type.Sliced; }
            // Anchored, not sized: LoadingScreen drives anchorMax.x, so the width follows the track
            // on every screen size without the scene having to know how wide the track ended up.
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;

            // ── Diagnostics ─────────────────────────────────────────────────────────────────────
            // Not in the comp, and deliberately quiet: the status line is the only thing that says
            // WHICH startup step is the slow one when a launch stalls, and it starts counting
            // seconds out loud after one. Dim enough to read as part of the art until it matters.
            var status = UiBuilder.Text(safe, "Status", new Color(1f, 1f, 1f, 0.42f), "Запуск…", 11, FontStyles.Normal);
            UiBuilder.Place(status.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, StatusY), new Vector2(320f, 18f));

            var version = UiBuilder.Text(safe, "Version", new Color(1f, 1f, 1f, 0.25f), "v0.1", 9, FontStyles.Normal);
            UiBuilder.Place(version.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, VersionY), new Vector2(200f, 14f));

            // ── Behaviours ──────────────────────────────────────────────────────────────────────
            var group = root.gameObject.AddComponent<CanvasGroup>();

            var loading = root.gameObject.AddComponent<LoadingScreen>();
            var loadingSO = new SerializedObject(loading);
            UiBuilder.Set(loadingSO, "_progressFill", fill);
            UiBuilder.Set(loadingSO, "_percent", percent);
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
            Debug.Log($"[BootSceneSetup] Boot.unity built (art: {(art != null ? "loading_bg" : "missing — text wordmark")}).");
        }

        /// <summary>First of <paramref name="paths"/> that resolves to a sprite. The list is a
        /// preference order — the exported art first, the generated stand-in behind it — so a path
        /// that is simply not there yet is expected and passed over quietly.</summary>
        private static Sprite LoadFirst(params string[] paths)
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var sprite = SpriteImporter.Load(path);
                if (sprite != null) return sprite;
            }

            Debug.LogWarning($"[BootSceneSetup] No sprite among: {string.Join(", ", paths)}");
            return null;
        }

    }
}
