using System.IO;
using UnityEditor;
using UnityEditor.Events;
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
    ///   • A dedicated stage camera renders the character (on the "Character" layer)
    ///     into a transparent RenderTexture.
    ///   • A RawImage in the middle of the Duel panel displays that texture, so the character
    ///     appears to stand "inside" the flat UI.
    ///   • The full mock-up composition is laid out around it: trophy / streak / gem / aura top
    ///     bar, SHOP tile, the PVP · BATTLE · PUSHUP plates, and a functional 3-tab bottom nav.
    ///
    /// Everything is assembled into <c>Main.unity</c> with a working <see cref="MainShellView"/>
    /// so tab switching runs on Play. The model on the stage is the owner's own character
    /// (<see cref="MainCharacterSetup"/>); swapping it for another one at runtime goes through
    /// <see cref="CharacterStage.SetAvatar"/> — nothing else on this screen changes.
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
        static Transform _avatarRoot;   // captured by the 3D stage so the turntable can spin it
        static int _charLayer;
        static RenderTexture _previewRt;
        static GameObject _findButtonGO; // BATTLE plate — captured so the search overlay can wire to it
        static GameObject _gearButtonGO; // profile settings gear — captured so the settings overlay can wire to it
        static CharacterRoster _roster;  // owns the body on the stage — captured so the М/Ж switch can wire to it

        [MenuItem("Tools/Push Stars/Build Main VS Screen", priority = 20)]
        public static void Run()
        {
            if (!RunHeadless(out string error))
            {
                EditorUtility.DisplayDialog("Push Stars", error, "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "Push Stars — Main VS Screen",
                "Built Main.unity with the 3D character pipeline.\n\n" +
                "• Scene is open and active — press Play.\n" +
                "• The character previews live in the centre (the stage camera\n" +
                "  renders into CharacterStageRT). If it looks blank, press Play\n" +
                "  once or move the Game view to force a repaint.\n\n" +
                "The models come from MainMan.prefab / MainWoman.prefab, and the\n" +
                "М/Ж button beside the character swaps between them on Play.\n" +
                "Not imported yet? Run Tools ▸ Push Stars ▸ Character ▸ Import\n" +
                "Main Characters, then rebuild — until then a blockman stands in.",
                "OK");
        }

        /// <summary>Does the actual rebuild with no modal dialog anywhere in it. <see cref="Run"/>
        /// is the interactive menu entry and keeps its dialogs (a human clicked the menu, so a
        /// dialog costs them nothing); this is for callers that cannot click one — the editor-task
        /// sentinel, or CI — where the OLD <c>Run</c> would sit forever waiting for an OK nobody is
        /// there to give, silently blocking whatever else was queued behind it in the same pass.
        /// Returns false with a message on failure instead of popping a dialog, and logs instead of
        /// showing one on success.</summary>
        public static bool RunHeadless(out string error)
        {
            error = null;
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
                    error = "Could not load or create PushStarsTheme. Run " +
                            "Tools → Push Stars → Setup UI Gallery first.";
                    Debug.LogError($"[MainVsScreen] {error}");
                    return false;
                }

                // The screen is composed from the Phase-03 design-system prefabs.
                // Rebuild them if they are missing — and rebuild the theme when it predates the
                // slots this screen needs (NavPlate/GlowRadial arrived with the plate layout),
                // so one menu command is still all it takes to get the current composition.
                bool staleTheme = _theme.NavPlate == null || _theme.GlowRadial == null;
                if (staleTheme || Load("PrimaryButton") == null || Load("SecondaryChip") == null)
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

                Debug.Log("[MainVsScreen] Built Main.unity with the 3D character pipeline.");
                return true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Prints the positioned elements of the open Main scene as name → anchoredPosition / size.
        ///
        /// This screen is generated: <see cref="BuildScene"/> starts from an empty scene and
        /// overwrites Main.unity, so anything nudged by hand is gone the next time it runs — the
        /// code here is the source of truth, not the scene file. Dragging in the Scene view is
        /// still the quickest way to FIND a number; this dumps whatever you arrived at so it can
        /// be written back into the builder and survive.
        ///
        /// Menu: Tools → Push Stars → Dump Main Screen Layout
        /// </summary>
        public const string LayoutDumpPath = "layout_dump.txt"; // project root, beside Assets/

        [MenuItem("Tools/Push Stars/Dump Main Screen Layout", priority = 21)]
        public static void DumpLayout()
        {
            var canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                Debug.LogWarning("[MainVsScreen] No Canvas in the open scene — open Main.unity first.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Main screen layout dump");
            sb.AppendLine("# UI — anchoredPosition / sizeDelta, in the canvas's 390 × 844 reference units");
            foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
            {
                // The bolt lattice is a few hundred generated tiles; nobody positions those by hand.
                if (IsUnder(rt, "LightningPattern")) continue;

                var p = rt.anchoredPosition;
                var s = rt.sizeDelta;
                sb.AppendLine($"{PathOf(rt, canvas.transform),-52} pos {p.x,8:0.##} {p.y,8:0.##}   size {s.x,7:0.##} x {s.y,-7:0.##}");
            }

            // The character is not a UI element — it lives on the world-space stage and reaches the
            // screen through a RenderTexture, so moving "the character" can mean the RawImage above
            // or the model and its camera down here. Both have to come back for a rebuild to match.
            var stage = GameObject.Find("CharacterStage3D");
            if (stage != null)
            {
                sb.AppendLine();
                sb.AppendLine("# 3D stage — local position / euler / scale");
                foreach (var t in stage.GetComponentsInChildren<Transform>(true))
                {
                    if (IsUnder(t, "AvatarRoot")) continue; // the rig's own bones, not layout
                    var lp = t.localPosition; var le = t.localEulerAngles; var ls = t.localScale;
                    sb.AppendLine($"{PathOf(t, stage.transform.parent),-52} " +
                                  $"pos {lp.x,7:0.###} {lp.y,7:0.###} {lp.z,7:0.###}   " +
                                  $"rot {le.x,6:0.#} {le.y,6:0.#} {le.z,6:0.#}   " +
                                  $"scale {ls.x,5:0.###} {ls.y,5:0.###} {ls.z,5:0.###}");
                    var cam = t.GetComponent<Camera>();
                    if (cam != null) sb.AppendLine($"{"  ^ camera",-52} fov {cam.fieldOfView:0.##}");
                }
            }

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", LayoutDumpPath));
            File.WriteAllText(path, sb.ToString());
            // Written to a file rather than the console: the Console list shows only the first two
            // lines of an entry, and this dump is far longer than that.
            Debug.Log($"[MainVsScreen] layout written to {path}");
        }

        static bool IsUnder(Transform t, string ancestorName)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p.name == ancestorName) return true;
            return false;
        }

        static string PathOf(Transform t, Transform root)
        {
            var name = t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent)
                name = p.name + "/" + name;
            return name;
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

            // Display camera — without one the Game view shows "Display 1: No cameras rendering".
            // It only clears the screen to the dark base; the 3D avatar renders via the stage camera
            // into a RenderTexture, and the ScreenSpaceOverlay UI draws on top.
            var dispCamGO = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            var dispCam   = dispCamGO.GetComponent<Camera>();
            dispCam.clearFlags      = CameraClearFlags.SolidColor;
            dispCam.backgroundColor = _theme.BgDark;
            dispCam.cullingMask     = ~(1 << _charLayer); // exclude the 3D avatar (it lives in the RT)
            dispCam.orthographic    = true;
            dispCam.depth           = -1;

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
            BuildProfilePanel(profilePanel);

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

            // ── Swipe-to-spin, on the surface the character is shown through ───────────
            var turntable = characterImage.gameObject.AddComponent<CharacterTurntable>();
            var turnSO = new SerializedObject(turntable);
            turnSO.FindProperty("_target").objectReferenceValue = _avatarRoot;
            turnSO.ApplyModifiedPropertiesWithoutUndo();

            // ── Search Opponent overlay (НАЙТИ СОПЕРНИКА → matchmaking screen) ──────────
            // Lives over the whole UI (sibling of SafeArea so it covers the bottom nav too),
            // hidden until the player taps the Find-Opponent CTA. The main screen recedes
            // behind it (scale + fade) via the mainContent/mainGroup it's handed here.
            BuildSearchOverlay(canvasGO.transform, mirror, safe, mainGroup);

            // ── Settings overlay (profile gear → settings screen, phase 07) ────────────
            // Same toggled-root + main-recede pattern as the search overlay; opened by the
            // gear captured in BuildProfilePanel, closed by its own back button.
            BuildSettingsOverlay(canvasGO.transform, mirror, safe, mainGroup);

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
            // The stage camera stands on -Z looking toward +Z; a Unity character faces +Z, so the
            // root turns around to face the lens. Sitting on the root, this also holds for any
            // model dropped in later through CharacterStage.SetAvatar.
            avatarRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var character = BuildCharacterModel(avatarRoot);
            if (character == null) BuildPlaceholderModel(avatarRoot, bodyMat, skinMat);
            SetLayerRecursive(avatarRoot.gameObject, _charLayer);

            // Break up the loop: every few idle cycles he stretches, then settles back.
            if (character != null)
            {
                var accent = character.AddComponent<CharacterIdleAccent>();
                var accentSO = new SerializedObject(accent);
                accentSO.FindProperty("_animator").objectReferenceValue = character.GetComponent<Animator>();
                accentSO.FindProperty("_idleState").stringValue   = MainCharacterSetup.IdleState;
                accentSO.FindProperty("_accentState").stringValue = MainCharacterSetup.AccentState;
                accentSO.ApplyModifiedPropertiesWithoutUndo();
            }

            _avatarRoot = avatarRoot;

            // ── Gender switch ─────────────────────────────────────────────────────────
            // The instance placed above is what makes the scene read correctly in edit mode,
            // before anything runs. On Play the roster re-seats the stage from the saved choice,
            // so the first load and every later tap on the М/Ж button take the same path.
            var roster = stageGO.AddComponent<CharacterRoster>();
            var rosterSO = new SerializedObject(roster);
            rosterSO.FindProperty("_stage").objectReferenceValue = stage;
            rosterSO.FindProperty("_malePrefab").objectReferenceValue =
                MainCharacterSetup.LoadCharacterPrefab(CharacterGender.Male);
            rosterSO.FindProperty("_femalePrefab").objectReferenceValue =
                MainCharacterSetup.LoadCharacterPrefab(CharacterGender.Female);
            rosterSO.FindProperty("_idleState").stringValue   = MainCharacterSetup.IdleState;
            rosterSO.FindProperty("_accentState").stringValue = MainCharacterSetup.AccentState;
            rosterSO.ApplyModifiedPropertiesWithoutUndo();
            _roster = roster;

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

            // Key light (directional). Only the stage camera renders 3D, so this is safe. It
            // throws light along +Z — from the camera's side of the stage onto the character's
            // front, slightly off-axis so the figure keeps some shape.
            var lightGO = new GameObject("KeyLight");
            lightGO.transform.SetParent(stageGO.transform, false);
            lightGO.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = CharacterLighting.KeyIntensity;
            light.color     = CharacterLighting.KeyColor;

            // Fill from the opposite side — a single key leaves the unlit half of a real
            // character in near-black, which the blockman never showed.
            var fillGO = new GameObject("FillLight");
            fillGO.transform.SetParent(stageGO.transform, false);
            fillGO.transform.rotation = Quaternion.Euler(15f, -35f, 0f);
            var fill = fillGO.AddComponent<Light>();
            fill.type      = LightType.Directional;
            fill.intensity = CharacterLighting.FillIntensity;
            fill.color     = CharacterLighting.FillColor;

            var so = new SerializedObject(stage);
            so.FindProperty("_stageCamera").objectReferenceValue = cam;
            so.FindProperty("_avatarRoot").objectReferenceValue  = avatarRoot;
            so.ApplyModifiedPropertiesWithoutUndo();

            return stage;
        }

        /// <summary>Puts the owner's main character on the stage. Returns null when he hasn't
        /// been imported yet (Tools ▸ Push Stars ▸ Character ▸ Import Main Character) — the
        /// blockman below still stands in, so this screen builds on a fresh clone.</summary>
        static GameObject BuildCharacterModel(Transform root)
        {
            var prefab = MainCharacterSetup.LoadCharacterPrefab();
            if (prefab == null) return null;

            var character = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            character.name = "MainMan";
            character.transform.localPosition = Vector3.zero;
            character.transform.localRotation = Quaternion.identity;
            return character;
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
        // ── Composition constants (reference 390 × 844, taken off the design mock-up) ──
        // Kept together so the layout can be nudged without hunting through the builder.
        const float CharAreaY       = 40f;    // character render surface, from the screen centre
        const float FeetY           = -178f;  // where his soles land inside that surface
        const float ActionRowBottom = 149f;   // baseline of the PVP / BATTLE / PUSHUP plates
        const float NavBarBottom    = 52f;    // centre of the bottom-nav plate

        static RawImage BuildDuelPanel(RectTransform panel)
        {
            // Sibling order is draw order: the glow sits behind everything, then the contact
            // shadow, the character, his wardrobe decor, the HUD, and the action plates on top.
            BuildStageGlow(panel);
            BuildGroundShadow(panel);

            // ── Character render surface (centre) ──────────────────────────────────────
            var charArea = MakeRect(panel, "CharacterArea");
            Anchor(charArea, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            charArea.anchoredPosition = new Vector2(0, CharAreaY);
            charArea.sizeDelta        = new Vector2(264, 470);

            var rawGO = new GameObject("CharacterImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rawGO.transform.SetParent(charArea, false);
            var raw = rawGO.GetComponent<RawImage>();
            Stretch(raw.rectTransform, 0, 0, 0, 0);
            // Nudged off the stage centre by hand: the render texture frames the model with
            // room around it, so shifting the quad is how the figure gets placed on the screen.
            raw.rectTransform.anchoredPosition = new Vector2(9f, 62f);
            raw.color         = new Color(1f, 1f, 1f, 0.04f); // faint in edit mode; CharacterStage sets white on Play
            // Raycastable so a swipe across the character can spin him. The action plates are
            // built after this and therefore draw — and are hit — on top of it.
            raw.raycastTarget = true;

            // Decorative "+" wardrobe slots (3, no backing — just the plus icon, like the mock).
            MakePlusSlot(panel, new Vector2(-118, 218));
            MakePlusSlot(panel, new Vector2( 132, 122));
            MakePlusSlot(panel, new Vector2( -93,  -49));

            // ── М / Ж — swaps the body on the stage ────────────────────────────────────
            BuildGenderSwitch(panel, new Vector2(132, -72));

            // ── Top bar (edge-anchored so it can never overflow the screen) + side tiles ─
            BuildTopBar(panel);
            BuildSideTiles(panel);

            // ── "Coming soon" hint banner (hidden until a stub plate needs it) ──────────
            var toast = BuildToast(panel);

            // ── PVP / BATTLE / PUSHUP ──────────────────────────────────────────────────
            BuildActionRow(panel, toast);

            return raw;
        }

        // Warm halo behind the figure — the mock-up's magenta-into-violet bloom. It is one white
        // radial sprite drawn twice: a wide violet spread with a tighter magenta core inside it.
        // A squashed rect turns the circle into the tall oval the composition wants; two tinted
        // copies give the hue shift that a single tint cannot.
        static void BuildStageGlow(RectTransform panel)
        {
            var glow = _theme.GlowRadial != null ? _theme.GlowRadial : ProcSprite("glow_radial");
            if (glow == null) return;

            AddGlow(panel, "GlowHalo", glow, _theme.GlowHalo, new Vector2(0f, 60f), new Vector2(470f, 600f));
            AddGlow(panel, "GlowCore", glow, _theme.GlowCore, new Vector2(0f, 70f), new Vector2(290f, 360f));
        }

        static void AddGlow(RectTransform panel, string name, Sprite sprite, Color tint,
                            Vector2 pos, Vector2 size)
        {
            var img = MakeImage(panel, name, tint, sprite);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rt.anchoredPosition = pos;
            rt.sizeDelta        = size;
        }

        // Flat ellipse under the soles. Without it the render-textured character reads as
        // floating in front of the background instead of standing on it.
        static void BuildGroundShadow(RectTransform panel)
        {
            var sprite = _theme.GroundShadow != null ? _theme.GroundShadow : ProcSprite("ground_shadow");
            if (sprite == null) return;

            var img = MakeImage(panel, "GroundShadow", new Color(0f, 0f, 0f, 0.5f), sprite);
            img.raycastTarget = false;
            var rt = img.rectTransform;
            Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rt.anchoredPosition = new Vector2(0f, CharAreaY + FeetY);
            rt.sizeDelta        = new Vector2(168f, 40f);
        }

        // SHOP tile and the spare slot beneath it, hugging the right edge under the currency tags.
        static void BuildSideTiles(RectTransform panel)
        {
            // SHOP — yellow plate, icon crowning it, label along the bottom, "i" in the corner.
            var shop = MakeRect(panel, "ShopTile");
            Anchor(shop, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            shop.anchoredPosition = new Vector2(-18f, -81f);
            shop.sizeDelta        = new Vector2(72f, 41f); // plashka_for_shop is 145 × 82

            var plateSprite = _theme.PlateShop != null ? _theme.PlateShop : ProcSprite("plashka_for_shop");
            var plate = MakeImage(shop, "Plate", plateSprite != null ? Color.white : _theme.AccentYellow,
                                  plateSprite);
            Stretch(plate.rectTransform, 0, 0, 0, 0);
            shop.gameObject.AddComponent<Button>().targetGraphic = plate;

            var lrt = MakeLettering(plate.rectTransform, "label_shop", "SHOP", _theme.TextPrimary, 12f);
            Anchor(lrt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            lrt.sizeDelta        = new Vector2(-6f, 16f);
            lrt.anchoredPosition = new Vector2(0f, 6f);

            var icon = _theme.IconShop != null ? _theme.IconShop : ProcSprite("shop");
            if (icon != null)
            {
                var ic = MakeImage(shop, "Icon", Color.white, icon);
                ic.preserveAspect = true;
                ic.raycastTarget  = false;
                var irt = ic.rectTransform;
                Anchor(irt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
                irt.sizeDelta        = new Vector2(38f, 38f);
                irt.anchoredPosition = new Vector2(0f, 6f);
            }
            AddInfoBadge(shop, new Vector2(-2f, -2f));

            // Spare slot — empty in the mock-up, so it is drawn but left unwired.
            var slot = MakeImage(panel, "SpareSlot", new Color32(38, 40, 58, 255), ProcSprite("pill_16"));
            slot.type          = Image.Type.Sliced;
            slot.raycastTarget = false;
            var srt = slot.rectTransform;
            Anchor(srt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            srt.anchoredPosition = new Vector2(-18f, -139f);
            srt.sizeDelta        = new Vector2(72f, 38f);
        }

        // Small red "i" disc, as it sits on the trophy pill and the shop tile in the mock-up.
        // Anchored to the parent's top-right corner and pivoted at its own centre, so it
        // overhangs that corner instead of tucking inside it.
        static void AddInfoBadge(RectTransform parent, Vector2 anchoredPos)
        {
            var circle = _theme.CircleShape != null ? _theme.CircleShape : ProcSprite("circle_128");
            var badge  = MakeImage(parent, "InfoBadge", _theme.DangerRed, circle);
            badge.preserveAspect = true;
            badge.raycastTarget  = false;
            var rt = badge.rectTransform;
            Anchor(rt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = new Vector2(16f, 16f);

            var glyph = MakeTMP(rt, "Glyph", _theme.TextPrimary, "i", 11, FontStyles.Bold);
            glyph.raycastTarget = false;
            Stretch(glyph.rectTransform, 0, 0, 0, 0);
        }

        // Transient hint banner above the plates. Toast.Show(...) fades it in/out.
        static Toast BuildToast(RectTransform panel)
        {
            var go = new GameObject("Toast", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(panel, false);
            var rt = (RectTransform)go.transform;
            Anchor(rt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            rt.anchoredPosition = new Vector2(0, 212);
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

        // PVP / BATTLE / PUSHUP — the three plates that replaced the wide "НАЙТИ СОПЕРНИКА" pill
        // and the mode/exercise/duration chips. BATTLE inherits the CTA's role, so it is what
        // _findButtonGO hands to BuildSearchOverlay; the two side plates are stubs until their
        // modes ship, and say so through the toast. Duel/60 s stay the defaults DuelModeController
        // already used, so nothing downstream changes by dropping the chips.
        static void BuildActionRow(RectTransform panel, Toast toast)
        {
            var row = MakeRect(panel, "ActionRow");
            Anchor(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            row.anchoredPosition = new Vector2(0f, ActionRowBottom);
            row.sizeDelta        = new Vector2(339f, 100f); // 78 + 163 + 78 plus two 10 pt gaps

            // No layout group here on purpose. The row holds exactly three plates of known size,
            // so a HorizontalLayoutGroup buys nothing — and it would recompute their positions on
            // every layout pass, which is what makes a plate snap straight back when you drag it
            // in the Scene view. Authored positions stay where they are put.

            // Sizes follow each plate's own aspect so the pre-coloured art is never distorted:
            // type / type_settings are 1.15 : 1, btn_start is 1.94 : 1.
            // duel_icon / pushup_icon (the comp's own art) come first — VSBadgeSearch/IconPushup
            // were placeholders standing in for exactly these two (a VS medal is a matchmaking
            // glyph, not a clash icon; the old IconPushup was never actually this drawing).
            var pvp = BuildActionPlate(row, "PvpButton",
                                       _theme.PlatePvp != null ? _theme.PlatePvp : ProcSprite("type"),
                                       new Vector2(78f, 68f), "PVP", _theme.TrophyGold,
                                       ProcSprite("duel_icon") ?? _theme.VSBadgeSearch, 42f);
            var battle = BuildActionPlate(row, "BattleButton",
                                          _theme.PlateBattle != null ? _theme.PlateBattle : ProcSprite("btn_start"),
                                          new Vector2(163f, 84f), "BATTLE", _theme.TextPrimary, null, 0f);
            var pushup = BuildActionPlate(row, "PushupButton",
                                          _theme.PlatePushup != null ? _theme.PlatePushup : ProcSprite("type_settings"),
                                          new Vector2(78f, 68f), "PUSHUP", _theme.TextPrimary,
                                          ProcSprite("pushup_icon") ?? _theme.IconPushup, 38f);

            // Bottoms on one baseline, centres spaced by half-width + gap + half-width.
            PlaceInRow(pvp,    -130.5f);
            PlaceInRow(battle,    0f);
            PlaceInRow(pushup,  130.5f);

            _findButtonGO = battle; // the search overlay opens from BATTLE

            WireComingSoon(pvp,    toast, "PVP скоро — пока доступен BATTLE");
            WireComingSoon(pushup, toast, "Тренировка скоро — пока доступен BATTLE");
        }

        /// <summary>Pins a plate to the action row's baseline at a given horizontal offset — the
        /// job a HorizontalLayoutGroup used to do, done once at build time so the result is a
        /// plain authored position the Scene view can edit.</summary>
        static void PlaceInRow(GameObject go, float x)
        {
            var rt = (RectTransform)go.transform;
            Anchor(rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            rt.anchoredPosition = new Vector2(x, 0f);
        }

        // One slanted plate: the pre-coloured sprite at its own aspect, a bold label across it,
        // and — where there is art for it — an icon crowning the top edge, as in the mock-up.
        static GameObject BuildActionPlate(RectTransform row, string name, Sprite plate, Vector2 size,
                                           string label, Color labelColor, Sprite icon, float iconSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(row, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = size;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = size.x; le.preferredHeight = size.y; le.flexibleWidth = 0;

            var img = go.GetComponent<Image>();
            img.sprite = plate;
            img.color  = plate != null ? Color.white : _theme.AccentYellow;
            img.type   = Image.Type.Simple;
            go.AddComponent<Button>().targetGraphic = img;

            // An icon crowds the top, so the label drops to the plate's foot; without one it
            // centres.
            bool crowned  = icon != null;
            float fontMax = crowned ? 12f : 20f;
            var lrt = MakeLettering(rt, "label_" + name.Replace("Button", "").ToLowerInvariant(),
                                    label, labelColor, fontMax);
            Anchor(lrt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f));
            lrt.sizeDelta        = new Vector2(-8f, 24f);
            lrt.anchoredPosition = new Vector2(0f, crowned ? 8f : (size.y - 24f) * 0.5f);

            if (crowned)
            {
                var ic = MakeImage(rt, "Icon", Color.white, icon);
                ic.preserveAspect = true;
                ic.raycastTarget  = false;
                var irt = ic.rectTransform;
                Anchor(irt, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f));
                irt.sizeDelta        = new Vector2(iconSize, iconSize);
                irt.anchoredPosition = new Vector2(0f, 6f);
            }
            return go;
        }

        /// <summary>
        /// A headline label, drawn from exported Figma art when it exists and set in TMP when it
        /// does not. Returns whichever RectTransform the caller has to place.
        ///
        /// TMP's outline is a threshold on a signed distance field, which makes it a dilation by a
        /// disc: every convex corner comes back rounded by the outline radius, at any atlas
        /// resolution. Figma strokes the vector contour and mitres the corners. The two cannot be
        /// made to agree, and no amount of tuning closes it.
        ///
        /// So for a label whose text never changes — never localised, never computed — the field
        /// buys nothing and costs the mismatch. Drop <c>label_battle.png</c> (and _pvp, _pushup,
        /// _shop) into UI/Sprites, exported at @3x with a transparent background, and the builder
        /// picks it up on the next run: pixel for pixel what the designer drew. Until then the TMP
        /// label stands in, so nothing is blocked on the export.
        /// </summary>
        static RectTransform MakeLettering(RectTransform parent, string artName, string text,
                                           Color color, float fontMax)
        {
            var art = ProcSprite(artName);
            if (art != null)
            {
                var img = MakeImage(parent, "Label", Color.white, art);
                img.preserveAspect = true;   // the export carries its own proportions
                img.raycastTarget  = false;
                return img.rectTransform;
            }

            var lbl = MakeTMP(parent, "Label", color, text, fontMax, FontStyles.Bold);
            lbl.alignment          = TextAlignmentOptions.Center;
            lbl.raycastTarget      = false;
            lbl.enableWordWrapping = false;
            lbl.enableAutoSizing   = true;
            lbl.fontSizeMin        = fontMax - 4f;
            lbl.fontSizeMax        = fontMax;
            return lbl.rectTransform;
        }

        // Serialized onClick → Toast.Show(message). A persistent listener rather than a runtime
        // controller: these two plates have no state to drive, only a line to say.
        static void WireComingSoon(GameObject go, Toast toast, string message)
        {
            var btn = go != null ? go.GetComponent<Button>() : null;
            if (btn == null || toast == null) return;
            UnityEventTools.AddStringPersistentListener(btn.onClick, toast.Show, message);
        }

        // Covers the shared lightning background with the flat base background, so a tab reads as a
        // calm screen (no animated bolts). Inserted as the first child of the panel.
        static void AddFlatBackground(RectTransform panel)
        {
            var bg = _theme.BgImage != null
                ? MakeImage(panel, "Background", Color.white, _theme.BgImage)
                : MakeImage(panel, "Background", _theme.BgDark);
            Stretch(bg.rectTransform, 0, 0, 0, 0);
            bg.raycastTarget = false;
            bg.rectTransform.SetAsFirstSibling();
        }

        static void BuildPlaceholderPanel(RectTransform panel, string title, string phase)
        {
            AddFlatBackground(panel); // ЛИГА / статистика: no lightning behind it
            var label = MakeTMP(panel, "Label", _theme.TextPrimary, $"{title}\n{phase}", 28, FontStyles.Bold);
            Anchor(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            label.rectTransform.sizeDelta = new Vector2(300, 120);
        }

        // ════════════════════════════════════════════════════════════════════════
        //  PROFILE PANEL  (phase 06 — binds to users/{uid} via ProfilePresenter)
        // ════════════════════════════════════════════════════════════════════════
        static void BuildProfilePanel(RectTransform panel)
        {
            AddFlatBackground(panel); // профиль / статистика: no lightning behind it

            // ── Settings gear (top-right) → opens the Settings overlay (phase 07) ───────
            // Circular tappable target consistent with the nav buttons; the gear glyph/sprite
            // sits on top. Captured into _gearButtonGO so BuildSettingsOverlay can wire its click.
            var gear   = new GameObject("GearButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gear.transform.SetParent(panel, false);
            var gearRT = (RectTransform)gear.transform;
            Anchor(gearRT, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            gearRT.anchoredPosition = new Vector2(-18f, -22f);
            gearRT.sizeDelta        = new Vector2(44f, 44f);
            var gearBg = gear.GetComponent<Image>();
            gearBg.sprite         = _theme.CircleShape;
            gearBg.color          = new Color(1f, 1f, 1f, 0.06f); // subtle circular hit target
            gearBg.preserveAspect = true;
            gear.AddComponent<Button>().targetGraphic = gearBg;

            var gIcon   = MakeImage(gearRT, "Icon", Color.white, _theme.IconSettings);
            var gIconRT = gIcon.rectTransform;
            Anchor(gIconRT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            gIconRT.sizeDelta     = new Vector2(26f, 26f);
            gIcon.preserveAspect  = true;
            gIcon.raycastTarget   = false;
            if (_theme.IconSettings == null)
            {
                // gear.png not imported yet — keep a tappable circle and a best-effort glyph.
                gIcon.enabled = false;
                var glyph = MakeTMP(gearRT, "Glyph", _theme.TextPrimary, "⚙", 22, FontStyles.Bold);
                Stretch(glyph.rectTransform, 0, 0, 0, 0);
                glyph.alignment    = TextAlignmentOptions.Center;
                glyph.raycastTarget = false;
                Debug.LogWarning("[MainVsScreen] gear.png not found — settings gear uses a glyph fallback. " +
                                 "Drop gear.png into the Sprites folder and rebuild for the real icon.");
            }
            _gearButtonGO = gear;

            // ── Everything scrolls together: one ScrollRect whose content is a vertical stack
            //    (avatar → name → KPIs → history). The gear and bottom nav stay fixed on top. ──
            var scroll = MakeRect(panel, "Scroll");
            Stretch(scroll, 0, 4, 0, 0); // fills the panel; bottom extends under the floating nav
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal        = false;
            scrollRect.vertical          = true;
            scrollRect.movementType      = ScrollRect.MovementType.Elastic;
            scrollRect.scrollSensitivity = 26f;

            var viewport = MakeRect(scroll, "Viewport");
            Stretch(viewport, 0, 0, 0, 0);
            var vpImg = viewport.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0f); // transparent raycast target → the whole area drags
            viewport.gameObject.AddComponent<RectMask2D>();
            scrollRect.viewport = viewport;

            var content = MakeRect(viewport, "Content");
            Anchor(content, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta        = Vector2.zero;
            var cVL = content.gameObject.AddComponent<VerticalLayoutGroup>();
            cVL.spacing                = 6;
            cVL.padding                = new RectOffset(0, 0, 22, 110); // top room; bottom clears the nav
            cVL.childAlignment         = TextAnchor.UpperCenter;
            cVL.childControlWidth      = true;
            cVL.childControlHeight     = true;
            cVL.childForceExpandWidth  = true;
            cVL.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = content;

            // ── Avatar (centered in a full-width row) ───────────────────────────────────
            var avatarRow = ContentRow(content, "AvatarRow", 100);
            var avatar = MakeImage(avatarRow, "Avatar", new Color(1f, 1f, 1f, 0.06f), _theme.CircleShape);
            Anchor(avatar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            avatar.rectTransform.sizeDelta = new Vector2(100, 100);
            avatar.raycastTarget = false;
            if (_theme.NavProfile != null)
            {
                var ic = MakeImage(avatar.rectTransform, "Icon", Color.white, _theme.NavProfile);
                Anchor(ic.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                ic.rectTransform.sizeDelta = new Vector2(100, 100);
                ic.preserveAspect = true;
                ic.raycastTarget  = false;
            }

            // ── Name / rank / streak ────────────────────────────────────────────────────
            var name   = ContentLabel(content, "Name",   "Игрок",          26, FontStyles.Bold, _theme.TextPrimary,   34);
            var rank   = ContentLabel(content, "Rank",   "БРОНЗА",         15, FontStyles.Bold, _theme.TrophyGold,    22);
            var streak = ContentLabel(content, "Streak", "СЕРИЯ ПОБЕД: 0", 13, FontStyles.Bold, _theme.TextSecondary, 20);

            // ── Wardrobe button (centered in a row) ─────────────────────────────────────
            var wardRow  = ContentRow(content, "WardrobeRow", 54);
            var wardrobe = Spawn(Load("SecondaryChip"), wardRow);
            if (wardrobe != null)
            {
                wardrobe.GetComponent<SecondaryChip>()?.SetLabel("ГАРДЕРОБ");
                var wrt = (RectTransform)wardrobe.transform;
                Anchor(wrt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
                wrt.anchoredPosition = Vector2.zero;
                wrt.sizeDelta        = new Vector2(168, 44);
                var wle = wardrobe.GetComponent<LayoutElement>();
                if (wle != null) { wle.preferredWidth = 168; wle.preferredHeight = 44; }
                var wlbl = wardrobe.GetComponentInChildren<TextMeshProUGUI>(true);
                if (wlbl != null) { wlbl.enableWordWrapping = false; wlbl.enableAutoSizing = false; wlbl.fontSize = 14; }
            }

            // ── KPI badges (centered row) ───────────────────────────────────────────────
            var kpis = ContentRow(content, "KPIs", 86);
            var kHL = kpis.gameObject.AddComponent<HorizontalLayoutGroup>();
            kHL.spacing               = 12;
            kHL.childAlignment        = TextAnchor.MiddleCenter;
            kHL.childControlWidth     = false;
            kHL.childControlHeight    = false;
            kHL.childForceExpandWidth = false;

            var winsBadge    = SpawnStat(kpis, "0",   "ПОБЕДЫ");
            var winRateBadge = SpawnStat(kpis, "0%",  "ВИНРЕЙТ");
            var repsBadge    = SpawnStat(kpis, "0",   "ВСЕГО");

            // ── History header row: "RECENT MATCHES" + cycle filters (TYPE / MODE) ──────
            var head = ContentRow(content, "HistoryHeader", 30);
            var histTitle = MakeTMP(head, "Title", _theme.TextSecondary, "RECENT MATCHES", 13, FontStyles.Bold);
            Anchor(histTitle.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
            histTitle.rectTransform.anchoredPosition = new Vector2(4, 0);
            histTitle.rectTransform.sizeDelta        = new Vector2(220, 22);
            histTitle.alignment = TextAlignmentOptions.MidlineLeft;
            histTitle.characterSpacing = 3f;

            var typeFilter = BuildCycleFilter(head, "TYPE", -84,
                new (string, string)[] { ("ВСЕ", ""), ("ОТЖИМ.", "pushups") });
            var modeFilter = BuildCycleFilter(head, "MODE", 0,
                new (string, string)[] { ("ВСЕ", ""), ("ДУЭЛЬ", "pvp"), ("ГОСТ", "ghost") });

            // ── Match cards (prefab template; the presenter clones it per match) ────────
            var template = BuildMatchRowTemplate(content);

            var empty = ContentLabel(content, "HistoryEmpty", "Пока нет сыгранных матчей",
                                     14, FontStyles.Normal, _theme.TextSecondary, 40);
            empty.gameObject.SetActive(false);

            // Keep the fixed gear above the scrolling content.
            gear.transform.SetAsLastSibling();

            // ── Presenter (reads users/{uid} + history on enable) ───────────────────────
            var presenter = panel.gameObject.AddComponent<ProfilePresenter>();
            var so = new SerializedObject(presenter);
            so.FindProperty("_nameText").objectReferenceValue          = name;
            so.FindProperty("_rankText").objectReferenceValue          = rank;
            so.FindProperty("_streakText").objectReferenceValue        = streak;
            so.FindProperty("_winsBadge").objectReferenceValue         = winsBadge;
            so.FindProperty("_winRateBadge").objectReferenceValue      = winRateBadge;
            so.FindProperty("_repsBadge").objectReferenceValue         = repsBadge;
            so.FindProperty("_historyContent").objectReferenceValue    = content;
            so.FindProperty("_matchRowTemplate").objectReferenceValue  = template;
            so.FindProperty("_historyEmptyState").objectReferenceValue = empty.gameObject;
            so.FindProperty("_typeFilter").objectReferenceValue        = typeFilter;
            so.FindProperty("_modeFilter").objectReferenceValue        = modeFilter;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // A fixed-height row inside the scroll content (the VerticalLayoutGroup sizes it).
        static RectTransform ContentRow(RectTransform content, string name, float height)
        {
            var r  = MakeRect(content, name);
            var le = r.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            return r;
        }

        // A centered text row inside the scroll content.
        static TextMeshProUGUI ContentLabel(RectTransform content, string name, string text,
                                            float size, FontStyles style, Color color, float height)
        {
            var tmp = MakeTMP(content, name, color, text, size, style);
            tmp.alignment = TextAlignmentOptions.Center;
            var le = tmp.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height;
            return tmp;
        }

        // "TITLE" filter that cycles options on tap (no popup — robust inside a ScrollRect).
        static FilterDropdown BuildCycleFilter(RectTransform parent, string title, float xFromRight,
                                               (string label, string value)[] options)
        {
            var root = MakeRect(parent, $"Filter_{title}");
            Anchor(root, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f));
            root.anchoredPosition = new Vector2(xFromRight, 0);
            root.sizeDelta        = new Vector2(78, 24);

            var toggleImg = MakeImage(root, "Toggle", new Color(1f, 1f, 1f, 0.0001f), null);
            Stretch(toggleImg.rectTransform, 0, 0, 0, 0);
            var toggleBtn = toggleImg.gameObject.AddComponent<Button>();
            toggleBtn.targetGraphic = toggleImg;
            var label = MakeTMP(toggleImg.transform, "Label", _theme.TextSecondary, title, 13, FontStyles.Bold);
            Stretch(label.rectTransform, 0, 0, 0, 0);
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.raycastTarget = false;

            var optLabels = new string[options.Length];
            var optValues = new string[options.Length];
            for (int i = 0; i < options.Length; i++) { optLabels[i] = options[i].label; optValues[i] = options[i].value; }

            var fd = root.gameObject.AddComponent<FilterDropdown>();
            var so = new SerializedObject(fd);
            so.FindProperty("_toggle").objectReferenceValue = toggleBtn;
            so.FindProperty("_label").objectReferenceValue  = label;
            SetObjectArray(so, "_optionButtons", new Object[0]); // no popup → cycle mode
            SetStringArray(so, "_optionLabels", optLabels);
            SetStringArray(so, "_optionValues", optValues);
            so.FindProperty("_title").stringValue = title;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fd;
        }

        // Functional "TITLE ▾" filter: a clickable label that opens a popup of options.
        // Returns the FilterDropdown the presenter listens to.
        static FilterDropdown BuildFilter(RectTransform parent, string title, float xFromRight,
                                          (string label, string value)[] options)
        {
            var root = MakeRect(parent, $"Filter_{title}");
            Anchor(root, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f));
            root.anchoredPosition = new Vector2(xFromRight, 0);
            root.sizeDelta        = new Vector2(78, 24);

            // Toggle button (near-invisible image as the raycast target) + label.
            var toggleImg = MakeImage(root, "Toggle", new Color(1f, 1f, 1f, 0.0001f), null);
            Stretch(toggleImg.rectTransform, 0, 0, 0, 0);
            var toggleBtn = toggleImg.gameObject.AddComponent<Button>();
            toggleBtn.targetGraphic = toggleImg;
            var label = MakeTMP(toggleImg.transform, "Label", _theme.TextSecondary, title, 13, FontStyles.Bold);
            Stretch(label.rectTransform, 0, 0, 0, 0);
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.raycastTarget = false;

            // Popup (anchored just below the toggle, grows left from the right edge).
            var popup = MakeRect(root, "Popup");
            Anchor(popup, new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 1));
            popup.anchoredPosition = new Vector2(0, -6);
            popup.sizeDelta        = new Vector2(150, options.Length * 36 + 8);
            var pbg = MakeImage(popup, "Bg", new Color32(0x22, 0x22, 0x30, 0xFF), ProcSprite("pill_16"));
            pbg.type = Image.Type.Sliced;
            Stretch(pbg.rectTransform, 0, 0, 0, 0);
            var pvl = popup.gameObject.AddComponent<VerticalLayoutGroup>();
            pvl.padding                = new RectOffset(4, 4, 4, 4);
            pvl.spacing                = 2;
            pvl.childControlWidth      = true;
            pvl.childControlHeight     = true;
            pvl.childForceExpandWidth  = true;
            pvl.childForceExpandHeight = false;

            var optBtns   = new Object[options.Length];
            var optLabels = new string[options.Length];
            var optValues = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                var ob = MakeRect(popup, $"Opt{i}");
                ob.sizeDelta = new Vector2(0, 32);
                ob.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;
                var obImg = MakeImage(ob, "Bg", new Color(1f, 1f, 1f, 0.04f), ProcSprite("pill_12"));
                obImg.type = Image.Type.Sliced;
                Stretch(obImg.rectTransform, 0, 0, 0, 0);
                var obBtn = ob.gameObject.AddComponent<Button>();
                obBtn.targetGraphic = obImg;
                var obLbl = MakeTMP(ob.transform, "L", _theme.TextPrimary, options[i].label, 13, FontStyles.Bold);
                Stretch(obLbl.rectTransform, 10, 0, 10, 0);
                obLbl.alignment = TextAlignmentOptions.MidlineLeft;
                obLbl.raycastTarget = false;

                optBtns[i]   = obBtn;
                optLabels[i] = options[i].label;
                optValues[i] = options[i].value;
            }
            popup.gameObject.SetActive(false);

            var fd = root.gameObject.AddComponent<FilterDropdown>();
            var so = new SerializedObject(fd);
            so.FindProperty("_toggle").objectReferenceValue = toggleBtn;
            so.FindProperty("_label").objectReferenceValue  = label;
            so.FindProperty("_popup").objectReferenceValue  = popup.gameObject;
            SetObjectArray(so, "_optionButtons", optBtns);
            SetStringArray(so, "_optionLabels", optLabels);
            SetStringArray(so, "_optionValues", optValues);
            so.FindProperty("_title").stringValue = title;
            so.ApplyModifiedPropertiesWithoutUndo();
            return fd;
        }

        static void SetStringArray(SerializedObject so, string propName, string[] items)
        {
            var arr = so.FindProperty(propName);
            arr.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
                arr.GetArrayElementAtIndex(i).stringValue = items[i];
        }

        // The match card is a REAL prefab now (Assets/_Project/UI/Prefabs/MatchCard.prefab) so you can
        // hand-edit it in Unity (Prefab Mode) and the tweaks persist — the build tool only
        // INSTANTIATES it here, never regenerates it.
        static MatchRow BuildMatchRowTemplate(RectTransform parent)
        {
            EnsureMatchCardPrefab();
            var go = Spawn(Load("MatchCard"), parent);
            if (go == null) return null;
            go.SetActive(false); // template — the presenter clones it per match
            return go.GetComponent<MatchRow>();
        }

        // Creates MatchCard.prefab ONCE. Never overwritten afterwards, so manual visual edits
        // (positions, colours, rounding, transparency) survive every rebuild.
        static void EnsureMatchCardPrefab()
        {
            if (Load("MatchCard") != null) return;
            CreateMatchCardPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void CreateMatchCardPrefab()
        {
            var win        = (Color)new Color32(107, 255, 74, 255); // #6BFF4A — win score
            var winSprite  = _theme.IconWin  != null ? _theme.IconWin  : ProcSprite("win");
            var loseSprite = _theme.IconLose != null ? _theme.IconLose : ProcSprite("lose");
            var cardSprite = _theme.BgCard   != null ? _theme.BgCard   : ProcSprite("bg_card"); // #181B21 rounded card

            var rootGO = new GameObject("MatchCard", typeof(RectTransform));
            var row    = rootGO.GetComponent<RectTransform>();
            row.sizeDelta = new Vector2(360, 78);
            var le = rootGO.AddComponent<LayoutElement>();
            le.preferredHeight = 78; le.minHeight = 78;

            var bg = MakeImage(row, "Bg", new Color(1f, 1f, 1f, 0.80f), cardSprite); // semi-transparent
            bg.type = Image.Type.Sliced;
            Stretch(bg.rectTransform, 0, 0, 0, 0);
            bg.raycastTarget = false;

            // W/L badge — ready icon (win.png / lose.png); MatchRow swaps the sprite by result.
            var badge = MakeImage(row, "Badge", Color.white, winSprite);
            Anchor(badge.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
            badge.rectTransform.anchoredPosition = new Vector2(30, 0); // hug the left edge (win.png has small padding)
            badge.rectTransform.sizeDelta        = new Vector2(54, 54);
            badge.preserveAspect = true;
            badge.raycastTarget  = false;

            // Opponent + meta (clearly to the right of the badge — no overlap).
            var vsName = MakeTMP(row, "VsName", _theme.TextPrimary, "vs NOX_92", 17, FontStyles.Bold);
            Anchor(vsName.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
            vsName.rectTransform.anchoredPosition = new Vector2(66, 13);
            vsName.rectTransform.sizeDelta        = new Vector2(180, 24);
            vsName.alignment = TextAlignmentOptions.MidlineLeft;
            vsName.enableWordWrapping = false;
            vsName.overflowMode = TextOverflowModes.Ellipsis;

            var meta = MakeTMP(row, "Meta", _theme.TextSecondary, "2h - 60s - PUSHUPS", 11, FontStyles.Normal);
            Anchor(meta.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
            meta.rectTransform.anchoredPosition = new Vector2(66, -13);
            meta.rectTransform.sizeDelta        = new Vector2(210, 18);
            meta.alignment = TextAlignmentOptions.MidlineLeft;
            meta.enableWordWrapping = false;

            // Score (own reps coloured win/loss) — right-anchored horizontal group, grows left.
            var scoreRow = MakeRect(row, "Score");
            Anchor(scoreRow, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f));
            scoreRow.anchoredPosition = new Vector2(-18, 12);
            scoreRow.sizeDelta        = new Vector2(0, 30);
            var sHL = scoreRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            sHL.spacing                = 5;
            sHL.childAlignment         = TextAnchor.MiddleRight;
            sHL.childControlWidth      = true;
            sHL.childControlHeight     = true;
            sHL.childForceExpandWidth  = false;
            sHL.childForceExpandHeight = false;
            scoreRow.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var myScore  = MakeScoreCell(scoreRow, "MyScore",  "18", 24, win);
            MakeScoreCell(scoreRow, "Dash", "-", 22, _theme.TextSecondary);
            var oppScore = MakeScoreCell(scoreRow, "OppScore", "16", 24, _theme.TextSecondary);

            var record = MakeTMP(row, "Record", win, "NEW RECORD", 10, FontStyles.Bold);
            Anchor(record.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f));
            record.rectTransform.anchoredPosition = new Vector2(-18, -15);
            record.rectTransform.sizeDelta        = new Vector2(150, 16);
            record.alignment = TextAlignmentOptions.MidlineRight;

            var mr = row.gameObject.AddComponent<MatchRow>();
            var so = new SerializedObject(mr);
            so.FindProperty("_badge").objectReferenceValue      = badge;
            so.FindProperty("_winSprite").objectReferenceValue  = winSprite;
            so.FindProperty("_loseSprite").objectReferenceValue = loseSprite;
            so.FindProperty("_vsName").objectReferenceValue     = vsName;
            so.FindProperty("_meta").objectReferenceValue       = meta;
            so.FindProperty("_myScore").objectReferenceValue    = myScore;
            so.FindProperty("_oppScore").objectReferenceValue   = oppScore;
            so.FindProperty("_record").objectReferenceValue     = record;
            so.ApplyModifiedPropertiesWithoutUndo();

            EnsureFolder(PrefabsDir);
            PrefabUtility.SaveAsPrefabAsset(rootGO, $"{PrefabsDir}/MatchCard.prefab");
            Object.DestroyImmediate(rootGO);
        }

        static TextMeshProUGUI MakeScoreCell(RectTransform parent, string name, string text, float size, Color color)
        {
            var tmp = MakeTMP(parent.transform, name, color, text, size, FontStyles.Bold);
            tmp.alignment           = TextAlignmentOptions.Center;
            tmp.enableWordWrapping  = false; // never wrap a 2-digit score onto two lines
            tmp.overflowMode        = TextOverflowModes.Overflow;
            var le = tmp.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth  = name == "Dash" ? 10 : 30;
            le.preferredHeight = 28;
            return tmp;
        }

        // Spawns a design-system StatBadge, previews value/label in edit mode, returns the component.
        static StatBadge SpawnStat(RectTransform parent, string value, string label)
        {
            var go = Spawn(Load("StatBadge"), parent);
            if (go == null) return null;
            var badge = go.GetComponent<StatBadge>();
            badge?.SetStat(value, label);
            return badge;
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
        //  SETTINGS OVERLAY  (phase 07 — store-required settings + GDPR delete)
        // ════════════════════════════════════════════════════════════════════════
        // Full-screen overlay reached from the profile gear. Toggles (sound / vibration /
        // notifications) persist via PlayerPrefsSettingsStore; language is RU/EN; Privacy &
        // Terms open in the browser; delete-account runs FirebaseAuth.DeleteAsync after an
        // explicit confirmation, then restarts from Boot. Everything is wired to SettingsScreen.
        static void BuildSettingsOverlay(Transform canvasRoot, RectTransform mirror,
                                         RectTransform mainContent, CanvasGroup mainGroup)
        {
            // ── Overlay root: full-screen, fades in, swallows input ─────────────────────
            var overlay = MakeRect(mirror, "SettingsOverlay");
            Stretch(overlay, 0, 0, 0, 0);
            var canvasGroup = overlay.gameObject.AddComponent<CanvasGroup>();

            // Settings is a flat solid screen (#1F2229) — no background image, no lightning.
            var bg = MakeImage(overlay, "Background", new Color32(0x1F, 0x22, 0x29, 0xFF));
            Stretch(bg.rectTransform, 0, 0, 0, 0);
            bg.raycastTarget = true;

            var safe = MakeRect(overlay, "SafeArea");
            Stretch(safe, 0, 0, 0, 0);
            safe.gameObject.AddComponent<SafeAreaFitter>();

            var content = MakeRect(safe, "Content");
            Stretch(content, 0, 0, 0, 0);

            // ── Header: back button (left) + title ───────────────────────────────────────
            var backBtn = MakePillButton(content, "BackButton", "НАЗАД", _theme.BtnSecondaryBg, _theme.TextPrimary, 92, 40, out _);
            var backRT  = (RectTransform)backBtn.transform;
            Anchor(backRT, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            backRT.anchoredPosition = new Vector2(16f, -26f);

            var title = MakeTMP(content, "Title", _theme.TextPrimary, "НАСТРОЙКИ", 22, FontStyles.Bold);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            title.rectTransform.anchoredPosition = new Vector2(0f, -30f);
            title.rectTransform.sizeDelta        = new Vector2(-32f, 32f);
            title.alignment = TextAlignmentOptions.Center;
            // Full-width title overlaps the back button — must not eat its clicks (TMP defaults to true).
            title.raycastTarget = false;

            // ── Vertical list of setting rows (top-anchored, sized by its content) ──────
            var list = MakeRect(content, "List");
            Anchor(list, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            list.anchoredPosition = new Vector2(0f, -90f);
            list.sizeDelta        = new Vector2(-48f, 0f); // 24 px inset each side; height from fitter
            var vl = list.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing                = 10;
            vl.childAlignment         = TextAnchor.UpperCenter;
            vl.childControlWidth      = true;
            vl.childControlHeight     = true;
            vl.childForceExpandWidth  = true;
            vl.childForceExpandHeight = false;
            list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Toggles (persisted via PlayerPrefsSettingsStore in SettingsScreen) ──────────
            var soundRow = MakeCardRow(list, "Row_Sound", "Звук", out _, out _);
            var soundToggle = MakeSwitch(soundRow);

            var vibrationRow = MakeCardRow(list, "Row_Vibration", "Вибрация", out _, out _);
            var vibrationToggle = MakeSwitch(vibrationRow);

            var notificationsRow = MakeCardRow(list, "Row_Notifications", "Уведомления", out _, out _);
            var notificationsToggle = MakeSwitch(notificationsRow);

            // Language RU / EN ───────────────────────────────────────────────────────────
            var langRow = MakeCardRow(list, "Row_Language", "Язык", out _, out _);
            var langGroup = MakeRect(langRow, "LangGroup");
            var lgLE = langGroup.gameObject.AddComponent<LayoutElement>();
            lgLE.preferredWidth = 132; lgLE.preferredHeight = 40; lgLE.flexibleWidth = 0;
            var lgHL = langGroup.gameObject.AddComponent<HorizontalLayoutGroup>();
            lgHL.spacing = 8; lgHL.childAlignment = TextAnchor.MiddleRight;
            lgHL.childControlWidth = false; lgHL.childControlHeight = false; lgHL.childForceExpandWidth = false;
            var ruBtn = MakePillButton(langGroup, "LangRu", "RU", _theme.BtnSecondaryBg, _theme.TextPrimary, 60, 40, out var ruLbl);
            var enBtn = MakePillButton(langGroup, "LangEn", "EN", _theme.BtnSecondaryBg, _theme.TextSecondary, 60, 40, out var enLbl);

            // Legal links (open in browser) ───────────────────────────────────────────────
            var privacyBtn = MakeLinkRow(list, "Row_Privacy", "Политика конфиденциальности");
            var termsBtn   = MakeLinkRow(list, "Row_Terms",   "Условия использования");

            // App version (read-only) ──────────────────────────────────────────────────────
            var versionRow = MakeCardRow(list, "Row_Version", "Версия", out _, out _);
            var versionText = MakeTMP(versionRow, "Value", _theme.TextSecondary, "v—", 15, FontStyles.Bold);
            versionText.alignment = TextAlignmentOptions.MidlineRight;
            versionText.raycastTarget = false;
            var verLE = versionText.gameObject.AddComponent<LayoutElement>();
            verLE.preferredWidth = 96; verLE.preferredHeight = 24; verLE.flexibleWidth = 0;

            // Delete account (danger, full width) ────────────────────────────────────────
            var deleteBtn = MakePillButton(list, "DeleteButton", "УДАЛИТЬ АККАУНТ", _theme.BtnDangerBg, _theme.BtnDangerFg, 0, 54, out _);
            var delLE = deleteBtn.GetComponent<LayoutElement>();
            delLE.preferredHeight = 54; delLE.flexibleWidth = 1;

            // ── Confirm-delete dialog (above content; hidden until delete is tapped) ─────
            var confirm = MakeRect(overlay, "ConfirmDialog");
            Stretch(confirm, 0, 0, 0, 0);
            var scrim = MakeImage(confirm, "Scrim", new Color(0f, 0f, 0f, 0.72f));
            Stretch(scrim.rectTransform, 0, 0, 0, 0);
            scrim.raycastTarget = true;

            var card = MakeRect(confirm, "Card");
            Anchor(card, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            card.sizeDelta = new Vector2(322f, 244f);
            var cardBg = MakeImage(card, "Bg", _theme.NavBg, ProcSprite("pill_24"));
            cardBg.type = Image.Type.Sliced; Stretch(cardBg.rectTransform, 0, 0, 0, 0);

            var cTitle = MakeTMP(card, "Title", _theme.TextPrimary, "Удалить аккаунт?", 20, FontStyles.Bold);
            Anchor(cTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            cTitle.rectTransform.anchoredPosition = new Vector2(0f, -26f);
            cTitle.rectTransform.sizeDelta        = new Vector2(-32f, 28f);
            cTitle.alignment = TextAlignmentOptions.Center;
            cTitle.raycastTarget = false;

            var cBody = MakeTMP(card, "Body", _theme.TextSecondary,
                "Это действие необратимо. Весь прогресс,\nтрофеи и история матчей будут удалены.",
                14, FontStyles.Normal);
            Anchor(cBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            cBody.rectTransform.anchoredPosition = new Vector2(0f, 14f);
            cBody.rectTransform.sizeDelta        = new Vector2(280f, 80f);
            cBody.alignment = TextAlignmentOptions.Center;
            cBody.raycastTarget = false;

            var noBtn = MakePillButton(card, "ConfirmNo", "ОТМЕНА", _theme.BtnSecondaryBg, _theme.TextPrimary, 130, 46, out _);
            var noRT = (RectTransform)noBtn.transform;
            Anchor(noRT, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));
            noRT.anchoredPosition = new Vector2(20f, 22f);

            var yesBtn = MakePillButton(card, "ConfirmYes", "УДАЛИТЬ", _theme.BtnDangerBg, _theme.BtnDangerFg, 130, 46, out _);
            var yesRT = (RectTransform)yesBtn.transform;
            Anchor(yesRT, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            yesRT.anchoredPosition = new Vector2(-20f, 22f);

            confirm.gameObject.SetActive(false);

            // ── Juicy entrance (matches the search overlay's calm staggered feel) ────────
            var juicy = overlay.gameObject.AddComponent<JuicyScreen>();
            var jso   = new SerializedObject(juicy);
            jso.FindProperty("_overlayGroup").objectReferenceValue = canvasGroup;
            jso.FindProperty("_root").objectReferenceValue         = content;
            var elements = new (RectTransform target, int entrance, float dist, float delay, float dur)[]
            {
                (title.rectTransform, 2, 40f, 0.06f, 0.46f), // title eases down from the top
                (backRT,              2, 30f, 0.08f, 0.46f), // back button slides down
                (list,                0,  0f, 0.14f, 0.50f), // rows pop in together
            };
            var arr = jso.FindProperty("_elements");
            arr.arraySize = elements.Length;
            for (int i = 0; i < elements.Length; i++)
            {
                var el = arr.GetArrayElementAtIndex(i);
                el.FindPropertyRelative("target").objectReferenceValue = elements[i].target;
                el.FindPropertyRelative("entrance").enumValueIndex      = elements[i].entrance;
                el.FindPropertyRelative("moveDistance").floatValue      = elements[i].dist;
                el.FindPropertyRelative("delay").floatValue             = elements[i].delay;
                el.FindPropertyRelative("duration").floatValue          = elements[i].dur;
            }
            jso.ApplyModifiedPropertiesWithoutUndo();

            // Hidden until the player taps the profile gear.
            overlay.gameObject.SetActive(false);

            // ── Controller: always-active GO so it keeps running while the overlay toggles ─
            var ctrlGO = new GameObject("SettingsScreenController");
            ctrlGO.transform.SetParent(canvasRoot, false);
            var ctrl = ctrlGO.AddComponent<SettingsScreen>();

            var gearButton = _gearButtonGO != null ? _gearButtonGO.GetComponent<Button>() : null;

            var so = new SerializedObject(ctrl);
            so.FindProperty("_overlay").objectReferenceValue            = overlay.gameObject;
            so.FindProperty("_juicy").objectReferenceValue              = juicy;
            so.FindProperty("_mainContent").objectReferenceValue        = mainContent;
            so.FindProperty("_mainGroup").objectReferenceValue          = mainGroup;
            so.FindProperty("_gearButton").objectReferenceValue         = gearButton;
            so.FindProperty("_backButton").objectReferenceValue         = backBtn;
            so.FindProperty("_soundToggle").objectReferenceValue        = soundToggle;
            so.FindProperty("_vibrationToggle").objectReferenceValue    = vibrationToggle;
            so.FindProperty("_notificationsToggle").objectReferenceValue = notificationsToggle;
            so.FindProperty("_langRuButton").objectReferenceValue       = ruBtn;
            so.FindProperty("_langEnButton").objectReferenceValue       = enBtn;
            so.FindProperty("_langRuLabel").objectReferenceValue        = ruLbl;
            so.FindProperty("_langEnLabel").objectReferenceValue        = enLbl;
            so.FindProperty("_privacyButton").objectReferenceValue      = privacyBtn;
            so.FindProperty("_termsButton").objectReferenceValue        = termsBtn;
            so.FindProperty("_versionText").objectReferenceValue        = versionText;
            so.FindProperty("_deleteButton").objectReferenceValue       = deleteBtn;
            so.FindProperty("_confirmDialog").objectReferenceValue      = confirm.gameObject;
            so.FindProperty("_confirmYesButton").objectReferenceValue   = yesBtn;
            so.FindProperty("_confirmNoButton").objectReferenceValue    = noBtn;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // A settings row "card": rounded background, a left-aligned label, and room on the right
        // for a control (added by the caller as the next layout child). Label flex-expands so the
        // control hugs the right edge. The background is layout-ignored so the row's HLG never
        // resizes it.
        static RectTransform MakeCardRow(RectTransform list, string name, string label,
                                         out TextMeshProUGUI lbl, out Image bg, float height = 58f)
        {
            var row = MakeRect(list, name);
            var le  = row.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.minHeight = height; le.flexibleWidth = 1;

            bg = MakeImage(row, "Bg", new Color(1f, 1f, 1f, 0.05f), ProcSprite("pill_16"));
            bg.type = Image.Type.Sliced;
            Stretch(bg.rectTransform, 0, 0, 0, 0);
            bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding                = new RectOffset(18, 14, 0, 0);
            hl.spacing                = 10;
            hl.childAlignment         = TextAnchor.MiddleLeft;
            hl.childControlWidth      = true;  hl.childControlHeight      = true;
            hl.childForceExpandWidth  = false; hl.childForceExpandHeight  = false;

            lbl = MakeTMP(row, "Label", _theme.TextPrimary, label, 16, FontStyles.Normal);
            lbl.alignment = TextAlignmentOptions.MidlineLeft;
            var lle = lbl.gameObject.AddComponent<LayoutElement>();
            lle.flexibleWidth = 1; lle.preferredHeight = 24;
            return row;
        }

        // A full-row link (the whole card is the button) that opens a URL. A ">" affordance sits
        // on the right.
        static Button MakeLinkRow(RectTransform list, string name, string label)
        {
            var row = MakeCardRow(list, name, label, out _, out var bg);
            bg.raycastTarget = true;
            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;

            var chevron = MakeTMP(row, "Chevron", _theme.TextSecondary, ">", 18, FontStyles.Bold);
            chevron.alignment = TextAlignmentOptions.MidlineRight;
            var cle = chevron.gameObject.AddComponent<LayoutElement>();
            cle.preferredWidth = 18; cle.preferredHeight = 24; cle.flexibleWidth = 0;
            return btn;
        }

        // A binary switch built on Unity's Toggle: a dark capsule track, filled lime when on
        // (Toggle.graphic). No knob animation — the colour fill is the on/off signal.
        static Toggle MakeSwitch(RectTransform parent)
        {
            var go = new GameObject("Switch", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(54f, 30f);
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = 54f; le.preferredHeight = 30f; le.flexibleWidth = 0;

            var toggle = go.AddComponent<Toggle>();

            var track = MakeImage(rt, "Track", new Color(1f, 1f, 1f, 0.12f), ProcSprite("pill_capsule"));
            track.type = Image.Type.Sliced;
            Stretch(track.rectTransform, 0, 0, 0, 0);
            toggle.targetGraphic = track;

            var fill = MakeImage(track.rectTransform, "OnFill", _theme.AccentLime, ProcSprite("pill_capsule"));
            fill.type = Image.Type.Sliced;
            Stretch(fill.rectTransform, 3, 3, 3, 3);
            fill.raycastTarget = false;
            toggle.graphic = fill;

            toggle.isOn = true; // SettingsScreen syncs the real value from the store on enable
            return toggle;
        }

        // A solid pill button with a centred label. Pass w = 0 when the parent layout controls the
        // width (e.g. the full-width delete button inside the rows list).
        static Button MakePillButton(RectTransform parent, string name, string label,
                                     Color bg, Color fg, float w, float h, out TextMeshProUGUI lbl)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt  = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(w, h);

            var img = go.GetComponent<Image>();
            img.sprite = ProcSprite("pill_16");
            img.type   = Image.Type.Sliced;
            img.color  = bg;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = w; le.preferredHeight = h; le.flexibleWidth = 0;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            lbl = MakeTMP(rt, "Label", fg, label, 14, FontStyles.Bold);
            lbl.alignment = TextAlignmentOptions.Center;
            Stretch(lbl.rectTransform, 6, 0, 6, 0);
            lbl.raycastTarget = false;
            return btn;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  BOTTOM NAV (functional)
        // ════════════════════════════════════════════════════════════════════════
        static TabButton[] BuildBottomNav(RectTransform safe)
        {
            var navBar = MakeRect(safe, "BottomNav");
            Anchor(navBar, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0));
            navBar.anchoredPosition = new Vector2(0, NavBarBottom);
            navBar.sizeDelta        = new Vector2(316, 66);

            // menu_floor.png (the gold-rimmed plate from the comp) first; the theme's own NavPlate
            // (plashka.png, 4.82:1 — close enough to this bar's 4.79:1 to stretch cleanly) and the
            // procedural pill are the fallbacks if it isn't in the project. menu_floor is 2.92:1, far
            // off the bar's own ratio, so unlike plashka it needs a real 9-slice border to keep its
            // rounded caps circular instead of pinched into ellipses — see SpriteImporter's
            // menu_floor case for where that border comes from.
            var menuFloor = ProcSprite("menu_floor");
            var navPlate  = menuFloor != null
                ? menuFloor
                : (_theme.NavPlate != null ? _theme.NavPlate : ProcSprite("plashka"));
            var navBg    = navPlate != null
                ? MakeImage(navBar, "Bg", Color.white, navPlate)
                : MakeImage(navBar, "Bg", new Color(0f, 0f, 0f, 0.55f), ProcSprite("pill_24"));
            // Unconditional: a zero sprite border (plashka, pill_24) makes Sliced behave exactly
            // like Simple, so this only changes anything for a sprite that actually has a border —
            // which is precisely the case that needs it.
            navBg.type = Image.Type.Sliced;
            Stretch(navBg.rectTransform, 0, 0, 0, 0);
            if (menuFloor != null && navPlate == menuFloor)
            {
                // Hand-tuned in the Scene view (Tools ▸ Push Stars ▸ Dump Main Screen Layout is the
                // usual way to pull numbers like this back out) because the imported sprite border
                // was not taking effect when this was tuned — the Inspector still reported "This
                // Image doesn't have a border" with Sliced selected. This scale is what actually
                // fit the capsule to the bar in that state; if SpriteImporter's menu_floor border
                // is confirmed working later, this compensating scale should be revisited — the
                // two are two different fixes for the same squashed-caps problem, not meant to
                // stack.
                navBg.rectTransform.localScale = new Vector3(0.76f, 1.1515151f, 1.1515151f);
            }

            // No layout group, same reasoning as the action row: three fixed buttons, so their
            // positions are authored below and stay draggable in the Scene view.

            // Ready Figma nav buttons (whole circular sprites). Centre VS is larger.
            //   League  → statics.png (Статистика, bar chart)
            //   Duel    → main_btn_VS_active.png (blue VS, primary centre)
            //   Profile → profile.png / profile_active.png
            var league  = MakeNavButton(navBar, TabId.League,  _theme.IconStatics, _theme.IconStatics,      52);
            var duel    = MakeNavButton(navBar, TabId.Duel,    _theme.VSBadge,     _theme.VSBadge,          66);
            var profile = MakeNavButton(navBar, TabId.Profile, _theme.NavProfile,  _theme.NavProfileActive, 52);

            // Off-centre by design, not a leftover: nudged in the Scene view against the actual
            // menu_floor plate (whose visible capsule, after the scale above, sits slightly
            // narrower than the bar) so all three icons read as centred within the SHAPE the
            // player sees, not within the invisible 316 pt bar behind it.
            PlaceInNav(league,  -80.3f);
            PlaceInNav(duel,      0f);
            PlaceInNav(profile,  81.3f);

            // MainShellView expects an array; order is cosmetic (each knows its TabId).
            return new[] { league, duel, profile };
        }

        /// <summary>Centres a nav button vertically in the bar at a given horizontal offset.</summary>
        static void PlaceInNav(TabButton tab, float x)
        {
            var rt = (RectTransform)tab.transform;
            Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rt.anchoredPosition = new Vector2(x, 0f);
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

        // Top bar pinned to the safe-area edges: the trophy pill hugs the LEFT, the currency tags
        // hug the RIGHT (sized to their content, growing leftward). No fixed-width math is done
        // against the screen, so the content can never overflow — it adapts to any width.
        static void BuildTopBar(RectTransform panel)
        {
            const float topY = -14f;

            BuildTrophyPill(panel, new Vector2(16f, topY));

            // Streak + gem + aura — group anchored to the top-right corner, content-sized,
            // growing left. Three tags plus the trophy pill is what fits at 390 pt; that is why
            // BuildHudPill runs at 18 pt tall rather than the 22 it used with two.
            var hud = MakeRect(panel, "HudGroup");
            Anchor(hud, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f));
            hud.anchoredPosition = new Vector2(-16f, topY);

            var hl = hud.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing                = 4;
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

        // Streak + gem + aura as three separate black tags. The icon is LARGER than the tag and
        // sits ON TOP of its left edge (sticking out left), with the number inside on the right.
        static void BuildStreakAura(RectTransform parent)
        {
            var streakIcon = _theme.IconStreak != null ? _theme.IconStreak : _theme.IconLightning;
            BuildHudPill(parent, "StreakPill", streakIcon,      "12",  _theme.AccentYellow);
            BuildHudPill(parent, "GemPill",    _theme.IconGem,  "312", _theme.GemGreen);
            BuildHudPill(parent, "AuraPill",   _theme.IconAura, "660", _theme.AuraViolet);
        }

        // Black tag with the cup overhanging its left edge and an "i" badge on its right — the
        // same overhang trick the currency tags use, so the whole bar reads as one family. Two
        // rows inside it: the trophy count on top, league progress underneath.
        // Replaces the old "2 УРОВЕНЬ" pill: the mock-up shows trophies here, and the level
        // already has its own home on the profile tab.
        static void BuildTrophyPill(RectTransform panel, Vector2 anchoredPos)
        {
            const float plateW = 90f, plateH = 30f, iconSize = 36f, overhang = 14f;
            const float barW = 48f, barH = 9f, barFill = 0.55f;

            var root = MakeRect(panel, "TrophyPill");
            Anchor(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            root.anchoredPosition = anchoredPos;
            root.sizeDelta        = new Vector2(plateW + overhang, iconSize);

            var plate = MakeImage(root, "Plate", new Color32(20, 20, 28, 235), ProcSprite("pill_24"));
            plate.type          = Image.Type.Sliced;
            plate.raycastTarget = false;
            var prt = plate.rectTransform;
            Anchor(prt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
            prt.sizeDelta        = new Vector2(plateW, plateH);
            prt.anchoredPosition = new Vector2(overhang, 0f);

            // Upper row — the count. Left padding clears the cup that overlaps the plate.
            var num = MakeTMP(prt, "Number", _theme.TrophyGold, "955", 14, FontStyles.Bold);
            num.alignment     = TextAlignmentOptions.MidlineLeft;
            num.raycastTarget = false;
            var nrt = num.rectTransform;
            Anchor(nrt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
            nrt.sizeDelta        = new Vector2(-30f, 18f);
            nrt.anchoredPosition = new Vector2(9f, -1f);

            // Lower row — progress toward the next league, on its own dark track.
            var track = MakeImage(prt, "ProgressTrack", new Color32(48, 48, 62, 255), ProcSprite("pill_12"));
            track.type          = Image.Type.Sliced;
            track.raycastTarget = false;
            var trt = track.rectTransform;
            Anchor(trt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));
            trt.sizeDelta        = new Vector2(barW, barH);
            trt.anchoredPosition = new Vector2(30f, 4f);

            var fill = MakeImage(trt, "Fill", new Color32(240, 138, 30, 255), ProcSprite("pill_12"));
            fill.type          = Image.Type.Sliced;
            fill.raycastTarget = false;
            var frt = fill.rectTransform;
            Anchor(frt, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f));
            frt.sizeDelta        = new Vector2(barW * barFill, 0f);
            frt.anchoredPosition = new Vector2(0f, 0f);

            var cup = MakeImage(root, "Cup", Color.white, _theme.IconCup);
            cup.preserveAspect = true;
            cup.raycastTarget  = false;
            var crt = cup.rectTransform;
            Anchor(crt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f));
            crt.sizeDelta        = new Vector2(iconSize, iconSize);
            crt.anchoredPosition = new Vector2(iconSize * 0.5f, 0f);

            AddInfoBadge(root, new Vector2(-2f, -2f));
        }

        // Backing is drawn at the sprite's OWN aspect ratio (not stretched). Smaller overall.
        static void BuildHudPill(RectTransform parent, string name, Sprite icon, string number,
                                 Color numberColor)
        {
            const float pillH = 18f;                 // three of these have to share the right edge
            const float nativeAspect = 66f / 21f;    // bg_streak_aura native ratio — keep it
            float pillW = Mathf.Round(pillH * nativeAspect);
            const float iconSize = 26f, overhang = 9f;
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
            var num = MakeTMP(root, "Number", numberColor, number, 12, FontStyles.Bold);
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

        /// <summary>Round М/Ж button that flips the body on the stage. It sits in the free corner
        /// of the wardrobe-slot grid, on the character's own panel, because that is what it
        /// changes: an appearance control with its result visible right behind it — not a setting
        /// buried two screens away. The label is authored as "М" and re-read from the saved choice
        /// by <see cref="CharacterRoster"/> on Play.</summary>
        static void BuildGenderSwitch(RectTransform panel, Vector2 anchoredPos)
        {
            var go = new GameObject("GenderSwitch", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(panel, false);
            var rt = (RectTransform)go.transform;
            Anchor(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta        = new Vector2(40, 40);

            var img = go.GetComponent<Image>();
            img.sprite = _theme.CircleShape != null ? _theme.CircleShape : ProcSprite("circle_128");
            img.color  = _theme.NavBg;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var lbl = MakeTMP(rt, "Label", _theme.TextPrimary, "М", 16, FontStyles.Bold);
            lbl.alignment     = TextAlignmentOptions.Center;
            lbl.raycastTarget = false;
            Stretch(lbl.rectTransform, 0, 0, 0, 0);

            if (_roster == null) return;
            var so = new SerializedObject(_roster);
            so.FindProperty("_switchButton").objectReferenceValue = btn;
            so.FindProperty("_switchLabel").objectReferenceValue  = lbl;
            so.ApplyModifiedPropertiesWithoutUndo();
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
            // Small labels move to the light-keyline preset; the full one fuses their letters.
            FontSetup.ApplyOutlineFor(tmp, size);

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
            // Pipeline-appropriate shader: a URP material renders magenta under the built-in
            // pipeline (which is what this project is actually set to) and vice versa. Plain lit,
            // not the character's toon shader — the blockman is a stand-in, not the styled look.
            var shader = MainCharacterSetup.LitShader();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            if (!MainCharacterSetup.RendersInThisPipeline(mat) && shader != null) mat.shader = shader;

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
