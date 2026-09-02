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
    /// set: the camera permission, where to put the phone, which body they wear, and the level
    /// test. The last button starts that 60-second test.
    ///
    /// <para>The pages are plain toggled GameObjects; <see cref="OnboardingController"/> owns only
    /// the sequence. Rewriting a page here needs no code change there.</para>
    ///
    /// Menu: Tools ▸ Push Stars ▸ Build Onboarding.
    /// </summary>
    public static class OnboardingSceneSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Onboarding.unity";
        private const string SpritesDir = "Assets/_Project/UI/Sprites/";
        private const string GlowSprite = SpritesDir + "glow_radial.png";
        private const string PillSprite = SpritesDir + "pill_16.png";
        private const string CircleSprite = SpritesDir + "circle_128.png";

        // The camera page's artwork, exported from Figma at @3x and read back at ppu 300 — see
        // SpriteImporter. Every size below is therefore the size it has in the comp.
        private const string CamViewSprite = SpritesDir + "onb_cam_view.png";
        private const string CamPointsSprite = SpritesDir + "onb_cam_points.png";
        private const string CamAvatarSprite = SpritesDir + "onb_cam_avatar.png";
        private const string AllowSprite = SpritesDir + "onb_btn_allow.png";
        private const string CameraIconSprite = SpritesDir + "onb_icon_camera.png";
        private const string ArrowSprite = SpritesDir + "arrow_right.png";
        private const string DashSprite = SpritesDir + "dash_tile.png";
        private const string GroundShadowSprite = SpritesDir + "ground_shadow.png";
        private const string ClockSprite = SpritesDir + "time.png";

        // The placement page's artwork, same @3x pipeline as the camera page's.
        private const string PlacePersonSprite = SpritesDir + "onb_place_person.png";
        private const string PlacePhoneSprite = SpritesDir + "onb_place_phone.png";

        // ── The gender page's 3D stages ─────────────────────────────────────────────────────────
        private const string CharacterLayer = "Character";
        private const string RenderingDir = "Assets/_Project/UI/Rendering";
        private const string MalePreviewRtPath = RenderingDir + "/GenderMaleRT.renderTexture";
        private const string FemalePreviewRtPath = RenderingDir + "/GenderFemaleRT.renderTexture";
        private const string MaterialsDir = "Assets/_Project/UI/Materials";
        private const string SaturatedMatPath = MaterialsDir + "/UiSaturated.mat";
        private const string DrainedMatPath = MaterialsDir + "/UiDrained.mat";
        private const string SaturationShader = "PushStars/UI Saturation";

        // Render target and the surface showing it share one aspect (1:2). Any mismatch stretches
        // the figure, which is the one thing a "pick your body" screen must not do.
        private const int StageRtWidth = 540;
        private const int StageRtHeight = 1080;
        private const float CardWidth = 170f;
        private const float CardHeight = 340f;
        private const float CardTop = 55f;

        // The bottom glow, as left in the Scene view. Width carries a 1.53 scale that was set on
        // the RectTransform — baked in, because a scaled rect measures one thing and draws another.
        private const float GlowY = -272f;
        private const float GlowWidth = 1101.6f;
        private const float GlowHeight = 560f;

        private const int PermissionPage = 0;
        private const int PlacementPage = 1;
        private const int GenderPage = 2;
        private const int CameraPage = 3;

        // ── Camera page palette ─────────────────────────────────────────────────────────────────
        private static readonly Color CalloutRed = new Color32(226, 32, 40, 255);
        private static readonly Color CalloutFill = new Color32(24, 6, 8, 235);
        private static readonly Color Caption = new Color32(198, 198, 208, 255);
        private static readonly Color LinkBlue = new Color32(150, 172, 235, 255);

        // ── Placement page palette ──────────────────────────────────────────────────────────────
        // Green, where the camera page's callout was red: one says "this never leaves the phone",
        // the other says "this is right". Same shape, opposite verdict, and the colour carries it.
        private static readonly Color PlaceGreen = new Color32(61, 220, 107, 255);
        private static readonly Color PlaceGreenFill = new Color32(8, 26, 16, 235);

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

            var glow = SpriteImporter.Load(GlowSprite);
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

            // First, before any of the explaining: see the controller for why the permission leads.
            pages.Add(CameraPermissionPage(safe, out var allowButton, out var howItWorksButton));

            pages.Add(PhonePlacementPage(safe, out var okButton));

            pages.Add(GenderChoicePage(safe, out var maleCard, out var femaleCard, out var genderNext));

            pages.Add(LevelTestPage(safe, out var cameraStatus, out var letsGo, out var skip));

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
                new[] { "", "", "", "НАЧАТЬ ЗАМЕР" });
            // Every page now carries its own button, so the shared bar never shows. It is still
            // built and still wired — a page added later without one falls back to it.
            UiBuilder.SetIntArray(so, "_ownCtaPages",
                new[] { PermissionPage, PlacementPage, GenderPage, CameraPage });
            UiBuilder.Set(so, "_allowButton", allowButton);
            UiBuilder.SetArray(so, "_advanceButtons",
                new Object[] { howItWorksButton, okButton, genderNext, letsGo });
            UiBuilder.Set(so, "_skipButton", skip);
            UiBuilder.SetArray(so, "_chrome",
                new Object[] { dotsRow.gameObject, next.gameObject, back.gameObject });
            so.FindProperty("_genderPageIndex").intValue = GenderPage;
            UiBuilder.SetArray(so, "_genderCards", new Object[] { maleCard, femaleCard });
            so.FindProperty("_cameraPageIndex").intValue = CameraPage;
            UiBuilder.Set(so, "_cameraStatus", cameraStatus);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Only the first page is left on. Every page is a full-bleed rect at the same place, so
            // a scene saved with all of them active is four screens stacked into an unreadable
            // smear in the Scene view — which is where these get reviewed. The controller switches
            // to whichever page it wants on Start, so this costs nothing at runtime.
            for (int i = 1; i < pages.Count; i++)
                if (pages[i] != null) pages[i].SetActive(false);

            EditorSceneManager.SaveScene(scene, ScenePath);
            UiBuilder.EnsureSceneInBuildSettings(ScenePath);
            Debug.Log("[OnboardingSceneSetup] Onboarding.unity built.");
        }

        // ── Page builders ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The permission page, and the first thing the app ever asks for.
        ///
        /// <para>It argues rather than asks: three pictures showing what actually happens to the
        /// frame — camera, joint coordinates, an avatar on the opponent's screen — and a callout
        /// tied by a leader line to the phone the video never leaves. The claim in the heading is
        /// the whole reason a stranger says yes to a camera, so it is made where the eye already
        /// is, against the picture it is about, rather than in a paragraph underneath.</para>
        ///
        /// <para>The line between the callout and the phone is derived at runtime by
        /// <see cref="CalloutLine"/>, not baked — see that class for what a fixed one costs.</para>
        /// </summary>
        private static GameObject CameraPermissionPage(RectTransform parent,
                                                       out Button allow, out Button howItWorks)
        {
            var page = NewPage(parent, "Page0_Camera");

            // This page paints its own ground, and has to reach past the safe area to do it — the
            // notch strip and the home-indicator strip are screen the design covers. The shared
            // halo overhead belongs to the pages still using it; this one is black at the top.
            var backdrop = UiBuilder.Image(page, "Backdrop", AppColors.BgDark);
            UiBuilder.Stretch(backdrop.rectTransform, 0f, -140f, 0f, -140f);

            BottomGlow(page);

            // ── The three steps ─────────────────────────────────────────────────────────────────
            // Not a set of equal tiles, and not a shared centre line either: these are the
            // numbers the row was left at after being sized by hand in the Scene view, read back
            // off the Inspector. Only the widths are still derived — each follows its own sprite's
            // aspect, so a re-export at a different crop cannot squash anything.
            const float BeatY = -177.5f;
            var steps = new[]
            {
                (sprite: CamViewSprite,   x: -128f, y: -107.0f, height: 146.0f, caption: "Камера снимает\nтело"),
                (sprite: CamPointsSprite, x:    0f, y:  -97.6f, height: 179.2f, caption: "Берем только\nкоординаты точек"),
                (sprite: CamAvatarSprite, x:  128f, y:  -83.6f, height: 201.0f, caption: "Показываем\nсопернику аватар"),
            };

            var widths = new float[steps.Length];
            var stepGroups = new CanvasGroup[steps.Length];
            RectTransform camView = null;

            for (int i = 0; i < steps.Length; i++)
            {
                var sprite = SpriteImporter.Load(steps[i].sprite);
                // Width follows the art, so a re-export at a different crop cannot squash it.
                widths[i] = steps[i].height * (sprite != null ? sprite.rect.width / sprite.rect.height : 0.75f);

                // A picture and its caption are one thing to reveal, so they share a group. The
                // group is stretched over the page and only ever has its alpha touched — the two
                // keep the coordinates they were placed at.
                var group = UiBuilder.Rect(page, "Step" + i + "Group");
                UiBuilder.Stretch(group);
                stepGroups[i] = group.gameObject.AddComponent<CanvasGroup>();

                var img = UiBuilder.Image(group, "Step" + i, Color.white);
                if (sprite != null) { img.sprite = sprite; img.preserveAspect = true; }
                UiBuilder.Place(img.rectTransform, new Vector2(0.5f, 1f),
                                new Vector2(steps[i].x, steps[i].y),
                                new Vector2(widths[i], steps[i].height));
                if (i == 0) camView = img.rectTransform;

                var caption = UiBuilder.Text(group, "Step" + i + "Caption", Caption, steps[i].caption,
                                             11, FontStyles.Normal);
                caption.lineSpacing = 4f;
                UiBuilder.Place(caption.rectTransform, new Vector2(0.5f, 1f),
                                new Vector2(steps[i].x, -266f), new Vector2(132f, 34f));
            }

            var circle = SpriteImporter.Load(CircleSprite);

            // Three beats in each gap, centred on whatever gap the widths above actually left.
            // Each carries its own group: they arrive one at a time, which is what makes the row
            // read left to right instead of all at once.
            var dots = new CanvasGroup[(steps.Length - 1) * 3];
            for (int i = 0; i < steps.Length - 1; i++)
            {
                float gap = (steps[i].x + widths[i] * 0.5f + steps[i + 1].x - widths[i + 1] * 0.5f) * 0.5f;
                for (int d = -1; d <= 1; d++)
                {
                    var beat = UiBuilder.Image(page, "Beat" + i + "_" + (d + 1), AppColors.AccentYellow);
                    if (circle != null) beat.sprite = circle;
                    UiBuilder.Place(beat.rectTransform, new Vector2(0.5f, 1f),
                                    new Vector2(gap + d * 11f, BeatY), new Vector2(5f, 5f));
                    dots[i * 3 + d + 1] = beat.gameObject.AddComponent<CanvasGroup>();
                }
            }

            // ── Callout ─────────────────────────────────────────────────────────────────────────
            var pill = SpriteImporter.Load(PillSprite);

            var calloutGroup = UiBuilder.Rect(page, "CalloutGroup");
            UiBuilder.Stretch(calloutGroup);
            var calloutAlpha = calloutGroup.gameObject.AddComponent<CanvasGroup>();

            var callout = UiBuilder.Image(calloutGroup, "Callout", CalloutRed);
            if (pill != null) { callout.sprite = pill; callout.type = Image.Type.Sliced; }
            UiBuilder.Place(callout.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -20f),
                            new Vector2(200f, 42f));

            // Border and fill as two stacked pills — the same two-layer trick the exit button uses,
            // and the only way to outline a sliced sprite without a second asset.
            var calloutFill = UiBuilder.Image(callout.rectTransform, "Fill", CalloutFill);
            if (pill != null) { calloutFill.sprite = pill; calloutFill.type = Image.Type.Sliced; }
            UiBuilder.Stretch(calloutFill.rectTransform, 2f, 2f, 2f, 2f);

            var calloutText = UiBuilder.Text(callout.rectTransform, "Label", AppColors.TextPrimary,
                                             "Видео не видит\nсоперник", 12, FontStyles.Normal,
                                             TextAlignmentOptions.Left);
            calloutText.lineSpacing = 2f;
            // Left inset clears the dot the line starts from, which sits inside the plate.
            UiBuilder.Stretch(calloutText.rectTransform, 30f, 3f, 10f, 3f);

            // ── The leader line ─────────────────────────────────────────────────────────────────
            // Built after both ends so its dots draw over them.
            var lineRoot = UiBuilder.Rect(calloutGroup, "CalloutLine");
            UiBuilder.Stretch(lineRoot);

            var horizontal = UiBuilder.Image(lineRoot, "Horizontal", CalloutRed);
            var vertical = UiBuilder.Image(lineRoot, "Vertical", CalloutRed);
            var fromDot = UiBuilder.Image(lineRoot, "FromDot", CalloutRed);
            var toDot = UiBuilder.Image(lineRoot, "ToDot", CalloutRed);
            if (circle != null) { fromDot.sprite = circle; toDot.sprite = circle; }

            var line = lineRoot.gameObject.AddComponent<CalloutLine>();
            var lineSO = new SerializedObject(line);
            UiBuilder.Set(lineSO, "_from", callout.rectTransform);
            UiBuilder.Set(lineSO, "_to", camView);
            // Fractions of each rect, not points: the plate's dot sits just inside its left edge and
            // a little above centre, the other end just inside the phone's top-right corner.
            lineSO.FindProperty("_fromPoint").vector2Value = new Vector2(0.09f, 0.64f);
            lineSO.FindProperty("_toPoint").vector2Value = new Vector2(0.89f, 0.92f);
            UiBuilder.Set(lineSO, "_horizontal", horizontal.rectTransform);
            UiBuilder.Set(lineSO, "_vertical", vertical.rectTransform);
            UiBuilder.Set(lineSO, "_fromDot", fromDot.rectTransform);
            UiBuilder.Set(lineSO, "_toDot", toDot.rectTransform);
            lineSO.ApplyModifiedPropertiesWithoutUndo();

            // Drawn once now, so the saved scene shows a line instead of four stray rectangles
            // waiting for the first frame of play. It re-derives itself from then on.
            Canvas.ForceUpdateCanvases();
            line.Rebuild();

            // ── The order it happens in ─────────────────────────────────────────────────────────
            // Three pictures with dots between them state a sequence; revealed in order, they show
            // one. On a page whose entire argument is "the video stops at the first picture and
            // only the second one travels", the order is the argument, and this is what makes it
            // without being read.
            //
            // Times are seconds from the start. The page opens empty and is fully drawn in about
            // a second and a half — fast enough to be over before anyone decides to look away, slow
            // enough that the three stages still arrive one at a time rather than together. It runs
            // once and stops: this argues a point, it does not decorate the page.
            var timeline = new (CanvasGroup group, float at)[]
            {
                (stepGroups[0], 0.00f),                                  // the camera sees a body
                (calloutAlpha,  0.22f),                                  // and the video stops there
                (dots[0], 0.42f), (dots[1], 0.50f), (dots[2], 0.58f),
                (stepGroups[1], 0.74f),                                  // only joints travel
                (dots[3], 0.94f), (dots[4], 1.02f), (dots[5], 1.10f),
                (stepGroups[2], 1.26f),                                  // the opponent sees an avatar
            };

            var sequence = page.gameObject.AddComponent<RevealSequence>();
            var seqSO = new SerializedObject(sequence);
            var beats = seqSO.FindProperty("_beats");
            beats.arraySize = timeline.Length;
            for (int i = 0; i < timeline.Length; i++)
            {
                var element = beats.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("target").objectReferenceValue = timeline[i].group;
                element.FindPropertyRelative("at").floatValue = timeline[i].at;
            }
            seqSO.ApplyModifiedPropertiesWithoutUndo();

            // ── The claim ───────────────────────────────────────────────────────────────────────
            string yellow = ColorUtility.ToHtmlStringRGB(AppColors.AccentYellow);
            var title = UiBuilder.Text(page, "Title", AppColors.TextPrimary,
                                       "МЫ БЕРЕМ\n<color=#" + yellow + ">ТОЛЬКО СКЕЛЕТ.</color>",
                                       36, FontStyles.Bold);
            title.enableWordWrapping = false;
            title.enableAutoSizing = true;
            title.fontSizeMin = 24f;
            title.fontSizeMax = 36f;
            UiBuilder.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -455f),
                            new Vector2(360f, 100f));

            var body = UiBuilder.Text(page, "Body", Caption,
                                      "Видео с камеры никогда не покидает телефон — по сети идут " +
                                      "только координаты точек тела.", 15, FontStyles.Normal);
            body.lineSpacing = 8f;
            UiBuilder.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -521f),
                            new Vector2(330f, 90f));

            // ── The one button ──────────────────────────────────────────────────────────────────
            allow = UiBuilder.Button(page, "Allow", "ALLOW", Color.white, AppColors.TextPrimary, 18,
                                    out var allowLabel);
            var allowImage = allow.GetComponent<Image>();
            var allowSprite = SpriteImporter.Load(AllowSprite);
            if (allowSprite != null) { allowImage.sprite = allowSprite; allowImage.preserveAspect = true; }
            UiBuilder.Place((RectTransform)allow.transform, new Vector2(0.5f, 0f), new Vector2(0f, 66f),
                            new Vector2(135f, 52f));

            // The label is pulled left of centre to leave the camera its place beside it — the two
            // read as one lockup, which is why neither is centred on its own.
            UiBuilder.Place(allowLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-16f, 1f),
                            new Vector2(84f, 28f));

            var cameraIcon = SpriteImporter.Load(CameraIconSprite);
            if (cameraIcon != null)
            {
                var icon = UiBuilder.Image((RectTransform)allow.transform, "CameraIcon", Color.white);
                icon.sprite = cameraIcon;
                icon.preserveAspect = true;
                // Scaled to 0.9 by hand in the Scene view; baked into the size instead, since a
                // scaled UI rect measures one thing and draws another.
                UiBuilder.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(40f, 0f),
                                new Vector2(24.3f, 22.5f));
            }

            // Not a second way to grant anything: it walks on into the pages that explain the game,
            // and the camera is asked for again where it is actually needed.
            howItWorks = UiBuilder.Button(page, "HowItWorks", "How its work?",
                                          new Color(0f, 0f, 0f, 0f), LinkBlue, 13, out var linkLabel);
            var linkRect = (RectTransform)howItWorks.transform;
            UiBuilder.Place(linkRect, new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(200f, 30f));

            // The arrow is a sprite, not a character: Rubik has no U+2192 and neither does anything
            // it falls back to, so a typed one would come out as a missing-glyph box.
            SpriteFactory.BuildLinkArrow();
            var arrow = SpriteImporter.Load(ArrowSprite);
            if (arrow != null)
            {
                // No keyline on this one. UiBuilder.Text hands anything 15pt or under the small
                // outline preset; this label was switched back to the font's plain material by
                // hand, and at 13pt over a flat ground it does read better without one.
                if (linkLabel.font != null) linkLabel.fontSharedMaterial = linkLabel.font.material;

                UiBuilder.Place(linkLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-13f, 0f),
                                new Vector2(120f, 26f));

                var tip = UiBuilder.Image(linkRect, "Arrow", LinkBlue);
                tip.sprite = arrow;
                tip.preserveAspect = true;
                UiBuilder.Place(tip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(52f, 0f),
                                new Vector2(17f, 10f));
            }

            return page.gameObject;
        }

        private static GameObject PhonePlacementPage(RectTransform parent, out Button ok)
        {
            var page = NewPage(parent, "Page1_Placement");

            var backdrop = UiBuilder.Image(page, "Backdrop", AppColors.BgDark);
            UiBuilder.Stretch(backdrop.rectTransform, 0f, -140f, 0f, -140f);

            BottomGlow(page);

            // ── The two ends of the measurement ─────────────────────────────────────────────────
            // The person runs off the left edge on purpose: cropping them says "you are here, out
            // of shot" far more directly than shrinking the whole scene to fit them in would.
            var person = UiBuilder.Image(page, "Person", Color.white);
            var personSprite = SpriteImporter.Load(PlacePersonSprite);
            if (personSprite != null) { person.sprite = personSprite; person.preserveAspect = true; }
            UiBuilder.Place(person.rectTransform, new Vector2(0.5f, 1f), new Vector2(-123f, -95f),
                            new Vector2(181f, 205f));

            var phone = UiBuilder.Image(page, "Phone", Color.white);
            var phoneSprite = SpriteImporter.Load(PlacePhoneSprite);
            if (phoneSprite != null) { phone.sprite = phoneSprite; phone.preserveAspect = true; }
            UiBuilder.Place(phone.rectTransform, new Vector2(0.5f, 1f), new Vector2(134f, -166f),
                            new Vector2(94f, 118f));

            // ── The measure between them ────────────────────────────────────────────────────────
            // Tiled, so the dashes keep their size and only their count changes with the span.
            SpriteFactory.BuildDashSprite();
            var dashSprite = SpriteImporter.Load(DashSprite);
            if (dashSprite != null)
            {
                var measure = UiBuilder.Image(page, "Measure", AppColors.AccentYellow);
                measure.sprite = dashSprite;
                measure.type = Image.Type.Tiled;
                UiBuilder.Place(measure.rectTransform, new Vector2(0.5f, 1f), new Vector2(12f, -256f),
                                new Vector2(210f, 5f));
            }

            var distance = UiBuilder.Text(page, "Distance", AppColors.AccentYellow, "1.5 - 2.0 м", 23,
                                          FontStyles.Bold);
            distance.enableWordWrapping = false;
            UiBuilder.Place(distance.rectTransform, new Vector2(0.5f, 1f), new Vector2(22f, -213f),
                            new Vector2(180f, 32f));

            // ── Captions ────────────────────────────────────────────────────────────────────────
            var left = UiBuilder.Text(page, "PersonCaption", Caption, "Встаньте напротив\nтелефона", 11,
                                      FontStyles.Normal);
            left.lineSpacing = 4f;
            UiBuilder.Place(left.rectTransform, new Vector2(0.5f, 1f), new Vector2(-132f, -290f),
                            new Vector2(150f, 40f));

            var right = UiBuilder.Text(page, "PhoneCaption", Caption,
                                       "Установите телефон\nвозле стены или\nна штатив", 11,
                                       FontStyles.Normal);
            right.lineSpacing = 4f;
            UiBuilder.Place(right.rectTransform, new Vector2(0.5f, 1f), new Vector2(128f, -290f),
                            new Vector2(150f, 54f));

            // ── Callout ─────────────────────────────────────────────────────────────────────────
            var pill = SpriteImporter.Load(PillSprite);

            var callout = UiBuilder.Image(page, "Callout", PlaceGreen);
            if (pill != null) { callout.sprite = pill; callout.type = Image.Type.Sliced; }
            UiBuilder.Place(callout.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -28f),
                            new Vector2(212f, 45f));

            var calloutFill = UiBuilder.Image(callout.rectTransform, "Fill", PlaceGreenFill);
            if (pill != null) { calloutFill.sprite = pill; calloutFill.type = Image.Type.Sliced; }
            UiBuilder.Stretch(calloutFill.rectTransform, 2f, 2f, 2f, 2f);

            var calloutText = UiBuilder.Text(callout.rectTransform, "Label", AppColors.TextPrimary,
                                             "Телефон расположен\nна расстоянии 1.5 - 2м", 12,
                                             FontStyles.Normal, TextAlignmentOptions.Left);
            calloutText.lineSpacing = 2f;
            // Room on the right for the dot the line leaves from — the mirror of the permission page,
            // where the subject is to the left of its callout instead of the right.
            UiBuilder.Stretch(calloutText.rectTransform, 12f, 3f, 28f, 3f);

            // ── The leader line ─────────────────────────────────────────────────────────────────
            var circle = SpriteImporter.Load(CircleSprite);

            var lineRoot = UiBuilder.Rect(page, "CalloutLine");
            UiBuilder.Stretch(lineRoot);

            var horizontal = UiBuilder.Image(lineRoot, "Horizontal", PlaceGreen);
            var vertical = UiBuilder.Image(lineRoot, "Vertical", PlaceGreen);
            var fromDot = UiBuilder.Image(lineRoot, "FromDot", PlaceGreen);
            var toDot = UiBuilder.Image(lineRoot, "ToDot", PlaceGreen);
            if (circle != null) { fromDot.sprite = circle; toDot.sprite = circle; }

            var line = lineRoot.gameObject.AddComponent<CalloutLine>();
            var lineSO = new SerializedObject(line);
            UiBuilder.Set(lineSO, "_from", callout.rectTransform);
            UiBuilder.Set(lineSO, "_to", phone.rectTransform);
            lineSO.FindProperty("_fromPoint").vector2Value = new Vector2(0.91f, 0.47f);
            lineSO.FindProperty("_toPoint").vector2Value = new Vector2(0.5f, 0.86f);
            UiBuilder.Set(lineSO, "_horizontal", horizontal.rectTransform);
            UiBuilder.Set(lineSO, "_vertical", vertical.rectTransform);
            UiBuilder.Set(lineSO, "_fromDot", fromDot.rectTransform);
            UiBuilder.Set(lineSO, "_toDot", toDot.rectTransform);
            lineSO.ApplyModifiedPropertiesWithoutUndo();

            Canvas.ForceUpdateCanvases();
            line.Rebuild();

            // ── The instruction ─────────────────────────────────────────────────────────────────
            string yellow = ColorUtility.ToHtmlStringRGB(AppColors.AccentYellow);
            var title = UiBuilder.Text(page, "Title", AppColors.TextPrimary,
                                       "ВСТАНЬ ТАК,\nЧТОБЫ <color=#" + yellow + ">ВЛЕЗТЬ\nЦЕЛИКОМ</color>",
                                       34, FontStyles.Bold);
            title.enableWordWrapping = false;
            title.enableAutoSizing = true;
            title.fontSizeMin = 24f;
            title.fontSizeMax = 34f;
            UiBuilder.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -420f),
                            new Vector2(360f, 140f));

            var body = UiBuilder.Text(page, "Body", Caption,
                                      "Поставь телефон на уровне груди в ~2 метрах и отойди — " +
                                      "от макушки до стоп должно быть видно на экране.", 15,
                                      FontStyles.Normal);
            body.lineSpacing = 8f;
            UiBuilder.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -566f),
                            new Vector2(340f, 90f));

            // ── The one button ──────────────────────────────────────────────────────────────────
            ok = UiBuilder.Button(page, "Ok", "OK", Color.white, AppColors.TextPrimary, 18, out var okLabel);
            var okImage = ok.GetComponent<Image>();
            var okSprite = SpriteImporter.Load(AllowSprite);
            if (okSprite != null) { okImage.sprite = okSprite; okImage.preserveAspect = true; }
            UiBuilder.Place((RectTransform)ok.transform, new Vector2(0.5f, 0f), new Vector2(0f, 60f),
                            new Vector2(119f, 46f));
            UiBuilder.Place(okLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f),
                            new Vector2(80f, 26f));

            return page.gameObject;
        }

        /// <summary>
        /// Who the player is playing as: two figures, both alive, one of them in colour.
        ///
        /// <para><b>They are the real 3D characters, not renders of them.</b> Each stands on its own
        /// <see cref="CharacterStage"/> — camera, render texture, idle animation — which is the same
        /// pipeline the main screen uses. A pair of stills would have been far less code, and would
        /// have shown the player a poster of something the game then animates differently; this
        /// shows the actual body they are choosing, breathing, before they commit to it.</para>
        ///
        /// <para>The two stages stand 40 m apart in world space rather than on separate layers.
        /// Each camera frames a two-metre box around its own figure, so the other one is nowhere
        /// near the frustum, and the project spends no layer on a screen shown once.</para>
        /// </summary>
        private static GameObject GenderChoicePage(RectTransform parent,
                                             out GenderChoiceCard maleCard,
                                             out GenderChoiceCard femaleCard,
                                             out Button next)
        {
            var page = NewPage(parent, "Page2_Gender");

            var backdrop = UiBuilder.Image(page, "Backdrop", AppColors.BgDark);
            UiBuilder.Stretch(backdrop.rectTransform, 0f, -140f, 0f, -140f);

            BottomGlow(page);

            // One rig for both stages: a directional light does not care where it is, so lighting
            // each figure separately would only mean two chances to light them differently.
            int layer = UiBuilder.EnsureLayer(CharacterLayer);
            var stages = new GameObject("GenderStages").transform;
            BuildStageLights(stages);

            var saturated = EnsureSaturationMaterial(SaturatedMatPath, 1f);
            var drained = EnsureSaturationMaterial(DrainedMatPath, 0f);

            maleCard = BuildChoiceCard(page, stages, CharacterGender.Male, -92f, -20f, layer,
                                       saturated, drained);
            femaleCard = BuildChoiceCard(page, stages, CharacterGender.Female, 92f, 20f, layer,
                                         saturated, drained);

            // ── The question ────────────────────────────────────────────────────────────────────
            string yellow = ColorUtility.ToHtmlStringRGB(AppColors.AccentYellow);
            var title = UiBuilder.Text(page, "Title", AppColors.TextPrimary,
                                       "ВЫБЕРИ\n<color=#" + yellow + ">ЗА КОГО ИГРАТЬ</color>", 34,
                                       FontStyles.Bold);
            title.enableWordWrapping = false;
            title.enableAutoSizing = true;
            title.fontSizeMin = 24f;
            title.fontSizeMax = 34f;
            UiBuilder.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -490f),
                            new Vector2(360f, 100f));

            var body = UiBuilder.Text(page, "Body", Caption,
                                      "Ты можешь поменять персонажа в любой момент.", 15,
                                      FontStyles.Normal);
            body.lineSpacing = 8f;
            UiBuilder.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -598f),
                            new Vector2(330f, 60f));

            // ── The one button ──────────────────────────────────────────────────────────────────
            next = UiBuilder.Button(page, "NextCta", "NEXT", Color.white, AppColors.TextPrimary, 17,
                                   out var nextLabel);
            var nextImage = next.GetComponent<Image>();
            var plate = SpriteImporter.Load(AllowSprite);
            if (plate != null) { nextImage.sprite = plate; nextImage.preserveAspect = true; }
            UiBuilder.Place((RectTransform)next.transform, new Vector2(0.5f, 0f), new Vector2(0f, 62f),
                            new Vector2(115f, 44f));
            UiBuilder.Place(nextLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f),
                            new Vector2(90f, 24f));

            return page.gameObject;
        }

        /// <summary>
        /// One figure: a 3D stage out in the world, the surface it draws onto, the radio dot under
        /// it, and the hit area that selects it.
        /// </summary>
        private static GenderChoiceCard BuildChoiceCard(RectTransform page, Transform stages,
                                                        CharacterGender gender, float x, float worldX,
                                                        int layer, Material saturated, Material drained)
        {
            bool male = gender == CharacterGender.Male;
            string id = male ? "Male" : "Female";

            // The whole card is the hit target, figure and dot together: a 28pt radio dot is a poor
            // thing to ask a thumb to find when there is a 170pt figure standing above it saying
            // the same thing.
            var button = UiBuilder.Button(page, id + "Card", "", new Color(0f, 0f, 0f, 0f),
                                          AppColors.TextPrimary, 12, out var unusedLabel);
            unusedLabel.gameObject.SetActive(false);
            var cardRect = (RectTransform)button.transform;
            UiBuilder.Place(cardRect, new Vector2(0.5f, 1f), new Vector2(x, -CardTop),
                            new Vector2(CardWidth, CardHeight + 60f));

            // Sized to the render texture's own aspect. Any other rect stretches the figure, and a
            // stretched character is the one thing a "pick your body" screen must not show.
            var portrait = UiBuilder.RawImage(cardRect, "Portrait", Color.white);
            UiBuilder.Place(portrait.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, 0f),
                            new Vector2(CardWidth, CardHeight));

            // ── Radio dot ───────────────────────────────────────────────────────────────────────
            var circle = SpriteImporter.Load(CircleSprite);

            var ring = UiBuilder.Image(cardRect, "DotRing", AppColors.AccentYellow);
            if (circle != null) ring.sprite = circle;
            UiBuilder.Place(ring.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -(CardHeight + 16f)),
                            new Vector2(30f, 30f));

            var well = UiBuilder.Image(ring.rectTransform, "DotWell", new Color(0.09f, 0.09f, 0.11f, 1f));
            if (circle != null) well.sprite = circle;
            UiBuilder.Stretch(well.rectTransform, 4f, 4f, 4f, 4f);

            var core = UiBuilder.Image(ring.rectTransform, "DotCore", Color.white);
            if (circle != null) core.sprite = circle;
            UiBuilder.Stretch(core.rectTransform, 9f, 9f, 9f, 9f);

            // ── The stage behind it ─────────────────────────────────────────────────────────────
            BuildFigureStage(stages, gender, worldX, layer, portrait);

            var card = button.gameObject.AddComponent<GenderChoiceCard>();
            var so = new SerializedObject(card);
            UiBuilder.Set(so, "_portrait", portrait);
            UiBuilder.Set(so, "_saturated", saturated);
            UiBuilder.Set(so, "_drained", drained);
            UiBuilder.Set(so, "_dotRing", ring);
            UiBuilder.Set(so, "_dotCore", core);
            UiBuilder.Set(so, "_button", button);
            so.FindProperty("_gender").enumValueIndex = (int)gender;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Edit-mode default, so the Scene view is not two grey figures before anything runs.
            card.SetSelected(male);
            return card;
        }

        /// <summary>Camera, figure and render texture for one body. Mirrors the main screen's stage
        /// — see <c>MainVsScreenSetup.Build3DStage</c> — minus the roster, because this page shows
        /// both bodies at once instead of swapping between them.</summary>
        private static void BuildFigureStage(Transform parent, CharacterGender gender,
                                             float worldX, int layer, RawImage surface)
        {
            bool male = gender == CharacterGender.Male;
            string id = male ? "Male" : "Female";

            var stageGO = new GameObject("Stage_" + id);
            stageGO.transform.SetParent(parent, false);
            stageGO.transform.position = new Vector3(worldX, 0f, 0f);
            var stage = stageGO.AddComponent<CharacterStage>();

            var avatarRoot = new GameObject("AvatarRoot").transform;
            avatarRoot.SetParent(stageGO.transform, false);
            // The camera stands on -Z; a Unity character faces +Z, so the root turns to the lens.
            avatarRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var prefab = MainCharacterSetup.LoadCharacterPrefab(gender);
            if (prefab != null)
            {
                var figure = (GameObject)PrefabUtility.InstantiatePrefab(prefab, avatarRoot);
                figure.transform.localPosition = Vector3.zero;
                figure.transform.localRotation = Quaternion.identity;

                // Break up the loop: every few idle cycles the figure shifts, then settles back.
                var accent = figure.AddComponent<CharacterIdleAccent>();
                var accentSO = new SerializedObject(accent);
                accentSO.FindProperty("_animator").objectReferenceValue = figure.GetComponent<Animator>();
                accentSO.FindProperty("_idleState").stringValue = MainCharacterSetup.IdleState;
                accentSO.FindProperty("_accentState").stringValue = MainCharacterSetup.AccentState;
                accentSO.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning($"[OnboardingSceneSetup] {id} character not imported — its stage " +
                                 "will render empty. Run Tools ▸ Push Stars ▸ Character ▸ Import Main Characters.");
            }
            SetLayerRecursive(avatarRoot.gameObject, layer);

            var camGO = new GameObject("StageCamera");
            camGO.transform.SetParent(stageGO.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.0f, -4.2f);
            camGO.transform.localRotation = Quaternion.identity;
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            cam.cullingMask = 1 << layer;
            cam.useOcclusionCulling = false;
            cam.allowMSAA = true;
            // A saved texture so the figure previews without Play; CharacterStage swaps in a fresh
            // one on Awake. Without it this camera would blit to the whole screen in edit mode.
            cam.targetTexture = EnsurePreviewRt(male ? MalePreviewRtPath : FemalePreviewRtPath, id);

            var so = new SerializedObject(stage);
            so.FindProperty("_stageCamera").objectReferenceValue = cam;
            so.FindProperty("_avatarRoot").objectReferenceValue = avatarRoot;
            so.FindProperty("_targetImage").objectReferenceValue = surface;
            so.FindProperty("_width").intValue = StageRtWidth;
            so.FindProperty("_height").intValue = StageRtHeight;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Key and fill for both stages at once, matching the main screen's rig so a figure
        /// does not change complexion between the screen that picks it and the screen that uses
        /// it.</summary>
        private static void BuildStageLights(Transform parent)
        {
            var keyGO = new GameObject("KeyLight");
            keyGO.transform.SetParent(parent, false);
            keyGO.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
            var key = keyGO.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = CharacterLighting.KeyIntensity;
            key.color = CharacterLighting.KeyColor;

            var fillGO = new GameObject("FillLight");
            fillGO.transform.SetParent(parent, false);
            fillGO.transform.rotation = Quaternion.Euler(15f, -35f, 0f);
            var fill = fillGO.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = CharacterLighting.FillIntensity;
            fill.color = CharacterLighting.FillColor;
        }

        /// <summary>The two materials the choice switches between, created once and shared between
        /// the cards. Two fixed assets rather than one material per card carrying its own
        /// saturation: there are two states to show, and an asset is what makes the Scene view
        /// show the right one before anything runs.</summary>
        private static Material EnsureSaturationMaterial(string path, float saturation)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.SetFloat("_Saturation", saturation);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var shader = Shader.Find(SaturationShader);
            if (shader == null)
            {
                Debug.LogWarning($"[OnboardingSceneSetup] Shader '{SaturationShader}' not found — " +
                                 "the figures will both stay in colour.");
                return null;
            }

            EnsureFolder(MaterialsDir);
            var material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            material.SetFloat("_Saturation", saturation);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static RenderTexture EnsurePreviewRt(string path, string id)
        {
            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
            if (existing != null) return existing;

            EnsureFolder(RenderingDir);
            var rt = new RenderTexture(StageRtWidth, StageRtHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "Gender" + id + "RT",
                antiAliasing = 2,
            };
            AssetDatabase.CreateAsset(rt, path);
            return rt;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayerRecursive(child.gameObject, layer);
        }

        /// <summary>
        /// The last page: an invitation to the 60-second level test, and permission to put it off.
        ///
        /// <para><b>Every number on it is a question mark.</b> Reps, form, tempo — the three things
        /// the test measures, shown blank. It states what the next minute is for without promising
        /// a score the player has not earned, and it is the same three-up the result screen fills
        /// in afterwards, so the reveal lands on a layout they have already read.</para>
        ///
        /// <para>The skip is deliberate and it is not a decline: a player who taps it goes to the
        /// main screen without a level, and the router stops sending them back here — see
        /// <see cref="OnboardingState.LevelTestSkipped"/>. Forcing a maximum-effort set on someone
        /// who opened the app on a bus loses the install, not the set.</para>
        /// </summary>
        private static GameObject LevelTestPage(RectTransform parent, out TextMeshProUGUI status,
                                                out Button letsGo, out Button skip)
        {
            var page = NewPage(parent, "Page3_LevelTest");

            var backdrop = UiBuilder.Image(page, "Backdrop", AppColors.BgDark);
            UiBuilder.Stretch(backdrop.rectTransform, 0f, -140f, 0f, -140f);

            BottomGlow(page);

            var glowSprite = SpriteImporter.Load(GlowSprite);

            // ── The figure ──────────────────────────────────────────────────────────────────────
            // A still, not a stage. The pose is a push-up hold and the only clips the characters
            // have are Idle and WarriorIdle, so a live figure here would stand up straight in the
            // middle of a page about getting down on the floor. Swapping this for a stage is a
            // clip away — add one to MainCharacterSetup.Clips and it builds like the gender page.
            if (glowSprite != null)
            {
                var halo = UiBuilder.Image(page, "FigureGlow", new Color(1f, 0.76f, 0.22f, 0.20f));
                halo.sprite = glowSprite;
                UiBuilder.Place(halo.rectTransform, new Vector2(0.5f, 1f), new Vector2(-62f, -40f),
                                new Vector2(320f, 300f));
            }

            var shadowSprite = SpriteImporter.Load(GroundShadowSprite);
            if (shadowSprite != null)
            {
                // What stops him floating: the pose has both hands and both feet on a floor the
                // page does not otherwise draw.
                var shadow = UiBuilder.Image(page, "GroundShadow", new Color(0f, 0f, 0f, 0.55f));
                shadow.sprite = shadowSprite;
                UiBuilder.Place(shadow.rectTransform, new Vector2(0.5f, 1f), new Vector2(-62f, -272f),
                                new Vector2(196f, 34f));
            }

            var figure = UiBuilder.Image(page, "Figure", Color.white);
            var figureSprite = SpriteImporter.Load(CamAvatarSprite);
            if (figureSprite != null) { figure.sprite = figureSprite; figure.preserveAspect = true; }
            UiBuilder.Place(figure.rectTransform, new Vector2(0.5f, 1f), new Vector2(-62f, -55f),
                            new Vector2(205f, 249f));

            // ── The three blanks ────────────────────────────────────────────────────────────────
            var heading = UiBuilder.Text(page, "MaxHeading", AppColors.AccentYellow, "MAX\nPUSHUPS", 20,
                                         FontStyles.Bold, TextAlignmentOptions.Right);
            heading.lineSpacing = 2f;
            UiBuilder.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(93f, -50f),
                            new Vector2(150f, 58f));

            var reps = UiBuilder.Text(page, "RepsUnknown", AppColors.AccentYellow, "?", 104, FontStyles.Bold);
            UiBuilder.Place(reps.rectTransform, new Vector2(0.5f, 1f), new Vector2(129f, -104f),
                            new Vector2(140f, 106f));

            Metric(page, "Form", "FORM", 129f, -207f);
            Metric(page, "Tempo", "TEMPO", 129f, -256f);

            // ── How long it takes ───────────────────────────────────────────────────────────────
            var clockSprite = SpriteImporter.Load(ClockSprite);
            if (clockSprite != null)
            {
                var clock = UiBuilder.Image(page, "Clock", Color.white);
                clock.sprite = clockSprite;
                clock.preserveAspect = true;
                UiBuilder.Place(clock.rectTransform, new Vector2(0.5f, 1f), new Vector2(-27f, -319f),
                                new Vector2(28f, 28f));
            }

            var duration = UiBuilder.Text(page, "Duration", AppColors.TextPrimary, "01:00", 22,
                                          FontStyles.Bold);
            UiBuilder.Place(duration.rectTransform, new Vector2(0.5f, 1f), new Vector2(24f, -318f),
                            new Vector2(120f, 30f));

            // ── The invitation ──────────────────────────────────────────────────────────────────
            string yellow = ColorUtility.ToHtmlStringRGB(AppColors.AccentYellow);
            var title = UiBuilder.Text(page, "Title", AppColors.TextPrimary,
                                       "ПРОВЕРИМ\n<color=#" + yellow + ">НА ЧТО\nТЫ СПОСОБЕН</color>",
                                       34, FontStyles.Bold);
            title.enableWordWrapping = false;
            title.enableAutoSizing = true;
            title.fontSizeMin = 24f;
            title.fontSizeMax = 34f;
            UiBuilder.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -424f),
                            new Vector2(360f, 130f));

            var body = UiBuilder.Text(page, "Body", Caption,
                                      "Ты можешь пропустить этот шаг если пока что не готов, " +
                                      "проверим когда будет возможность.", 15, FontStyles.Normal);
            body.lineSpacing = 8f;
            UiBuilder.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -580f),
                            new Vector2(340f, 70f));

            // Silent unless something is actually wrong — see RefreshCameraStatus. The permission
            // was asked for four pages ago; this is the last place a refusal can still be repaired
            // before it costs the player a set that counts nothing.
            status = UiBuilder.Text(page, "CameraStatus", AppColors.AccentYellow, "", 12,
                                    FontStyles.Normal);
            UiBuilder.Place(status.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -652f),
                            new Vector2(340f, 20f));

            // ── Go, or not yet ──────────────────────────────────────────────────────────────────
            letsGo = UiBuilder.Button(page, "LetsGo", "LETS GO", Color.white, AppColors.TextPrimary, 17,
                                     out var goLabel);
            var goImage = letsGo.GetComponent<Image>();
            var plate = SpriteImporter.Load(AllowSprite);
            if (plate != null) { goImage.sprite = plate; goImage.preserveAspect = true; }
            UiBuilder.Place((RectTransform)letsGo.transform, new Vector2(0.5f, 0f), new Vector2(0f, 55f),
                            new Vector2(120f, 46f));
            UiBuilder.Place(goLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 1f),
                            new Vector2(104f, 24f));

            // Underlined and plain, beside the plate rather than under it: a skip that looks like a
            // second button competes with the one the page is actually asking for, and a skip
            // hidden below the fold is the same as no skip at all.
            skip = UiBuilder.Button(page, "Skip", "SKIP", new Color(0f, 0f, 0f, 0f),
                                    AppColors.TextPrimary, 15, out var skipLabel);
            skipLabel.fontStyle = FontStyles.Bold | FontStyles.Underline;
            UiBuilder.Place((RectTransform)skip.transform, new Vector2(0.5f, 0f), new Vector2(127f, 55f),
                            new Vector2(90f, 40f));

            return page.gameObject;
        }

        /// <summary>One of the two secondary blanks: its name, and the question mark under it.</summary>
        private static void Metric(RectTransform page, string id, string label, float x, float y)
        {
            var name = UiBuilder.Text(page, id + "Label", Caption, label, 11, FontStyles.Normal);
            UiBuilder.Place(name.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, y),
                            new Vector2(120f, 16f));

            var value = UiBuilder.Text(page, id + "Unknown", AppColors.TextPrimary, "?", 20,
                                       FontStyles.Bold);
            UiBuilder.Place(value.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, y - 20f),
                            new Vector2(120f, 26f));
        }

        // ── Primitives ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The blue wash every page stands on.
        ///
        /// <para>One helper rather than the same six lines four times, because it is one design
        /// element: it gets nudged in the Scene view on whichever page happens to be open, and it
        /// has to come back identical on all of them. Four copies meant a tweak that landed on one
        /// page and quietly disagreed with the other three.</para>
        /// </summary>
        private static void BottomGlow(RectTransform page)
        {
            var sprite = SpriteImporter.Load(GlowSprite);
            if (sprite == null) return;

            var glow = UiBuilder.Image(page, "BottomGlow", new Color(0.13f, 0.30f, 0.86f, 0.62f));
            glow.sprite = sprite;
            // Wider than the screen and hung well below it: only the top of the falloff is meant to
            // be on screen, which is what makes it read as a horizon rather than a circle.
            UiBuilder.Place(glow.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, GlowY),
                            new Vector2(GlowWidth, GlowHeight));
        }

        private static RectTransform NewPage(RectTransform parent, string name)
        {
            var page = UiBuilder.Rect(parent, name);
            UiBuilder.Stretch(page);
            return page;
        }

    }
}
