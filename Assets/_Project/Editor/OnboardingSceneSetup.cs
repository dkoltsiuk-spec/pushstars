using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using PushStars.UI;

namespace PushStars.Editor
{
    /// <summary>
    /// Builds <c>Onboarding.unity</c> — four pages between the loading screen and the player's first
    /// set: what the app is, where to put the phone, which body they wear, and the camera
    /// permission. The last button starts the 60-second level test.
    ///
    /// <para>The pages are plain toggled GameObjects; <see cref="OnboardingController"/> owns only
    /// the sequence. Rewriting a page here needs no code change there.</para>
    ///
    /// Menu: Tools ▸ Push Stars ▸ Build Onboarding.
    /// </summary>
    public static class OnboardingSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Onboarding.unity";
        private const string PushupSprite = "Assets/_Project/UI/Sprites/pushup.png";
        private const string GlowSprite = "Assets/_Project/UI/Sprites/glow_radial.png";

        private const int GenderPage = 2;
        private const int CameraPage = 3;

        [MenuItem("Tools/Push Stars/Build Onboarding", priority = 5)]
        public static void Build()
        {
            BuildScene();
            EditorUtility.DisplayDialog("Push Stars — Onboarding",
                "Onboarding.unity built: 4 pages ending in the 60-second level test.\n\n" +
                "The scene was added to Build Settings.", "OK");
        }

        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            UiBuilder.ClearCamera();
            UiBuilder.EventSystem();
            UiBuilder.Canvas("OnboardingCanvas", out var root);
            root.gameObject.AddComponent<ThemeInitializer>();

            var bg = UiBuilder.Image(root, "Background", AppColors.BgDark);
            UiBuilder.Stretch(bg.rectTransform);

            var glow = AssetDatabase.LoadAssetAtPath<Sprite>(GlowSprite);
            if (glow != null)
            {
                var halo = UiBuilder.Image(root, "Glow", new Color(0.28f, 0.36f, 0.9f, 0.30f));
                halo.sprite = glow;
                UiBuilder.Place(halo.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(700f, 700f));
            }

            var safe = UiBuilder.Rect(root, "SafeArea");
            UiBuilder.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // ── Pages ───────────────────────────────────────────────────────────────────────────
            var pages = new List<GameObject>();

            pages.Add(TextPage(safe, "Page0_Welcome", "PUSH STARS",
                "Отжимания превращаются в дуэли.\n\n" +
                "Камера считает повторы и судит технику прямо на телефоне — " +
                "видео никуда не уходит, только счёт."));

            pages.Add(PhonePlacementPage(safe));

            pages.Add(GenderPage2(safe, out var maleButton, out var maleFrame,
                                  out var femaleButton, out var femaleFrame));

            pages.Add(CameraPage3(safe, out var cameraStatus));

            // ── Bottom bar: dots + back + primary CTA ───────────────────────────────────────────
            var dots = new List<Image>();
            var dotsRow = UiBuilder.Rect(safe, "Dots");
            UiBuilder.Place(dotsRow, new Vector2(0.5f, 0f), new Vector2(0f, 158f), new Vector2(200f, 10f));
            for (int i = 0; i < pages.Count; i++)
            {
                var dot = UiBuilder.Image(dotsRow, $"Dot{i}", new Color(1f, 1f, 1f, 0.2f));
                UiBuilder.Place(dot.rectTransform, new Vector2(0.5f, 0.5f),
                                new Vector2((i - (pages.Count - 1) * 0.5f) * 18f, 0f), new Vector2(7f, 7f));
                dots.Add(dot);
            }

            var next = UiBuilder.Button(safe, "Next", "ДАЛЕЕ",
                                        AppColors.BtnPrimaryBg, AppColors.BtnPrimaryFg, 20, out var nextLabel);
            UiBuilder.PlaceWide((RectTransform)next.transform, 0f, 88f, 58f, 28f);

            var back = UiBuilder.Button(safe, "Back", "НАЗАД",
                                        new Color(1f, 1f, 1f, 0.06f), AppColors.TextSecondary, 14, out _);
            UiBuilder.Place((RectTransform)back.transform, new Vector2(0f, 1f), new Vector2(20f, -18f), new Vector2(84f, 34f));

            // ── Controller ──────────────────────────────────────────────────────────────────────
            var controller = root.gameObject.AddComponent<OnboardingController>();
            var so = new SerializedObject(controller);
            UiBuilder.SetArray(so, "_pages", pages.ToArray());
            UiBuilder.SetArray(so, "_dots", dots.ToArray());
            UiBuilder.Set(so, "_nextButton", next);
            UiBuilder.Set(so, "_nextLabel", nextLabel);
            UiBuilder.Set(so, "_backButton", back);
            UiBuilder.SetStringArray(so, "_nextLabels",
                new[] { "ДАЛЕЕ", "ПОНЯТНО", "ДАЛЕЕ", "НАЧАТЬ ЗАМЕР" });
            so.FindProperty("_genderPageIndex").intValue = GenderPage;
            UiBuilder.Set(so, "_maleButton", maleButton);
            UiBuilder.Set(so, "_femaleButton", femaleButton);
            UiBuilder.Set(so, "_maleFrame", maleFrame);
            UiBuilder.Set(so, "_femaleFrame", femaleFrame);
            so.FindProperty("_cameraPageIndex").intValue = CameraPage;
            UiBuilder.Set(so, "_cameraStatus", cameraStatus);
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            UiBuilder.EnsureSceneInBuildSettings(ScenePath);
            Debug.Log("[OnboardingSceneSetup] Onboarding.unity built.");
        }

        // ── Page builders ───────────────────────────────────────────────────────────────────────

        /// <summary>The shared page frame: a full-bleed rect with a title and a body paragraph.</summary>
        private static GameObject TextPage(RectTransform parent, string name, string title, string body)
        {
            var page = NewPage(parent, name);
            Title(page, title);
            Body(page, body);
            return page.gameObject;
        }

        private static GameObject PhonePlacementPage(RectTransform parent)
        {
            var page = NewPage(parent, "Page1_Placement");
            Title(page, "ПОСТАВЬ ТЕЛЕФОН");
            Body(page, "Экраном к себе, на 1.5–2 метра.\n" +
                       "В кадр должно попасть всё тело: голова, ладони и ступни.");

            var art = AssetDatabase.LoadAssetAtPath<Sprite>(PushupSprite);
            if (art != null)
            {
                var img = UiBuilder.Image(page, "Illustration", new Color(1f, 1f, 1f, 0.85f));
                img.sprite = art;
                img.preserveAspect = true;
                UiBuilder.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(260f, 200f));
            }

            // The three failures the CV stack rejects most often, said before they can happen.
            var tips = new[]
            {
                "Свет спереди, не из-за спины",
                "Телефон стоит ровно, не лежит",
                "Ладони на полу в кадре",
            };
            for (int i = 0; i < tips.Length; i++)
            {
                var tip = UiBuilder.Text(page, $"Tip{i}", AppColors.TextSecondary, "· " + tips[i], 14,
                                         FontStyles.Normal, TextAlignmentOptions.Left);
                UiBuilder.PlaceWide(tip.rectTransform, 0f, 250f - i * 26f, 22f, 40f);
            }
            return page.gameObject;
        }

        private static GameObject GenderPage2(RectTransform parent,
                                              out Button maleButton, out Image maleFrame,
                                              out Button femaleButton, out Image femaleFrame)
        {
            var page = NewPage(parent, "Page2_Gender");
            Title(page, "КЕМ ИГРАЕШЬ");
            Body(page, "Внешность персонажа. Поменять можно в любой момент на главном экране.");

            maleButton = GenderCard(page, "Male", "ПАРЕНЬ", -84f, out maleFrame);
            femaleButton = GenderCard(page, "Female", "ДЕВУШКА", 84f, out femaleFrame);
            return page.gameObject;
        }

        private static Button GenderCard(RectTransform page, string name, string label, float x, out Image frame)
        {
            var button = UiBuilder.Button(page, $"{name}Card", "", new Color(1f, 1f, 1f, 0.05f),
                                          AppColors.TextPrimary, 16, out var text);
            var rt = (RectTransform)button.transform;
            UiBuilder.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(x, -20f), new Vector2(150f, 190f));

            // The label sits at the bottom of the card; the frame above it is the selection state.
            UiBuilder.Place(text.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(140f, 24f));
            text.text = label;

            // Selection is a tint over the whole card rather than an outline: one Image.color
            // write, and it reads at a glance on a phone held at arm's length. Pushed behind the
            // label so the text stays on top of it.
            frame = UiBuilder.Image(rt, "Frame", new Color(1f, 1f, 1f, 0.06f));
            UiBuilder.Stretch(frame.rectTransform, 2, 2, 2, 2);
            frame.rectTransform.SetAsFirstSibling();
            return button;
        }

        private static GameObject CameraPage3(RectTransform parent, out TextMeshProUGUI status)
        {
            var page = NewPage(parent, "Page3_Camera");
            Title(page, "ЗАМЕР УРОВНЯ");
            Body(page, "60 секунд, максимум отжиманий.\n\n" +
                       "Результат станет твоим уровнем — и первым соперником: " +
                       "дальше ты дерёшься против записи этого подхода.");

            status = UiBuilder.Text(page, "CameraStatus", AppColors.AccentYellow,
                                    "Сейчас система спросит разрешение на камеру", 14, FontStyles.Normal);
            UiBuilder.PlaceWide(status.rectTransform, 0f, 210f, 44f, 34f);
            return page.gameObject;
        }

        // ── Primitives ──────────────────────────────────────────────────────────────────────────

        private static RectTransform NewPage(RectTransform parent, string name)
        {
            var page = UiBuilder.Rect(parent, name);
            UiBuilder.Stretch(page);
            return page;
        }

        private static void Title(RectTransform page, string text)
        {
            var title = UiBuilder.Text(page, "Title", AppColors.TextPrimary, text, 34, FontStyles.Bold);
            title.characterSpacing = 2f;
            UiBuilder.PlaceWide(title.rectTransform, 1f, -110f, 46f, 28f);
        }

        private static void Body(RectTransform page, string text)
        {
            var body = UiBuilder.Text(page, "Body", AppColors.TextSecondary, text, 16, FontStyles.Normal);
            body.lineSpacing = 6f;
            UiBuilder.PlaceWide(body.rectTransform, 1f, -260f, 180f, 34f);
        }

    }
}
