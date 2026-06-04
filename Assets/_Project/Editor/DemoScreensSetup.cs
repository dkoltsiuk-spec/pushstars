using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using PushStars.UI;

namespace PushStars.Editor
{
    /// <summary>
    /// Builds two demo scenes that visually replicate the design mockups:
    ///   • Demo_MainDuel.unity      — main duel screen (screen 1)
    ///   • Demo_SearchOpponent.unity — matchmaking screen (screen 2)
    ///
    /// These scenes use components from the design system + procedurally-generated
    /// rounded sprites (run "Tools → Push Stars → Generate UI Sprites" first).
    ///
    /// Where the mockup shows a 3D character / emoji icons we use placeholder
    /// rectangles and coloured circles — production assets will land in phases 10+.
    ///
    /// Menu: Tools → Push Stars → Build Demo Screens
    /// </summary>
    public static class DemoScreensSetup
    {
        // ── Reference resolution (iPhone 14 Pro) ─────────────────────────────────
        const float REF_W = 390f;
        const float REF_H = 844f;

        // ── Sprite assets ────────────────────────────────────────────────────────
        static Sprite _pill24;
        static Sprite _pill16;
        static Sprite _pill12;
        static Sprite _circle;
        static Sprite _vsBadge;    // pre-coloured VS badge (blue + text baked in)
        static Sprite _ringDashed;
        static PushStarsTheme _theme;

        const string ScenesDir       = "Assets/_Project/Scenes";
        const string MainDuelPath    = "Assets/_Project/Scenes/Demo_MainDuel.unity";
        const string SearchPath      = "Assets/_Project/Scenes/Demo_SearchOpponent.unity";
        const string ThemeAsset      = "Assets/_Project/UI/Theme/Resources/PushStarsTheme.asset";

        [MenuItem("Tools/Push Stars/Build Demo Screens", priority = 202)]
        public static void Run()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Push Stars", "Loading theme & sprites …", 0.05f);

                // Ensure theme exists (it contains the authoritative sprite references).
                _theme = AssetDatabase.LoadAssetAtPath<PushStarsTheme>(ThemeAsset);
                if (_theme == null)
                {
                    UIGallerySetup.Run(); // creates theme + prefabs as a side effect
                    _theme = AssetDatabase.LoadAssetAtPath<PushStarsTheme>(ThemeAsset);
                }

                // If procedural sprites are missing, generate them and refresh the theme.
                if (!LoadSprites())
                {
                    SpriteFactory.GenerateAll();
                    UIGallerySetup.Run();           // re-assigns sprites on the theme
                    _theme = AssetDatabase.LoadAssetAtPath<PushStarsTheme>(ThemeAsset);
                    if (!LoadSprites())
                    {
                        EditorUtility.DisplayDialog(
                            "Push Stars",
                            "Failed to generate UI sprites. Aborting.", "OK");
                        return;
                    }
                }

                EditorUtility.DisplayProgressBar("Push Stars", "Building Demo_SearchOpponent …", 0.30f);
                BuildSearchOpponent();

                // Build MainDuel last — Unity leaves the last-saved scene open,
                // so the user sees the main screen immediately after running Setup.
                EditorUtility.DisplayProgressBar("Push Stars", "Building Demo_MainDuel …", 0.70f);
                BuildMainDuel();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog(
                    "Push Stars — Demo Screens",
                    "Demo screens built:\n\n" +
                    "  • Demo_MainDuel.unity\n" +
                    "  • Demo_SearchOpponent.unity\n\n" +
                    "Open them from Assets/_Project/Scenes/ and press Play.",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // Reads sprite refs from the theme (which already knows Figma → fallback priority).
        static bool LoadSprites()
        {
            if (_theme == null) return false;
            _pill24     = _theme.BtnShape;
            _pill16     = _theme.ChipShape;
            _pill12     = _theme.BadgeShape;
            _circle     = _theme.CircleShape;
            _vsBadge    = _theme.VSBadge;     // may be null — falls back to _circle + tint
            _ringDashed = _theme.RingShape;
            return _pill24 && _pill16 && _pill12 && _circle && _ringDashed;
        }

        // ──────────────────────────────────────────────────────────────────────────
        //                           DEMO 1 — MAIN DUEL
        // ──────────────────────────────────────────────────────────────────────────
        static void BuildMainDuel()
        {
            var scene  = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var canvas = MakeCanvas("UICanvas");

            // Real background image from Figma; solid colour fallback.
            var bg = _theme.BgImage != null
                ? MakeImage(canvas, "Background", Color.white, _theme.BgImage)
                : MakeImage(canvas, "Background", _theme.BgDark);
            Stretch(bg.rectTransform, 0, 0, 0, 0);

            // Tiled lightning bolt pattern (behind all UI, fades at screen edges).
            if (_theme.IconLightningBG != null)
                BuildLightningPattern(canvas.transform, _theme.IconLightningBG);

            var safe = MakeRect(canvas.transform, "SafeArea");
            Stretch(safe, 0, 0, 0, 0);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // ── Top bar ──────────────────────────────────────────────────────────
            var topBar = MakeRect(safe, "TopBar");
            Anchor(topBar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
            topBar.anchoredPosition = new Vector2(0, -16);
            topBar.sizeDelta        = new Vector2(0, 36);

            var topHL = topBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            topHL.padding              = new RectOffset(16, 16, 0, 0);
            topHL.spacing              = 8;
            topHL.childAlignment       = TextAnchor.MiddleLeft;
            topHL.childControlWidth    = false;
            topHL.childControlHeight   = true;
            topHL.childForceExpandWidth = false;
            topHL.childForceExpandHeight = true;

            // Yellow level pill
            MakeIconPill(topBar, "LevelPill", _theme.AccentYellow, _theme.BgDark,
                         iconColor: _theme.BgDark, label: "L 2 уровень", width: 130);

            // Spacer
            var spacer = MakeRect(topBar, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

            // Streak + Aura panel (uses bg_streak_aura.png if available)
            if (_theme.BgStreakAura != null)
            {
                var streakAuraPanel = new GameObject("StreakAuraPanel", typeof(RectTransform));
                streakAuraPanel.transform.SetParent(topBar, false);
                var panelImg = streakAuraPanel.AddComponent<Image>();
                panelImg.sprite = _theme.BgStreakAura;
                panelImg.color  = Color.white;
                panelImg.type   = Image.Type.Sliced;
                streakAuraPanel.AddComponent<LayoutElement>().preferredWidth = 160;

                var panelHL = streakAuraPanel.AddComponent<HorizontalLayoutGroup>();
                panelHL.padding             = new RectOffset(4, 4, 2, 2);
                panelHL.spacing             = 4;
                panelHL.childAlignment      = TextAnchor.MiddleCenter;
                panelHL.childControlWidth   = false;
                panelHL.childControlHeight  = true;
                panelHL.childForceExpandWidth = false;

                var streakAuraRT = streakAuraPanel.GetComponent<RectTransform>();
                MakeIconPill(streakAuraRT, "StreakPill", Color.clear, _theme.TextPrimary,
                             iconColor: new Color(1f, 0.4f, 0.2f), label: "12", width: 70,
                             iconSprite: _theme.IconLightning);
                MakeIconPill(streakAuraRT, "AuraPill", Color.clear, _theme.TextPrimary,
                             iconColor: new Color(0.7f, 0.3f, 0.9f), label: "660", width: 80,
                             iconSprite: _theme.IconAura);
            }
            else
            {
                MakeIconPill(topBar, "StreakPill", _theme.NavBg, _theme.TextPrimary,
                             iconColor: new Color(1f, 0.4f, 0.2f), label: "12", width: 70,
                             iconSprite: _theme.IconLightning);
                MakeIconPill(topBar, "AuraPill", _theme.NavBg, _theme.TextPrimary,
                             iconColor: new Color(0.7f, 0.3f, 0.9f), label: "660", width: 80,
                             iconSprite: _theme.IconAura);
            }

            // ── Character placeholder ────────────────────────────────────────────
            var charArea = MakeRect(safe, "CharacterArea");
            Anchor(charArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            charArea.anchoredPosition = new Vector2(0, 30);
            charArea.sizeDelta        = new Vector2(220, 440);

            var charBg = MakeImage(charArea, "Placeholder", new Color(0.16f, 0.16f, 0.24f, 0.6f), _pill24);
            Stretch(charBg.rectTransform, 0, 0, 0, 0);
            charBg.type = Image.Type.Sliced;

            var charLabel = MakeTMP(charBg.transform, "Label", _theme.TextSecondary,
                                     "3D AVATAR\n(phase 10)", 18, FontStyles.Bold);
            charLabel.alignment = TextAlignmentOptions.Center;
            Stretch(charLabel.rectTransform, 0, 0, 0, 0);

            // Plus slots (4 corners around character)
            MakePlusSlot(safe, new Vector2( -130, 200));
            MakePlusSlot(safe, new Vector2(  130, 200));
            MakePlusSlot(safe, new Vector2( -130,  -50));
            MakePlusSlot(safe, new Vector2(  130,  -50));

            // ── Find Opponent button ─────────────────────────────────────────────
            var btnArea = MakeRect(safe, "FindOpponentArea");
            Anchor(btnArea, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            btnArea.anchoredPosition = new Vector2(0, 196);
            btnArea.sizeDelta        = new Vector2(340, 60);

            var btnBg = MakeImage(btnArea, "Bg", _theme.AccentBlue, _pill24);
            btnBg.type = Image.Type.Sliced;
            Stretch(btnBg.rectTransform, 0, 0, 0, 0);
            btnBg.gameObject.AddComponent<Button>().targetGraphic = btnBg;

            var btnLabel = MakeTMP(btnBg.transform, "Label", _theme.BtnPrimaryFg,
                                    "FIND OPPONENT", 20, FontStyles.Bold);
            Anchor(btnLabel.rectTransform, new Vector2(0, 0.5f), new Vector2(0.7f, 0.5f), new Vector2(0.5f, 0.5f));
            btnLabel.rectTransform.anchoredPosition = new Vector2(0, 0);
            btnLabel.rectTransform.sizeDelta        = new Vector2(0, 28);
            btnLabel.alignment = TextAlignmentOptions.Center;

            var rewardLabel = MakeTMP(btnBg.transform, "Reward", _theme.AccentYellow,
                                       "+50", 18, FontStyles.Bold);
            Anchor(rewardLabel.rectTransform, new Vector2(0.7f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f));
            rewardLabel.rectTransform.anchoredPosition = new Vector2(-12, 0);
            rewardLabel.rectTransform.sizeDelta        = new Vector2(0, 28);
            rewardLabel.alignment = TextAlignmentOptions.Right;

            // ── Mode chips row ────────────────────────────────────────────────────
            var chipsRow = MakeRect(safe, "ChipsRow");
            Anchor(chipsRow, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            chipsRow.anchoredPosition = new Vector2(0, 130);
            chipsRow.sizeDelta        = new Vector2(0, 38);

            var chipsHL = chipsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipsHL.spacing              = 10;
            chipsHL.childAlignment       = TextAnchor.MiddleCenter;
            chipsHL.childControlWidth    = false;
            chipsHL.childControlHeight   = true;
            chipsHL.childForceExpandWidth = false;
            chipsRow.gameObject.AddComponent<ContentSizeFitter>().horizontalFit
                = ContentSizeFitter.FitMode.PreferredSize;

            MakeChip(chipsRow, "ДУЭЛЬ",  selected: true);
            MakeChip(chipsRow, "ОТЖИМ.", selected: false);
            MakeChip(chipsRow, "60 СЕК", selected: false);

            // ── Bottom navigation ─────────────────────────────────────────────────
            var navBar = MakeRect(safe, "BottomNav");
            Anchor(navBar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            navBar.anchoredPosition = new Vector2(0, 44);
            navBar.sizeDelta        = new Vector2(220, 64);

            var navBg = MakeImage(navBar, "Bg", _theme.NavBg, _pill24);
            navBg.type = Image.Type.Sliced;
            Stretch(navBg.rectTransform, 0, 0, 0, 0);

            var navHL = navBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            navHL.padding              = new RectOffset(14, 14, 8, 8);
            navHL.spacing              = 14;
            navHL.childAlignment       = TextAnchor.MiddleCenter;
            navHL.childControlWidth    = false;
            navHL.childControlHeight   = false;
            navHL.childForceExpandWidth = false;

            MakeNavCircle(navBar, "League",  isActive: false, label: "II",
                          navIcon: _theme.NavLeague);
            MakeNavCircle(navBar, "Duel",    isActive: true,  label: "VS",
                          activeColor: _theme.AccentBlue);
            MakeNavCircle(navBar, "Profile", isActive: false, label: "@",
                          navIcon: _theme.NavProfile, activeIcon: _theme.NavProfileActive);

            EditorSceneManager.SaveScene(scene, MainDuelPath);
            Debug.Log($"[DemoScreens] ✓ Saved: {MainDuelPath}");
        }

        // ──────────────────────────────────────────────────────────────────────────
        //                       DEMO 2 — SEARCH OPPONENT
        // ──────────────────────────────────────────────────────────────────────────
        static void BuildSearchOpponent()
        {
            var scene  = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var canvas = MakeCanvas("UICanvas");

            var bg = _theme.BgImage != null
                ? MakeImage(canvas, "Background", Color.white, _theme.BgImage)
                : MakeImage(canvas, "Background", _theme.BgDark);
            Stretch(bg.rectTransform, 0, 0, 0, 0);

            if (_theme.IconLightningBG != null)
                BuildLightningPattern(canvas.transform, _theme.IconLightningBG);

            var safe = MakeRect(canvas.transform, "SafeArea");
            Stretch(safe, 0, 0, 0, 0);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // ── Title ────────────────────────────────────────────────────────────
            var title = MakeTMP(safe, "Title", _theme.TextPrimary, "ПОИСК СОПЕРНИКА", 22, FontStyles.Bold);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
            title.rectTransform.anchoredPosition = new Vector2(0, -80);
            title.rectTransform.sizeDelta        = new Vector2(-32, 32);
            title.alignment = TextAlignmentOptions.Center;

            // ── Centre VS + dashed ring ──────────────────────────────────────────
            var vsArea = MakeRect(safe, "VsArea");
            Anchor(vsArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            vsArea.anchoredPosition = new Vector2(0, 60);  // shifted up from centre
            vsArea.sizeDelta        = new Vector2(240, 240);

            var ring = MakeImage(vsArea, "DashedRing", new Color(1f, 0.45f, 0.55f, 0.85f), _ringDashed);
            Stretch(ring.rectTransform, 0, 0, 0, 0);
            ring.gameObject.AddComponent<PushStars.UI.RingSpinner>();

            // VS badge: prefer pre-coloured Figma asset (no tint, no TMP child).
            // Fall back to plain white circle + AccentBlue tint + TMP label.
            bool hasVsBadge = _vsBadge != null;
            var vsBadge = MakeImage(vsArea, "VsBadge",
                                    hasVsBadge ? Color.white : _theme.AccentBlue,
                                    hasVsBadge ? _vsBadge : _circle);
            Anchor(vsBadge.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            vsBadge.rectTransform.anchoredPosition = Vector2.zero;
            vsBadge.rectTransform.sizeDelta        = new Vector2(110, 110);

            if (!hasVsBadge)
            {
                var vsText = MakeTMP(vsBadge.transform, "Text", _theme.TextPrimary, "VS", 38, FontStyles.Bold | FontStyles.Italic);
                Stretch(vsText.rectTransform, 0, 0, 0, 0);
                vsText.alignment = TextAlignmentOptions.Center;
            }

            // ── Tip block ────────────────────────────────────────────────────────
            var tipBlock = MakeRect(safe, "TipBlock");
            Anchor(tipBlock, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            tipBlock.anchoredPosition = new Vector2(0, 160);  // below the ring
            tipBlock.sizeDelta        = new Vector2(300, 120);

            var tipVL = tipBlock.gameObject.AddComponent<VerticalLayoutGroup>();
            tipVL.spacing               = 8;
            tipVL.childAlignment        = TextAnchor.UpperCenter;
            tipVL.childControlWidth     = true;
            tipVL.childControlHeight    = true;
            tipVL.childForceExpandWidth = true;

            var tipHeader = MakeTMP(tipBlock, "Header", _theme.TextPrimary, "СОВЕТ:", 14, FontStyles.Bold);
            tipHeader.alignment = TextAlignmentOptions.Center;
            tipHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;

            var tipBody = MakeTMP(tipBlock, "Body", _theme.TextSecondary,
                "Ауру вы можете использовать\nдля покупки уникальных анимаций\nв магазине.",
                14, FontStyles.Normal);
            tipBody.alignment = TextAlignmentOptions.Center;
            tipBody.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;

            // ── Exit button (red) ────────────────────────────────────────────────
            var exitArea = MakeRect(safe, "ExitButton");
            Anchor(exitArea, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            exitArea.anchoredPosition = new Vector2(0, 80);
            exitArea.sizeDelta        = new Vector2(180, 50);

            var exitBg = MakeImage(exitArea, "Bg", _theme.BtnDangerBg, _pill24);
            exitBg.type = Image.Type.Sliced;
            Stretch(exitBg.rectTransform, 0, 0, 0, 0);
            exitBg.gameObject.AddComponent<Button>().targetGraphic = exitBg;

            var exitLabel = MakeTMP(exitBg.transform, "Label", _theme.BtnDangerFg, "ВЫЙТИ", 18, FontStyles.Bold);
            Stretch(exitLabel.rectTransform, 0, 0, 0, 0);
            exitLabel.alignment = TextAlignmentOptions.Center;

            EditorSceneManager.SaveScene(scene, SearchPath);
            Debug.Log($"[DemoScreens] ✓ Saved: {SearchPath}");
        }

        // ── Composite helpers ────────────────────────────────────────────────────
        // Returns a full-stretch RectTransform ("MirrorRoot") that lives inside
        // the Canvas. All UI content should be parented to this RectTransform
        // so that DeviceSimulatorMirrorFix can compensate the Device Simulator
        // horizontal-flip bug by flipping a single root.
        static RectTransform MakeCanvas(string name)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvasGO = new GameObject(name, typeof(RectTransform));
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REF_W, REF_H);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            var mirrorGO = new GameObject("MirrorRoot", typeof(RectTransform));
            mirrorGO.transform.SetParent(canvasGO.transform, false);
            var mirrorRT = mirrorGO.GetComponent<RectTransform>();
            mirrorRT.anchorMin = Vector2.zero;
            mirrorRT.anchorMax = Vector2.one;
            mirrorRT.offsetMin = Vector2.zero;
            mirrorRT.offsetMax = Vector2.zero;
            mirrorGO.AddComponent<DeviceSimulatorMirrorFix>();

            return mirrorRT;
        }

        // iconSprite: when provided, the icon slot uses the Figma asset (Color.white, no tint).
        static void MakeIconPill(RectTransform parent, string name, Color bg, Color fg,
                                 Color iconColor, string label, float width,
                                 Sprite iconSprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            if (bg.a > 0f)
            {
                img.sprite = _pill16; img.type = Image.Type.Sliced; img.color = bg;
            }
            else
            {
                img.color = Color.clear; // transparent pill (inside a parent panel)
            }
            go.AddComponent<LayoutElement>().preferredWidth = width;

            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding              = new RectOffset(8, 12, 4, 4);
            hl.spacing              = 6;
            hl.childAlignment       = TextAnchor.MiddleLeft;
            hl.childControlWidth    = false;
            hl.childControlHeight   = true;
            hl.childForceExpandWidth = false;

            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            icon.transform.SetParent(go.transform, false);
            var iconImg = icon.GetComponent<Image>();
            if (iconSprite != null)
            {
                iconImg.sprite = iconSprite;
                iconImg.color  = Color.white;  // preserve original Figma colours
            }
            else
            {
                iconImg.sprite = _circle;
                iconImg.color  = iconColor;
            }
            icon.AddComponent<LayoutElement>().preferredWidth = 16;
            icon.GetComponent<RectTransform>().sizeDelta      = new Vector2(16, 16);

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var tmp = lblGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.color     = fg;
            tmp.fontSize  = 13;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            lblGo.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        static void MakeChip(RectTransform parent, string label, bool selected)
        {
            var go = new GameObject($"Chip_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth = 100;

            var bg = go.AddComponent<Image>();
            bg.sprite = _pill16; bg.type = Image.Type.Sliced;
            bg.color  = _theme.NavBg;

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            Stretch(lblGo.GetComponent<RectTransform>(), 8, 4, 8, 4);
            var tmp = lblGo.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.color     = selected ? _theme.AccentYellow : _theme.ChipNormalFg;
            tmp.fontSize  = 13;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
        }

        // navIcon:    pre-coloured Figma icon for the not-active state (no tinting).
        // activeIcon: pre-coloured Figma icon for the active state (e.g. profile_active.png).
        static void MakeNavCircle(RectTransform parent, string name, bool isActive, string label,
                                   Color? activeColor = null, Sprite navIcon = null,
                                   Sprite activeIcon = null)
        {
            var go = new GameObject($"Nav_{name}", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredWidth  = 48;
            go.GetComponent<RectTransform>().sizeDelta       = new Vector2(48, 48);

            var bg = go.AddComponent<Image>();

            // Pre-coloured VS badge (text baked in).
            bool useVsBadge = isActive && label == "VS" && _vsBadge != null;
            // Pre-coloured Figma icon for the active state (e.g. active profile tab).
            bool useActiveIcon = isActive && !useVsBadge && activeIcon != null;
            // Pre-coloured Figma icon for not-active tabs.
            bool useFigmaIcon = !isActive && navIcon != null;

            if (useVsBadge)
            {
                bg.sprite = _vsBadge;
                bg.color  = Color.white;
            }
            else if (useActiveIcon)
            {
                bg.sprite = activeIcon;
                bg.color  = Color.white;   // preserve original Figma colours
            }
            else if (useFigmaIcon)
            {
                bg.sprite = navIcon;
                bg.color  = Color.white;   // preserve original Figma colours
            }
            else
            {
                bg.sprite = _circle;
                bg.color  = isActive
                    ? (activeColor ?? _theme.AccentBlue)
                    : new Color(1f, 1f, 1f, 0.92f);
            }

            // TMP label only when no baked-in text.
            if (!useVsBadge && !useActiveIcon && !useFigmaIcon)
            {
                var lbl = MakeTMP(go.transform, "Label",
                                  isActive ? _theme.TextPrimary : _theme.BgDark,
                                  label, 16, FontStyles.Bold);
                Stretch(lbl.rectTransform, 0, 0, 0, 0);
                lbl.alignment = TextAlignmentOptions.Center;
            }
        }

        static void MakePlusSlot(RectTransform parent, Vector2 anchoredPos)
        {
            var go = new GameObject("PlusSlot", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = new Vector2(32, 32);

            var bg = go.AddComponent<Image>();
            bg.sprite = _circle;
            bg.color  = new Color(1f, 1f, 1f, 0.10f);

            var lbl = MakeTMP(go.transform, "Plus", _theme.TextSecondary, "+", 22, FontStyles.Bold);
            Stretch(lbl.rectTransform, 0, 0, 0, 0);
            lbl.alignment = TextAlignmentOptions.Center;
        }

        // ── Lightning background pattern ──────────────────────────────────────────
        /// <summary>
        /// Fills the canvas with a staggered grid of tinted lightning icons.
        /// Each row is shifted right by <c>rowShift</c> pixels (brick / diagonal feel).
        /// Horizontal alpha fades from <c>maxAlpha</c> at the centre to <c>minAlpha</c> at edges.
        /// </summary>
        static void BuildLightningPattern(Transform parent, Sprite icon)
        {
            const float iconSize  = 36f;   // rendered size in reference pixels
            const float spacingX  = 52f;   // horizontal distance between icon centres
            const float spacingY  = 60f;   // vertical distance between icon centres
            const float rowShift  = 18f;   // each row shifts right by this amount (stagger)
            const float tilt      = -14f;  // icon rotation in degrees
            const float maxAlpha  = 0.18f; // alpha at screen centre column
            const float minAlpha  = 0.02f; // alpha at screen edges

            var container = new GameObject("LightningPattern", typeof(RectTransform));
            container.transform.SetParent(parent, false);
            Stretch(container.GetComponent<RectTransform>(), 0, 0, 0, 0);

            float halfW = REF_W * 0.5f;
            float halfH = REF_H * 0.5f;

            int cols = Mathf.CeilToInt(REF_W / spacingX) + 2; // +2 for partial columns at edges
            int rows = Mathf.CeilToInt(REF_H / spacingY) + 1;

            for (int row = 0; row < rows; row++)
            {
                float y         = halfH - (row + 0.5f) * spacingY;
                // Each successive row shifts right; wrap within one cell width so pattern tiles.
                float rowOffset = (row * rowShift) % spacingX;

                for (int col = -1; col < cols; col++)
                {
                    float x = -halfW + (col + 0.5f) * spacingX + rowOffset;

                    // SmoothStep fade: full alpha up to 35 % from centre, then falls to minAlpha.
                    float edgeT = Mathf.SmoothStep(0.35f, 1f, Mathf.Abs(x) / halfW);
                    float alpha = Mathf.Lerp(maxAlpha, minAlpha, edgeT);
                    if (alpha < 0.015f) continue;

                    var go = new GameObject($"L{row}_{col}", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(container.transform, false);

                    var rt = go.GetComponent<RectTransform>();
                    rt.anchorMin        = new Vector2(0.5f, 0.5f);
                    rt.anchorMax        = new Vector2(0.5f, 0.5f);
                    rt.pivot            = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(x, y);
                    rt.sizeDelta        = new Vector2(iconSize, iconSize);
                    rt.localRotation    = Quaternion.Euler(0f, 0f, tilt);

                    var img = go.GetComponent<Image>();
                    img.sprite          = icon;
                    img.color           = new Color(1f, 1f, 1f, alpha);
                    img.raycastTarget   = false; // decorative — don't block input
                }
            }
        }

        // ── Primitive helpers ────────────────────────────────────────────────────
        static RectTransform MakeRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static Image MakeImage(Component parent, string name, Color color, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent.transform, false);
            var img = go.GetComponent<Image>();
            img.color  = color;
            if (sprite != null) img.sprite = sprite;
            return img;
        }

        static Image MakeImage(RectTransform parent, string name, Color color, Sprite sprite = null)
            => MakeImage((Component)parent, name, color, sprite);

        static TextMeshProUGUI MakeTMP(Transform parent, string name, Color color, string text,
                                        float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.color     = color;
            tmp.fontSize  = size;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;

            // Swap in the correct Rubik font asset for Bold/Italic instead of TMP simulation.
            var rubik = FontSetup.Resolve(style, out var remaining);
            if (rubik != null) { tmp.font = rubik; tmp.fontStyle = remaining; }

            return tmp;
        }

        static void Stretch(RectTransform rt, float left, float bottom, float right, float top)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2( left,   bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 pivot)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot     = pivot;
        }
    }

}
