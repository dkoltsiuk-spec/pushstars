using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using PushStars.UI;

namespace PushStars.Editor
{
    /// <summary>
    /// Builds the production main screen — the central <b>VS / Duel</b> tab — with a real
    /// 3D-character pipeline at its centre:
    ///
    ///   • A dedicated stage camera renders a placeholder humanoid (on the "Character" layer)
    ///     into a transparent RenderTexture.
    ///   • A RawImage in the middle of the Duel panel displays that texture, so the character
    ///     appears to stand "inside" the flat UI.
    ///   • The full mock-up composition is laid out around it: level / streak / aura top bar,
    ///     FIND OPPONENT button, mode chips, and a functional 3-tab bottom nav.
    ///
    /// Everything is assembled into <c>Main.unity</c> with a working <see cref="MainShellView"/>
    /// so tab switching runs on Play. In phase 10 the placeholder model is swapped for a Genies
    /// avatar via <see cref="CharacterStage.SetAvatar"/> — nothing else on this screen changes.
    ///
    /// Menu: Tools → Push Stars → Build Main VS Screen
    /// </summary>
    public static class MainVsScreenSetup
    {
        // Reference resolution — iPhone 14 Pro logical points (matches Demo screens).
        const float REF_W = 390f;
        const float REF_H = 844f;

        const string MainScenePath = "Assets/_Project/Scenes/Main.unity";
        const string ThemeAsset    = "Assets/_Project/UI/Theme/Resources/PushStarsTheme.asset";
        const string PrefabsDir    = "Assets/_Project/UI/Prefabs";
        const string MaterialsDir  = "Assets/_Project/UI/Materials";
        const string RenderingDir  = "Assets/_Project/UI/Rendering";
        const string PreviewRtPath = RenderingDir + "/CharacterStageRT.renderTexture";
        const string CharacterLayer = "Character";

        static PushStarsTheme _theme;
        static int _charLayer;
        static RenderTexture _previewRt;
        static GameObject _findButtonGO; // НАЙТИ СОПЕРНИКА — captured so the search overlay can wire to it

        [MenuItem("Tools/Push Stars/Build Main VS Screen", priority = 20)]
        public static void Run()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Push Stars", "Loading theme …", 0.05f);

                _theme = AssetDatabase.LoadAssetAtPath<PushStarsTheme>(ThemeAsset);
                if (_theme == null)
                {
                    UIGallerySetup.Run(); // creates theme + prefabs + sprites as a side effect
                    _theme = AssetDatabase.LoadAssetAtPath<PushStarsTheme>(ThemeAsset);
                }
                if (_theme == null)
                {
                    EditorUtility.DisplayDialog("Push Stars",
                        "Could not load or create PushStarsTheme. Run\n" +
                        "Tools → Push Stars → Setup UI Gallery first.", "OK");
                    return;
                }

                // The screen is composed from the Phase-03 design-system prefabs.
                // Rebuild them if they are missing.
                if (Load("PrimaryButton") == null || Load("SecondaryChip") == null)
                {
                    UIGallerySetup.Run();
                    _theme = AssetDatabase.LoadAssetAtPath<PushStarsTheme>(ThemeAsset);
                }

                _charLayer = EnsureLayer(CharacterLayer);

                EditorUtility.DisplayProgressBar("Push Stars", "Building Main VS scene …", 0.3f);
                BuildScene();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                EnsureSceneInBuildSettings(MainScenePath);

                EditorUtility.DisplayDialog(
                    "Push Stars — Main VS Screen",
                    "Built Main.unity with the 3D character pipeline.\n\n" +
                    "• Scene is open and active — press Play.\n" +
                    "• The placeholder avatar previews live in the centre (the stage\n" +
                    "  camera renders into CharacterStageRT). If it looks blank, press\n" +
                    "  Play once or move the Game view to force a repaint.\n\n" +
                    "Phase 10: call CharacterStage.SetAvatar(genies) to swap the\n" +
                    "placeholder for a real avatar — the layout stays identical.",
                    "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SCENE
        // ════════════════════════════════════════════════════════════════════════
        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 3D character stage (world space, off-screen of the overlay UI) ───────
            var stage = Build3DStage();

            // ── UI ───────────────────────────────────────────────────────────────────
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var canvasGO = new GameObject("MainCanvas", typeof(RectTransform));
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(REF_W, REF_H);
            scaler.matchWidthOrHeight  = 0.5f;
            canvasGO.AddComponent<GraphicRaycaster>();

            var mirror = new GameObject("MirrorRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            mirror.SetParent(canvasGO.transform, false);
            Stretch(mirror, 0, 0, 0, 0);
            mirror.gameObject.AddComponent<DeviceSimulatorMirrorFix>();

            // Background image (Figma) or solid dark fallback + lightning pattern.
            var bg = _theme.BgImage != null
                ? MakeImage(mirror, "Background", Color.white, _theme.BgImage)
                : MakeImage(mirror, "Background", _theme.BgDark);
            Stretch(bg.rectTransform, 0, 0, 0, 0);
            // Staggered lightning pattern (theme slot, else load the sprite directly).
            var boltBg = _theme.IconLightningBG != null ? _theme.IconLightningBG : ProcSprite("icon_lightning_BG");
            if (boltBg != null)
                BuildLightningPattern(mirror, boltBg);

            var safe = MakeRect(mirror, "SafeArea");
            Stretch(safe, 0, 0, 0, 0);
            safe.gameObject.AddComponent<SafeAreaFitter>();
            // CanvasGroup so the whole main screen can recede/fade when the search overlay opens.
            var mainGroup = safe.gameObject.AddComponent<CanvasGroup>();

            // ── Tab panels ─────────────────────────────────────────────────────────
            var duelPanel    = MakeRect(safe, "DuelPanel");    Stretch(duelPanel, 0, 0, 0, 0);
            var leaguePanel  = MakeRect(safe, "LeaguePanel");  Stretch(leaguePanel, 0, 0, 0, 0);
            var profilePanel = MakeRect(safe, "ProfilePanel"); Stretch(profilePanel, 0, 0, 0, 0);

            var characterImage = BuildDuelPanel(duelPanel);
            // Live preview in the editor (camera renders into the saved RT asset).
            // CharacterStage swaps in a fresh runtime RT on Play.
            characterImage.texture = _previewRt;
            characterImage.color   = Color.white;
            BuildPlaceholderPanel(leaguePanel,  "ЛИГА",    "[Фаза 11]");
            BuildPlaceholderPanel(profilePanel, "ПРОФИЛЬ", "[Фаза 06]");

            // Duel is the default tab — hide the others so edit mode shows one screen, not all
            // three stacked. MainShellView re-syncs panel visibility on Start at runtime.
            leaguePanel.gameObject.SetActive(false);
            profilePanel.gameObject.SetActive(false);

            // ── Bottom nav (shared across tabs) ──────────────────────────────────────
            var tabButtons = BuildBottomNav(safe);

            // ── Wire the shell ────────────────────────────────────────────────────────
            var shellGO = new GameObject("MainShell");
            shellGO.transform.SetParent(canvasGO.transform, false);
            var shell   = shellGO.AddComponent<MainShellView>();
            var shellSO = new SerializedObject(shell);
            SetObjectArray(shellSO, "_tabButtons", tabButtons);
            shellSO.FindProperty("_leaguePanel").objectReferenceValue  = leaguePanel.gameObject;
            shellSO.FindProperty("_duelPanel").objectReferenceValue    = duelPanel.gameObject;
            shellSO.FindProperty("_profilePanel").objectReferenceValue = profilePanel.gameObject;
            shellSO.ApplyModifiedPropertiesWithoutUndo();

            // ── Wire the stage's output into the character RawImage ────────────────────
            var stageSO = new SerializedObject(stage);
            stageSO.FindProperty("_targetImage").objectReferenceValue = characterImage;
            stageSO.ApplyModifiedPropertiesWithoutUndo();

            // ── Search Opponent overlay (НАЙТИ СОПЕРНИКА → matchmaking screen) ──────────
            // Lives over the whole UI (sibling of SafeArea so it covers the bottom nav too),
            // hidden until the player taps the Find-Opponent CTA. The main screen recedes
            // behind it (scale + fade) via the mainContent/mainGroup it's handed here.
            BuildSearchOverlay(canvasGO.transform, mirror, safe, mainGroup);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, MainScenePath);
            Debug.Log($"[MainVsScreen] ✓ Saved {MainScenePath}");
        }

        // ════════════════════════════════════════════════════════════════════════
        //  3D STAGE  (camera + placeholder humanoid + key light)
        // ════════════════════════════════════════════════════════════════════════
        static CharacterStage Build3DStage()
        {
            var bodyMat = MakeMaterial("PlaceholderBody", new Color(0.10f, 0.10f, 0.13f)); // black kit
            var skinMat = MakeMaterial("PlaceholderSkin", new Color(0.79f, 0.62f, 0.50f)); // skin tone
            _previewRt  = GetOrCreatePreviewRt();

            var stageGO = new GameObject("CharacterStage3D");
            stageGO.transform.position = new Vector3(0f, 0f, 0f);
            var stage = stageGO.AddComponent<CharacterStage>();

            var avatarRoot = new GameObject("AvatarRoot").transform;
            avatarRoot.SetParent(stageGO.transform, false);
            avatarRoot.gameObject.layer = _charLayer;
            BuildPlaceholderModel(avatarRoot, bodyMat, skinMat);
            SetLayerRecursive(avatarRoot.gameObject, _charLayer);

            // Stage camera — frames a ~1.8 m figure standing at the origin.
            var camGO = new GameObject("StageCamera");
            camGO.transform.SetParent(stageGO.transform, false);
            camGO.transform.position = new Vector3(0f, 1.0f, -4.0f);
            camGO.transform.LookAt(new Vector3(0f, 1.0f, 0f));
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags          = CameraClearFlags.SolidColor;
            cam.backgroundColor     = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView         = 30f;
            cam.nearClipPlane       = 0.1f;
            cam.farClipPlane        = 30f;
            cam.cullingMask         = 1 << _charLayer;
            cam.useOcclusionCulling = false;
            cam.allowMSAA           = true;
            // Render into the saved RT in edit mode so the avatar previews without Play
            // (and so this camera never blits to the full screen). Runtime replaces it.
            cam.targetTexture       = _previewRt;

            // Key light (directional). Only the stage camera renders 3D, so this is safe.
            var lightGO = new GameObject("KeyLight");
            lightGO.transform.SetParent(stageGO.transform, false);
            lightGO.transform.rotation = Quaternion.Euler(35f, 205f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1.2f;
            light.color     = new Color(1f, 0.97f, 0.9f);

            var so = new SerializedObject(stage);
            so.FindProperty("_stageCamera").objectReferenceValue = cam;
            so.FindProperty("_avatarRoot").objectReferenceValue  = avatarRoot;
            so.ApplyModifiedPropertiesWithoutUndo();

            return stage;
        }

        // Stylised blockman built from primitives. Feet on y=0, ~1.85 m tall.
        static void BuildPlaceholderModel(Transform root, Material body, Material skin)
        {
            AddBox   (root, "Hips",  body, new Vector3( 0.00f, 0.92f,  0.00f), new Vector3(0.32f, 0.28f, 0.20f));
            AddBox   (root, "Torso", body, new Vector3( 0.00f, 1.30f,  0.00f), new Vector3(0.40f, 0.55f, 0.22f));
            AddSphere(root, "Head",  skin, new Vector3( 0.00f, 1.78f,  0.00f), 0.22f);

            AddCapsule(root, "ArmL", skin, new Vector3(-0.30f, 1.28f, 0.00f), new Vector3(0.10f, 0.30f, 0.10f));
            AddCapsule(root, "ArmR", skin, new Vector3( 0.30f, 1.28f, 0.00f), new Vector3(0.10f, 0.30f, 0.10f));

            AddCapsule(root, "LegL", body, new Vector3(-0.12f, 0.45f, 0.00f), new Vector3(0.12f, 0.45f, 0.12f));
            AddCapsule(root, "LegR", body, new Vector3( 0.12f, 0.45f, 0.00f), new Vector3(0.12f, 0.45f, 0.12f));

            AddBox(root, "FootL", body, new Vector3(-0.12f, 0.04f, 0.05f), new Vector3(0.14f, 0.08f, 0.28f));
            AddBox(root, "FootR", body, new Vector3( 0.12f, 0.04f, 0.05f), new Vector3(0.14f, 0.08f, 0.28f));
        }

        static void AddBox(Transform parent, string name, Material mat, Vector3 pos, Vector3 size)
            => AddPrimitive(parent, name, PrimitiveType.Cube, mat, pos, size);

        static void AddSphere(Transform parent, string name, Material mat, Vector3 pos, float diameter)
            => AddPrimitive(parent, name, PrimitiveType.Sphere, mat, pos, Vector3.one * diameter);

        static void AddCapsule(Transform parent, string name, Material mat, Vector3 pos, Vector3 size)
            => AddPrimitive(parent, name, PrimitiveType.Capsule, mat, pos, size);

        static void AddPrimitive(Transform parent, string name, PrimitiveType type,
                                 Material mat, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;

            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  DUEL PANEL  (returns the character RawImage)
        // ════════════════════════════════════════════════════════════════════════
        static RawImage BuildDuelPanel(RectTransform panel)
        {
            // ── Top bar (edge-anchored so it can never overflow the screen) ─────────────
            BuildTopBar(panel);

            // ── Character render surface (centre) ──────────────────────────────────────
            var charArea = MakeRect(panel, "CharacterArea");
            Anchor(charArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            charArea.anchoredPosition = new Vector2(0, 40);
            charArea.sizeDelta        = new Vector2(264, 470);

            var rawGO = new GameObject("CharacterImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rawGO.transform.SetParent(charArea, false);
            var raw = rawGO.GetComponent<RawImage>();
            Stretch(raw.rectTransform, 0, 0, 0, 0);
            raw.color         = new Color(1f, 1f, 1f, 0.04f); // faint in edit mode; CharacterStage sets white on Play
            raw.raycastTarget = false;

            // Decorative "+" wardrobe slots (3, no backing — just the plus icon, like the mock).
            MakePlusSlot(panel, new Vector2(-135, 215));
            MakePlusSlot(panel, new Vector2( 145,  70));
            MakePlusSlot(panel, new Vector2(-135, -55));

            // ── FIND OPPONENT button (design-system PrimaryButton prefab) ───────────────
            var findButton = BuildFindButton(panel);

            // ── Mode chips (design-system SecondaryChip prefab ×3) ──────────────────────
            var chipsRow = MakeRect(panel, "ChipsRow");
            Anchor(chipsRow, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            chipsRow.anchoredPosition = new Vector2(0, 130);
            chipsRow.sizeDelta        = new Vector2(0, 40);

            var chipsHL = chipsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipsHL.spacing               = 10;
            chipsHL.childAlignment        = TextAnchor.MiddleCenter;
            chipsHL.childControlWidth     = false;
            chipsHL.childControlHeight    = false;
            chipsHL.childForceExpandWidth = false;
            chipsRow.gameObject.AddComponent<ContentSizeFitter>().horizontalFit
                = ContentSizeFitter.FitMode.PreferredSize;

            var chipPrefab   = Load("SecondaryChip");
            var modeChip     = SpawnChip(chipPrefab, chipsRow, "ДУЭЛЬ",  _theme.IconLightning, selected: true);
            var exerciseChip = SpawnChip(chipPrefab, chipsRow, "ОТЖИМ.", _theme.IconPushup,    selected: false);
            var durationChip = SpawnChip(chipPrefab, chipsRow, "60 СЕК", _theme.IconTime,      selected: false);

            // ── "Coming soon" hint banner (hidden until a chip needs it) ────────────────
            var toast = BuildToast(panel);

            // ── Controller: wires chip taps → mode/exercise/duration behaviour ──────────
            BuildModeController(panel, modeChip, exerciseChip, durationChip, findButton, toast);

            return raw;
        }

        // Transient hint banner above the CTA. DuelModeController.Show(...) fades it in/out.
        static Toast BuildToast(RectTransform panel)
        {
            var go = new GameObject("Toast", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(panel, false);
            var rt = (RectTransform)go.transform;
            Anchor(rt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            rt.anchoredPosition = new Vector2(0, 276);
            rt.sizeDelta        = new Vector2(330, 46);

            var bg = MakeImage(rt, "Bg", new Color(0f, 0f, 0f, 0.82f), ProcSprite("pill_24"));
            bg.type          = Image.Type.Sliced;
            bg.raycastTarget = false;
            Stretch(bg.rectTransform, 0, 0, 0, 0);

            var label = MakeTMP(rt, "Label", _theme.TextPrimary, "", 14, FontStyles.Bold);
            label.alignment           = TextAlignmentOptions.Center;
            label.raycastTarget       = false;
            label.enableWordWrapping  = true;
            Stretch(label.rectTransform, 14, 0, 14, 0);

            var group = go.GetComponent<CanvasGroup>();
            group.alpha          = 0f;
            group.blocksRaycasts = false;
            group.interactable   = false;

            var toast = go.AddComponent<Toast>();
            var so = new SerializedObject(toast);
            so.FindProperty("_label").objectReferenceValue = label;
            so.FindProperty("_group").objectReferenceValue = group;
            so.ApplyModifiedPropertiesWithoutUndo();
            return toast;
        }

        static void BuildModeController(RectTransform panel, SecondaryChip mode, SecondaryChip exercise,
                                        SecondaryChip duration, PrimaryButton find, Toast toast)
        {
            var go = new GameObject("DuelModeController");
            go.transform.SetParent(panel, false);
            var ctrl = go.AddComponent<DuelModeController>();
            var so   = new SerializedObject(ctrl);
            so.FindProperty("_modeChip").objectReferenceValue     = mode;
            so.FindProperty("_exerciseChip").objectReferenceValue = exercise;
            so.FindProperty("_durationChip").objectReferenceValue = duration;
            so.FindProperty("_findButton").objectReferenceValue   = find;
            so.FindProperty("_toast").objectReferenceValue        = toast;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // FIND OPPONENT: PrimaryButton prefab + regular main label + gold "+50" + yellow bolt.
        static PrimaryButton BuildFindButton(RectTransform panel)
        {
            var btn = Spawn(Load("PrimaryButton"), panel);
            if (btn == null) return null;
            _findButtonGO = btn; // remembered so BuildSearchOverlay can open the matchmaking screen on its click
            var rt = (RectTransform)btn.transform;
            Anchor(rt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            rt.anchoredPosition = new Vector2(0, 196);
            rt.sizeDelta        = new Vector2(344, 62);

            var primary = btn.GetComponent<PrimaryButton>();
            primary?.SetLabel("НАЙТИ СОПЕРНИКА");

            // Main label → Regular weight; leave clearance on the right for the reward.
            // Auto-sizing keeps the longer training label ("НАЧАТЬ ТРЕНИРОВКУ") inside the pill.
            var label = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                var reg = FontSetup.LoadRegular();
                if (reg != null) label.font = reg;
                label.fontStyle        = FontStyles.Normal;
                label.alignment        = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
                label.enableAutoSizing = true;
                label.fontSizeMax      = 19;
                label.fontSizeMin      = 14;
                var lrt = label.rectTransform;
                lrt.offsetMax = new Vector2(-94, lrt.offsetMax.y);
            }

            // Reward: "+50" gold + yellow lightning (lvl.png), pinned to the right edge.
            var reward = new GameObject("Reward", typeof(RectTransform));
            reward.transform.SetParent(btn.transform, false);
            var rrt = (RectTransform)reward.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0.5f);
            rrt.pivot     = new Vector2(1f, 0.5f);
            rrt.anchoredPosition = new Vector2(-18, 0);
            rrt.sizeDelta        = new Vector2(84, 30);

            var rhl = reward.AddComponent<HorizontalLayoutGroup>();
            rhl.spacing                = 4;
            rhl.childAlignment         = TextAnchor.MiddleRight;
            rhl.childControlWidth      = true;
            rhl.childControlHeight     = true;
            rhl.childForceExpandWidth  = false;
            rhl.childForceExpandHeight = false;

            var plus = MakeTMP(reward.transform, "Plus50", _theme.TrophyGold, "+50", 20, FontStyles.Bold);
            plus.alignment = TextAlignmentOptions.MidlineRight;
            var plusLE = plus.gameObject.AddComponent<LayoutElement>();
            plusLE.preferredWidth = 44; plusLE.preferredHeight = 28;

            var bolt    = new GameObject("Bolt", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bolt.transform.SetParent(reward.transform, false);
            var boltImg = bolt.GetComponent<Image>();
            boltImg.sprite        = _theme.IconLevel; // lvl.png — yellow lightning bolt
            boltImg.color         = Color.white;
            boltImg.preserveAspect = true;
            boltImg.raycastTarget  = false;
            var boltLE = bolt.AddComponent<LayoutElement>();
            boltLE.preferredWidth = 22; boltLE.preferredHeight = 26;

            return primary;
        }

        static void BuildPlaceholderPanel(RectTransform panel, string title, string phase)
        {
            var label = MakeTMP(panel, "Label", _theme.TextPrimary, $"{title}\n{phase}", 28, FontStyles.Bold);
            Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            label.rectTransform.sizeDelta = new Vector2(300, 120);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  SEARCH OPPONENT OVERLAY  (matchmaking screen — mock-up "ПОИСК СОПЕРНИКА")
        // ════════════════════════════════════════════════════════════════════════
        // Full-screen modal composed from the design-system LoadingVsRing + ExitButton
        // prefabs: spinning dashed VS ring, a tip block and the red ВЫЙТИ button.
        // НАЙТИ СОПЕРНИКА opens it; ВЫЙТИ closes it (wired via SearchOpponentController).
        static void BuildSearchOverlay(Transform canvasRoot, RectTransform mirror,
                                       RectTransform mainContent, CanvasGroup mainGroup)
        {
            // ── Overlay root: full-screen, fades in, swallows input ─────────────────────
            var overlay = MakeRect(mirror, "SearchOverlay");
            Stretch(overlay, 0, 0, 0, 0);
            var canvasGroup = overlay.gameObject.AddComponent<CanvasGroup>();

            // Opaque background so the main screen is fully hidden; raycastTarget blocks taps.
            // (Outside the scaled content root, so the panel "pop" never reveals the screen behind.)
            var bg = _theme.BgImage != null
                ? MakeImage(overlay, "Background", Color.white, _theme.BgImage)
                : MakeImage(overlay, "Background", _theme.BgDark);
            Stretch(bg.rectTransform, 0, 0, 0, 0);
            bg.raycastTarget = true;

            var boltBg = _theme.IconLightningBG != null ? _theme.IconLightningBG : ProcSprite("icon_lightning_BG");
            if (boltBg != null)
                BuildLightningPattern(overlay, boltBg);

            var safe = MakeRect(overlay, "SafeArea");
            Stretch(safe, 0, 0, 0, 0);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            // Content root — JuicyScreen scales THIS for the panel pop (SafeAreaFitter owns
            // `safe`, so we never fight it). Everything animated lives under here.
            var content = MakeRect(safe, "Content");
            Stretch(content, 0, 0, 0, 0);

            // ── Title ───────────────────────────────────────────────────────────────────
            var title = MakeTMP(content, "Title", _theme.TextPrimary, "ПОИСК СОПЕРНИКА", 22, FontStyles.Bold);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            title.rectTransform.anchoredPosition = new Vector2(0f, -80f);
            title.rectTransform.sizeDelta        = new Vector2(-32f, 32f);
            title.alignment = TextAlignmentOptions.Center;

            // ── Spinning VS ring ─────────────────────────────────────────────────────────
            // RingWrap is what JuicyScreen pops (scale overshoot); the LoadingVsRing prefab
            // sits inside it with a PulseScale "breathing" loop — two scales on two nodes so
            // the entrance pop and the idle pulse never fight per-frame.
            // Ring diameter (RingWrap) vs VS-badge diameter sets the gap between the dashed
            // ring and the icon — a smaller wrap pulls the ring in closer to the badge.
            const float ringSize  = 156f; // dashed ring (smaller, sits closer to the VS)
            const float badgeSize = 96f;  // VS icon (a touch smaller)

            var ringWrap = MakeRect(content, "RingWrap");
            Anchor(ringWrap, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            ringWrap.anchoredPosition = new Vector2(0f, 70f);
            ringWrap.sizeDelta        = new Vector2(ringSize, ringSize);

            var ringGO = Spawn(Load("LoadingVsRing"), ringWrap);
            LoadingVsRing ring = null;
            if (ringGO != null)
            {
                ring = ringGO.GetComponent<LoadingVsRing>();
                Stretch((RectTransform)ringGO.transform, 0, 0, 0, 0);
                ringGO.AddComponent<PulseScale>(); // gentle breathing while searching

                // Shrink the centre VS badge (the prefab fixes it at 120 px regardless of wrap).
                var badgeRT = (ringGO.transform.Find("VsBadge") ?? ringGO.transform.Find("VS")) as RectTransform;
                if (badgeRT != null) badgeRT.sizeDelta = new Vector2(badgeSize, badgeSize);
            }

            // ── Tip block (СОВЕТ:) ────────────────────────────────────────────────────────
            var tip = MakeRect(content, "TipBlock");
            Anchor(tip, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            tip.anchoredPosition = new Vector2(0f, 170f);
            tip.sizeDelta        = new Vector2(320f, 110f);

            var tipVL = tip.gameObject.AddComponent<VerticalLayoutGroup>();
            tipVL.spacing               = 8;
            tipVL.childAlignment        = TextAnchor.UpperCenter;
            tipVL.childControlWidth     = true;
            tipVL.childControlHeight    = true;
            tipVL.childForceExpandWidth = true;

            var tipHeader = MakeTMP(tip, "Header", _theme.TextPrimary, "СОВЕТ:", 15, FontStyles.Bold);
            tipHeader.alignment = TextAlignmentOptions.Center;
            tipHeader.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;

            var tipBody = MakeTMP(tip, "Body", _theme.TextSecondary,
                "Ауру вы можете использовать\nдля покупки уникальных анимаций\nв магазине.",
                14, FontStyles.Normal);
            tipBody.alignment = TextAlignmentOptions.Center;
            tipBody.gameObject.AddComponent<LayoutElement>().preferredHeight = 72;

            // ── ВЫЙТИ (design-system ExitButton prefab) ──────────────────────────────────
            var exitGO = Spawn(Load("ExitButton"), content);
            RectTransform exitRT = null;
            if (exitGO != null)
            {
                exitGO.GetComponent<ExitButton>()?.SetLabel("ВЫЙТИ");
                exitRT = (RectTransform)exitGO.transform;
                Anchor(exitRT, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
                exitRT.anchoredPosition = new Vector2(0f, 86f);
            }

            // ── Juicy transition (staggered Brawl/Clash-style entrance + snappy exit) ─────
            var juicy = overlay.gameObject.AddComponent<JuicyScreen>();
            var jso   = new SerializedObject(juicy);
            jso.FindProperty("_overlayGroup").objectReferenceValue = canvasGroup;
            jso.FindProperty("_root").objectReferenceValue         = content;

            var elements = new (RectTransform target, int entrance, float dist, float delay, float dur)[]
            {
                // entrance: 0 Pop, 1 PopBig, 2 FromTop, 3 FromBottom, 4 FromLeft, 5 FromRight, 6 FadeOnly
                // Calmer: smooth (no-bounce) slides, gentle ring pop, longer durations, softer stagger.
                (title.rectTransform, 2, 46f, 0.06f, 0.50f), // title eases down from the top
                (ringWrap,            1,  0f, 0.16f, 0.60f), // VS ring gently pops in
                (tip,                 3, 30f, 0.26f, 0.50f), // tip floats up
                (exitRT,              3, 80f, 0.32f, 0.54f), // ВЫЙТИ slides up from the bottom
            };
            var arr = jso.FindProperty("_elements");
            arr.arraySize = exitRT != null ? elements.Length : elements.Length - 1;
            int idx = 0;
            foreach (var e in elements)
            {
                if (e.target == null) continue; // skip exit if the prefab was missing
                var el = arr.GetArrayElementAtIndex(idx++);
                el.FindPropertyRelative("target").objectReferenceValue = e.target;
                el.FindPropertyRelative("entrance").enumValueIndex      = e.entrance;
                el.FindPropertyRelative("moveDistance").floatValue      = e.dist;
                el.FindPropertyRelative("delay").floatValue             = e.delay;
                el.FindPropertyRelative("duration").floatValue          = e.dur;
            }
            arr.arraySize = idx;
            jso.ApplyModifiedPropertiesWithoutUndo();

            // Hidden until the player taps НАЙТИ СОПЕРНИКА.
            overlay.gameObject.SetActive(false);

            // ── Controller: always-active GO so it keeps running while the overlay toggles ─
            var ctrlGO = new GameObject("SearchOpponentController");
            ctrlGO.transform.SetParent(canvasRoot, false);
            var ctrl = ctrlGO.AddComponent<SearchOpponentController>();

            var findButton = _findButtonGO != null ? _findButtonGO.GetComponent<Button>() : null;
            var exitButton = exitGO       != null ? exitGO.GetComponent<Button>()       : null;

            var so = new SerializedObject(ctrl);
            so.FindProperty("_overlay").objectReferenceValue     = overlay.gameObject;
            so.FindProperty("_juicy").objectReferenceValue       = juicy;
            so.FindProperty("_mainContent").objectReferenceValue = mainContent;
            so.FindProperty("_mainGroup").objectReferenceValue   = mainGroup;
            so.FindProperty("_findButton").objectReferenceValue  = findButton;
            so.FindProperty("_exitButton").objectReferenceValue  = exitButton;
            so.FindProperty("_ring").objectReferenceValue        = ring;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ════════════════════════════════════════════════════════════════════════
        //  BOTTOM NAV (functional)
        // ════════════════════════════════════════════════════════════════════════
        static TabButton[] BuildBottomNav(RectTransform safe)
        {
            var navBar = MakeRect(safe, "BottomNav");
            Anchor(navBar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            navBar.anchoredPosition = new Vector2(0, 42);
            navBar.sizeDelta        = new Vector2(234, 72);

            // Black, slightly transparent menu backing (like the mock).
            var navBg = MakeImage(navBar, "Bg", new Color(0f, 0f, 0f, 0.55f), ProcSprite("pill_24"));
            navBg.type = Image.Type.Sliced;
            Stretch(navBg.rectTransform, 0, 0, 0, 0);

            var navHL = navBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            navHL.padding               = new RectOffset(14, 14, 6, 6);
            navHL.spacing               = 14;
            navHL.childAlignment        = TextAnchor.MiddleCenter;
            navHL.childControlWidth     = false;
            navHL.childControlHeight    = false;
            navHL.childForceExpandWidth = false;

            // Ready Figma nav buttons (whole circular sprites). Centre VS is larger.
            //   League  → statics.png (Статистика, bar chart)
            //   Duel    → main_btn_VS_active.png (blue VS, primary centre)
            //   Profile → profile.png / profile_active.png
            var league  = MakeNavButton(navBar, TabId.League,  _theme.IconStatics, _theme.IconStatics,      50);
            var duel    = MakeNavButton(navBar, TabId.Duel,    _theme.VSBadge,     _theme.VSBadge,          60);
            var profile = MakeNavButton(navBar, TabId.Profile, _theme.NavProfile,  _theme.NavProfileActive, 50);

            // MainShellView expects an array; order is cosmetic (each knows its TabId).
            return new[] { league, duel, profile };
        }

        // inactiveSprite = the not-active Figma nav button (whole circular sprite, Color.white).
        // activeSprite    = the active variant (may equal inactiveSprite when there is no separate art).
        static TabButton MakeNavButton(RectTransform parent, TabId id, Sprite inactiveSprite, Sprite activeSprite, float size)
        {
            var go = new GameObject($"Nav_{id}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size; le.preferredHeight = size; le.flexibleWidth = 0;

            var baseImg = go.GetComponent<Image>();
            baseImg.sprite        = inactiveSprite != null ? inactiveSprite : _theme.CircleShape;
            baseImg.color         = inactiveSprite != null ? Color.white : new Color(1f, 1f, 1f, 0.92f);
            baseImg.preserveAspect = true;

            go.AddComponent<Button>().targetGraphic = baseImg;

            // Active overlay — TabButton toggles its .enabled when this tab is selected.
            var indicator = new GameObject("Active", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            indicator.transform.SetParent(go.transform, false);
            Stretch((RectTransform)indicator.transform, 0, 0, 0, 0);
            var indImg = indicator.GetComponent<Image>();
            indImg.sprite         = activeSprite != null ? activeSprite : _theme.CircleShape;
            indImg.color          = activeSprite != null ? Color.white  : _theme.AccentBlue;
            indImg.preserveAspect = true;
            indImg.raycastTarget  = false;
            indImg.enabled        = id == TabId.Duel; // Duel is the default tab; MainShell re-syncs on Start

            var tab = go.AddComponent<TabButton>();
            var so  = new SerializedObject(tab);
            so.FindProperty("_tabId").enumValueIndex                 = (int)id;
            so.FindProperty("_icon").objectReferenceValue            = baseImg;
            so.FindProperty("_activeIndicator").objectReferenceValue = indImg;
            so.ApplyModifiedPropertiesWithoutUndo();

            return tab;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  UI PRIMITIVES (self-contained; mirror Demo screen conventions)
        // ════════════════════════════════════════════════════════════════════════
        // bgSprite = pill background (use a procedural WHITE sprite when you need bgColor tinting;
        //            pass null for a transparent inner pill). icon keeps its native colours when
        //            iconColor is white. Fixed 32 px height so the sliced sprite never squishes.
        static GameObject MakeIconPill(RectTransform parent, string name, Sprite bgSprite, Color bgColor,
                                       Sprite icon, Color iconColor, string label, Color fg, float width)
        {
            var go  = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            if (bgSprite != null) { img.sprite = bgSprite; img.type = Image.Type.Sliced; img.color = bgColor; }
            else                  { img.color = Color.clear; }

            ((RectTransform)go.transform).sizeDelta = new Vector2(width, 32);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = width; le.preferredHeight = 32; le.flexibleWidth = 0;

            var hl = go.AddComponent<HorizontalLayoutGroup>();
            hl.padding                = new RectOffset(10, 12, 0, 0);
            hl.spacing                = 6;
            hl.childAlignment         = TextAnchor.MiddleLeft;
            hl.childControlWidth      = true;
            hl.childControlHeight     = true;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;

            if (icon != null)
            {
                var ic    = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                ic.transform.SetParent(go.transform, false);
                var icImg = ic.GetComponent<Image>();
                icImg.sprite         = icon;
                icImg.color          = iconColor;
                icImg.preserveAspect = true;
                icImg.raycastTarget  = false;
                var icLE = ic.AddComponent<LayoutElement>();
                icLE.preferredWidth = 18; icLE.preferredHeight = 18; icLE.flexibleWidth = 0;
            }

            var lbl = MakeTMP(go.transform, "Label", fg, label, 13, FontStyles.Bold);
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            lbl.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            return go;
        }

        // Top bar pinned to the safe-area edges: the level pill hugs the LEFT, the streak+aura
        // group hugs the RIGHT (sized to its content, growing leftward). No fixed-width math is
        // done against the screen, so the content can never overflow — it adapts to any width.
        static void BuildTopBar(RectTransform panel)
        {
            const float topY = -14f;

            // Level pill — anchored to the top-left corner.
            var level = MakeIconPill(panel, "LevelPill", ProcSprite("pill_24"), _theme.AccentYellow,
                                     _theme.IconLevel, _theme.BgDark, "2 УРОВЕНЬ", _theme.BgDark, 138);
            var lrt = (RectTransform)level.transform;
            Anchor(lrt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            lrt.anchoredPosition = new Vector2(16f, topY);

            // Streak + Aura — group anchored to the top-right corner, content-sized, growing left.
            var hud = MakeRect(panel, "HudGroup");
            Anchor(hud, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            hud.anchoredPosition = new Vector2(-16f, topY);

            var hl = hud.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing                = 8;
            hl.childAlignment         = TextAnchor.MiddleRight;
            hl.childControlWidth      = false;
            hl.childControlHeight     = false;
            hl.childForceExpandWidth  = false;
            hl.childForceExpandHeight = false;
            var csf = hud.gameObject.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

            BuildStreakAura(hud);
        }

        // Streak + Aura as TWO separate black tags. The icon is LARGER than the tag and sits
        // ON TOP of its left edge (sticking out left), with the number inside the tag on the right.
        static void BuildStreakAura(RectTransform parent)
        {
            var streakIcon = _theme.IconStreak != null ? _theme.IconStreak : _theme.IconLightning;
            BuildHudPill(parent, "StreakPill", streakIcon,     "12",  _theme.AccentYellow);
            BuildHudPill(parent, "AuraPill",   _theme.IconAura, "660", new Color32(206, 178, 247, 255));
        }

        // Backing is drawn at the sprite's OWN aspect ratio (not stretched). Smaller overall.
        static void BuildHudPill(RectTransform parent, string name, Sprite icon, string number,
                                 Color numberColor)
        {
            const float pillH = 22f;                 // smaller
            const float nativeAspect = 66f / 21f;    // bg_streak_aura native ratio — keep it
            float pillW = Mathf.Round(pillH * nativeAspect);
            const float iconSize = 30f, overhang = 12f;
            float rootW = pillW + overhang;

            var root = MakeRect(parent, name);
            root.sizeDelta = new Vector2(rootW, iconSize);
            var le = root.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = rootW; le.preferredHeight = iconSize; le.flexibleWidth = 0;

            // Backing — shown as the picture (Simple + preserveAspect → ratio never changes).
            var hasArt   = _theme.BgStreakAura != null;
            var black    = hasArt ? _theme.BgStreakAura : ProcSprite("pill_24");
            var blackCol = hasArt ? Color.white : (Color)new Color32(20, 20, 28, 235);
            var bgImg    = MakeImage(root, "Bg", blackCol, black);
            if (hasArt) { bgImg.type = Image.Type.Simple; bgImg.preserveAspect = true; }
            else        { bgImg.type = Image.Type.Sliced; }
            var bgRt = bgImg.rectTransform;
            Anchor(bgRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            bgRt.sizeDelta        = new Vector2(pillW, pillH);
            bgRt.anchoredPosition = new Vector2((rootW - pillW) * 0.5f, 0f);

            // Number — inside the tag, to the right of the icon overlap.
            var num = MakeTMP(root, "Number", numberColor, number, 14, FontStyles.Bold);
            num.alignment = TextAlignmentOptions.Center;
            var numRt = num.rectTransform;
            Anchor(numRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            numRt.sizeDelta        = new Vector2(pillW * 0.58f, pillH);
            numRt.anchoredPosition = new Vector2((rootW - pillW) * 0.5f + pillW * 0.18f, 0f);

            // Icon — bigger than the tag, overlapping its LEFT edge, drawn last (on top).
            var ic   = MakeImage(root, "Icon", Color.white, icon);
            var icRt = ic.rectTransform;
            Anchor(icRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            icRt.sizeDelta        = new Vector2(iconSize, iconSize);
            icRt.anchoredPosition = new Vector2(-rootW * 0.5f + iconSize * 0.5f, 0f);
            ic.preserveAspect = true;
            ic.raycastTarget  = false;
        }

        static Sprite ProcSprite(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"{SpriteFactory.SpritesDir}/{name}.png");

        static SecondaryChip SpawnChip(GameObject prefab, RectTransform parent, string label, Sprite icon, bool selected)
        {
            var go = Spawn(prefab, parent);
            if (go == null) return null;
            var chip = go.GetComponent<SecondaryChip>();
            if (chip == null) return null;
            chip.SetLabel(label);
            chip.SetIcon(icon);
            chip.SetSelected(selected);

            // Slightly larger chip (keep aspect) so the bolder text fits.
            var rt = (RectTransform)go.transform;
            if (rt.sizeDelta.y > 1f)
            {
                float k = 44f / rt.sizeDelta.y;
                rt.sizeDelta = rt.sizeDelta * k;
                var le = go.GetComponent<LayoutElement>();
                if (le != null) { le.preferredWidth = rt.sizeDelta.x; le.preferredHeight = rt.sizeDelta.y; }
            }

            // Bolder + smaller label so "ОТЖИМ." / "60 СЕК" fit cleanly. Auto-sizing lets a
            // runtime swap to a longer label ("ТРЕНИР.") shrink to fit instead of overflowing.
            var lbl = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (lbl != null)
            {
                var bold = FontSetup.LoadBold();
                if (bold != null) lbl.font = bold;
                lbl.fontStyle          = FontStyles.Bold;
                lbl.enableWordWrapping = false;
                lbl.enableAutoSizing   = true;
                lbl.fontSizeMax        = 13;
                lbl.fontSizeMin        = 9;
            }

            return chip;
        }

        static void MakePlusSlot(RectTransform parent, Vector2 anchoredPos)
        {
            var go = new GameObject("PlusSlot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = new Vector2(40, 40);

            // No backing — just the ready plus icon (plus.png); "+" glyph fallback.
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            if (_theme.IconPlus != null)
            {
                img.sprite         = _theme.IconPlus;
                img.color          = new Color(1f, 1f, 1f, 0.5f);
                img.preserveAspect = true;
            }
            else
            {
                img.color = Color.clear;
                var lbl = MakeTMP(go.transform, "Plus", _theme.TextSecondary, "+", 24, FontStyles.Bold);
                Stretch(lbl.rectTransform, 0, 0, 0, 0);
            }
        }

        static void BuildLightningPattern(Transform parent, Sprite icon)
        {
            // Bolts in a staggered checkerboard (every other row shifted half a cell).
            const float iconSize = 76f, spacingX = 90f, spacingY = 102f, tilt = -10f;
            const float scrollSpeed = 16f; // points/sec upward
            // Edge fade: small centre core full, smooth gradient out to -90% at the screen edge.
            const float edgeStart = 0.15f, edgeEnd = 1.0f, edgeFade = 0.90f;

            var container = new GameObject("LightningPattern", typeof(RectTransform));
            container.transform.SetParent(parent, false);
            Stretch(container.GetComponent<RectTransform>(), 0, 0, 0, 0);

            float halfW = REF_W * 0.5f, halfH = REF_H * 0.5f;
            int cols = Mathf.CeilToInt(REF_W / spacingX) + 2;

            // Tall enough to cover the screen plus a margin top & bottom; even row count so
            // wrapping by the full lattice height preserves the checkerboard stagger.
            int rows = Mathf.CeilToInt((REF_H + 2f * spacingY) / spacingY) + 1;
            if (rows % 2 != 0) rows++;
            float spanY   = rows * spacingY;     // wrap distance for the animation
            float topWrap = halfH + spacingY;    // bolts wrap once they pass this (off-screen top)

            // Build the COMPLETE lattice (no alpha-skip) so nothing pops in while scrolling.
            for (int row = 0; row < rows; row++)
            {
                float y         = topWrap - (row + 0.5f) * spacingY; // tiles [topWrap - spanY, topWrap]
                float rowOffset = (row % 2 == 0) ? 0f : spacingX * 0.5f; // checkerboard stagger
                for (int col = -1; col < cols; col++)
                {
                    float x = -halfW + (col + 0.5f) * spacingX + rowOffset;

                    var go  = new GameObject($"L{row}_{col}", typeof(RectTransform), typeof(Image));
                    go.transform.SetParent(container.transform, false);
                    var rt  = go.GetComponent<RectTransform>();
                    Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                    rt.anchoredPosition = new Vector2(x, y);
                    rt.sizeDelta        = new Vector2(iconSize, iconSize);
                    rt.localRotation    = Quaternion.Euler(0f, 0f, tilt);

                    // Bake the same edge-fade gradient so the transition shows in edit mode too;
                    // LightningField recomputes it per frame as the bolts scroll.
                    float edge  = Mathf.Max(Mathf.Abs(x) / halfW, Mathf.Abs(y) / halfH);
                    float alpha = 1f - edgeFade * Mathf.SmoothStep(edgeStart, edgeEnd, edge);

                    var img = go.GetComponent<Image>();
                    img.sprite         = icon;
                    img.color          = new Color(1f, 1f, 1f, alpha);
                    img.preserveAspect = true; // keep the bolt's real proportions (no vertical squish)
                    img.raycastTarget  = false;
                }
            }

            // Slow infinite upward drift + a light edge fade (runtime). The sprite keeps its
            // own transparency in the centre; only the screen borders gently fade out.
            var field = container.AddComponent<LightningField>();
            var so = new SerializedObject(field);
            so.FindProperty("_speed").floatValue     = scrollSpeed;
            so.FindProperty("_spanY").floatValue     = spanY;
            so.FindProperty("_topWrap").floatValue   = topWrap;
            so.FindProperty("_halfW").floatValue     = halfW;
            so.FindProperty("_halfH").floatValue     = halfH;
            so.FindProperty("_edgeStart").floatValue = edgeStart;
            so.FindProperty("_edgeEnd").floatValue   = edgeEnd;
            so.FindProperty("_edgeFade").floatValue  = edgeFade;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static RectTransform MakeRect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        static Image MakeImage(RectTransform parent, string name, Color color, Sprite sprite = null)
        {
            var go  = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            if (sprite != null) img.sprite = sprite;
            return img;
        }

        static TextMeshProUGUI MakeTMP(Transform parent, string name, Color color, string text,
                                       float size, FontStyles style)
        {
            var go  = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text      = text;
            tmp.color     = color;
            tmp.fontSize  = size;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;

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

        // ════════════════════════════════════════════════════════════════════════
        //  PROJECT-LEVEL HELPERS
        // ════════════════════════════════════════════════════════════════════════
        static Material MakeMaterial(string assetName, Color baseColor)
        {
            EnsureFolder(MaterialsDir);
            string path = $"{MaterialsDir}/{assetName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", baseColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.25f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static RenderTexture GetOrCreatePreviewRt()
        {
            EnsureFolder(RenderingDir);
            var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(PreviewRtPath);
            if (rt != null) return rt;

            rt = new RenderTexture(720, 1280, 24, RenderTextureFormat.ARGB32)
            {
                name         = "CharacterStageRT",
                antiAliasing = 2,
            };
            AssetDatabase.CreateAsset(rt, PreviewRtPath);
            return rt;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf   = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static int EnsureLayer(string layerName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogWarning("[MainVsScreen] TagManager not found; placing character on Default layer.");
                return 0;
            }
            var so     = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");

            for (int i = 8; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;

            for (int i = 8; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue))
                {
                    el.stringValue = layerName;
                    so.ApplyModifiedProperties();
                    Debug.Log($"[MainVsScreen] Created layer '{layerName}' at index {i}.");
                    return i;
                }
            }
            Debug.LogWarning("[MainVsScreen] No free user layer; placing character on Default layer.");
            return 0;
        }

        static void EnsureSceneInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            foreach (var s in scenes)
                if (s.path == scenePath) return;

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes)
            {
                new EditorBuildSettingsScene(scenePath, true)
            };
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void SetObjectArray(SerializedObject so, string propName, Object[] items)
        {
            var arr = so.FindProperty(propName);
            arr.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        // ── Design-system prefab helpers (same approach as UIGallerySetup) ─────────
        static GameObject Load(string name) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabsDir}/{name}.prefab");

        static GameObject Spawn(GameObject prefab, Transform parent)
        {
            if (prefab == null) return null;
            return PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        }

        static string ColorHex(Color c)
        {
            var c32 = (Color32)c;
            return $"{c32.r:X2}{c32.g:X2}{c32.b:X2}";
        }
    }
}
