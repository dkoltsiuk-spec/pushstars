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
    public static class RepMilestoneValidation
    {
        private const string Output = "output/rep-milestone";
        private const string Command = "Temp/pushstars.rep-milestone";
        private static double _nextPoll;
        static RepMilestoneValidation() => EditorApplication.update += Poll;
        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 1;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(Command)) return;
            File.Delete(Command);
            try { Run(); }
            catch (Exception e) { Directory.CreateDirectory(Output); File.WriteAllText(Output + "/validation.txt", "FAIL: " + e); Debug.LogException(e); }
        }

        [MenuItem("Tools/Push Stars/Validate Ten Rep Celebration")]
        public static void Run()
        {
            Directory.CreateDirectory(Output);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/Training.unity");
            RenderTexture target = null;
            try
            {
                var screen = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<TrainingScreen>(true)).Single();
                screen.Preview(TrainingScreen.View.Exercise);
                screen.ShowExercise(1, 3, 10, 92, 38, "", true, true);
                var hud = screen.GetComponent<FightHud>();
                hud.ConfigureSolo("SET 1/3"); hud.SetScoresVisible(true); hud.SetSoloRepsVisible(true);
                hud.SetPlayerReps(10); hud.SetPlayerForm(92); hud.SetTimer(38); hud.HideBanner(); hud.HideCountdown();
                var canvas = screen.GetComponent<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled = false;
                canvas.renderMode = RenderMode.WorldSpace;
                var rect = (RectTransform)canvas.transform;
                rect.position = Vector3.zero; rect.localScale = Vector3.one;
                rect.sizeDelta = new Vector2(390, 844);
                var camera = new GameObject("MilestonePreviewCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.transform.position = new Vector3(0, 0, -100);
                camera.orthographic = true; camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(12, 13, 28, 255);
                target = new RenderTexture(780, 1688, 24); camera.targetTexture = target; canvas.worldCamera = camera;
                Canvas.ForceUpdateCanvases();
                var effect = screen.gameObject.AddComponent<RepMilestoneEffect>();
                effect.Observe(9); Require(!effect.IsPlaying, "Nine reps must not trigger");
                effect.Observe(10); Require(effect.IsPlaying, "Ten accepted reps trigger");
                effect.Cancel(); effect.Observe(10); effect.Observe(11);
                Require(!effect.IsPlaying, "Duplicate count and later reps must not replay");
                effect.ResetSet(); effect.Observe(10); Require(effect.IsPlaying, "Next set can celebrate again");
                Require(effect.GetComponentsInChildren<RepMilestoneGraphic>(true).Length == 1, "Replays reuse art");
                Require(effect.GetComponentsInChildren<Graphic>(true).Where(g => g.transform.parent != null &&
                    (g.name == "TenRepCelebration" || g.name == "Number" || g.name == "Caption")).All(g => !g.raycastTarget), "Overlay does not block controls");
                for (int i = 0; i <= 54; i++)
                {
                    effect.Sample(i / 30f);
                    Capture(camera, target, "frame-" + i.ToString("D3"));
                }
                effect.Sample(.23f); Capture(camera, target, "lightning");
                effect.Sample(.34f); Capture(camera, target, "impact");
                rect.sizeDelta = new Vector2(320, 568); camera.orthographicSize = 284;
                camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
                target = new RenderTexture(640, 1136, 24); camera.targetTexture = target;
                Canvas.ForceUpdateCanvases(); effect.Sample(.48f); Capture(camera, target, "impact-small");
                // Runtime lifecycle callbacks do not fire on regular MonoBehaviours in edit-mode preview scenes.
                typeof(RepMilestoneEffect).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(effect, null);
                Require(!effect.IsPlaying && !effect.transform.Find("TenRepCelebration").gameObject.activeSelf, "Disable clears overlay");
                File.WriteAllText(Output + "/validation.txt", "PASS: threshold, once per set, reset, reused artwork, nonblocking controls, disable cleanup; 55 timeline frames and 390x844 / 320x568 captures.\nDevice haptics and live camera counting require device verification.\n");
                Debug.Log("[RepMilestone] Validation PASS");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            }
        }

        private static void Capture(Camera camera, RenderTexture target, string name)
        {
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
                File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); }
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new Exception("[RepMilestone] " + message); }
    }
}
