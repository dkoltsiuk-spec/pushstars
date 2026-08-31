using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using PushStars.UI;

namespace PushStars.Editor
{
    public static class SceneSetup
    {
        // ── colour palette (placeholder, replaced in Phase 03) ──────────────
        private static readonly Color BgDark      = new(0.08f, 0.08f, 0.10f);
        private static readonly Color NavBg        = new(0.13f, 0.13f, 0.16f);
        private static readonly Color AccentYellow = new(1.00f, 0.84f, 0.00f);
        private static readonly Color TextWhite    = new(0.95f, 0.95f, 0.95f);
        private static readonly Color TabInactive  = new(0.45f, 0.45f, 0.50f);

        // ── tab definitions ─────────────────────────────────────────────────
        private static readonly (string label, TabId id)[] Tabs =
        {
            ("LEAGUE",  TabId.League),
            ("VS",      TabId.Duel),
            ("PROFILE", TabId.Profile),
        };

        // ════════════════════════════════════════════════════════════════════
        //  MENU ITEMS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Kept as the old menu path; the boot screen itself is built by
        /// <see cref="BootSceneSetup"/>, which owns the loading UI as well as the bootstrap.</summary>
        [MenuItem("Tools/Push Stars/Setup Boot Scene", priority = 1)]
        public static void SetupBootScene() => BootSceneSetup.BuildScene();

        [MenuItem("Tools/Push Stars/Setup Main Scene", priority = 2)]
        public static void SetupMainScene()
        {
            var scene = OpenOrCreateScene("Assets/_Project/Scenes/Main.unity");
            ClearScene();

            // ── Root canvas ─────────────────────────────────────────────────
            var canvasGO = CreateCanvas("MainCanvas");
            var canvas   = canvasGO.GetComponent<Canvas>();

            // Dark background image on the canvas itself
            SetImageColor(canvasGO.AddComponent<Image>(), BgDark);

            // ── Content area (above nav bar) ────────────────────────────────
            var contentArea = CreateRectFill(canvasGO, "ContentArea");
            var contentRect = contentArea.GetComponent<RectTransform>();
            // Leave 80 px at bottom for nav bar
            contentRect.offsetMin = new Vector2(0, 80);
            contentRect.offsetMax = Vector2.zero;

            // ── Three tab panels ────────────────────────────────────────────
            var leaguePanel  = CreateTabPanel(contentArea, "LeaguePanel",  "LEAGUE\n[Phase 11]",  new Color(0.12f, 0.16f, 0.22f));
            var duelPanel    = CreateTabPanel(contentArea, "DuelPanel",    "VS / DUEL\n[Phase 03–14]", new Color(0.10f, 0.10f, 0.14f));
            var profilePanel = CreateTabPanel(contentArea, "ProfilePanel", "PROFILE\n[Phase 06]", new Color(0.12f, 0.10f, 0.18f));

            // ── Nav bar ─────────────────────────────────────────────────────
            var navBar     = CreateRectFill(canvasGO, "NavBar");
            var navRect    = navBar.GetComponent<RectTransform>();
            navRect.anchorMin = new Vector2(0, 0);
            navRect.anchorMax = new Vector2(1, 0);
            navRect.pivot     = new Vector2(0.5f, 0);
            navRect.offsetMin = Vector2.zero;
            navRect.offsetMax = new Vector2(0, 80);
            SetImageColor(navBar.AddComponent<Image>(), NavBg);

            // Horizontal layout for buttons
            var hLayout = navBar.AddComponent<HorizontalLayoutGroup>();
            hLayout.childForceExpandWidth  = true;
            hLayout.childForceExpandHeight = true;
            hLayout.padding = new RectOffset(8, 8, 8, 8);
            hLayout.spacing = 4;

            // ── Tab buttons ─────────────────────────────────────────────────
            var tabButtons = new TabButton[Tabs.Length];
            for (int i = 0; i < Tabs.Length; i++)
            {
                tabButtons[i] = CreateTabButton(navBar, Tabs[i].label, Tabs[i].id);
            }

            // ── MainShell ───────────────────────────────────────────────────
            var shellGO = new GameObject("MainShell");
            shellGO.transform.SetParent(canvasGO.transform, false);
            var shell = shellGO.AddComponent<MainShellView>();

            var shellSO = new SerializedObject(shell);
            SetObjectArray(shellSO, "_tabButtons", tabButtons);
            shellSO.FindProperty("_leaguePanel").objectReferenceValue  = leaguePanel;
            shellSO.FindProperty("_duelPanel").objectReferenceValue    = duelPanel;
            shellSO.FindProperty("_profilePanel").objectReferenceValue = profilePanel;
            shellSO.ApplyModifiedPropertiesWithoutUndo();

            // ── EventSystem ─────────────────────────────────────────────────
            EnsureEventSystem(canvasGO);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[PushStars] Main.unity setup complete. Press Play to test tab switching.");
        }

        [MenuItem("Tools/Push Stars/Add Scenes to Build Settings", priority = 3)]
        public static void AddScenesToBuild()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene(BootSceneSetup.ScenePath, true),
                new EditorBuildSettingsScene("Assets/_Project/Scenes/Main.unity", true),
                new EditorBuildSettingsScene(OnboardingSceneSetup.ScenePath, true),
                new EditorBuildSettingsScene(FightSceneSetup.ScenePath, true),
            };
            EditorBuildSettings.scenes = scenes;
            Debug.Log("[PushStars] Build Settings updated: Boot(0), Main(1), Onboarding(2), Fight(3).");
        }

        [MenuItem("Tools/Push Stars/Run Full Setup", priority = 10)]
        public static void RunFullSetup()
        {
            SetupBootScene();
            SetupMainScene();
            OnboardingSceneSetup.BuildScene();
            AddScenesToBuild();
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Main.unity");
            Debug.Log("[PushStars] Full setup done. Scene Main is now active — press Play.");
        }

        // ════════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════════

        private static Scene OpenOrCreateScene(string path)
        {
            // Save current dirty scene first
            if (EditorSceneManager.GetActiveScene().isDirty)
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static void ClearScene()
        {
            var roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var r in roots)
                Object.DestroyImmediate(r);
        }

        private static GameObject CreateCanvas(string name)
        {
            var go     = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight  = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        private static GameObject CreateRectFill(GameObject parent, string name)
        {
            var go   = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go;
        }

        private static GameObject CreateTabPanel(GameObject parent, string name, string labelText, Color bgColor)
        {
            var panel = CreateRectFill(parent, name);
            SetImageColor(panel.AddComponent<Image>(), bgColor);

            // Centered placeholder label
            var labelGO  = new GameObject("Label");
            labelGO.transform.SetParent(panel.transform, false);
            var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0.1f, 0.3f);
            labelRect.anchorMax = new Vector2(0.9f, 0.7f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = labelText;
            tmp.fontSize  = 48;
            tmp.color     = TextWhite;
            tmp.alignment = TextAlignmentOptions.Center;
            var bold = FontSetup.LoadBold();
            if (bold != null) tmp.font = bold; // else TMP falls back to LiberationSans

            return panel;
        }

        private static TabButton CreateTabButton(GameObject parent, string label, TabId tabId)
        {
            // Container
            var go   = new GameObject($"Tab_{label}");
            var rect = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent.transform, false);

            var btn = go.AddComponent<Button>();
            var img = go.AddComponent<Image>();
            img.color = Color.clear;

            // Serialize tabId via SO
            var btnComp = go.AddComponent<TabButton>();
            var so = new SerializedObject(btnComp);
            so.FindProperty("_tabId").enumValueIndex = (int)tabId;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Active indicator bar (top edge)
            var indicator    = new GameObject("Indicator");
            var indRect      = indicator.AddComponent<RectTransform>();
            indicator.transform.SetParent(go.transform, false);
            indRect.anchorMin = new Vector2(0.15f, 0.85f);
            indRect.anchorMax = new Vector2(0.85f, 0.95f);
            indRect.offsetMin = Vector2.zero;
            indRect.offsetMax = Vector2.zero;
            var indImg = indicator.AddComponent<Image>();
            indImg.color = AccentYellow;

            // Wire indicator into TabButton
            so.FindProperty("_activeIndicator").objectReferenceValue = indImg;

            // Label
            var labelGO = new GameObject("Label");
            var labRect = labelGO.AddComponent<RectTransform>();
            labelGO.transform.SetParent(go.transform, false);
            labRect.anchorMin = new Vector2(0, 0);
            labRect.anchorMax = new Vector2(1, 0.85f);
            labRect.offsetMin = Vector2.zero;
            labRect.offsetMax = Vector2.zero;

            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = label;
            tmp.fontSize  = 28;
            tmp.color     = TabInactive;
            tmp.alignment = TextAlignmentOptions.Center;
            var tabFont = FontSetup.LoadBold();
            if (tabFont != null) tmp.font = tabFont; // else TMP falls back to LiberationSans

            so.FindProperty("_label").objectReferenceValue = tmp;
            so.ApplyModifiedPropertiesWithoutUndo();

            return btnComp;
        }

        private static void EnsureEventSystem(GameObject canvasGO)
        {
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null)
                return;

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        private static void SetImageColor(Image img, Color color) => img.color = color;

        private static void SetObjectArray(SerializedObject so, string propName, Object[] items)
        {
            var arr = so.FindProperty(propName);
            arr.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }
    }
}
