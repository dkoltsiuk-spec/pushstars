using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PushStars.Fight;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace PushStars.Editor
{
    [InitializeOnLoad]
    public static class PreparationPerformanceValidation
    {
        private const string Request = "Temp/preparation-performance.request";
        private const string PlayKey = "PushStars.PreparationPerformance.Play";
        private static int _playFrames, _lastFrame;
        static PreparationPerformanceValidation()
        {
            EditorApplication.update += Poll;
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(PlayKey, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                { _playFrames = 0; _lastFrame = -1; EditorApplication.update += CheckPlay; }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    EditorApplication.update -= CheckPlay;
                    string previous = SessionState.GetString(PlayKey + ".start", "");
                    EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
                    Application.runInBackground = SessionState.GetBool(PlayKey + ".background", false);
                    SessionState.EraseBool(PlayKey);
                }
            };
        }
        private static void Poll()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Request)) return;
            if (File.GetLastWriteTimeUtc("Assets/_Project/Editor/PreparationPerformanceValidation.cs") > File.GetLastWriteTimeUtc(typeof(PreparationPerformanceValidation).Assembly.Location)
                || File.GetLastWriteTimeUtc("Assets/_Project/Scripts/Fight/FightAvatar.cs") > File.GetLastWriteTimeUtc(typeof(FightAvatar).Assembly.Location)
                || File.GetLastWriteTimeUtc("Assets/_Project/Scripts/UI/Avatar/CharacterStage.cs") > File.GetLastWriteTimeUtc(typeof(CharacterStage).Assembly.Location)) return;
            string label = File.ReadAllText(Request).Trim();
            File.Delete(Request);
            try { if (label == "play") StartPlay(); else Run(label); }
            catch (Exception e) { Directory.CreateDirectory("output/preparation-performance"); File.WriteAllText("output/preparation-performance/error.txt", e.ToString()); }
        }

        [MenuItem("Tools/Push Stars/Validate Preparation Performance")]
        public static void Run() => Run("current");

        private static void StartPlay()
        {
            SessionState.SetString(PlayKey + ".start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(PlayKey + ".background", Application.runInBackground);
            SessionState.SetBool(PlayKey, true);
            Application.runInBackground = true;
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/_Project/Scenes/FightPreparation.unity");
            EditorApplication.isPlaying = true;
        }

        private static void CheckPlay()
        {
            if (_lastFrame == Time.frameCount) return;
            _lastFrame = Time.frameCount;
            if (++_playFrames < 120) return;
            EditorApplication.update -= CheckPlay;
            var report = new StringBuilder();
            try
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var stages = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterStage>(true)).ToArray();
                if (stages.Length != 2) throw new InvalidOperationException("Expected two live portraits.");
                foreach (var stage in stages)
                {
                    var texture = stage.RenderTarget;
                    if (texture == null || !texture.IsCreated() || texture.height != 810) throw new InvalidOperationException("Scaled portrait target was not created.");
                    report.AppendLine($"Live portrait: {texture.width}x{texture.height}, MSAA {texture.antiAliasing}, {Profiler.GetRuntimeMemorySizeLong(texture)} bytes");
                }
                foreach (var avatar in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true)))
                {
                    if (typeof(FightAvatar).GetField("_pushupSilhouette", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(avatar) != null)
                        throw new InvalidOperationException("Live preparation baked unused poses.");
                    if (!avatar.IsPreparationFramed || !avatar.TryGetBodyViewport(out var bounds) || bounds.height <= 0)
                        throw new InvalidOperationException("Live portrait was not framed.");
                }
                report.AppendLine("PASS: real Awake/Start, both scaled render targets, standing camera framing, and no push-up silhouette baking.");
            }
            catch (Exception e) { report.AppendLine("FAIL: " + e); }
            Directory.CreateDirectory("output/preparation-performance");
            File.WriteAllText("output/preparation-performance/play.txt", report.ToString());
            EditorApplication.isPlaying = false;
        }

        public static void Run(string label)
        {
            var report = new StringBuilder("Editor preview CPU measurements; not device frame timings.\n");
            var clock = Stopwatch.StartNew();
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/FightPreparation.unity");
            report.AppendLine($"Open preview scene: {clock.Elapsed.TotalMilliseconds:F2} ms (editor asset cache may be warm)");
            try
            {
                var panel = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<DuelReadyPanel>(true)).Single();
                var canvas = panel.GetComponentInParent<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled = false;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(390, 844);
                int count = -1;
                for (int i = 0; i < 4; i++)
                {
                    clock.Restart();
                    panel.Show(new DuelReadyPanel.Side("BEASTCORE_DEV", 120, 32, 52), new DuelReadyPanel.Side("OSKAT009", 98, 32, 52), null, null, false);
                    Canvas.ForceUpdateCanvases();
                    report.AppendLine($"Show + canvas rebuild {i}: {clock.Elapsed.TotalMilliseconds:F2} ms");
                    int current = panel.Root.GetComponentsInChildren<Transform>(true).Length;
                    if (count >= 0 && count != current) throw new InvalidOperationException("Repeated Show duplicated UI objects.");
                    count = current;
                    panel.Hide();
                }
                long overlayBytes = 0;
                foreach (string resource in new[] { "RedOverlay", "BlueOverlay" })
                {
                    var texture = Resources.Load<Texture2D>("MatchFound/" + resource);
                    long bytes = Profiler.GetRuntimeMemorySizeLong(texture);
                    overlayBytes += bytes;
                    report.AppendLine($"{resource}: {texture.width}x{texture.height}, {texture.format}, {bytes} bytes");
                }
                report.AppendLine($"Overlay total: {overlayBytes} bytes");
                report.AppendLine($"Stable hierarchy: {count} objects. PASS");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            if (label != "before")
            {
                BenchmarkAvatars(report);
                MatchFoundImpactValidation.Run();
                PreparationStanceRegression.Run();
                report.AppendLine("PASS: match-found entrance, male/female idle, crop, resize, and READY release regressions.");
            }
            Directory.CreateDirectory("output/preparation-performance");
            File.WriteAllText("output/preparation-performance/" + label + ".txt", report.ToString());
            UnityEngine.Debug.Log("[PreparationPerformance] " + report);
        }

        private static void BenchmarkAvatars(StringBuilder report)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            // Compare both paths with the same scene and asset cache. The first pair
            // includes warm-up; the second pair is the useful CPU comparison.
            for (int pass = 0; pass < 4; pass++)
            {
                bool bake = pass % 2 == 0;
                var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/FightPreparation.unity");
                try
                {
                    var avatars = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FightAvatar>(true)).ToArray();
                    var clock = Stopwatch.StartNew();
                    foreach (var avatar in avatars)
                    {
                        typeof(FightAvatar).GetField("_preparePushupFraming", flags).SetValue(avatar, bake);
                        typeof(FightAvatar).GetMethod("Build", flags).Invoke(avatar, null);
                        if (!bake && typeof(FightAvatar).GetField("_pushupSilhouette", flags).GetValue(avatar) != null)
                            throw new InvalidOperationException("Preparation still baked push-up poses.");
                    }
                    report.AppendLine($"Avatar Build pair {pass}, bake={bake}: {clock.Elapsed.TotalMilliseconds:F2} ms");
                    if (pass == 3)
                    {
                        foreach (var stage in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CharacterStage>(true)))
                        {
                            var settings = new SerializedObject(stage);
                            int w = settings.FindProperty("_width").intValue, h = settings.FindProperty("_height").intValue;
                            float scale = settings.FindProperty("_renderScale").floatValue;
                            if (!Mathf.Approximately(scale, .75f)) throw new InvalidOperationException("Preparation render scale missing.");
                            int width = Mathf.RoundToInt(w * AvatarWideCamera.WidthMultiplier * scale), height = Mathf.RoundToInt(h * scale);
                            if (Mathf.Abs((float)width / height - w * AvatarWideCamera.WidthMultiplier / h) > .001f)
                                throw new InvalidOperationException("Portrait aspect changed.");
                            report.AppendLine($"Portrait target: {width}x{height}, unchanged aspect, {scale * scale:P0} of original pixels");
                        }
                    }
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
        }
    }
}
