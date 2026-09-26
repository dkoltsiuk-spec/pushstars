using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PushStars.Core;
using PushStars.CV;
using PushStars.Fight;
using PushStars.UI.Layout;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Play-mode screenshots of every screen that shows the push-up (boss battle, duel, training,
    /// assessment) with the real layouts, the real FightAvatar framing and the armed push-up pose —
    /// compared against the design mockups for body size and contact shadow. CV stays off: the
    /// fight request is set after FightController.Awake (see BossCombatPlayValidation), the duel
    /// uses the authored screen preview. Output: output/pushup-presentation/*.png.
    /// </summary>
    [InitializeOnLoad]
    public static class PushupPresentationCapture
    {
        private const string Running = "PushStars.PushupPresentationCapture.Running";
        private const string ReturnScene = "PushStars.PushupPresentationCapture.ReturnScene";
        private const string Output = "output/pushup-presentation/";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string[] Modes = { "boss", "duel", "training", "assessment" };

        private static int _mode, _step;
        private static double _due, _deadline;
        private static Camera _camera;
        private static RenderTexture _texture;
        private static StringBuilder _report;

        static PushupPresentationCapture() => EditorApplication.playModeStateChanged += StateChanged;

        [MenuItem("Tools/Push Stars/CV/Capture Push-up Presentation (all screens)", priority = 361)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start the capture outside Play Mode.");
            if (SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Save the open scene first — the capture switches scenes.");
            Directory.CreateDirectory(Output);
            SessionState.SetString(ReturnScene, SceneManager.GetActiveScene().path);
            SessionState.SetBool(Running, true);
            FightRequest.Clear();
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Fight.unity");
            EditorApplication.isPlaying = true;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Running, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _mode = _step = 0;
                _report = new StringBuilder("Push-up presentation capture\n");
                _deadline = EditorApplication.timeSinceStartup + 120;
                _due = EditorApplication.timeSinceStartup + 1.5;
                Application.runInBackground = true;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Running, false);
                EditorApplication.update -= Tick;
                string back = SessionState.GetString(ReturnScene, "");
                // Unity restores the pre-Play scene (Fight) after this callback and after delayCall;
                // reopen the caller's scene a few editor ticks later.
                if (!string.IsNullOrEmpty(back) && File.Exists(back))
                {
                    int ticks = 0;
                    EditorApplication.CallbackFunction reopen = null;
                    reopen = () =>
                    {
                        if (EditorApplication.isPlayingOrWillChangePlaymode || ++ticks < 10) return;
                        EditorApplication.update -= reopen;
                        if (SceneManager.GetActiveScene().path != back && !SceneManager.GetActiveScene().isDirty)
                            EditorSceneManager.OpenScene(back);
                    };
                    EditorApplication.update += reopen;
                }
            }
        }

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new TimeoutException("capture timed out");
                if (_mode >= Modes.Length) { Finish("RESULT: done"); return; }
                Step(Modes[_mode]);
            }
            catch (Exception e) { Finish("FAIL: " + e); }
        }

        private static void Step(string mode)
        {
            switch (_step++)
            {
                case 0:
                    DisposeCapture();
                    FightRequest.Clear();
                    SceneManager.sceneLoaded += ArmAfterAwake;
                    SceneManager.LoadScene(mode == "training" ? FightConfig.TrainingSceneName : FightConfig.FightSceneName);
                    Delay(2.5);
                    break;
                case 1:
                    ForcePushup(0f);
                    Delay(1.0);
                    break;
                case 2:
                    PrepareCapture();
                    Delay(.4);
                    break;
                case 3:
                    Capture(mode + "-top");
                    Measure();
                    ForcePushup(1f);
                    Delay(.6);
                    break;
                default:
                    Capture(mode + "-bottom");
                    _mode++;
                    _step = 0;
                    break;
            }
        }

        private static void ArmAfterAwake(Scene scene, LoadSceneMode loadMode)
        {
            SceneManager.sceneLoaded -= ArmAfterAwake;
            // FightController.Awake saw no request and disabled tracking; Start runs the mode.
            switch (Modes[_mode])
            {
                case "boss": FightRequest.Boss(); break;
                case "training": FightRequest.Training(new TrainingPlan(3, 60)); break;
                case "assessment": FightRequest.LevelTest(); break;
                // duel: no request → the authored PvP screen preview.
            }
        }

        /// <summary>The armed push-up as live play shows it: drivers, mirror and anchor off, the
        /// pose correction owning the body at <paramref name="depth"/>.</summary>
        private static void ForcePushup(float depth)
        {
            var boss = Object.FindObjectsByType<BossCombatScreen>(FindObjectsSortMode.None).FirstOrDefault(b => b.Active);
            foreach (var avatar in Object.FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
            {
                if (avatar.Character == null || !avatar.isActiveAndEnabled) continue;
                if (boss != null && avatar == boss.BossStage) continue; // the boss stands
                var animator = avatar.Character.GetComponentInChildren<Animator>();
                if (animator == null) continue;
                var mirror = avatar.GetComponent<PoseMirrorRetargeter>();
                if (mirror != null && mirror.MirrorPhase)
                    typeof(PoseMirrorRetargeter).GetMethod("SetPhase", Private).Invoke(mirror, new object[] { true, Time.unscaledTime });
                foreach (var behaviour in avatar.GetComponents<MonoBehaviour>())
                    if (behaviour is IAvatarAnimator) behaviour.enabled = false;
                animator.enabled = true;
                animator.speed = 0f;
                if (animator.HasState(0, Animator.StringToHash("PushUp"))) animator.Play("PushUp", 0, .5f);
                var correction = PushupPoseCorrection.Bind(animator);
                correction.SetDepth(depth, true, 0f);
                _report.AppendLine($"{Modes[_mode]}: {avatar.name} forced push-up depth {depth}");
            }
        }

        private static void PrepareCapture()
        {
            Scene active = SceneManager.GetActiveScene();
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene == active && c.isRootCanvas && c.enabled).ToArray();
            _texture = new RenderTexture(780, 1688, 24, RenderTextureFormat.ARGB32);
            _texture.Create();
            _camera = new GameObject("PushupCaptureCamera", typeof(Camera)).GetComponent<Camera>();
            _camera.transform.position = new Vector3(0, 0, -50);
            _camera.orthographic = true;
            _camera.orthographicSize = 422;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.cullingMask = 1 << 5;
            _camera.targetTexture = _texture;
            foreach (var canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _camera;
                canvas.planeDistance = 10;
                var scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null) scaler.enabled = false;
                canvas.scaleFactor = 2;
                foreach (var rect in canvas.GetComponentsInChildren<RectTransform>(true)) rect.gameObject.layer = 5;
            }
            foreach (var layout in Object.FindObjectsByType<ScreenLayoutRoot>(FindObjectsSortMode.None))
                layout.ShowEditButton = false;
            Canvas.ForceUpdateCanvases();
        }

        private static void Capture(string name)
        {
            foreach (var avatar in Object.FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
                if (avatar.StageCamera != null && avatar.StageCamera.targetTexture != null) avatar.StageCamera.Render();
            Canvas.ForceUpdateCanvases();
            _camera.Render();
            var previous = RenderTexture.active;
            var image = new Texture2D(_texture.width, _texture.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = _texture;
                image.ReadPixels(new Rect(0, 0, _texture.width, _texture.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Output + name + ".png", image.EncodeToPNG());
                _report.AppendLine("captured " + name);
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
        }

        /// <summary>Hands' outer span, palm line and head in canvas units (390 wide, y from the top),
        /// plus the displayed image's rect — the numbers compared against the mockups.</summary>
        private static void Measure()
        {
            foreach (var avatar in Object.FindObjectsByType<FightAvatar>(FindObjectsSortMode.None))
            {
                if (avatar.Character == null || !avatar.isActiveAndEnabled || avatar.StageCamera == null) continue;
                var stage = avatar.StageCamera.GetComponentInParent<PushStars.UI.CharacterStage>(true);
                var image = stage != null ? stage.TargetImage : null;
                var animator = avatar.Character.GetComponentInChildren<Animator>();
                if (image == null || animator == null || !image.isActiveAndEnabled) continue;
                var cam = avatar.StageCamera;
                var root = image.canvas.rootCanvas.transform;
                Vector2 ToCanvas(Vector3 world)
                {
                    var vp = cam.WorldToViewportPoint(world);
                    var uv = image.uvRect; var r = image.rectTransform.rect;
                    var local = new Vector2(r.xMin + (vp.x - uv.x) / uv.width * r.width, r.yMin + (vp.y - uv.y) / uv.height * r.height);
                    var c = root.InverseTransformPoint(image.rectTransform.TransformPoint(local));
                    return new Vector2(c.x, 422f - c.y);
                }
                Vector2 Bone(HumanBodyBones b) => animator.GetBoneTransform(b) != null ? ToCanvas(animator.GetBoneTransform(b).position) : new Vector2(float.NaN, float.NaN);
                Vector2 l = Bone(HumanBodyBones.LeftHand), rr = Bone(HumanBodyBones.RightHand);
                Vector2 li = Bone(HumanBodyBones.LeftIndexProximal), ll = Bone(HumanBodyBones.LeftLittleProximal);
                float palm = Vector2.Distance(li, ll) * 1.3f;
                var corners = new Vector3[4]; image.rectTransform.GetWorldCorners(corners);
                float imgBottom = 422f - root.InverseTransformPoint(corners[0]).y, imgTop = 422f - root.InverseTransformPoint(corners[1]).y;
                _report.AppendLine($"  {Modes[_mode]} {avatar.name} → {image.name}: handSpan={Mathf.Abs(l.x - rr.x) + 2 * palm:0} (wrists {Mathf.Abs(l.x - rr.x):0}) wristsY={(l.y + rr.y) / 2:0} head={Bone(HumanBodyBones.Head).y:0} image top={imgTop:0} bottom={imgBottom:0} w={Mathf.Abs(root.InverseTransformPoint(corners[2]).x - root.InverseTransformPoint(corners[0]).x):0} shot={avatar.PushupHandSpan}");
            }
        }

        private static void DisposeCapture()
        {
            if (_camera != null) Object.DestroyImmediate(_camera.gameObject);
            if (_texture != null) { _texture.Release(); Object.DestroyImmediate(_texture); }
            _camera = null; _texture = null;
        }

        private static void Delay(double seconds) => _due = EditorApplication.timeSinceStartup + seconds;

        private static void Finish(string line)
        {
            _report?.AppendLine(line);
            File.WriteAllText(Output + "report.txt", _report?.ToString() ?? line);
            DisposeCapture();
            SceneManager.sceneLoaded -= ArmAfterAwake;
            FightRequest.Clear();
            EditorApplication.update -= Tick;
            // Running stays set until EnteredEditMode, which reopens the caller's scene.
            EditorApplication.isPlaying = false;
        }
    }
}
