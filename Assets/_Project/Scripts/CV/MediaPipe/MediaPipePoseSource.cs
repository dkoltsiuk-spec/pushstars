using System;
using System.Collections;
using System.Diagnostics;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PushStars.CV
{
    /// <summary>
    /// Real <see cref="IPoseSource"/> backed by the Homuler MediaPipeUnityPlugin (Pose Landmarker,
    /// Tasks API, LIVE_STREAM mode). Self-contained: owns its own <see cref="WebCamTexture"/> and
    /// resource manager so it does NOT depend on the plugin's sample scripts (Bootstrap /
    /// ImageSourceProvider), which live in Assembly-CSharp and can't be referenced from an asmdef.
    ///
    /// This whole assembly (PushStars.CV.MediaPipe) only compiles when the scripting define
    /// <c>PUSHSTARS_MEDIAPIPE</c> is set — so the project builds without the plugin installed. Once
    /// the plugin is imported and the define added, attach this component instead of MockPoseSource.
    ///
    /// Editor: loads the model from the plugin's PackageResources via LocalResourceManager. Device
    /// builds: copy the .bytes model into StreamingAssets (StreamingAssetsResourceManager is used).
    ///
    /// The MediaPipe result callback runs off the main thread, so it only converts the landmarks into
    /// a buffer; <see cref="Update"/> raises <see cref="OnFrame"/> / <see cref="OnQualityChanged"/> on
    /// the main thread.
    /// </summary>
    public sealed class MediaPipePoseSource : MonoBehaviour, IPoseSource
    {
        [Header("Model")]
        [Tooltip("pose_landmarker_lite.bytes / _full.bytes / _heavy.bytes. Lite = fastest on CPU.")]
        [SerializeField] private string _modelFileName = "pose_landmarker_lite.bytes";

        [Header("Camera (landscape — webcams don't do portrait)")]
        [SerializeField] private int _requestedWidth = 640;
        [SerializeField] private int _requestedHeight = 480;
        [SerializeField] private int _requestedFps = 30;
        [Tooltip("Use the front (selfie) camera. If no explicit Device Name is set, picks a front/back device accordingly.")]
        [SerializeField] private bool _useFrontCamera = true;
        [SerializeField] private string _deviceName = ""; // explicit device name overrides the front/back pick

        [Header("Image orientation (toggle if the skeleton doesn't track)")]
        [SerializeField] private bool _flipHorizontally = false;
        [SerializeField] private bool _flipVertically = true;

        [Tooltip("Rotate landmarks into UPRIGHT screen space (0/90/180/270, CW). The webcam frame " +
                 "is sensor-native landscape and the display rotates it 90° — landmarks must get " +
                 "the same rotation or every 'below/above' anti-cheat check compares the wrong " +
                 "axis (found on device: plank read as WristsAirborne, κ≈0 instead of ~0.15). " +
                 "Verified iPhone front camera: 90.")]
        [SerializeField] private int _landmarkRotationDeg = 90;

        [Header("Detection confidence")]
        [SerializeField, Range(0f, 1f)] private float _minPoseDetectionConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _minPosePresenceConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _minTrackingConfidence = 0.5f;

        public event Action<PoseFrame> OnFrame;
        public event Action<TrackingQuality> OnQualityChanged;

        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;
        public bool IsRunning { get; private set; }

        /// <summary>Human-readable last status / error — surfaced on the debug HUD for on-device diagnosis.</summary>
        public string StatusMessage { get; private set; } = "idle";

        private void SetStatus(string s)
        {
            StatusMessage = s;
            Debug.Log($"[MediaPipe] {s}");
        }

        /// <summary>The live camera texture — assign to a RawImage for a preview if desired.</summary>
        public WebCamTexture CameraTexture => _webCam;

        /// <summary>The horizontal/vertical flips applied when the camera frame is handed to MediaPipe.
        /// A skeleton overlay must apply the same flips to align landmarks with the raw camera texture.</summary>
        public bool SourceFlipHorizontally => _flipHorizontally;
        public bool SourceFlipVertically   => _flipVertically;

        /// <summary>The upright rotation applied to landmarks before they leave this source. The
        /// skeleton overlay must INVERT this to get back to raw texture coordinates (it draws
        /// through the raw-texture display matrix).</summary>
        public int LandmarkRotationDeg => _landmarkRotationDeg;

        private WebCamTexture     _webCam;
        private PoseLandmarker    _poseLandmarker;
        private TextureFramePool  _framePool;
        private Coroutine         _loop;
        private readonly Stopwatch _clock = new Stopwatch();
        private long _lastTimestamp = -1;

        private Mediapipe.Tasks.Vision.Core.ImageProcessingOptions _imageProcessingOptions;

        // Cross-thread hand-off from the MediaPipe callback to Update().
        private readonly object _gate = new object();
        private Landmark[] _pending;
        private Landmark[] _pendingWorld; // optional, null when not provided this frame
        private float _pendingTime;
        private bool _hasPending;

        private void OnEnable()  => StartTracking();
        private void OnDisable() => StopTracking();

        public void StartTracking()
        {
            if (IsRunning) return;
            IsRunning = true;
            // The user is mid-push-up and cannot touch the screen — never sleep while tracking.
            // Also covers the testCV build, which boots straight into this scene without
            // AppBootstrap (where the app-wide NeverSleep is set).
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _loop = StartCoroutine(RunAsync());
        }

        public void StopTracking()
        {
            if (!IsRunning) return;
            IsRunning = false;

            if (_loop != null) { StopCoroutine(_loop); _loop = null; }

            try { _poseLandmarker?.Close(); } catch (Exception e) { Debug.LogWarning($"[MediaPipe] Close: {e.Message}"); }
            _poseLandmarker = null;

            if (_webCam != null) { _webCam.Stop(); _webCam = null; }

            _framePool?.Dispose();
            _framePool = null;

            SetQuality(TrackingQuality.None);
            _clock.Reset();
            _lastTimestamp = -1;
        }

        private IEnumerator RunAsync()
        {
            // glog is intentionally NOT initialized (it aborts the process if init twice).

            // ── 1) Camera permission FIRST, so the iOS prompt appears regardless of MediaPipe init. ──
            SetStatus("requesting camera permission");
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                SetStatus("CAMERA PERMISSION DENIED");
                IsRunning = false;
                yield break;
            }

            // ── 2) Start the camera (independent of MediaPipe — the preview must work even if the
            //       pose model fails to load). ──
            string device = ResolveCameraDevice();
            SetStatus(string.IsNullOrEmpty(device) ? "starting camera (default)" : $"starting camera: {device}");
            _webCam = string.IsNullOrEmpty(device)
                ? new WebCamTexture(_requestedWidth, _requestedHeight, _requestedFps)
                : new WebCamTexture(device, _requestedWidth, _requestedHeight, _requestedFps);
            _webCam.Play();
            float camStart = Time.realtimeSinceStartup;
            yield return new WaitUntil(() => _webCam.width > 16 || Time.realtimeSinceStartup - camStart > 6f);
            if (_webCam.width <= 16) { SetStatus("CAMERA NOT STARTING"); }
            else SetStatus($"camera {_webCam.width}x{_webCam.height}");

            int w = Mathf.Max(_webCam.width, 16), h = Mathf.Max(_webCam.height, 16);
            _framePool = new TextureFramePool(w, h, TextureFormat.RGBA32, 10);

            // ── 3) Make the model available, then build the Pose Landmarker. Any failure here leaves
            //       the camera preview running but disables detection (status shows why). ──
            string modelPath = StageModel(); // editor: model name; device: absolute persistentData path
            if (modelPath == null)
            {
                SetStatus("MODEL NOT FOUND (skipping detection)");
            }
            else
            {
                SetStatus("preparing model");
                bool prepareFailed = false;
#if UNITY_EDITOR
                IResourceManager resources = new LocalResourceManager();
                yield return RunSafely(resources.PrepareAssetAsync(_modelFileName, _modelFileName, false),
                                       e => { prepareFailed = true; SetStatus("PREPARE ERROR: " + e.Message); });
                string assetPath = _modelFileName;
#else
                // Device: we already copied the model to persistentData (StageModel); point MediaPipe at it.
                string assetPath = modelPath;
#endif
                if (!prepareFailed)
                {
                    SetStatus("creating pose landmarker");
                    try
                    {
                        var baseOptions = new Mediapipe.Tasks.Core.BaseOptions(
                            Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU, modelAssetPath: assetPath);
                        var options = new PoseLandmarkerOptions(
                            baseOptions,
                            runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                            numPoses: 1,
                            minPoseDetectionConfidence: _minPoseDetectionConfidence,
                            minPosePresenceConfidence: _minPosePresenceConfidence,
                            minTrackingConfidence: _minTrackingConfidence,
                            outputSegmentationMasks: false,
                            resultCallback: OnPoseResult);
                        _poseLandmarker = PoseLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
                        _imageProcessingOptions = new Mediapipe.Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: 0);
                        SetStatus("running");
                    }
                    catch (Exception e)
                    {
                        SetStatus("LANDMARKER ERROR: " + e.Message);
                        _poseLandmarker = null;
                    }
                }
            }

            _clock.Restart();

            // ── 4) Capture loop. If the landmarker failed, we still keep the camera preview alive. ──
            var waitForEndOfFrame = new WaitForEndOfFrame();
            while (IsRunning)
            {
                if (_poseLandmarker == null || _webCam == null || _webCam.width <= 16)
                {
                    yield return waitForEndOfFrame; // camera-preview-only / not ready
                    continue;
                }
                if (!_framePool.TryGetTextureFrame(out var textureFrame))
                {
                    yield return waitForEndOfFrame;
                    continue;
                }

                var req = textureFrame.ReadTextureAsync(_webCam, _flipHorizontally, _flipVertically);
                yield return new WaitUntil(() => req.done);

                if (req.hasError)
                {
                    textureFrame.Release();
                    yield return waitForEndOfFrame;
                    continue;
                }

                var image = textureFrame.BuildCPUImage();
                textureFrame.Release();

                long ts = _clock.ElapsedMilliseconds;
                if (ts <= _lastTimestamp) ts = _lastTimestamp + 1; // timestamps must strictly increase
                _lastTimestamp = ts;

                _poseLandmarker.DetectAsync(image, ts, _imageProcessingOptions);

                yield return waitForEndOfFrame;
            }
        }

        // Runs on a MediaPipe thread — only copy primitive data out, no Unity API calls here.
        private void OnPoseResult(PoseLandmarkerResult result, Image image, long timestamp)
        {
            Landmark[] arr = null;
            Landmark[] world = null;

            int rot = ((_landmarkRotationDeg % 360) + 360) % 360;

            var poses = result.poseLandmarks;
            if (poses != null && poses.Count > 0)
            {
                var lms = poses[0].landmarks;
                if (lms != null && lms.Count >= PoseLandmarks.Count)
                {
                    arr = new Landmark[PoseLandmarks.Count];
                    for (int i = 0; i < PoseLandmarks.Count; i++)
                    {
                        var lm = lms[i];
                        float vis = lm.visibility ?? lm.presence ?? 1f;
                        // Rotate into upright screen space: after this, +y = down toward the floor
                        // (for the propped-up phone) and every downstream signed check is honest.
                        float x, y;
                        switch (rot)
                        {
                            case 90:  x = 1f - lm.y; y = lm.x;      break; // CW
                            case 180: x = 1f - lm.x; y = 1f - lm.y; break;
                            case 270: x = lm.y;      y = 1f - lm.x; break;
                            default:  x = lm.x;      y = lm.y;      break;
                        }
                        arr[i] = new Landmark(x, y, lm.z, vis);
                    }
                }
            }

            // World landmarks (meters, hip-centered, body frame). MediaPipe does not always populate
            // visibility on world landmarks — we reuse the per-keypoint visibility from the image
            // landmarks since it's the same skeleton. If `arr` is null (no image landmarks this
            // frame), world is also skipped — we don't want a partially-populated frame.
            var worldPoses = result.poseWorldLandmarks;
            if (arr != null && worldPoses != null && worldPoses.Count > 0)
            {
                var wlms = worldPoses[0].landmarks;
                if (wlms != null && wlms.Count >= PoseLandmarks.Count)
                {
                    world = new Landmark[PoseLandmarks.Count];
                    for (int i = 0; i < PoseLandmarks.Count; i++)
                    {
                        var wlm = wlms[i];
                        // Reuse visibility from the image landmark (set above on arr[i]) — world
                        // landmarks share the same per-keypoint presence/visibility scores.
                        float vis = arr[i].Visibility;
                        // Same upright rotation for the world x/y (hip-centered — no +1 offset).
                        float wx, wy;
                        switch (rot)
                        {
                            case 90:  wx = -wlm.y; wy = wlm.x;  break;
                            case 180: wx = -wlm.x; wy = -wlm.y; break;
                            case 270: wx = wlm.y;  wy = -wlm.x; break;
                            default:  wx = wlm.x;  wy = wlm.y;  break;
                        }
                        world[i] = new Landmark(wx, wy, wlm.z, vis);
                    }
                }
            }

            lock (_gate)
            {
                _pending = arr;
                _pendingWorld = world;
                _pendingTime = timestamp / 1000f;
                _hasPending = true;
            }
        }

        private void Update()
        {
            Landmark[] arr;
            Landmark[] world;
            float t;
            bool has;
            lock (_gate)
            {
                has = _hasPending;
                arr = _pending;
                world = _pendingWorld;
                t = _pendingTime;
                _hasPending = false;
            }
            if (!has) return;

            if (arr == null)
            {
                SetQuality(TrackingQuality.Lost); // no person detected this frame
                return;
            }

            // World landmarks are optional — `world` may be null and consumers must guard via
            // PoseFrame.HasWorldLandmarks. Aspect follows the UPRIGHT (rotated) landmark space:
            // for 90/270 the effective image is height×width.
            float aspect = 1f;
            if (_webCam != null && _webCam.height > 16)
            {
                int r = ((_landmarkRotationDeg % 360) + 360) % 360;
                bool quarter = r == 90 || r == 270;
                aspect = quarter
                    ? (float)_webCam.height / _webCam.width
                    : (float)_webCam.width / _webCam.height;
            }
            var frame = new PoseFrame(arr, world, t, aspect);
            OnFrame?.Invoke(frame);
            SetQuality(PoseQuality.Classify(frame));
        }

        private void SetQuality(TrackingQuality q)
        {
            if (q == Quality) return;
            Quality = q;
            OnQualityChanged?.Invoke(q);
        }

        /// <summary>Picks the camera device: an explicit <see cref="_deviceName"/> wins; otherwise the
        /// first front- or back-facing device per <see cref="_useFrontCamera"/>; else the default.</summary>
        private string ResolveCameraDevice()
        {
            if (!string.IsNullOrEmpty(_deviceName)) return _deviceName;
            var devices = WebCamTexture.devices;
            foreach (var d in devices)
                if (d.isFrontFacing == _useFrontCamera) return d.name;
            return devices.Length > 0 ? devices[0].name : "";
        }

        /// <summary>Editor: returns the model name (loaded from the package via LocalResourceManager).
        /// Device: copies the model from the read-only StreamingAssets into persistentData (where the
        /// native loader can open it) and returns that absolute path, or null if the model is missing.</summary>
        private string StageModel()
        {
#if UNITY_EDITOR
            return _modelFileName;
#else
            try
            {
                string src = System.IO.Path.Combine(Application.streamingAssetsPath, _modelFileName);
                string dst = System.IO.Path.Combine(Application.persistentDataPath, _modelFileName);
                if (!System.IO.File.Exists(dst))
                {
                    if (!System.IO.File.Exists(src)) return null; // (Android packs StreamingAssets in the APK — needs UnityWebRequest; iOS is a real path)
                    System.IO.File.Copy(src, dst, true);
                }
                return dst;
            }
            catch (Exception e)
            {
                SetStatus("STAGE ERROR: " + e.Message);
                return null;
            }
#endif
        }

        /// <summary>Runs an inner coroutine, routing any thrown exception to <paramref name="onError"/>
        /// instead of letting it kill the parent coroutine.</summary>
        private IEnumerator RunSafely(IEnumerator inner, Action<Exception> onError)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) yield break;
                    current = inner.Current;
                }
                catch (Exception e) { onError(e); yield break; }
                yield return current;
            }
        }
    }
}
