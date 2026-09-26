using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace PushStars.CV
{
    /// <summary>
    /// <see cref="IPoseSource"/> that replays a pose recording extracted from a real video by
    /// <c>tools/cv/dump_pose.py</c> (same pose_landmarker model as the device). Lets acceptance
    /// recordings (clap push-ups, knee reps, arm waving…) drive the REAL counter/anti-cheat chain in
    /// the editor instead of a synthetic mock.
    ///
    /// <para>Format (<c>*.pose.csv</c>): <c># … aspect=W/H …</c> header, then one line per frame:
    /// <c>t, 33×(x,y,z,vis) image-normalized, 33×(x,y,z) world meters</c>; a line with only
    /// <c>t</c> is a frame without a detected pose. Landmarks are already upright (screen space),
    /// i.e. what <c>MediaPipePoseSource</c> emits after its landmark rotation.</para>
    ///
    /// <para>At most ONE frame is emitted per Update, when the recording clock reaches it — the
    /// session stamps frames with <c>Time.time</c>, so two frames in one Update would share a
    /// timestamp. For deterministic offline runs set <c>Time.captureDeltaTime</c> ≤ 1/60.</para>
    /// </summary>
    public sealed class RecordedPoseSource : MonoBehaviour, IPoseSource
    {
        [Tooltip("Absolute path, or relative to the project root (e.g. CVRecordings/clap/x.pose.csv).")]
        [SerializeField] private string _recordingPath = "";
        [SerializeField] private bool _loop;
        [SerializeField] private bool _playOnEnable = true;

        public event Action<PoseFrame> OnFrame;
        public event Action<TrackingQuality> OnQualityChanged;

        public TrackingQuality Quality { get; private set; } = TrackingQuality.None;
        public bool IsRunning { get; private set; }
        public string StatusMessage { get; private set; } = "recording not loaded";

        /// <summary>True once the last frame has been emitted (never, when looping).</summary>
        public bool Finished { get; private set; }
        public int FrameCount => _frames.Count;
        public int EmittedFrames { get; private set; }
        /// <summary>Recording-clock time of the last emitted frame.</summary>
        public float RecordingTimeSec { get; private set; }

        private readonly List<(float t, Landmark[] image, Landmark[] world)> _frames = new();
        private float _aspect = 1f;
        private float _startTime;
        private int _next;

        public string RecordingPath
        {
            get => _recordingPath;
            set { _recordingPath = value; _frames.Clear(); }
        }

        private void OnEnable() { if (_playOnEnable) StartTracking(); }
        private void OnDisable() => StopTracking();

        public void StartTracking()
        {
            if (_frames.Count == 0 && !Load()) return;
            _startTime = Time.time;
            _next = 0;
            EmittedFrames = 0;
            Finished = false;
            IsRunning = true;
        }

        public void StopTracking() => IsRunning = false;

        private void Update()
        {
            if (!IsRunning || Finished) return;
            if (_next >= _frames.Count)
            {
                if (!_loop) { Finished = true; StatusMessage = "recording finished"; return; }
                _next = 0;
                _startTime = Time.time;
            }

            var (t, image, world) = _frames[_next];
            if (Time.time - _startTime < t - _frames[0].t) return;
            _next++;
            EmittedFrames++;
            RecordingTimeSec = t;

            var frame = ToFrame(image, world, Time.time, _aspect);
            OnFrame?.Invoke(frame);
            SetQuality(image == null ? TrackingQuality.Lost : PoseQuality.Classify(frame));
            StatusMessage = $"replay {Path.GetFileName(_recordingPath)} {t:0.00}s";
        }

        private void SetQuality(TrackingQuality q)
        {
            if (q == Quality) return;
            Quality = q;
            OnQualityChanged?.Invoke(q);
        }

        private bool Load()
        {
            string path = ResolvePath(_recordingPath);
            if (!File.Exists(path))
            {
                StatusMessage = "recording not found: " + path;
                Debug.LogError("[RecordedPoseSource] " + StatusMessage);
                return false;
            }
            _frames.Clear();
            _frames.AddRange(Read(path, out _aspect));
            StatusMessage = $"loaded {_frames.Count} frames";
            return _frames.Count > 0;
        }

        /// <summary>Relative paths resolve against the project root (the parent of Assets/).</summary>
        public static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path)) return path ?? "";
            return Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", path);
        }

        /// <summary>Parses a <c>*.pose.csv</c>. <c>image</c> is null on frames without a pose;
        /// <c>world</c> is null when the recording carries no world landmarks.</summary>
        public static List<(float t, Landmark[] image, Landmark[] world)> Read(string path, out float aspect)
        {
            aspect = 1f;
            var frames = new List<(float, Landmark[], Landmark[])>();
            var inv = CultureInfo.InvariantCulture;
            foreach (string line in File.ReadLines(path))
            {
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    int a = line.IndexOf("aspect=", StringComparison.Ordinal);
                    if (a >= 0 && float.TryParse(line.Substring(a + 7).Split(' ')[0],
                            NumberStyles.Float, inv, out float asp))
                        aspect = asp;
                    continue;
                }

                string[] p = line.Split(',');
                float t = float.Parse(p[0], inv);
                Landmark[] image = null, world = null;
                if (p.Length >= 1 + PoseLandmarks.Count * 4)
                {
                    image = new Landmark[PoseLandmarks.Count];
                    for (int i = 0; i < PoseLandmarks.Count; i++)
                    {
                        int o = 1 + i * 4;
                        image[i] = new Landmark(float.Parse(p[o], inv), float.Parse(p[o + 1], inv),
                            float.Parse(p[o + 2], inv), float.Parse(p[o + 3], inv));
                    }
                    int w0 = 1 + PoseLandmarks.Count * 4;
                    if (p.Length >= w0 + PoseLandmarks.Count * 3)
                    {
                        world = new Landmark[PoseLandmarks.Count];
                        for (int i = 0; i < PoseLandmarks.Count; i++)
                        {
                            int o = w0 + i * 3;
                            // BlazePose shares per-keypoint visibility between image and world.
                            world[i] = new Landmark(float.Parse(p[o], inv), float.Parse(p[o + 1], inv),
                                float.Parse(p[o + 2], inv), image[i].Visibility);
                        }
                    }
                }
                frames.Add((t, image, world));
            }
            return frames;
        }

        /// <summary>The frame to emit for a recording entry, stamped with the session clock. A
        /// no-pose entry becomes a valid-but-invisible skeleton, exactly like a live dropout.</summary>
        public static PoseFrame ToFrame(Landmark[] image, Landmark[] world, float now, float aspect)
            => new PoseFrame(image ?? new Landmark[PoseLandmarks.Count], world, now, aspect);
    }
}
