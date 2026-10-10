using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PushStars.Editor
{
    /// <summary>
    /// Headless build entry points for CI (Codemagic). Run from batchmode, e.g.
    ///   Unity -batchmode -nographics -projectPath . -executeMethod PushStars.Editor.BuildScript.BuildiOS -quit
    ///
    /// Before building it: (1) turns on the PUSHSTARS_MEDIAPIPE define for iOS so the real pose
    /// adapter compiles into the player, (2) copies the Pose Landmarker model into StreamingAssets
    /// (LocalResourceManager is editor-only; the device uses StreamingAssetsResourceManager), and
    /// (3) sets the iOS camera-usage string + bundle id. Assumes the MediaPipe plugin has already
    /// been imported (the CI step does `-importPackage` first).
    /// </summary>
    public static class BuildScript
    {
        private const string Define       = "PUSHSTARS_MEDIAPIPE";
        // FULL model (was lite): lite's skeleton breaks at the bottom of frontal reps. Lite ships
        // too — the runtime falls back to it when the GPU delegate fails (full-on-CPU ≈ 12fps trap).
        private static readonly string[] ModelNames =
            { "pose_landmarker_full.bytes", "pose_landmarker_lite.bytes" };
        private const string ModelName    = "pose_landmarker_full.bytes"; // primary (kept for logs)
        private const string ModelSrcDir  = "Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe";
        private const string StreamingDir = "Assets/StreamingAssets";
        private const string BundleId     = "com.pushstars.app";

        // The real app. Boot MUST stay at index 0 — it is the loading screen and the router that
        // sends a first launch to Onboarding, an unfinished onboarding to the level test, and
        // everyone else to Main. testCV ships as a hidden debug scene (loadable by name only).
        private static readonly string[] AppScenes =
        {
            BootSceneSetup.ScenePath,
            "Assets/_Project/Scenes/Main.unity",
            OnboardingSceneSetup.ScenePath,
            FightSceneSetup.ScenePath,
            TrainingScreenSetup.ScenePath,
            "Assets/testCV.unity",
            "Assets/_Project/Scenes/FightPreparation.unity",
            "Assets/_Project/Scenes/FightResults.unity",
            "Assets/_Project/Scenes/RewardSummary.unity",
            "Assets/_Project/Scenes/CaseAward.unity",
            "Assets/_Project/Scenes/CaseOpening.unity",
            "Assets/_Project/Scenes/CaseReward.unity",
            "Assets/_Project/Scenes/AuraReward.unity",
        };

        // CI passes its own scene list to BuildPlayer; Editor Build Settings alone do not
        // guarantee these screens ship. Fail before building if a route is omitted or missing.
        public static void ValidateAppScenes()
        {
            var required = System.Enum.GetValues(typeof(PushStars.Fight.FightScreen))
                .Cast<PushStars.Fight.FightScreen>()
                .Select(PushStars.Fight.FightScreenNavigation.ScenePath);
            var omitted = required.Except(AppScenes).ToArray();
            var missing = AppScenes.Where(p => !File.Exists(p)).ToArray();
            if (omitted.Length > 0 || missing.Length > 0)
                throw new BuildFailedException("[Build] Invalid shipping scene list. Omitted routes: " +
                    string.Join(", ", omitted) + "; missing files: " + string.Join(", ", missing));
            if (AppScenes[0] != BootSceneSetup.ScenePath || AppScenes.Distinct().Count() != AppScenes.Length)
                throw new BuildFailedException("[Build] Boot must be first and shipping scenes must be unique.");
            foreach (var boss in PushStars.Core.BossCatalog.Bosses)
            {
                string path = "Assets/_Project/Resources/Bosses/" + boss.PrefabId;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab");
                if (prefab == null || prefab.GetComponentInChildren<Animator>() == null ||
                    prefab.GetComponent<PushStars.Fight.BossAvatarPresentation>() == null)
                    throw new BuildFailedException("[Build] Missing boss model, animation or presentation: " + boss.Id);
            }
            Debug.Log("[Build] Validated shipping scenes: " +
                      string.Join(", ", AppScenes.Select(Path.GetFileNameWithoutExtension)));
        }

        /// <summary>Creates missing flow scenes and refreshes Build Settings.
        /// Existing scenes own all authored UI and are never regenerated.
        ///
        /// <para>Also the batch entry point used to verify the flow compiles and builds without
        /// opening the editor:
        /// <c>Unity -batchmode -quit -projectPath . -executeMethod PushStars.Editor.BuildScript.RebuildFlowScenes</c></para>
        /// </summary>
        [MenuItem("Tools/Push Stars/Create Missing Flow Scenes", priority = 6)]
        public static void RebuildFlowScenes()
        {
            BootSceneSetup.BuildScene();
            OnboardingSceneSetup.BuildScene();
            bool mediapipe = FightSceneSetup.BuildFightScene();
            TrainingScreenSetup.Run();

            ValidateAppScenes();
            EditorBuildSettings.scenes =
                AppScenes.Select(p => new EditorBuildSettingsScene(p, true))
                         .ToArray();

            Debug.Log($"[Build] Flow scenes ready (fight pose source: {(mediapipe ? "MediaPipe" : "Mock or missing")}). " +
                      $"Build Settings: {string.Join(", ", EditorBuildSettings.scenes.Select(s => Path.GetFileNameWithoutExtension(s.path)))}");
        }

        public static void BuildiOS()
        {
            ValidateAppScenes();
            EnableMediaPipeDefine(NamedBuildTarget.iOS);
            CopyModelToStreamingAssets();
            ConfigureIOS();
            MobileTextureCompression.Apply();
            FontSetup.BakeGlyphs();

            string outDir = GetArg("-buildOutput") ?? "ios_build";
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes           = AppScenes,
                locationPathName = outDir,
                target           = BuildTarget.iOS,
                targetGroup      = BuildTargetGroup.iOS,
                // Explicit opt-in for the private BOT LAB entry on a phone/CI build.
                options          = System.Array.Exists(System.Environment.GetCommandLineArgs(), arg => arg == "-botRecordingTest")
                    ? BuildOptions.Development : BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Build] iOS build FAILED: {report.summary.result} ({report.summary.totalErrors} errors)");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[Build] iOS Xcode project generated at {outDir}");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Headless Android build. Start the editor with <c>-buildTarget Android</c>: Firebase and
        /// the dependency resolver only prepare the Android side while Android is the active platform.
        ///   Unity -batchmode -buildTarget Android -projectPath . -executeMethod PushStars.Editor.BuildScript.BuildAndroid
        /// Writes an APK to <c>-buildOutput</c>, or an AAB for Google Play with <c>-appBundle</c>.
        /// Without a keystore in Player Settings the result is signed with the debug key.
        /// </summary>
        public static void BuildAndroid()
        {
            ValidateAppScenes();
            EnableMediaPipeDefine(NamedBuildTarget.Android);
            CopyModelToStreamingAssets();
            ConfigureAndroid();
            MobileTextureCompression.Apply();
            FontSetup.BakeGlyphs();
            ResolveAndroidDependencies();

            bool appBundle = HasArg("-appBundle");
            string outPath = GetArg("-buildOutput") ?? "Builds/Android/PushupStars" + (appBundle ? ".aab" : ".apk");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            EditorUserBuildSettings.buildAppBundle = appBundle;

            var options = new BuildPlayerOptions
            {
                scenes           = AppScenes,
                locationPathName = outPath,
                target           = BuildTarget.Android,
                targetGroup      = BuildTargetGroup.Android,
                options          = HasArg("-botRecordingTest") ? BuildOptions.Development : BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Build] Android build FAILED: {report.summary.result} ({report.summary.totalErrors} errors)");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"[Build] Android build written to {outPath} ({report.summary.totalSize / (1024 * 1024)} MB)");
            EditorApplication.Exit(0);
        }

        /// <summary>
        /// Pre-export hook for Unity Build Automation (set as the build target's "Pre-export method":
        /// <c>PushStars.Editor.BuildScript.PrepareForUBA</c>). UBA runs its own BuildPlayer, so this
        /// only prepares the project: sets the shipping scene list (Boot → Main + the regenerated CV
        /// test scene as a debug extra), copies the pose model into StreamingAssets, and sets the iOS
        /// camera string + bundle id. The PUSHSTARS_MEDIAPIPE define is committed in ProjectSettings;
        /// the plugin itself is fetched by the UBA pre-build script before the editor opens, which
        /// also writes the real GoogleService-Info.plist from an env var (placeholder otherwise —
        /// the app then boots without a backend and the UI falls back to mock data).
        /// </summary>
        public static void PrepareForUBA()
        {
            CopyModelToStreamingAssets();
            ConfigureIOS();
            MobileTextureCompression.Apply();

            // Bake all UI glyphs before building the scenes. The Rubik
            // atlases are dynamic SDF32; a character missing from them is rendered by the player,
            // on the main thread, the first time it appears — which cost a 195-second frame on
            // device. Baking here moves that back to where it was always assumed to happen.
            FontSetup.BakeGlyphs();

            // Regenerate the CV test scene FRESH so the build never depends on stale serialized
            // references (e.g. a MediaPipePoseSource saved as a "missing script" when the define was
            // off — which left PushupSession with no pose source → STATUS "no source" on device).
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = CvTestSceneSetup.CreateCvTestObject();
            if (go == null)
            {
                // Fail loudly instead of shipping an empty scene (= black screen, no text on device).
                // Root cause if this fires: the PushStars.CV.MediaPipe assembly didn't compile, i.e. the
                // PUSHSTARS_MEDIAPIPE define wasn't active (UBA's "Inject Scripting Define Symbols" can
                // overwrite the committed iOS defines) or the MediaPipe package wasn't fetched. The
                // asmdef's versionDefines now auto-sets the define when the package is present, so this
                // should not happen on CI — but if it does, abort rather than ship a black screen.
                Debug.LogError("[Build] CVTest could not be created — PUSHSTARS_MEDIAPIPE off or plugin missing. ABORTING.");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("[Build] CVTest regenerated and wired.");

            const string scenePath = "Assets/testCV.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            // Preserve the authored fight scene and validate its saved session binding.
            // A missing plugin or mock source fails the build instead of replacing the user's UI.
            if (!FightSceneSetup.BuildFightScene())
            {
                Debug.LogError("[Build] Saved Fight scene has no session bound to MediaPipePoseSource. Fix its CV binding; authored UI was preserved. ABORTING.");
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("[Build] Authored Fight scene validated.");

            // Only bootstrap missing scenes. Ctrl+S edits ship unchanged.
            BootSceneSetup.BuildScene();
            OnboardingSceneSetup.BuildScene();
            Debug.Log("[Build] Boot + Onboarding ready; existing scenes preserved.");

            // Every route ships as an authored, embedded scene on all platforms.
            ValidateAppScenes();

            // Ship the real app: Boot (index 0) initializes Firebase and routes by onboarding
            // state; testCV stays in the list as a debug scene reachable via SceneManager.LoadScene.
            EditorBuildSettings.scenes =
                AppScenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();

            Debug.Log("[Build] PrepareForUBA done: scenes = " +
                      string.Join(", ", AppScenes.Select(Path.GetFileNameWithoutExtension)) + ", " +
                      "model copied, iOS configured.");
        }

        private static void EnableMediaPipeDefine(NamedBuildTarget target)
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(target);
            if (defines.Split(';').Contains(Define)) return;
            PlayerSettings.SetScriptingDefineSymbols(target,
                string.IsNullOrEmpty(defines) ? Define : defines + ";" + Define);
            Debug.Log($"[Build] Enabled {Define} for {target.TargetName}.");
        }

        private static void CopyModelToStreamingAssets()
        {
            if (!Directory.Exists(StreamingDir)) Directory.CreateDirectory(StreamingDir);
            foreach (var model in ModelNames)
            {
                var src = Path.Combine(ModelSrcDir, model);
                var dst = Path.Combine(StreamingDir, model);
                if (File.Exists(src))
                {
                    File.Copy(src, dst, true);
                    Debug.Log($"[Build] Copied {model} into StreamingAssets.");
                }
                else
                {
                    throw new BuildFailedException($"[Build] Required camera model missing: {src}. Import the MediaPipe plugin before building.");
                }
            }
            AssetDatabase.Refresh();
        }

        private const string TeamId = "Y4N58LLV3T"; // Apple Developer Team (Daniel Coltiuc)

        private static void ConfigureIOS()
        {
            PlayerSettings.iOS.cameraUsageDescription = "Pushup Stars uses the camera to count your push-ups and check your form.";
            PlayerSettings.iOS.targetOSVersionString  = "15.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);

            // Bake the Apple Team ID into the Xcode project so ALL targets (incl. Firebase's SPM
            // dependencies: GoogleUtilities / abseil / gRPC) inherit DEVELOPMENT_TEAM and can be
            // signed. Without this they fail with "Signing ... requires a development team".
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.appleDeveloperTeamID = TeamId;

            ConfigureRuntimePerformance();
        }

        private const int AndroidTargetSdk = 36;

        private static void ConfigureAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // The MediaPipe library ships arm64 only, and Google Play requires arm64.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.targetSdkVersion    = (AndroidSdkVersions)AndroidTargetSdk;
            // Unlike iOS (see ConfigureRuntimePerformance) Unity compiles the Android C++ itself,
            // at this level. The project file carried Debug: a 121 MB libil2cpp.so, unoptimised.
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);

            ConfigureRuntimePerformance();
        }

        /// <summary>
        /// Has the External Dependency Manager write the Firebase and sign-in libraries into the
        /// Gradle templates under Assets/Plugins/Android. A player built without them crashes as
        /// soon as Firebase starts. Called by name: the resolver ships as editor-only DLLs that
        /// this assembly does not reference.
        /// </summary>
        private static void ResolveAndroidDependencies()
        {
            var resolve = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("GooglePlayServices.PlayServicesResolver"))
                .FirstOrDefault(t => t != null)?
                .GetMethods().FirstOrDefault(m => m.Name == "ResolveSync");
            if (resolve == null)
                throw new BuildFailedException("[Build] External Dependency Manager not loaded; Android libraries cannot be resolved.");

            var args = resolve.GetParameters()
                .Select((p, i) => i == 0 ? true : p.HasDefaultValue ? p.DefaultValue : false).ToArray();
            if (!(bool)resolve.Invoke(null, args))
                throw new BuildFailedException("[Build] Android dependency resolution failed. See the log above.");
            AssetDatabase.Refresh();
            Debug.Log("[Build] Android dependencies resolved.");
        }

        /// <summary>
        /// Settings the player's speed depends on, asserted here rather than left to whatever the
        /// project file happens to carry — they are invisible in the editor and only bite in the
        /// shipped app.
        ///
        /// <para><b>Not here:</b> the optimisation level the managed code ends up running at.
        /// <c>PlayerSettings.SetIl2CppCompilerConfiguration</c> is a no-op for iOS (setting it and
        /// reading it back returns the old value) — Unity only emits the Xcode project, and the
        /// C++ is compiled at whatever level the Xcode <i>build configuration</i> says. A player
        /// built as a Development Build compiles Debug and runs several times slower everywhere,
        /// which is what an app that lags in its own onboarding looks like. That switch lives on
        /// the build target in Unity Build Automation, not in this repo.</para>
        /// </summary>
        private static void ConfigureRuntimePerformance()
        {
            // The characters are ~50k-vertex skinned meshes and one of them is on screen at all
            // times, so skinning them on the CPU is the single most expensive thing the app does
            // per frame. Metal does it for free on the GPU.
            PlayerSettings.gpuSkinning = true;

            Debug.Log($"[Build] Runtime perf: GPU skinning={PlayerSettings.gpuSkinning}. " +
                      $"(iOS optimisation level comes from the Xcode build configuration, not from here.)");
        }

        private static bool HasArg(string name)
            => System.Array.Exists(System.Environment.GetCommandLineArgs(), arg => arg == name);

        private static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
