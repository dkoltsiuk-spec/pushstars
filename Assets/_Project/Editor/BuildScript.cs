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

        public static void BuildiOS()
        {
            EnableMediaPipeDefine(NamedBuildTarget.iOS);
            CopyModelToStreamingAssets();
            ConfigureIOS();

            string outDir = GetArg("-buildOutput") ?? "ios_build";
            Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes           = new[] { "Assets/testCV.unity" }, // CV test scene (rep counter on camera)
                locationPathName = outDir,
                target           = BuildTarget.iOS,
                targetGroup      = BuildTargetGroup.iOS,
                options          = BuildOptions.None,
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
        /// Pre-export hook for Unity Build Automation (set as the build target's "Pre-export method":
        /// <c>PushStars.Editor.BuildScript.PrepareForUBA</c>). UBA runs its own BuildPlayer, so this
        /// only prepares the project: makes the CV test scene the one that ships, copies the pose
        /// model into StreamingAssets, and sets the iOS camera string + bundle id. The
        /// PUSHSTARS_MEDIAPIPE define is committed in ProjectSettings; the plugin itself is fetched by
        /// the UBA pre-build script before the editor opens.
        /// </summary>
        public static void PrepareForUBA()
        {
            CopyModelToStreamingAssets();
            ConfigureIOS();

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
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };

            Debug.Log("[Build] PrepareForUBA done: scene regenerated, model copied, iOS configured.");
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
                    Debug.LogError($"[Build] Model not found at {src} — was the MediaPipe plugin imported first?");
                }
            }
            AssetDatabase.Refresh();
        }

        private const string TeamId = "Y4N58LLV3T"; // Apple Developer Team (Daniel Coltiuc)

        private static void ConfigureIOS()
        {
            PlayerSettings.iOS.cameraUsageDescription = "Push Stars uses the camera to count your push-ups and check your form.";
            PlayerSettings.iOS.targetOSVersionString  = "13.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);

            // Bake the Apple Team ID into the Xcode project so ALL targets (incl. Firebase's SPM
            // dependencies: GoogleUtilities / abseil / gRPC) inherit DEVELOPMENT_TEAM and can be
            // signed. Without this they fail with "Signing ... requires a development team".
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.appleDeveloperTeamID = TeamId;
        }

        private static string GetArg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
