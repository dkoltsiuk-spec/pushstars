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
        [SerializeField] private string _deviceName = ""; // empty = default camera

        [Header("Image orientation (toggle if the skeleton doesn't track)")]
        [SerializeField] private bool _flipHorizontally = false;
        [SerializeField] private bool _flipVertically = true;

        [Header("Detection confidence")]
        [SerializeField, Range(0f, 1f)] private float _minPoseDetectionConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _minPosePresenceConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _minTrackingConfidence = 0.5f;

        public event Action<PoseFrame> OnFrame;
        public event Action<TrackingQuality> OnQualityChanged;

        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;
        public bool IsRunning { get; private set; }

        /// <summary>The live camera texture — assign to a RawImage for a preview if desired.</summary>
        public WebCamTexture CameraTexture => _webCam;

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
        private float _pendingTime;
        private bool _hasPending;

        private void OnEnable()  => StartTracking();
        private void OnDisable() => StopTracking();

        public void StartTracking()
        {
            if (IsRunning) return;
            IsRunning = true;
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
            // NOTE: do NOT call Glog.Initialize()/InitGoogleLogging here — glog aborts the process if
            // initialized twice (and native glog state survives editor domain reloads / the plugin's
            // own Bootstrap). The Pose Landmarker Tasks API does not require it.

            // 1) Resource manager + model. Editor reads from the package; builds read StreamingAssets.
            IResourceManager resources =
#if UNITY_EDITOR
                new LocalResourceManager();
#else
                new StreamingAssetsResourceManager();
#endif
            yield return StartCoroutine(resources.PrepareAssetAsync(_modelFileName, _modelFileName, false));

            // 2) Build the Pose Landmarker (CPU on desktop; LIVE_STREAM → async callback).
            var baseOptions = new Mediapipe.Tasks.Core.BaseOptions(
                Mediapipe.Tasks.Core.BaseOptions.Delegate.CPU, modelAssetPath: _modelFileName);
            var options = new PoseLandmarkerOptions(
                baseOptions,
                runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.LIVE_STREAM,
                numPoses: 1,
                minPoseDetectionConfidence: _minPoseDetectionConfidence,
                minPosePresenceConfidence: _minPosePresenceConfidence,
                minTrackingConfidence: _minTrackingConfidence,
                outputSegmentationMasks: false,
                resultCallback: OnPoseResult);

            try
            {
                _poseLandmarker = PoseLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
            }
            catch (Exception e)
            {
                Debug.LogError($"[MediaPipe] Failed to create PoseLandmarker: {e}");
                IsRunning = false;
                yield break;
            }

            _imageProcessingOptions = new Mediapipe.Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: 0);

            // 3) Camera.
            _webCam = string.IsNullOrEmpty(_deviceName)
                ? new WebCamTexture(_requestedWidth, _requestedHeight, _requestedFps)
                : new WebCamTexture(_deviceName, _requestedWidth, _requestedHeight, _requestedFps);
            _webCam.Play();
            yield return new WaitUntil(() => _webCam.width > 16); // wait for the device to warm up

            _framePool = new TextureFramePool(_webCam.width, _webCam.height, TextureFormat.RGBA32, 10);
            _clock.Restart();

            // 4) Capture loop — read each camera frame and submit it for async detection.
            var waitForEndOfFrame = new WaitForEndOfFrame();
            while (IsRunning)
            {
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
                        arr[i] = new Landmark(lm.x, lm.y, lm.z, vis);
                    }
                }
            }

            lock (_gate)
            {
                _pending = arr;
                _pendingTime = timestamp / 1000f;
                _hasPending = true;
            }
        }

        private void Update()
        {
            Landmark[] arr;
            float t;
            bool has;
            lock (_gate)
            {
                has = _hasPending;
                arr = _pending;
                t = _pendingTime;
                _hasPending = false;
            }
            if (!has) return;

            if (arr == null)
            {
                SetQuality(TrackingQuality.Lost); // no person detected this frame
                return;
            }

            var frame = new PoseFrame(arr, t);
            OnFrame?.Invoke(frame);
            SetQuality(PoseQuality.Classify(frame));
        }

        private void SetQuality(TrackingQuality q)
        {
            if (q == Quality) return;
            Quality = q;
            OnQualityChanged?.Invoke(q);
        }
    }
}
