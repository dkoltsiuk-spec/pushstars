using System.IO;
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
    /// One-click builder for the Phase 03 design-system artefacts.
    /// Menu: Tools → Push Stars → Setup UI Gallery
    ///
    /// What it creates:
    ///   Assets/_Project/UI/Theme/Resources/PushStarsTheme.asset   (ScriptableObject)
    ///   Assets/_Project/UI/Prefabs/PrimaryButton.prefab
    ///   Assets/_Project/UI/Prefabs/SecondaryChip.prefab
    ///   Assets/_Project/UI/Prefabs/StatBadge.prefab
    ///   Assets/_Project/UI/Prefabs/PlayerPill.prefab
    ///   Assets/_Project/UI/Prefabs/TrophyRow.prefab
    ///   Assets/_Project/UI/Prefabs/LoadingVsRing.prefab
    ///   Assets/_Project/UI/Prefabs/ExitButton.prefab
    ///   Assets/_Project/UI/Prefabs/ReadyButton.prefab
    ///   Assets/_Project/Scenes/UI_Gallery.unity
    /// </summary>
    public static class UIGallerySetup
    {
        // ── Path constants ────────────────────────────────────────────────────────
        private const string UIRoot     = "Assets/_Project/UI";
        private const string ThemeDir   = "Assets/_Project/UI/Theme";
        private const string ResDir     = "Assets/_Project/UI/Theme/Resources";
        private const string PrefabsDir = "Assets/_Project/UI/Prefabs";
        private const string ThemeAsset = "Assets/_Project/UI/Theme/Resources/PushStarsTheme.asset";
        private const string ScenePath  = "Assets/_Project/Scenes/UI_Gallery.unity";

        // ── Entry point ───────────────────────────────────────────────────────────
        [MenuItem("Tools/Push Stars/Setup UI Gallery", priority = 201)]
        public static void Run()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Push Stars", "Creating directories …", 0.05f);
                EnsureDirectories();

                EditorUtility.DisplayProgressBar("Push Stars", "Generating procedural sprites …", 0.08f);
                SpriteFactory.GenerateAll();     // regenerate pill_*, card_* with correct 9-slice borders

                EditorUtility.DisplayProgressBar("Push Stars", "Configuring Figma sprites …", 0.10f);
                SpriteImporter.ConfigureAll();   // ensure all PNGs are imported as Sprite assets

                EditorUtility.DisplayProgressBar("Push Stars", "Creating theme asset …", 0.20f);
                var theme = GetOrCreateTheme();

                EditorUtility.DisplayProgressBar("Push Stars", "Building prefabs …", 0.30f);
                BuildAllPrefabs(theme);

                EditorUtility.DisplayProgressBar("Push Stars", "Building UI_Gallery scene …", 0.70f);
                BuildGalleryScene(theme);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("[Push Stars] ✓ UI Gallery setup complete. " +
                          "Open Assets/_Project/Scenes/UI_Gallery.unity to preview.");

                EditorUtility.DisplayDialog(
                    "Push Stars — Design System Gallery",
                    "UI Gallery setup complete!\n\n" +
                    "Open Assets/_Project/Scenes/UI_Gallery.unity to see:\n" +
                    "  1. Color palette\n" +
                    "  2. Typography (Rubik)\n" +
                    "  3. Component prefabs\n" +
                    "  4. Sprite inventory  ← shows which slots already use Figma PNGs and which still fall back to procedural / are missing.",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ── Directory helpers ─────────────────────────────────────────────────────
        static void EnsureDirectories()
        {
            EnsureDir(UIRoot);
            EnsureDir(ThemeDir);
            EnsureDir(ResDir);
            EnsureDir(PrefabsDir);
        }

        static void EnsureDir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
            var name   = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, name);
        }

        // ── Theme SO ──────────────────────────────────────────────────────────────
        // Always recreate so re-running Setup applies the latest code-defined colour defaults.
        // Sprite slots are auto-assigned after creation: prefer real Figma PNGs, fall back to
        // procedurally-generated shapes from SpriteFactory.
        static PushStarsTheme GetOrCreateTheme()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PushStarsTheme>(ThemeAsset);
            if (existing != null)
                AssetDatabase.DeleteAsset(ThemeAsset);

            var theme = ScriptableObject.CreateInstance<PushStarsTheme>();
            AssetDatabase.CreateAsset(theme, ThemeAsset);
            AssetDatabase.SaveAssets();

            AutoAssignSprites(theme);
            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();

            Debug.Log($"[UIGallery] Created: {ThemeAsset}");
            return theme;
        }

        // Fills sprite slots on the theme. Call order: Figma name → generated fallback.
        static void AutoAssignSprites(PushStarsTheme theme)
        {
            // ── Button / chip shapes ──────────────────────────────────────────────
            theme.BtnShape   = TryLoadSprite("btn_primary")
                            ?? TryLoadSprite("btn_start")
                            ?? TryLoadSprite("pill_24");

            theme.ChipShapeNormal   = TryLoadSprite("type")
                                   ?? TryLoadSprite("btn_type_time");
            theme.ChipShapeSelected = TryLoadSprite("mode")
                                   ?? TryLoadSprite("btn_mode");

            // ChipShape keeps backward-compat: prefer type.png, fall back to pill_16
            theme.ChipShape  = theme.ChipShapeNormal
                            ?? TryLoadSprite("chip")
                            ?? TryLoadSprite("pill_16");

            theme.BadgeShape = TryLoadSprite("badge")
                            ?? TryLoadSprite("pill_12");

            // ── Pre-coloured Figma button shapes (use Color.white, not tinted) ───
            theme.BtnExitShape  = TryLoadSprite("red_btn");
            theme.BtnReadyShape = TryLoadSprite("green_btn");

            // ── Icons / rings ─────────────────────────────────────────────────────
            theme.CircleShape = TryLoadSprite("icon_circle")
                             ?? TryLoadSprite("circle_128");

            // Figma shipped the VS badge under two different names across exports.
            theme.VSBadge  = TryLoadSprite("main_btn_VS_active")
                          ?? TryLoadSprite("btn_VS_main_nav_active");

            theme.VSBadgeSearch = TryLoadSprite("VS_for_serching");

            theme.RingShape = TryLoadSprite("ring_dashed")
                           ?? TryLoadSprite("dashed_ring_512");

            theme.RingFinding = TryLoadSprite("VS_search_anim")
                             ?? TryLoadSprite("circle_finding_animation");

            // ── Backgrounds ───────────────────────────────────────────────────────
            theme.BgImage      = TryLoadSprite("BG");
            theme.BgStreakAura = TryLoadSprite("bg_streak_aura");
            theme.BgCard       = TryLoadSprite("bg_card");

            // ── Navigation icons ──────────────────────────────────────────────────
            theme.NavLeague  = TryLoadSprite("btn_Leage_nav_notactive");
            theme.NavProfile = TryLoadSprite("btn_profile_nav_notactive")
                            ?? TryLoadSprite("profile");
            theme.NavProfileActive = TryLoadSprite("profile_active");

            // ── Small icons ───────────────────────────────────────────────────────
            // Several Figma exports dropped the "icon_" prefix.
            theme.IconAura        = TryLoadSprite("aura")
                                 ?? TryLoadSprite("icon_aura");
            theme.IconLightning   = TryLoadSprite("icon_lightning");
            theme.IconLightningBG = TryLoadSprite("icon_lightning_BG")
                                 ?? TryLoadSprite("icon_lightning_for_BG");
            theme.IconPushup      = TryLoadSprite("pushup")
                                 ?? TryLoadSprite("icon_pushup");
            theme.IconTime        = TryLoadSprite("time")
                                 ?? TryLoadSprite("icon_time");

            theme.IconCup     = TryLoadSprite("cup_");
            theme.IconStreak  = TryLoadSprite("streak");
            theme.IconXP      = TryLoadSprite("exp_lightning");
            theme.IconWin     = TryLoadSprite("win");
            theme.IconLose    = TryLoadSprite("lose");
            theme.IconUp      = TryLoadSprite("up");
            theme.IconDown    = TryLoadSprite("down");
            theme.IconPlus    = TryLoadSprite("plus");
            theme.IconLevel   = TryLoadSprite("lvl");
            theme.IconStatics = TryLoadSprite("statics");

            // Settings gear (phase 07). Drop gear.png into the Sprites folder; null is fine until then
            // (the builder falls back to a text "⚙" / "НАСТРОЙКИ" label).
            theme.IconSettings = TryLoadSprite("gear")
                              ?? TryLoadSprite("settings")
                              ?? TryLoadSprite("icon_settings");
        }

        static Sprite TryLoadSprite(string name)
        {
            // 1. Direct path in the canonical Sprites folder.
            var s = AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteFactory.SpritesDir}/{name}.png");
            if (s != null) return s;

            // 2. Fallback: asset database search (handles subfolder / capitalisation differences).
            var guids = AssetDatabase.FindAssets($"{name} t:Sprite");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path)
                        .Equals(name, System.StringComparison.OrdinalIgnoreCase))
                {
                    var found = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (found != null) return found;
                }
            }
            return null;
        }

        // ── Prefabs ───────────────────────────────────────────────────────────────
        static void BuildAllPrefabs(PushStarsTheme t)
        {
            CreatePrimaryButton(t);
            CreateSecondaryChip(t);
            CreateStatBadge(t);
            CreatePlayerPill(t);
            CreateTrophyRow(t);
            CreateLoadingVsRing(t);
            CreateExitButton(t);
            CreateReadyButton(t);
        }

        // PrimaryButton ────────────────────────────────────────────────────────────
        static void CreatePrimaryButton(PushStarsTheme t)
        {
            var root = new GameObject("PrimaryButton");
            SetSize(root.AddComponent<RectTransform>(), 320, 56);
            AddLayoutElement(root, 320, 56);

            var bg  = root.AddComponent<Image>();
            bg.color = t.BtnPrimaryBg;
            if (t.BtnShape != null) { bg.sprite = t.BtnShape; bg.type = Image.Type.Sliced; }

            var btn = root.AddComponent<Button>();
            btn.targetGraphic = bg;
            var cb = btn.colors;
            cb.normalColor      = t.BtnPrimaryBg;
            cb.highlightedColor = Lighter(t.BtnPrimaryBg, 0.15f);
            cb.pressedColor     = Darker(t.BtnPrimaryBg,  0.15f);
            cb.selectedColor    = t.BtnPrimaryBg;
            btn.colors = cb;

            var labelGO = MakeTMP(root, "Label", t.BtnPrimaryFg, "FIND OPPONENT", 22, FontStyles.Bold);
            Stretch(labelGO.GetComponent<RectTransform>(), 16, 8);

            var comp = root.AddComponent<PrimaryButton>();
            Wire(comp, "_background",  bg);
            Wire(comp, "_label",       labelGO.GetComponent<TextMeshProUGUI>());
            Wire(comp, "_fallbackText","FIND OPPONENT");

            SavePrefab(root, $"{PrefabsDir}/PrimaryButton.prefab");
            Object.DestroyImmediate(root);
        }

        // SecondaryChip ────────────────────────────────────────────────────────────
        static void CreateSecondaryChip(PushStarsTheme t)
        {
            // Determine base size from Figma sprite or fixed fallback.
            var normalSprite   = t.ChipShapeNormal;
            var selectedSprite = t.ChipShapeSelected;
            var baseSprite     = normalSprite ?? selectedSprite ?? t.ChipShape;

            float W = 120f, H = 40f;
            if (baseSprite != null)
            {
                float aspect = baseSprite.rect.width / baseSprite.rect.height;
                H = 40f;
                W = Mathf.Round(H * aspect);
            }

            var root = new GameObject("SecondaryChip");
            SetSize(root.AddComponent<RectTransform>(), W, H);
            var le = root.AddComponent<LayoutElement>();
            le.preferredWidth  = W;
            le.preferredHeight = H;
            le.flexibleWidth   = 0;

            // Background — starts with normal (blue border) sprite.
            var bg = root.AddComponent<Image>();
            if (baseSprite != null)
            {
                bg.sprite         = baseSprite;
                bg.type           = Image.Type.Simple;
                bg.preserveAspect = true;
                bg.color          = Color.white;
            }
            else
            {
                bg.color = t.BtnSecondaryBg;
            }

            var btn = root.AddComponent<Button>();
            btn.targetGraphic = bg;
            var cb = btn.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
            cb.pressedColor     = new Color(1f, 1f, 1f, 0.65f);
            cb.selectedColor    = Color.white;
            btn.colors = cb;

            // Horizontal layout: optional icon + label.
            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment        = TextAnchor.MiddleCenter;
            hlg.spacing               = 6;
            hlg.padding               = new RectOffset(12, 12, 0, 0);
            hlg.childControlWidth     = true;
            hlg.childControlHeight    = true;
            hlg.childForceExpandWidth = false;

            // Icon slot (hidden by default; activated via SetIcon at runtime).
            var iconGO  = new GameObject("Icon");
            iconGO.transform.SetParent(root.transform, false);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.color           = Color.white;
            iconImg.preserveAspect  = true;
            iconImg.raycastTarget   = false;
            var iconLE = iconGO.AddComponent<LayoutElement>();
            iconLE.preferredWidth  = 20f;
            iconLE.preferredHeight = 20f;
            iconLE.flexibleWidth   = 0;
            iconGO.SetActive(false);

            // Label.
            var labelGO = MakeTMP(root, "Label", t.ChipNormalFg, "Chip", 14, FontStyles.Normal);
            labelGO.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            var labelLE = labelGO.AddComponent<LayoutElement>();
            labelLE.flexibleWidth = 1;

            var comp = root.AddComponent<SecondaryChip>();
            Wire(comp, "_background",     bg);
            Wire(comp, "_label",          labelGO.GetComponent<TextMeshProUGUI>());
            Wire(comp, "_icon",           iconImg);
            if (normalSprite   != null) Wire(comp, "_normalShape",   normalSprite);
            if (selectedSprite != null) Wire(comp, "_selectedShape", selectedSprite);
            Wire(comp, "_fallbackText",   "Chip");
            Wire(comp, "_normalBg",       t.BtnSecondaryBg);
            Wire(comp, "_selectedBg",     t.BtnSecondaryBg);
            Wire(comp, "_normalFg",       t.ChipNormalFg);
            Wire(comp, "_selectedFg",     t.ChipSelectedFg);

            SavePrefab(root, $"{PrefabsDir}/SecondaryChip.prefab");
            Object.DestroyImmediate(root);
        }

        // StatBadge ────────────────────────────────────────────────────────────────
        static void CreateStatBadge(PushStarsTheme t)
        {
            var root = new GameObject("StatBadge");
            SetSize(root.AddComponent<RectTransform>(), 110, 60);
            AddLayoutElement(root, 110, 60);

            var vlg = root.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment         = TextAnchor.MiddleCenter;
            vlg.spacing                = 2;
            vlg.childControlHeight     = true;
            vlg.childControlWidth      = true;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;

            var valueGO = MakeTMP(root, "Value", t.TextPrimary,   "142",       30, FontStyles.Bold);
            var labelGO = MakeTMP(root, "Label", t.TextSecondary, "MAX PUSHUP", 11, FontStyles.Normal);
            valueGO.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            labelGO.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;

            var comp = root.AddComponent<StatBadge>();
            Wire(comp, "_valueText", valueGO.GetComponent<TextMeshProUGUI>());
            Wire(comp, "_labelText", labelGO.GetComponent<TextMeshProUGUI>());

            SavePrefab(root, $"{PrefabsDir}/StatBadge.prefab");
            Object.DestroyImmediate(root);
        }

        // PlayerPill ───────────────────────────────────────────────────────────────
        static void CreatePlayerPill(PushStarsTheme t)
        {
            var root = new GameObject("PlayerPill");
            SetSize(root.AddComponent<RectTransform>(), 320, 48);
            // preferredWidth = 0 so the parent VerticalLayoutGroup can freely
            // expand this prefab to fill the container width.
            var rootLE = root.AddComponent<LayoutElement>();
            rootLE.preferredHeight = 48;
            rootLE.flexibleWidth   = 1;

            var bg = root.AddComponent<Image>();
            bg.color = t.BtnSecondaryBg;
            if (t.BadgeShape != null) { bg.sprite = t.BadgeShape; bg.type = Image.Type.Sliced; }

            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment         = TextAnchor.MiddleLeft;
            hlg.spacing                = 6;
            hlg.padding                = new RectOffset(10, 10, 6, 6);
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;   // required for flexibleWidth to work
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth  = false;

            var flagGO   = MakeTMP(root, "Flag",   t.TextPrimary, "US",        20, FontStyles.Bold);
            var nameGO   = MakeTMP(root, "Name",   t.TextPrimary, "Player123", 15, FontStyles.Bold);
            var trophyGO = MakeTMP(root, "Trophy", t.TrophyGold,  "T 1250",    13, FontStyles.Normal);

            var flagLE   = flagGO.AddComponent<LayoutElement>();
            flagLE.minWidth = 36; flagLE.preferredWidth = 36; flagLE.flexibleWidth = 0;
            var nameLE   = nameGO.AddComponent<LayoutElement>();
            nameLE.minWidth = 0; nameLE.flexibleWidth = 1;
            var trophyLE = trophyGO.AddComponent<LayoutElement>();
            trophyLE.minWidth = 72; trophyLE.preferredWidth = 72; trophyLE.flexibleWidth = 0;

            var comp = root.AddComponent<PlayerPill>();
            Wire(comp, "_background", bg);
            Wire(comp, "_flagText",   flagGO.GetComponent<TextMeshProUGUI>());
            Wire(comp, "_nameText",   nameGO.GetComponent<TextMeshProUGUI>());
            Wire(comp, "_trophyText", trophyGO.GetComponent<TextMeshProUGUI>());

            SavePrefab(root, $"{PrefabsDir}/PlayerPill.prefab");
            Object.DestroyImmediate(root);
        }

        // TrophyRow ────────────────────────────────────────────────────────────────
        static void CreateTrophyRow(PushStarsTheme t)
        {
            var root = new GameObject("TrophyRow");
            SetSize(root.AddComponent<RectTransform>(), 320, 52);
            var rootLE = root.AddComponent<LayoutElement>();
            rootLE.preferredHeight = 52;
            rootLE.flexibleWidth   = 1;

            var bg = root.AddComponent<Image>();
            bg.color = t.BtnSecondaryBg;
            if (t.BadgeShape != null) { bg.sprite = t.BadgeShape; bg.type = Image.Type.Sliced; }

            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment         = TextAnchor.MiddleLeft;
            hlg.spacing                = 8;
            hlg.padding                = new RectOffset(12, 12, 8, 8);
            hlg.childControlHeight     = true;
            hlg.childControlWidth      = true;   // required for flexibleWidth to work
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth  = false;

            var rankGO  = MakeTMP(root, "Rank",        t.TextSecondary, "#1",     18, FontStyles.Bold);
            var nameGO  = MakeTMP(root, "Name",        t.TextPrimary,   "Player", 15, FontStyles.Normal);
            var countGO = MakeTMP(root, "TrophyCount", t.TrophyGold,    "1250",   18, FontStyles.Bold);

            var rankLE  = rankGO.AddComponent<LayoutElement>();
            rankLE.minWidth = 40; rankLE.preferredWidth = 40; rankLE.flexibleWidth = 0;
            var nameLE  = nameGO.AddComponent<LayoutElement>();
            nameLE.minWidth = 0; nameLE.flexibleWidth = 1;
            var countLE = countGO.AddComponent<LayoutElement>();
            countLE.minWidth = 60; countLE.preferredWidth = 60; countLE.flexibleWidth = 0;

            countGO.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Right;

            var comp = root.AddComponent<TrophyRow>();
            Wire(comp, "_background",     bg);
            Wire(comp, "_rankText",        rankGO.GetComponent<TextMeshProUGUI>());
            Wire(comp, "_nameText",        nameGO.GetComponent<TextMeshProUGUI>());
            Wire(comp, "_trophyCountText", countGO.GetComponent<TextMeshProUGUI>());

            SavePrefab(root, $"{PrefabsDir}/TrophyRow.prefab");
            Object.DestroyImmediate(root);
        }

        // LoadingVsRing ────────────────────────────────────────────────────────────
        static void CreateLoadingVsRing(PushStarsTheme t)
        {
            var root = new GameObject("LoadingVsRing");
            SetSize(root.AddComponent<RectTransform>(), 200, 200);
            AddLayoutElement(root, 200, 200);

            // ── Outer rotating ring ────────────────────────────────────────────────
            // Prefer the pre-coloured search-animation PNG (VS_search_anim); fall back
            // to the procedural dashed ring (radial fill) so it still works without art.
            var ringSprite = t.RingFinding ?? t.RingShape;

            var ringGO  = new GameObject("Ring");
            ringGO.transform.SetParent(root.transform, false);
            var ringRT  = ringGO.AddComponent<RectTransform>();
            Stretch(ringRT, 0, 0);

            var ringImg = ringGO.AddComponent<Image>();
            if (ringSprite != null)
            {
                // PNG ring: draw as-is. preserveAspect keeps it a perfect circle even
                // when the parent rect isn't square (this fixes the stretched ellipse).
                ringImg.sprite         = ringSprite;
                ringImg.type           = Image.Type.Simple;
                ringImg.color          = (ringSprite == t.RingFinding) ? Color.white : t.AccentBlue;
                ringImg.preserveAspect = true;
            }
            else
            {
                // Procedural fallback: simulate dashed ring via radial fill.
                ringImg.color      = t.AccentBlue;
                ringImg.type       = Image.Type.Filled;
                ringImg.fillMethod = Image.FillMethod.Radial360;
                ringImg.fillAmount = 0.72f;
            }

            var comp = root.AddComponent<LoadingVsRing>();
            Wire(comp, "_ring",      ringRT);
            Wire(comp, "_ringImage", ringImg);

            // ── Centre VS badge (static — sibling of the ring, so it doesn't spin) ──
            if (t.VSBadge != null)
            {
                var badgeGO = new GameObject("VsBadge");
                badgeGO.transform.SetParent(root.transform, false);
                var badgeRT = badgeGO.AddComponent<RectTransform>();
                badgeRT.anchorMin = badgeRT.anchorMax = new Vector2(0.5f, 0.5f);
                badgeRT.pivot     = new Vector2(0.5f, 0.5f);
                badgeRT.sizeDelta = new Vector2(120f, 120f); // ~60 % of the 200 px ring

                var badgeImg = badgeGO.AddComponent<Image>();
                badgeImg.sprite         = t.VSBadge;
                badgeImg.type           = Image.Type.Simple;
                badgeImg.color          = Color.white; // pre-coloured PNG, no tint
                badgeImg.preserveAspect = true;
            }
            else
            {
                // No badge art: fall back to a centred "VS" label.
                var vsGO = MakeTMP(root, "VS", t.TextPrimary, "VS", 52, FontStyles.Bold);
                Stretch(vsGO.GetComponent<RectTransform>(), 0, 0);
                vsGO.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
                Wire(comp, "_vsLabel", vsGO.GetComponent<TextMeshProUGUI>());
            }

            SavePrefab(root, $"{PrefabsDir}/LoadingVsRing.prefab");
            Object.DestroyImmediate(root);
        }

        // ExitButton ───────────────────────────────────────────────────────────────
        // Visual: vivid coral-red border (full-size pill) + very dark maroon fill
        // (inset 2 px) + bold white label.  Matches the ВЫЙТИ pill in the
        // Search Opponent mockup.
        static void CreateExitButton(PushStarsTheme t)
        {
            const float W = 200f, H = 56f;
            const float BorderPx = 3f;

            // Use the procedural white capsule sprite so Image.color tinting works
            // correctly. btn_start.png / BtnShape may be a pre-coloured Figma asset.
            var pillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                                 $"{SpriteFactory.SpritesDir}/pill_capsule.png")
                          ?? AssetDatabase.LoadAssetAtPath<Sprite>(
                                 $"{SpriteFactory.SpritesDir}/pill_24.png");

            var root = new GameObject("ExitButton");
            SetSize(root.AddComponent<RectTransform>(), W, H);
            var exitLE = root.AddComponent<LayoutElement>();
            exitLE.preferredWidth  = W;
            exitLE.preferredHeight = H;
            exitLE.flexibleWidth   = 0; // never stretch to fill parent

            Image borderImg;

            if (t.BtnExitShape != null)
            {
                // ── Figma mode: scale proportionally inside fixed bounds, no deformation ──
                var bgImg = root.AddComponent<Image>();
                bgImg.sprite         = t.BtnExitShape;
                bgImg.type           = Image.Type.Simple;
                bgImg.preserveAspect = true;
                bgImg.color          = Color.white;
                bgImg.raycastTarget  = true;

                // Recalculate preferred width from the sprite's aspect ratio at H=56.
                var s = t.BtnExitShape;
                float aspect = s.rect.width / s.rect.height;
                exitLE.preferredWidth  = Mathf.Round(56f * aspect);
                exitLE.preferredHeight = 56f;
                SetSize(root.GetComponent<RectTransform>(), exitLE.preferredWidth, 56f);

                var btn = root.AddComponent<Button>();
                btn.targetGraphic = bgImg;
                var cb = btn.colors;
                cb.normalColor      = Color.white;
                cb.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
                cb.pressedColor     = new Color(1f, 1f, 1f, 0.65f);
                cb.selectedColor    = Color.white;
                btn.colors = cb;

                borderImg = bgImg; // _border wired to the single image for alpha control
            }
            else
            {
                // ── Procedural mode: white capsule tinted to border + fill colours ──
                var hitImg = root.AddComponent<Image>();
                hitImg.color = Color.clear;
                hitImg.raycastTarget = true;
                if (pillSprite != null) { hitImg.sprite = pillSprite; hitImg.type = Image.Type.Sliced; }

                var btn = root.AddComponent<Button>();
                btn.targetGraphic = hitImg;
                var cb = btn.colors;
                cb.normalColor      = Color.white;
                cb.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
                cb.pressedColor     = new Color(1f, 1f, 1f, 0.65f);
                cb.selectedColor    = Color.white;
                btn.colors = cb;

                var borderGO = new GameObject("BorderLayer");
                borderGO.transform.SetParent(root.transform, false);
                Stretch(borderGO.AddComponent<RectTransform>(), 0, 0);
                borderImg = borderGO.AddComponent<Image>();
                borderImg.color = t.BtnExitBorder;
                if (pillSprite != null) { borderImg.sprite = pillSprite; borderImg.type = Image.Type.Sliced; }

                var fillGO = new GameObject("FillLayer");
                fillGO.transform.SetParent(root.transform, false);
                Stretch(fillGO.AddComponent<RectTransform>(), BorderPx, BorderPx);
                var fillImg = fillGO.AddComponent<Image>();
                fillImg.color = t.BtnExitFill;
                if (pillSprite != null) { fillImg.sprite = pillSprite; fillImg.type = Image.Type.Sliced; }

                var comp2 = root.AddComponent<ExitButton>();
                Wire(comp2, "_border",       borderImg);
                Wire(comp2, "_fill",         fillImg);
                Wire(comp2, "_label",        MakeButtonLabel(root, t, "ВЫЙТИ", BorderPx, t.BtnExitFg));
                Wire(comp2, "_fallbackText", "ВЫЙТИ");
                SavePrefab(root, $"{PrefabsDir}/ExitButton.prefab");
                Object.DestroyImmediate(root);
                return;
            }

            var comp = root.AddComponent<ExitButton>();
            Wire(comp, "_border",       borderImg);
            Wire(comp, "_label",        MakeButtonLabel(root, t, "ВЫЙТИ", 0, t.BtnExitFg));
            Wire(comp, "_fallbackText", "ВЫЙТИ");

            SavePrefab(root, $"{PrefabsDir}/ExitButton.prefab");
            Object.DestroyImmediate(root);
        }

        // ReadyButton ──────────────────────────────────────────────────────────────
        // Same pill-border structure as ExitButton, green palette.
        static void CreateReadyButton(PushStarsTheme t)
        {
            const float W = 200f, H = 56f;
            const float BorderPx = 3f;

            var pillSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                                 $"{SpriteFactory.SpritesDir}/pill_capsule.png")
                          ?? AssetDatabase.LoadAssetAtPath<Sprite>(
                                 $"{SpriteFactory.SpritesDir}/pill_24.png");

            var root = new GameObject("ReadyButton");
            SetSize(root.AddComponent<RectTransform>(), W, H);
            var readyLE = root.AddComponent<LayoutElement>();
            readyLE.preferredWidth  = W;
            readyLE.preferredHeight = H;
            readyLE.flexibleWidth   = 0; // never stretch to fill parent

            Image borderImg;

            if (t.BtnReadyShape != null)
            {
                // ── Figma mode: scale proportionally inside fixed bounds, no deformation ──
                var bgImg = root.AddComponent<Image>();
                bgImg.sprite         = t.BtnReadyShape;
                bgImg.type           = Image.Type.Simple;
                bgImg.preserveAspect = true;
                bgImg.color          = Color.white;
                bgImg.raycastTarget  = true;

                var s = t.BtnReadyShape;
                float aspect = s.rect.width / s.rect.height;
                readyLE.preferredWidth  = Mathf.Round(56f * aspect);
                readyLE.preferredHeight = 56f;
                SetSize(root.GetComponent<RectTransform>(), readyLE.preferredWidth, 56f);

                var btn = root.AddComponent<Button>();
                btn.targetGraphic = bgImg;
                var cb = btn.colors;
                cb.normalColor      = Color.white;
                cb.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
                cb.pressedColor     = new Color(1f, 1f, 1f, 0.65f);
                cb.selectedColor    = Color.white;
                btn.colors = cb;

                borderImg = bgImg;
            }
            else
            {
                var hitImg = root.AddComponent<Image>();
                hitImg.color = Color.clear;
                hitImg.raycastTarget = true;
                if (pillSprite != null) { hitImg.sprite = pillSprite; hitImg.type = Image.Type.Sliced; }

                var btn = root.AddComponent<Button>();
                btn.targetGraphic = hitImg;
                var cb = btn.colors;
                cb.normalColor      = Color.white;
                cb.highlightedColor = new Color(1f, 1f, 1f, 0.85f);
                cb.pressedColor     = new Color(1f, 1f, 1f, 0.65f);
                cb.selectedColor    = Color.white;
                btn.colors = cb;

                var borderGO = new GameObject("BorderLayer");
                borderGO.transform.SetParent(root.transform, false);
                Stretch(borderGO.AddComponent<RectTransform>(), 0, 0);
                borderImg = borderGO.AddComponent<Image>();
                borderImg.color = t.BtnReadyBorder;
                if (pillSprite != null) { borderImg.sprite = pillSprite; borderImg.type = Image.Type.Sliced; }

                var fillGO = new GameObject("FillLayer");
                fillGO.transform.SetParent(root.transform, false);
                Stretch(fillGO.AddComponent<RectTransform>(), BorderPx, BorderPx);
                var fillImg = fillGO.AddComponent<Image>();
                fillImg.color = t.BtnReadyFill;
                if (pillSprite != null) { fillImg.sprite = pillSprite; fillImg.type = Image.Type.Sliced; }

                var comp2 = root.AddComponent<ReadyButton>();
                Wire(comp2, "_border",       borderImg);
                Wire(comp2, "_fill",         fillImg);
                Wire(comp2, "_label",        MakeButtonLabel(root, t, "ГОТОВ", BorderPx, t.BtnReadyFg));
                Wire(comp2, "_fallbackText", "ГОТОВ");
                SavePrefab(root, $"{PrefabsDir}/ReadyButton.prefab");
                Object.DestroyImmediate(root);
                return;
            }

            var comp = root.AddComponent<ReadyButton>();
            Wire(comp, "_border",       borderImg);
            Wire(comp, "_label",        MakeButtonLabel(root, t, "ГОТОВ", 0, t.BtnReadyFg));
            Wire(comp, "_fallbackText", "ГОТОВ");

            SavePrefab(root, $"{PrefabsDir}/ReadyButton.prefab");
            Object.DestroyImmediate(root);
        }

        // Shared label helper used by ExitButton / ReadyButton builders.
        static TextMeshProUGUI MakeButtonLabel(GameObject root, PushStarsTheme t,
                                               string text, float hPad, Color labelColor)
        {
            var labelGO = MakeTMP(root, "Label", labelColor, text, 20, FontStyles.Normal);
            var tmp = labelGO.GetComponent<TextMeshProUGUI>();

            // Override with Rubik Medium — FontStyles has no Medium variant so we
            // swap the font asset directly after MakeTMP sets up Regular.
            var medium = FontSetup.LoadMedium();
            if (medium != null) { tmp.font = medium; tmp.fontStyle = FontStyles.Normal; }

            Stretch(labelGO.GetComponent<RectTransform>(), hPad + 8, hPad);
            tmp.alignment = TextAlignmentOptions.Center;
            return tmp;
        }
        // ── Gallery Scene ─────────────────────────────────────────────────────────
        static void BuildGalleryScene(PushStarsTheme theme)
        {
            // Save & close any open scene first to avoid TMP layout warnings from its
            // ScrollRect firing one last LateUpdate while the new scene loads.
            var active = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            if (active.isDirty)
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(active);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // EventSystem
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();

            // Canvas
            var canvasGO = new GameObject("UICanvas");
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390, 844); // iPhone 14 Pro
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            // UI Gallery is a developer tool — view it in the Game tab, not
            // the Device Simulator. No MirrorRoot / flip fix here because the
            // 180° rotation breaks ScrollRect + Mask clipping.

            // Full-screen dark background
            var bgGO  = MakePanel(canvasGO, "Background", theme.BgDark);
            Stretch(bgGO.GetComponent<RectTransform>(), 0, 0);

            // Safe area panel
            var safeGO = new GameObject("SafeArea");
            safeGO.transform.SetParent(canvasGO.transform, false);
            Stretch(safeGO.AddComponent<RectTransform>(), 0, 0);
            safeGO.AddComponent<SafeAreaFitter>();

            // Scroll view
            var contentRT = BuildScrollContent(safeGO, theme);

            // Force ContentSizeFitter to calculate heights before saving.
            Canvas.ForceUpdateCanvases();
            if (contentRT != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRT);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[UIGallery] Scene saved: {ScenePath}");
        }

        static RectTransform BuildScrollContent(GameObject parent, PushStarsTheme theme)
        {
            // ScrollRect container
            var scrollGO = new GameObject("ScrollView");
            scrollGO.transform.SetParent(parent.transform, false);
            Stretch(scrollGO.AddComponent<RectTransform>(), 0, 0);
            var scroll      = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal              = false;
            scroll.verticalScrollbar       = null;
            scroll.horizontalScrollbar     = null;
            scroll.verticalScrollbarVisibility   = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // Viewport — stencil-buffer Mask (RectMask2D doesn't support rotated
            // parents, which breaks under DeviceSimulatorMirrorFix's 180° flip).
            var vpGO    = new GameObject("Viewport");
            vpGO.transform.SetParent(scrollGO.transform, false);
            var vpRT = vpGO.AddComponent<RectTransform>();
            Stretch(vpRT, 0, 0);

            var vpImg          = vpGO.AddComponent<Image>();
            vpImg.color        = new Color(1f, 1f, 1f, 0.01f); // near-invisible but maskable
            vpImg.raycastTarget = true;
            var vpMask         = vpGO.AddComponent<Mask>();
            vpMask.showMaskGraphic = false;

            scroll.viewport = vpRT;

            // Content pane
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(vpGO.transform, false);
            var contentRT   = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot     = new Vector2(0.5f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            scroll.content = contentRT;

            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment        = TextAnchor.UpperCenter;
            vlg.spacing               = 16;
            vlg.padding               = new RectOffset(20, 20, 28, 32);
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = true;
            vlg.childForceExpandWidth = true;

            var csf = contentGO.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ── Load prefab references ────────────────────────────────────────────
            var btnPrefab   = Load("PrimaryButton");
            var chipPrefab  = Load("SecondaryChip");
            var badgePrefab = Load("StatBadge");
            var pillPrefab  = Load("PlayerPill");
            var rowPrefab   = Load("TrophyRow");
            var ringPrefab  = Load("LoadingVsRing");
            var exitPrefab  = Load("ExitButton");
            var readyPrefab = Load("ReadyButton");

            // ── 0. TITLE BAR ──────────────────────────────────────────────────────
            BuildTitleBar(contentGO, theme);

            // ── 1. COLOR PALETTE ──────────────────────────────────────────────────
            AddSectionHeader(contentGO, "1 · COLOR PALETTE", theme.AccentYellow);
            BuildColorPalette(contentGO, theme);

            // ── 2. TYPOGRAPHY (Rubik) ─────────────────────────────────────────────
            AddSectionHeader(contentGO, "2 · TYPOGRAPHY  (Rubik)", theme.AccentYellow);
            BuildTypography(contentGO, theme);

            // ── 3. COMPONENTS ─────────────────────────────────────────────────────
            AddSectionHeader(contentGO, "3 · COMPONENTS", theme.AccentYellow);

            AddSubHeader(contentGO, "Buttons", theme.TextSecondary);
            var btn = Spawn(btnPrefab, contentGO.transform);
            btn?.GetComponent<PrimaryButton>()?.SetLabel("FIND OPPONENT  +50");

            var btnSuccess = Spawn(btnPrefab, contentGO.transform);
            if (btnSuccess != null)
            {
                btnSuccess.GetComponent<Image>().color = theme.BtnSuccessBg;
                btnSuccess.GetComponent<PrimaryButton>()?.SetLabel("I'M READY");
            }

            var btnDanger = Spawn(btnPrefab, contentGO.transform);
            if (btnDanger != null)
            {
                btnDanger.GetComponent<Image>().color = theme.BtnDangerBg;
                btnDanger.GetComponent<PrimaryButton>()?.SetLabel("EXIT");
            }

            AddSubHeader(contentGO, "Exit Button (ВЫЙТИ — Search Opponent)", theme.TextSecondary);
            var exitRow = MakeHStack(contentGO.transform, "ExitButtonRow");
            var exitBtn = Spawn(exitPrefab, exitRow.transform);
            exitBtn?.GetComponent<ExitButton>()?.SetLabel("ВЫЙТИ");

            AddSubHeader(contentGO, "Ready Button (ГОТОВ — confirm screen)", theme.TextSecondary);
            var readyRow = MakeHStack(contentGO.transform, "ReadyButtonRow");
            var readyBtn = Spawn(readyPrefab, readyRow.transform);
            readyBtn?.GetComponent<ReadyButton>()?.SetLabel("ГОТОВ");

            AddSubHeader(contentGO, "Mode Chips", theme.TextSecondary);
            var chipsRow = MakeHStack(contentGO.transform, "ChipsRow");
            var c1 = Spawn(chipPrefab, chipsRow.transform);
            var c2 = Spawn(chipPrefab, chipsRow.transform);
            var c3 = Spawn(chipPrefab, chipsRow.transform);
            c1?.GetComponent<SecondaryChip>()?.SetLabel("ДУЭЛЬ");
            c2?.GetComponent<SecondaryChip>()?.SetLabel("отжим.");
            c3?.GetComponent<SecondaryChip>()?.SetLabel("60 сек");
            if (c1 != null) { c1.GetComponent<SecondaryChip>()?.SetIcon(theme.IconLightning); c1.GetComponent<SecondaryChip>()?.SetSelected(true); }
            if (c2 != null) { c2.GetComponent<SecondaryChip>()?.SetIcon(theme.IconPushup);    c2.GetComponent<SecondaryChip>()?.SetSelected(false); }
            if (c3 != null) { c3.GetComponent<SecondaryChip>()?.SetIcon(theme.IconTime);      c3.GetComponent<SecondaryChip>()?.SetSelected(false); }

            AddSubHeader(contentGO, "Stat Badges", theme.TextSecondary);
            var badgesRow = MakeHStack(contentGO.transform, "BadgesRow");
            var b1 = Spawn(badgePrefab, badgesRow.transform);
            var b2 = Spawn(badgePrefab, badgesRow.transform);
            var b3 = Spawn(badgePrefab, badgesRow.transform);
            b1?.GetComponent<StatBadge>()?.SetStat("142",  "MAX PUSHUP");
            b2?.GetComponent<StatBadge>()?.SetStat("87%",  "WIN RATE");
            b3?.GetComponent<StatBadge>()?.SetStat("1250", "TROPHIES");

            AddSubHeader(contentGO, "Player Pill", theme.TextSecondary);
            var pill = Spawn(pillPrefab, contentGO.transform);
            pill?.GetComponent<PlayerPill>()?.SetPlayer("US", "IronMike_88", 1432);

            AddSubHeader(contentGO, "Trophy Rows", theme.TextSecondary);
            string[] names = { "IronMike_88", "PushKing99", "FitWarrior" };
            int[]    pts   = {          1500,         1350,         1200 };
            for (int i = 0; i < 3; i++)
            {
                var row = Spawn(rowPrefab, contentGO.transform);
                row?.GetComponent<TrophyRow>()?.SetRow(i + 1, names[i], pts[i], i == 0);
            }

            AddSubHeader(contentGO, "Loading VS Ring", theme.TextSecondary);
            Spawn(ringPrefab, contentGO.transform);

            AddSubHeader(contentGO, "Bottom Navigation (tab states)", theme.TextSecondary);
            AddPlainText(contentGO,
                "Inactive tabs use the not-active Figma icons; the active Profile tab uses profile_active.png.",
                theme.TextSecondary, 11, FontStyles.Normal, TextAlignmentOptions.Left, 16);

            // Row 1 — Duel (VS) active            Row 2 — Profile active
            var navRowDuel = MakeHStack(contentGO.transform, "NavRow_DuelActive");
            MakeNavIcon(navRowDuel, theme, "League",  false, "II", theme.NavLeague,  null);
            MakeNavIcon(navRowDuel, theme, "Duel",    true,  "VS", null,             null);
            MakeNavIcon(navRowDuel, theme, "Profile", false, "@",  theme.NavProfile, theme.NavProfileActive);

            var navRowProfile = MakeHStack(contentGO.transform, "NavRow_ProfileActive");
            MakeNavIcon(navRowProfile, theme, "League",  false, "II", theme.NavLeague,  null);
            MakeNavIcon(navRowProfile, theme, "Duel",    false, "VS", null,             null);
            MakeNavIcon(navRowProfile, theme, "Profile", true,  "@",  theme.NavProfile, theme.NavProfileActive);

            // ── 4. PANELS (procedural rounded cards — no PNG needed) ──────────────
            AddSectionHeader(contentGO, "4 · PANELS  (procedural, no PNG needed)", theme.AccentYellow);
            AddPlainText(contentGO,
                "All cards use Image + 9-slice pill sprite + Image.color tint. One sprite, infinite sizes.",
                theme.TextSecondary, 11, FontStyles.Normal, TextAlignmentOptions.Left, 20);
            BuildMatchHistoryCards(contentGO, theme);

            // ── 5. SPRITE INVENTORY ───────────────────────────────────────────────
            AddSectionHeader(contentGO, "5 · SPRITE INVENTORY", theme.AccentYellow);
            BuildSpriteInventory(contentGO, theme);

            return contentRT;
        }

        // ── Dashboard sections ────────────────────────────────────────────────────
        static void BuildTitleBar(GameObject parent, PushStarsTheme theme)
        {
            var (assigned, total) = CountAssignedSprites(theme);

            var card = MakePanel(parent, "TitleCard", theme.NavBg);
            AddLayoutElement(card, 0, 88, flexible: false);
            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 12, 12);
            vlg.spacing = 4;
            vlg.childAlignment        = TextAnchor.MiddleLeft;
            vlg.childControlWidth     = true;
            vlg.childControlHeight    = false;
            vlg.childForceExpandWidth = true;

            AddPlainText(card, "PUSH STARS · Design System",
                         theme.TextPrimary, 18, FontStyles.Bold,  TextAlignmentOptions.Left, 24);
            AddPlainText(card, "Phase 03 — reusable components, theme tokens, Rubik typography",
                         theme.TextSecondary, 11, FontStyles.Normal, TextAlignmentOptions.Left, 16);

            string statusColor = assigned == total ? "#6BFF4A"
                              : assigned >  0     ? "#F5C842"
                                                  : "#C62828";
            AddPlainText(card, $"<color={statusColor}>{assigned}/{total}</color> sprite slots filled from Figma",
                         theme.TextPrimary, 12, FontStyles.Bold, TextAlignmentOptions.Left, 18);
        }

        static void BuildColorPalette(GameObject parent, PushStarsTheme theme)
        {
            var swatches = new (string label, Color color)[]
            {
                ("BG Dark",        theme.BgDark),
                ("Nav Bg",         theme.NavBg),
                ("Accent Blue",    theme.AccentBlue),
                ("Accent Yellow",  theme.AccentYellow),
                ("Accent Lime",    theme.AccentLime),
                ("Trophy Gold",    theme.TrophyGold),
                ("Danger Red",     theme.DangerRed),
                ("Arena Red",      theme.ArenaRed),
                ("Arena Blue",     theme.ArenaBlue),
                ("Btn Success",    theme.BtnSuccessBg),
                ("Text Primary",   theme.TextPrimary),
                ("Text Secondary", theme.TextSecondary),
            };

            var grid = MakeGrid(parent.transform, "ColorGrid", cellSize: new Vector2(108, 56),
                                 spacing: new Vector2(8, 8), columns: 3);
            foreach (var (label, color) in swatches)
                MakeSwatch(grid.transform, label, color, theme);
        }

        static void MakeSwatch(Transform parent, string label, Color color, PushStarsTheme theme)
        {
            var go = new GameObject($"Swatch_{label}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var bgImg   = go.AddComponent<Image>();
            bgImg.color = color;

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);
            var labelRT = labelGO.GetComponent<RectTransform>();
            Stretch(labelRT, 4, 2);

            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = $"{label}\n<size=9><color=#FFFFFF99>{ColorHex(color)}</color></size>";
            tmp.fontSize  = 11;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = ContrastingTextColor(color);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.richText  = true;
            var rubik = FontSetup.Resolve(FontStyles.Bold, out var rem);
            if (rubik != null) { tmp.font = rubik; tmp.fontStyle = rem; }
        }

        static void BuildTypography(GameObject parent, PushStarsTheme theme)
        {
            var samples = new (string label, string text, float size, FontStyles style)[]
            {
                ("H1 / 32 / Bold",            "Push Stars",            32, FontStyles.Bold),
                ("H2 / 24 / Bold",            "ПОИСК СОПЕРНИКА",       24, FontStyles.Bold),
                ("H3 / 18 / Bold",            "FIND OPPONENT",         18, FontStyles.Bold),
                ("Body / 16 / Regular",       "Auras can be used to buy unique animations", 16, FontStyles.Normal),
                ("Body / 14 / Italic",        "tip: keep your form straight", 14, FontStyles.Italic),
                ("Caption / 12 / Bold",       "MAX PUSHUP",            12, FontStyles.Bold),
                ("Caption / 12 / BoldItalic", "VS",                    12, FontStyles.Bold | FontStyles.Italic),
            };

            foreach (var (label, text, size, style) in samples)
                MakeTypeSample(parent, label, text, size, style, theme);
        }

        static void MakeTypeSample(GameObject parent, string label, string sampleText,
                                    float size, FontStyles style, PushStarsTheme theme)
        {
            var row = new GameObject($"Type_{label}", typeof(RectTransform));
            row.transform.SetParent(parent.transform, false);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding             = new RectOffset(8, 8, 4, 4);
            hlg.spacing             = 12;
            hlg.childAlignment      = TextAnchor.MiddleLeft;
            hlg.childControlWidth   = false;
            hlg.childControlHeight  = true;

            AddLayoutElement(row, 0, Mathf.Max(size + 12, 28), flexible: false);

            // Spec label (left)
            var specGO = MakeTMP(row, "Spec", theme.TextSecondary, label, 10, FontStyles.Normal);
            specGO.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;
            AddLayoutElement(specGO, 130, 0, flexible: false);

            // Sample (right)
            var sampleGO = MakeTMP(row, "Sample", theme.TextPrimary, sampleText, size, style);
            sampleGO.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;
            sampleGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        }

        static void BuildMatchHistoryCards(GameObject parent, PushStarsTheme theme)
        {
            const float CardH    = 48f;
            const float BorderPx = 1f;

            var fillColor   = new Color32( 31,  34,  48, 255); // #1F2230
            var borderColor = new Color32( 51,  52,  63, 255); // #33343F

            // Procedural white rounded-rect — generated by SpriteFactory, never a Figma PNG.
            var cardSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                                 $"{SpriteFactory.SpritesDir}/card_history.png")
                          ?? AssetDatabase.LoadAssetAtPath<Sprite>(
                                 $"{SpriteFactory.SpritesDir}/pill_24.png");

            var matches = new (string opp, string result, string score, string mode, bool win)[]
            {
                ("PushKing99",  "WIN",  "24 – 18", "DUEL",   true),
                ("FitWarrior",  "LOSE", "11 – 19", "60 СЕК", false),
                ("IronMike_88", "WIN",  "32 – 27", "PUSHUP", true),
            };

            foreach (var (opp, result, score, mode, win) in matches)
            {
                // ── 1px border ring ───────────────────────────────────────────────
                var card = new GameObject($"MatchCard_{opp}", typeof(RectTransform));
                card.transform.SetParent(parent.transform, false);
                var borderImg = card.AddComponent<Image>();
                borderImg.sprite = cardSprite;
                borderImg.type   = Image.Type.Sliced;
                borderImg.color  = borderColor;
                var cardLE = card.AddComponent<LayoutElement>();
                cardLE.preferredHeight = CardH;

                // ── Fill (inset 1 px from border) ─────────────────────────────────
                var fillGO = new GameObject("Fill");
                fillGO.transform.SetParent(card.transform, false);
                Stretch(fillGO.AddComponent<RectTransform>(), BorderPx, BorderPx);
                var fillImg = fillGO.AddComponent<Image>();
                fillImg.sprite = cardSprite;
                fillImg.type   = Image.Type.Sliced;
                fillImg.color  = fillColor;

                // ── Horizontal content layout ─────────────────────────────────────
                var hlg = fillGO.AddComponent<HorizontalLayoutGroup>();
                hlg.padding               = new RectOffset(10, 12, 0, 0);
                hlg.spacing               = 8;
                hlg.childAlignment        = TextAnchor.MiddleLeft;
                hlg.childControlWidth     = true;
                hlg.childControlHeight    = false;
                hlg.childForceExpandWidth  = false;
                hlg.childForceExpandHeight = true;

                // ── Win / Lose icon ───────────────────────────────────────────────
                var badge  = new GameObject("Result", typeof(RectTransform));
                badge.transform.SetParent(fillGO.transform, false);
                var badgeLE = badge.AddComponent<LayoutElement>();
                badgeLE.minWidth = badgeLE.preferredWidth = 32f;
                badgeLE.flexibleWidth = 0;

                var resultIcon = win ? theme.IconWin : theme.IconLose;
                if (resultIcon != null)
                {
                    var iconImg = badge.AddComponent<Image>();
                    iconImg.sprite         = resultIcon;
                    iconImg.type           = Image.Type.Simple;
                    iconImg.preserveAspect = true;
                    iconImg.color          = Color.white;
                    iconImg.raycastTarget  = false;
                }
                else
                {
                    var badgeImg = badge.AddComponent<Image>();
                    badgeImg.sprite = cardSprite;
                    badgeImg.type   = Image.Type.Sliced;
                    badgeImg.color  = win ? theme.BtnSuccessBg : theme.BtnDangerBg;
                    var lbl   = MakeTMP(badge, "Lbl", theme.TextPrimary, result, 10, FontStyles.Bold);
                    var lblRT = lbl.GetComponent<RectTransform>();
                    lblRT.anchorMin = Vector2.zero; lblRT.anchorMax = Vector2.one;
                    lblRT.offsetMin = lblRT.offsetMax = Vector2.zero;
                    lbl.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
                }

                // ── Opponent name ─────────────────────────────────────────────────
                var nameGO = MakeTMP(fillGO, "Opponent", theme.TextPrimary, opp, 13, FontStyles.Bold);
                nameGO.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Left;
                nameGO.AddComponent<LayoutElement>().flexibleWidth = 1;

                // ── Score ─────────────────────────────────────────────────────────
                var scoreLbl = MakeTMP(fillGO, "Score", theme.TextSecondary, score, 11, FontStyles.Normal);
                scoreLbl.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Right;
                var scoreLE = scoreLbl.AddComponent<LayoutElement>();
                scoreLE.minWidth = scoreLE.preferredWidth = 50f; scoreLE.flexibleWidth = 0;

                // ── Mode chip ─────────────────────────────────────────────────────
                // Layout cell controls only the width; the row's HLG force-expands
                // child height, so the visible pill lives in a fixed-size, centred
                // child that is immune to that stretch (otherwise a 54×46 element with
                // a 24 px-radius sprite renders as a tall over-rounded blob).
                var chipSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                                     $"{SpriteFactory.SpritesDir}/pill_12.png") ?? cardSprite;

                var modeGO = new GameObject("Mode", typeof(RectTransform));
                modeGO.transform.SetParent(fillGO.transform, false);
                var modeLE = modeGO.AddComponent<LayoutElement>();
                modeLE.minWidth = modeLE.preferredWidth = 54f; modeLE.flexibleWidth = 0;

                var pillGO  = new GameObject("Pill", typeof(RectTransform));
                pillGO.transform.SetParent(modeGO.transform, false);
                var pillRT  = pillGO.GetComponent<RectTransform>();
                pillRT.anchorMin = pillRT.anchorMax = new Vector2(0.5f, 0.5f);
                pillRT.pivot     = new Vector2(0.5f, 0.5f);
                pillRT.sizeDelta = new Vector2(54f, 22f);
                var modeImg = pillGO.AddComponent<Image>();
                modeImg.sprite = chipSprite; // small rounded chip (12 px radius)
                modeImg.type   = Image.Type.Sliced;
                modeImg.color  = theme.BtnSecondaryBg;

                var modeLbl = MakeTMP(pillGO, "Lbl", theme.ChipNormalFg, mode, 9, FontStyles.Normal);
                var modeRT  = modeLbl.GetComponent<RectTransform>();
                modeRT.anchorMin = Vector2.zero; modeRT.anchorMax = Vector2.one;
                modeRT.offsetMin = modeRT.offsetMax = Vector2.zero;
                modeLbl.GetComponent<TextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            }
        }

        static void BuildSpriteInventory(GameObject parent, PushStarsTheme theme)
        {
            var spriteFields = typeof(PushStarsTheme).GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var f in spriteFields)
            {
                if (f.FieldType != typeof(Sprite)) continue;
                var sprite = f.GetValue(theme) as Sprite;
                MakeSpriteRow(parent, f.Name, sprite, theme);
            }
        }

        static void MakeSpriteRow(GameObject parent, string slotName, Sprite sprite, PushStarsTheme theme)
        {
            var row = new GameObject($"Sprite_{slotName}", typeof(RectTransform));
            row.transform.SetParent(parent.transform, false);
            row.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.padding             = new RectOffset(10, 10, 8, 8);
            hlg.spacing             = 12;
            hlg.childAlignment      = TextAnchor.MiddleLeft;
            hlg.childControlWidth   = false;
            hlg.childControlHeight  = false;

            AddLayoutElement(row, 0, 56, flexible: false);

            // Thumbnail (40×40)
            var thumbGO = new GameObject("Thumb", typeof(RectTransform));
            thumbGO.transform.SetParent(row.transform, false);
            AddLayoutElement(thumbGO, 40, 40);

            var bg = new GameObject("Bg", typeof(RectTransform));
            bg.transform.SetParent(thumbGO.transform, false);
            Stretch(bg.GetComponent<RectTransform>(), 0, 0);
            bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

            var thumb = new GameObject("Img", typeof(RectTransform));
            thumb.transform.SetParent(thumbGO.transform, false);
            Stretch(thumb.GetComponent<RectTransform>(), 4, 4);
            var thumbImg = thumb.AddComponent<Image>();
            thumbImg.preserveAspect = true;
            if (sprite != null)
            {
                thumbImg.sprite = sprite;
                thumbImg.color  = Color.white;
            }
            else
            {
                thumbImg.color = new Color(0.4f, 0.4f, 0.5f, 0.6f);
            }

            // Slot name + status
            string status, statusColor, source;
            if (sprite == null)
            {
                status      = "MISSING";
                statusColor = "#C62828";
                source      = "—";
            }
            else
            {
                string assetPath = AssetDatabase.GetAssetPath(sprite);
                string fileName  = string.IsNullOrEmpty(assetPath)
                                  ? sprite.name
                                  : System.IO.Path.GetFileName(assetPath);

                bool isProcedural = assetPath.Contains("/UI/Sprites/")
                                    && (fileName.StartsWith("pill_") || fileName.StartsWith("circle_") ||
                                        fileName.StartsWith("dashed_ring_"));
                if (isProcedural)
                {
                    status      = "PROCEDURAL";
                    statusColor = "#F5C842";
                }
                else
                {
                    status      = "FIGMA";
                    statusColor = "#6BFF4A";
                }
                source = fileName;
            }

            var info = new GameObject("Info", typeof(RectTransform));
            info.transform.SetParent(row.transform, false);
            var infoLE = info.AddComponent<LayoutElement>();
            infoLE.flexibleWidth = 1;
            infoLE.preferredHeight = 40;

            var vlg = info.AddComponent<VerticalLayoutGroup>();
            vlg.spacing             = 2;
            vlg.childAlignment      = TextAnchor.MiddleLeft;
            vlg.childControlWidth   = true;
            vlg.childControlHeight  = false;

            AddPlainText(info, slotName,
                         theme.TextPrimary, 13, FontStyles.Bold,
                         TextAlignmentOptions.Left, 18);
            AddPlainText(info, $"<color={statusColor}>[{status}]</color>  <color=#FFFFFF99>{source}</color>",
                         theme.TextSecondary, 10, FontStyles.Normal,
                         TextAlignmentOptions.Left, 14);
        }

        // ── Helpers for the dashboard ─────────────────────────────────────────────
        static void AddSectionHeader(GameObject parent, string text, Color color)
        {
            var go = new GameObject("Section_" + text, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<Image>().color = new Color(color.r, color.g, color.b, 0.15f);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 36;

            var labelGO = new GameObject("Label", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);
            Stretch(labelGO.GetComponent<RectTransform>(), 12, 2);

            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.color     = color;
            tmp.fontSize  = 14;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Left;
            var rubik = FontSetup.Resolve(FontStyles.Bold, out var rem);
            if (rubik != null) { tmp.font = rubik; tmp.fontStyle = rem; }
        }

        static void AddSubHeader(GameObject parent, string text, Color color)
        {
            AddPlainText(parent, text, color, 11, FontStyles.Bold,
                         TextAlignmentOptions.Left, 22);
        }

        static GameObject AddPlainText(GameObject parent, string text, Color color,
                                        float size, FontStyles style, TextAlignmentOptions align,
                                        float preferredHeight)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.color     = color;
            tmp.fontSize  = size;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.richText  = true;
            var rubik = FontSetup.Resolve(style, out var remaining);
            if (rubik != null) { tmp.font = rubik; tmp.fontStyle = remaining; }
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = preferredHeight;
            return go;
        }

        static GameObject MakeGrid(Transform parent, string name, Vector2 cellSize,
                                    Vector2 spacing, int columns)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var grid = go.AddComponent<GridLayoutGroup>();
            grid.cellSize        = cellSize;
            grid.spacing         = spacing;
            grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;

            var csf = go.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        static (int assigned, int total) CountAssignedSprites(PushStarsTheme theme)
        {
            int assigned = 0, total = 0;
            foreach (var f in typeof(PushStarsTheme).GetFields(
                         System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (f.FieldType != typeof(Sprite)) continue;
                total++;
                var sprite = f.GetValue(theme) as Sprite;
                if (sprite == null) continue;
                string path = AssetDatabase.GetAssetPath(sprite);
                string file = System.IO.Path.GetFileName(path);
                bool procedural = file.StartsWith("pill_") || file.StartsWith("circle_") ||
                                  file.StartsWith("dashed_ring_");
                if (!procedural) assigned++;
            }
            return (assigned, total);
        }

        static string ColorHex(Color c)
        {
            var c32 = (Color32)c;
            return $"#{c32.r:X2}{c32.g:X2}{c32.b:X2}";
        }

        static Color ContrastingTextColor(Color bg)
        {
            // Perceived brightness — white text on dark backgrounds, black on light.
            float lum = 0.299f * bg.r + 0.587f * bg.g + 0.114f * bg.b;
            return lum > 0.55f ? new Color(0.05f, 0.05f, 0.05f) : Color.white;
        }

        // ── Scene helpers ─────────────────────────────────────────────────────────
        static GameObject MakePanel(GameObject parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            go.AddComponent<Image>().color = color;
            return go;
        }

        static void AddHeader(GameObject parent, string text, Color color, float size = 13)
        {
            var go = new GameObject($"Header");
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.color     = color;
            tmp.fontSize  = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Left;
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = 30;
        }

        static GameObject MakeHStack(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.childAlignment        = TextAnchor.MiddleCenter;
            hlg.spacing               = 12;
            hlg.childControlWidth     = false;
            hlg.childControlHeight    = false;
            hlg.childForceExpandWidth = false;
            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        static GameObject Spawn(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;
            return PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        }

        // One 48×48 bottom-nav tab. Mirrors DemoScreensSetup.MakeNavCircle priority:
        // active VS badge → active Figma icon → not-active Figma icon → tinted circle + label.
        static void MakeNavIcon(GameObject parent, PushStarsTheme theme, string name, bool isActive,
                                 string label, Sprite inactiveIcon, Sprite activeIcon)
        {
            var circleSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                                   $"{SpriteFactory.SpritesDir}/circle_128.png");

            var go = new GameObject($"Nav_{name}", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            AddLayoutElement(go, 48, 48);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(48, 48);

            var img = go.AddComponent<Image>();

            bool useVsBadge      = isActive && name == "Duel" && theme.VSBadge != null;
            bool useActiveIcon   = isActive && !useVsBadge && activeIcon != null;
            bool useInactiveIcon = !isActive && inactiveIcon != null;

            if (useVsBadge)        { img.sprite = theme.VSBadge; img.color = Color.white; img.preserveAspect = true; }
            else if (useActiveIcon)   { img.sprite = activeIcon;   img.color = Color.white; img.preserveAspect = true; }
            else if (useInactiveIcon) { img.sprite = inactiveIcon; img.color = Color.white; img.preserveAspect = true; }
            else
            {
                img.sprite = circleSprite;
                img.type   = Image.Type.Simple;
                img.color  = isActive ? theme.AccentBlue : new Color(1f, 1f, 1f, 0.92f);
            }

            // Label only when there's no baked-in art.
            if (!useVsBadge && !useActiveIcon && !useInactiveIcon)
            {
                var lbl = MakeTMP(go, "Label",
                                  isActive ? theme.TextPrimary : theme.BgDark,
                                  label, 16, FontStyles.Bold);
                Stretch(lbl.GetComponent<RectTransform>(), 0, 0);
            }
        }

        static GameObject Load(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/{name}.prefab");

        // ── Prefab helpers ────────────────────────────────────────────────────────
        static GameObject MakeTMP(GameObject parent, string name, Color color, string text,
                                   float size, FontStyles style)
        {
            var go  = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.AddComponent<RectTransform>();
            var tmp   = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.color     = color;
            tmp.fontSize  = size;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;

            // Swap in the correct Rubik font asset for Bold/Italic instead of TMP simulation.
            var rubik = FontSetup.Resolve(style, out var remaining);
            if (rubik != null) { tmp.font = rubik; tmp.fontStyle = remaining; }

            return go;
        }

        static void SetSize(RectTransform rt, float w, float h)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
        }

        static void Stretch(RectTransform rt, float hPad, float vPad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2( hPad,  vPad);
            rt.offsetMax = new Vector2(-hPad, -vPad);
        }

        static void AddLayoutElement(GameObject go, float preferredW, float preferredH,
                                      bool flexible = false)
        {
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = preferredW;
            le.preferredHeight = preferredH;
            if (flexible) le.flexibleWidth = 1;
        }

        static void Wire(Component comp, string field, object value)
        {
            var so   = new SerializedObject(comp);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[UIGallery] Field '{field}' not found on {comp.GetType().Name}");
                return;
            }
            switch (value)
            {
                case Object obj:   prop.objectReferenceValue = obj; break;
                case string  s:    prop.stringValue          = s;   break;
                case float   f:    prop.floatValue           = f;   break;
                case bool    b:    prop.boolValue            = b;   break;
                case Color   c:    prop.colorValue           = c;   break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SavePrefab(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Debug.Log($"[UIGallery] Created prefab: {path}");
        }

        // ── Colour math ───────────────────────────────────────────────────────────
        static Color Lighter(Color c, float t) => Color.Lerp(c, Color.white, t);
        static Color Darker (Color c, float t) => Color.Lerp(c, Color.black, t);
    }
}
