using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.CV;
using PushStars.Fight;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Editor
{
    /// <summary>Exercises the loader used by BATTLE's first-measurement branch.</summary>
    [InitializeOnLoad]
    public static class MeasurementPresentationValidation
    {
        private const string Key = "PushStars.MeasurementPresentationValidation";
        private const string Output = "output/measurement/";
        private static int _step;
        private static double _due;
        private static bool _background;

        static MeasurementPresentationValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    _background = Application.runInBackground;
                    Application.runInBackground = true;
                    _step = 0;
                    _due = EditorApplication.timeSinceStartup + 1.5;
                    EditorApplication.update += Tick;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                    SessionState.SetBool(Key, false);
            };
        }

        [MenuItem("Tools/Push Stars/CV/Validate Measurement Presentation")]
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !scene.isDirty
                && scene.path == "Assets/_Project/Scenes/Main.unity", "Start from saved Main in Edit mode.");
            var home = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Image>(true))
                .Single(i => i.name == "Background" && i.transform.parent.name == "MirrorRoot");
            SessionState.SetString(Key + ".background", AssetDatabase.GetAssetPath(home.sprite));
            Directory.CreateDirectory(Output);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void MeasurementLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "Fight") return;
            SceneManager.sceneLoaded -= MeasurementLoaded;
            // Awake disables CV for this isolated check. Set the actual request before Start,
            // which then configures the same measurement UI as the first BATTLE search.
            FightRequest.LevelTest();
        }

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _due) return;
            _due = EditorApplication.timeSinceStartup + 2;
            try
            {
                switch (_step++)
                {
                    case 0:
                        FightRequest.Clear();
                        SceneManager.sceneLoaded += MeasurementLoaded;
                        // OTA is an optional runtime assembly, outside this editor asmdef.
                        var loader = AppDomain.CurrentDomain.GetAssemblies()
                            .Select(a => a.GetType("PushStars.OTA.OtaSceneLoader")).First(t => t != null);
                        loader.GetMethod("LoadScene").Invoke(null, new object[] { "Fight", LoadSceneMode.Single });
                        break;
                    case 1:
                        var scene = SceneManager.GetActiveScene();
                        Require(scene.path == "Assets/_Project/Scenes/Fight.unity",
                            "BATTLE loader opened a stale remote scene in the Editor.");
                        var images = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Image>(true));
                        var backdrop = images.Single(i => i.name == "Backdrop" && i.transform.parent.name == "LevelTestScenery");
                        Require(backdrop.gameObject.activeInHierarchy && AssetDatabase.GetAssetPath(backdrop.sprite)
                            == SessionState.GetString(Key + ".background", ""), "Measurement must display the main hero background.");
                        var hud = UnityEngine.Object.FindFirstObjectByType<FightHud>();
                        var portrait = hud.GetComponentsInChildren<RawImage>()
                            .Single(i => i.GetComponent<AspectRatioFitter>()?.aspectMode == AspectRatioFitter.AspectMode.HeightControlsWidth);
                        Require(Mathf.Abs(portrait.rectTransform.anchoredPosition.x) < .01f,
                            "Measurement portrait has a horizontal offset.");
                        Require(!UnityEngine.Object.FindFirstObjectByType<PushupSession>().enabled,
                            "Validation must not start live tracking or award reps.");
                        ScreenCapture.CaptureScreenshot(Output + "waiting.png");
                        break;
                    case 2:
                        Require(File.Exists(Output + "waiting.png"), "Waiting screenshot was not captured.");
                        File.WriteAllText(Output + "validation.txt",
                            "PASS: Main -> BATTLE loader -> authored Fight; visible backdrop equals Main hero background; centered solo portrait. Live CV disabled, no reps or rewards. Screenshot: waiting.png\n");
                        Finish();
                        break;
                }
            }
            catch (Exception exception)
            {
                File.WriteAllText(Output + "validation.txt", "FAIL: " + exception);
                Finish();
            }
        }

        private static void Finish()
        {
            SceneManager.sceneLoaded -= MeasurementLoaded;
            EditorApplication.update -= Tick;
            FightRequest.Clear();
            Application.runInBackground = _background;
            EditorApplication.isPlaying = false;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
