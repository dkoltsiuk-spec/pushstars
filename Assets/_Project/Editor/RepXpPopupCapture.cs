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
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    /// <summary>
    /// Play-mode screenshots of the per-rep XP popups (<see cref="RepXpPopups"/>) on the duel,
    /// the boss battle and training, with the player held in the push-up. CV stays off; the
    /// popups are thrown straight at the HUD the way <c>FightController.ShowRepXp</c> does — three
    /// reps, the last a clap — and the frame is taken while all three are in the air.
    ///
    /// <para>Plays from whatever scene is open, dirty or not, and loads the fight scenes inside
    /// Play Mode, so another session's unsaved edits survive the run. Output: output/rep-xp/.</para>
    /// </summary>
    [InitializeOnLoad]
    public static class RepXpPopupCapture
    {
        private const string Running = "PushStars.RepXpPopupCapture.Running";
        private const string Locked = "PushStars.RepXpPopupCapture.ReloadLocked";
        private const string Output = "output/rep-xp/";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string[] Modes = { "duel", "boss", "training" };

        private static int _mode, _step;
        private static double _due, _deadline;
        private static Camera _camera;
        private static RenderTexture _texture;
        private static StringBuilder _report;
        private static RepXpPopups _popups;

        static RepXpPopupCapture() => EditorApplication.playModeStateChanged += StateChanged;

        [MenuItem("Tools/Push Stars/CV/Capture Rep XP Popups (duel, boss, training)", priority = 364)]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start the capture outside Play Mode.");
            // Compile whatever is pending now, not on the way into Play.
            AssetDatabase.Refresh();
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("Scripts are compiling — run the capture again once they finish.");
            Directory.CreateDirectory(Output);
            SessionState.SetBool(Running, true);
            FightRequest.Clear();
            EditorApplication.isPlaying = true;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Running, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                _mode = _step = 0;
                _report = new StringBuilder("Rep XP popup capture\n");
                _deadline = EditorApplication.timeSinceStartup + 120;
                _due = EditorApplication.timeSinceStartup + 1.5;
                Application.runInBackground = true;
                // Other sessions edit scripts in this Editor: a recompile mid-run reloads the
                // domain, drops this capture's state and leaves the posed bodies throwing.
                EditorApplication.LockReloadAssemblies();
                SessionState.SetBool(Locked, true);
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Running, false);
                EditorApplication.update -= Tick;
                Unlock();
            }
        }

        private static void Unlock()
        {
            if (!SessionState.GetBool(Locked, false)) return;
            SessionState.SetBool(Locked, false);
            EditorApplication.UnlockReloadAssemblies();
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
                    ForcePushup(.3f);
                    Delay(1.0);
                    break;
                case 2:
                    PrepareCapture();
                    Throw(84f, false);
                    Delay(.42);
                    break;
                case 3:
                    Throw(97f, false);
                    Delay(.42);
                    break;
                case 4:
                    Throw(97f, true);
                    Delay(.22);
                    break;
                default:
                    Capture(mode);
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
                // duel: no request → the authored PvP screen preview.
            }
        }

        /// <summary>One accepted rep, as FightController hands it to the popups.</summary>
        private static void Throw(float form, bool clap)
        {
            if (_popups == null)
            {
                var hud = Object.FindObjectsByType<FightHud>(FindObjectsSortMode.None).FirstOrDefault(h => h.gameObject.scene == SceneManager.GetActiveScene());
                if (hud == null) throw new InvalidOperationException("no FightHud in " + SceneManager.GetActiveScene().name);
                _popups = hud.gameObject.AddComponent<RepXpPopups>();
                _popups.Bind(Object.FindFirstObjectByType<PushupSession>());
            }
            _popups.Show(form, clap);
            var image = typeof(RepXpPopups).GetField("_image", Private).GetValue(_popups) as RawImage;
            var avatar = typeof(RepXpPopups).GetField("_avatar", Private).GetValue(_popups) as FightAvatar;
            _report.AppendLine($"{Modes[_mode]}: +{XpCalculator.XpForRep(form)}{(clap ? " clap" : "")} on {(image != null ? image.name : "NO IMAGE")} (avatar {(avatar != null ? avatar.name : "NONE")})");
        }

        /// <summary>The armed push-up as live play shows it (see PushupPresentationCapture).</summary>
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
                PushupPoseCorrection.Bind(animator).SetDepth(depth, true, 0f);
            }
        }

        private static void PrepareCapture()
        {
            Scene active = SceneManager.GetActiveScene();
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .Where(c => c.gameObject.scene == active && c.isRootCanvas && c.enabled).ToArray();
            _texture = new RenderTexture(780, 1688, 24, RenderTextureFormat.ARGB32);
            _texture.Create();
            _camera = new GameObject("RepXpCaptureCamera", typeof(Camera)).GetComponent<Camera>();
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

        private static void DisposeCapture()
        {
            _popups = null;
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
            EditorApplication.isPlaying = false;
        }
    }
}
