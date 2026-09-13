using System;
using System.IO;
using System.Linq;
using PushStars.Fight;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    [InitializeOnLoad]
    public static class TrainingScreenValidation
    {
        private const string Key = "PushStars.TrainingScreenValidation";
        private const string Output = "Logs/training-screen";
        private static int _step;
        private static double _due;
        private static TrainingScreen _screen;
        private static Camera _camera;
        private static RenderTexture _texture;
        private static string _error;
        static TrainingScreenValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Key, false)) return;
                _step = 0; _due = EditorApplication.timeSinceStartup + 2; _error = null;
                Application.logMessageReceived += OnLog; EditorApplication.update += Tick;
            };
        }
        public static void Run()
        {
            Directory.CreateDirectory(Output); File.WriteAllText(Output + "/validation.txt", "Training screen validation\n");
            TrainingSettingsRegression.Run(); TrainingScreenSetup.Run();
            EditorSceneManager.OpenScene(TrainingScreenSetup.ScenePath);
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }
        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                if (_error != null) throw new Exception(_error);
                switch (_step++)
                {
                    case 0: _screen = Object.FindObjectOfType<TrainingScreen>(); Require(_screen != null, "Training scene has its authored presenter"); Prepare(390, 844); _screen.Preview(TrainingScreen.View.Loading); break;
                    case 1: Capture("loading"); _screen.Preview(TrainingScreen.View.Exercise); break;
                    case 2: Capture("exercise"); Click("Next"); Require(_screen.CurrentView == TrainingScreen.View.Rest, "NEXT opens rest"); break;
                    case 3: Capture("rest"); Click("Pause"); break;
                    case 4: Capture("paused"); Click("Resume"); Click("AddRest"); Click("StartSet"); Require(_screen.CurrentView == TrainingScreen.View.Exercise, "START SET opens next exercise"); break;
                    case 5: _screen.Preview(TrainingScreen.View.Results); break;
                    case 6: Capture("results"); Click("MoreSet"); Require(_screen.CurrentView == TrainingScreen.View.Exercise, "One more set returns to exercise"); Prepare(320, 568); _screen.Preview(TrainingScreen.View.Rest); break;
                    case 7: Capture("rest-small"); Finish(0); return;
                }
                _due = EditorApplication.timeSinceStartup + .6;
            }
            catch (Exception e) { File.AppendAllText(Output + "/validation.txt", "FAIL: " + e + "\n"); Debug.LogException(e); Finish(1); }
        }
        private static void Click(string name)
        {
            var button = name == "Next" ? new SerializedObject(_screen).FindProperty("_next").objectReferenceValue as Button :
                _screen.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
            Require(button.gameObject.activeInHierarchy && button.IsInteractable(), name + " is enabled"); button.onClick.Invoke();
        }
        private static void Prepare(int w, int h)
        {
            if (_camera == null)
            {
                _camera = new GameObject("TrainingCapture", typeof(Camera)).GetComponent<Camera>(); _camera.transform.position = new Vector3(0, 0, -50);
                _camera.orthographic = true; _camera.clearFlags = CameraClearFlags.SolidColor; _camera.backgroundColor = Color.black; _camera.cullingMask = 1 << 5;
            }
            if (_texture != null) { _camera.targetTexture = null; _texture.Release(); Object.Destroy(_texture); }
            _texture = new RenderTexture(w, h, 24); _texture.Create(); _camera.targetTexture = _texture; _camera.orthographicSize = h / 2f;
            var canvas = _screen.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = _camera; canvas.planeDistance = 10;
            canvas.GetComponent<CanvasScaler>().enabled = false; canvas.scaleFactor = 1; Canvas.ForceUpdateCanvases();
        }
        private static void Capture(string name)
        {
            foreach (var rect in _screen.GetComponentsInChildren<RectTransform>(true)) rect.gameObject.layer = 5;
            Canvas.ForceUpdateCanvases(); _camera.Render(); var previous = RenderTexture.active; RenderTexture.active = _texture;
            var image = new Texture2D(_texture.width, _texture.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, _texture.width, _texture.height), 0, 0); image.Apply();
            Require(image.GetPixels32().Count(p => p.r + p.g + p.b > 30) > image.width * image.height / 4, "Visible UI rendered for " + name);
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG()); Object.Destroy(image); RenderTexture.active = previous;
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); File.AppendAllText(Output + "/validation.txt", "PASS: " + message + "\n"); }
        private static void Finish(int code) { EditorApplication.update -= Tick; Application.logMessageReceived -= OnLog; SessionState.SetBool(Key, false); File.AppendAllText(Output + "/validation.txt", "RESULT: " + (code == 0 ? "PASS" : "FAIL") + "\n"); EditorApplication.Exit(code); }
        private static void OnLog(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception) _error = message; }
    }
}
