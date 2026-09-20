using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PushStars.Core;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Editor
{
    /// <summary>Read-only scene checks and isolated regressions for the shipping mobile app.</summary>
    [InitializeOnLoad]
    public static class MobileAppValidation
    {
        private const string Command = "Temp/pushstars.mobile-validation";
        private const string Output = "output/mobile-audit";
        private static double _nextPoll;
        private const string PlayKey = "PushStars.MobileAudit.Play";
        [Serializable] private sealed class SavedScene { public string path; public bool loaded, active; }
        [Serializable] private sealed class SavedSetup { public SavedScene[] scenes; }

        static MobileAppValidation()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(PlayKey, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode) Application.runInBackground = true;
                if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += RestoreEditor;
            };
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 1;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Command)) return;
            string task = File.ReadAllText(Command).Trim();
            File.Delete(Command);
            if (task == "edit") Run();
            else if (task == "boss-routing")
            {
                Directory.CreateDirectory(Output);
                try
                {
                    FirstBossValidation.Run();
                    FightFlowRegression.Run();
                    File.WriteAllText(Output + "/boss-routing.txt", "PASS: all boss models and seven fight/reward scene bindings.\n");
                }
                catch (Exception e) { File.WriteAllText(Output + "/boss-routing.txt", "FAIL: " + e); }
            }
            else if (task == "ios-scripts") CompileIOS();
            else if (task.StartsWith("play-")) StartPlay(task);
            else if (task == "state")
            {
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/editor-state.txt", "Playing: " + EditorApplication.isPlaying + "\n" +
                    string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(i =>
                    { var s = SceneManager.GetSceneAt(i); return s.path + " dirty=" + s.isDirty; })));
            }
        }

        private static void StartPlay(string task)
        {
            Directory.CreateDirectory(Output);
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Stop Play Mode before validation");
                var setup = EditorSceneManager.GetSceneManagerSetup();
                Require(Enumerable.Range(0, SceneManager.sceneCount).All(i => !SceneManager.GetSceneAt(i).isDirty),
                    "Open scenes contain unsaved edits; play validation has not changed them");
                SessionState.SetString(PlayKey + ".setup", JsonUtility.ToJson(new SavedSetup { scenes = setup.Select(s =>
                    new SavedScene { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray() }));
                SessionState.SetBool(PlayKey + ".background", Application.runInBackground);
                SessionState.SetBool(PlayKey, true);
                switch (task)
                {
                    case "play-fight": FightScreensPlayValidation.Run(); break;
                    case "play-modes": ModeSelectionPlayValidation.Run(); break;
                    case "play-training": TrainingScreenValidation.Run(); break;
                    case "play-experience": MobileExperiencePlayValidation.Run(); break;
                    case "play-measurement":
                        EditorSceneManager.OpenScene(AuthoredScenes.MainPath);
                        MeasurementPresentationValidation.Run(); break;
                    case "play-home":
                        EditorSceneManager.OpenScene(AuthoredScenes.MainPath);
                        HomeNavigationPlayValidation.Run(); break;
                    case "play-boss-map":
                        EditorSceneManager.OpenScene(AuthoredScenes.MainPath);
                        BossMapPlayValidation.Run(); break;
                    case "play-boss-combat":
                        EditorSceneManager.OpenScene(AuthoredScenes.MainPath);
                        BossCombatPlayValidation.Run(); break;
                    default: throw new InvalidOperationException("Unknown play suite: " + task);
                }
                File.WriteAllText(Output + "/play-dispatch.txt", "Started " + task);
            }
            catch (Exception e)
            {
                File.WriteAllText(Output + "/play-dispatch.txt", "FAIL " + task + ": " + e);
                if (SessionState.GetBool(PlayKey, false)) RestoreEditor();
            }
        }

        private static void CompileIOS()
        {
            Directory.CreateDirectory(Output);
            try
            {
                var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(
                    new UnityEditor.Build.Player.ScriptCompilationSettings {
                        target = BuildTarget.iOS, group = BuildTargetGroup.iOS,
                        options = UnityEditor.Build.Player.ScriptCompilationOptions.None,
                        extraScriptingDefines = new[] { "PUSHSTARS_MEDIAPIPE" }
                    }, Output + "/ios-scripts");
                Require(result.assemblies != null && result.assemblies.Any(), "iOS compilation returned no assemblies");
                File.WriteAllText(Output + "/ios-compilation.txt", "PASS: iOS player scripts compiled with PUSHSTARS_MEDIAPIPE.\n" +
                    string.Join("\n", result.assemblies));
            }
            catch (Exception e) { File.WriteAllText(Output + "/ios-compilation.txt", "FAIL: " + e); }
        }

        private static void RestoreEditor()
        {
            var json = SessionState.GetString(PlayKey + ".setup", "");
            SessionState.EraseBool(PlayKey);
            Application.runInBackground = SessionState.GetBool(PlayKey + ".background", false);
            if (!string.IsNullOrEmpty(json))
            {
                var saved = JsonUtility.FromJson<SavedSetup>(json);
                EditorSceneManager.RestoreSceneManagerSetup(saved.scenes.Select(s =>
                    new UnityEditor.SceneManagement.SceneSetup { path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
            }
            File.AppendAllText(Output + "/play-dispatch.txt", "\nEditor scene setup restored.");
        }

        [MenuItem("Tools/Push Stars/Validate English Mobile App")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            var report = new StringBuilder("English mobile app validation — " + DateTime.UtcNow.ToString("u") + "\n");
            int passed = 0, failed = 0;
            void Check(string name, Action action)
            {
                try { action(); report.AppendLine("PASS " + name); passed++; }
                catch (Exception e) { report.AppendLine("FAIL " + name + ": " + e); failed++; }
                File.WriteAllText(Output + "/edit-mode.txt", report.ToString());
            }
            Check("Shipping scene routes", BuildScript.ValidateAppScenes);
            Check("Safe areas: compact, notched, island, landscape and transient invalid data", SafeAreas);
            Check("English remains active for a saved Russian preference", Language);
            Check("Camera background lifecycle and explicit cancellation", CameraLifecycle);
            Check("Case rewards persistence, eligibility and replay protection", CaseRewardsRegression.Run);
            Check("Fight and reward navigation", FightFlowRegression.Run);
            Check("Layout persistence and mouse/touch input", ScreenLayoutRegression.Run);
            Check("Training sets, rest timers and limits", TrainingSettingsRegression.Run);
            Check("Camera placement at 30/60/120 FPS and portrait clipping", CameraPresentationRegression.Run);
            Check("Avatar aspect ratios and zoom", FightAspectValidation.Run);
            Check("Friend room state and presentation", FriendDuelValidation.Run);
            Check("Boss battle", BossCombatValidation.Run);
            Check("Boss map", BossMapValidation.Run);
            Check("League presentation", LeaguePresentationValidation.Run);
            Check("Pose retargeting and camera orientation", RetargetRegression.Run);
            foreach (var scene in EditorBuildSettings.scenes.Where(s => s.enabled))
                Check("Scene text and bindings: " + Path.GetFileNameWithoutExtension(scene.path), () => SceneBindings(scene.path));
            report.AppendLine($"TOTAL: {passed} passed, {failed} failed");
            File.WriteAllText(Output + "/edit-mode.txt", report.ToString());
            Debug.Log($"[MobileAudit] Completed: {passed} passed, {failed} failed. {Output}/edit-mode.txt");
        }

        private static void SafeAreas()
        {
            foreach (var sample in new[] {
                new Vector4(320, 568, 0, 0), new Vector4(375, 667, 0, 0),
                new Vector4(390, 844, 34, 47), new Vector4(430, 932, 34, 59) })
            {
                var size = new Vector2Int((int)sample.x, (int)sample.y);
                var area = new Rect(0, sample.z, sample.x, sample.y - sample.z - sample.w);
                Require(SafeAreaFitter.TryGetAnchors(area, size, out var min, out var max), "Valid portrait area rejected");
                Require(Vector2.Distance(Vector2.Scale(min, size), area.min) < .001f &&
                    Vector2.Distance(Vector2.Scale(max, size), area.max) < .001f, "Safe area inset changed");
            }
            Require(SafeAreaFitter.TryGetAnchors(new Rect(59, 21, 726, 369), new Vector2Int(844, 390), out _, out _), "Landscape rejected");
            foreach (var bad in new[] { Rect.zero, new Rect(0, 0, -1, 100), new Rect(float.NaN, 0, 390, 844),
                new Rect(0, 0, float.PositiveInfinity, 844), new Rect(1000, 0, 50, 844) })
                Require(!SafeAreaFitter.TryGetAnchors(bad, new Vector2Int(390, 844), out _, out _), "Invalid area accepted: " + bad);
            Require(!SafeAreaFitter.TryGetAnchors(new Rect(0, 0, 390, 844), Vector2Int.zero, out _, out _), "Zero screen accepted");
            Require(SafeAreaFitter.TryGetAnchors(new Rect(-3, -2, 396, 848), new Vector2Int(390, 844), out var a, out var b)
                && a == Vector2.zero && b == Vector2.one, "Out-of-bounds area was not clamped");
        }

        private static void Language()
        {
            const string key = "settings.language";
            bool existed = PlayerPrefs.HasKey(key);
            string original = PlayerPrefs.GetString(key);
            try
            {
                PlayerPrefs.SetString(key, "ru");
                Require(new PlayerPrefsSettingsStore().Language == "en", "Legacy preference changed the supported language");
            }
            finally
            {
                if (existed) PlayerPrefs.SetString(key, original); else PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        private static void CameraLifecycle()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("PushStars.CV.MediaPipePoseSource"))
                .FirstOrDefault(t => t != null);
            Require(type != null, "Real MediaPipe assembly unavailable");
            var host = new GameObject("Inactive camera lifecycle fixture");
            host.SetActive(false); // Never starts the webcam or a native inference task.
            try
            {
                var source = host.AddComponent(type);
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var requested = type.GetField("_trackingRequested", flags);
                var resume = type.GetField("_resumeTracking", flags);
                var pause = type.GetMethod("OnApplicationPause", flags);
                var clock = (System.Diagnostics.Stopwatch)type.GetField("_clock", flags).GetValue(source);
                clock.Start();
                requested.SetValue(source, true);
                pause.Invoke(source, new object[] { true });
                Require((bool)resume.GetValue(source), "Backgrounding lost the tracking request");
                Require(clock.IsRunning, "Camera restart reset the capture clock");
                pause.Invoke(source, new object[] { true });
                Require((bool)resume.GetValue(source), "Duplicate pause canceled camera recovery");
                type.GetMethod("StopTracking").Invoke(source, null);
                pause.Invoke(source, new object[] { false });
                Require(!(bool)resume.GetValue(source) && !(bool)requested.GetValue(source)
                    && !(bool)type.GetProperty("IsRunning").GetValue(source), "Explicit stop resurrected the camera");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        private static void SceneBindings(string path)
        {
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var roots = scene.GetRootGameObjects();
                foreach (var t in roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)))
                    Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0,
                        "Missing script on " + t.name);
                foreach (var label in roots.SelectMany(r => r.GetComponentsInChildren<TMP_Text>(true)))
                {
                    Require(!Regex.IsMatch(label.text ?? "", "[\\u0400-\\u04ff]"), "Untranslated text: " + label.name + " = " + label.text);
                    Require(label.font != null, "Missing font on " + label.name);
                }
                foreach (var session in roots.SelectMany(r => r.GetComponentsInChildren<PushStars.CV.PushupSession>(true)))
                {
                    var source = new SerializedObject(session).FindProperty("_poseSourceBehaviour").objectReferenceValue;
                    Require(source != null && source is PushStars.CV.IPoseSource, "Missing camera pose source");
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
