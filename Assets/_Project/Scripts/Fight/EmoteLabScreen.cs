using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>
    /// Emote Lab: records the owner performing a gesture in front of the phone and parks the take in
    /// the cloud for the desktop mocap pipeline (Tools/EmoteMocap), which turns it into an emote
    /// clip. Like <see cref="BotLabScreen"/> the UI is built at runtime, so no scene carries it, and
    /// who sees the entry is decided by <see cref="LabAccess"/> — a release build encodes frames
    /// several times faster than a Development one, so that is the better build to record in.
    ///
    /// <para>The phone stands on a tripod and the owner steps back, so everything is readable and
    /// audible from three metres: a long countdown in huge digits with a beep per second, a red
    /// frame while recording, a chime when the take ends. Frames are kept as the sensor delivers
    /// them (JPEG, with capture times and the sensor rotation) — turning them upright is the
    /// desktop's job, not something to spend the phone's frame time on.</para>
    /// </summary>
    public sealed class EmoteLabScreen : MonoBehaviour
    {
        private const int RequestWidth = 1280, RequestHeight = 720, RequestFps = 30;
        private const float MaxFps = 24f;
        private const int JpegQuality = 70, EncoderBuffers = 4;

        private static readonly string[] Names =
            { "hello", "gg", "boo", "you", "loser", "laugh", "flex", "dance1", "dance2", "custom1", "custom2", "custom3" };
        private static readonly int[] Lengths = { 5, 3, 8 };
        private static readonly int[] Delays = { 8, 5, 12 };

        private static EmoteLabScreen _instance;
        private GameObject _entry, _panel, _controls;
        private RectTransform _safe, _content, _previewBox;
        private RawImage _preview;
        private Image _frame;
        private TMP_Text _status, _big, _nameLabel, _lengthLabel, _delayLabel, _cameraLabel;
        private Button _record, _retry;
        private int _name, _length, _delay;
        private bool _front = true, _busy, _recording;
        private bool _allowed, _accessChecked;
        private float _nextCheck;
        private string _uid;

        private WebCamTexture _camera;
        private Coroutine _cameraStart;
        private EmoteCaptureStore.Writer _writer;
        private readonly Stack<Color32[]> _buffers = new Stack<Color32[]>();
        private readonly SortedDictionary<int, (float seconds, byte[] jpeg)> _encoded = new SortedDictionary<int, (float, byte[])>();
        private readonly object _lock = new object();
        private int _pending, _sequence, _nextToWrite, _dropped;
        private float _recordStart, _lastFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var root = new GameObject("EmoteLab");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<EmoteLabScreen>();
            _instance.Build();
        }

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 31001;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390, 844); scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _safe = Rect("SafeArea", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _entry = Button("EMOTE LAB", _safe, new Vector2(198, -105), new Vector2(112, 36), Open).gameObject;

            _panel = Rect("EmoteLabPanel", _safe, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            _panel.AddComponent<Image>().color = new Color(.025f, .035f, .07f, 1f);
            _content = Rect("Content", _panel.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(390, 770));
            Label("EMOTE CAPTURE · PRIVATE TEST", _content, -14, 30, 18).alignment = TextAlignmentOptions.Center;

            // Preview: a 3:4 window, red-framed while recording.
            _frame = Rect("Frame", _content, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -252), new Vector2(312, 412))
                .gameObject.AddComponent<Image>();
            _frame.color = new Color(.2f, .25f, .5f);
            _previewBox = Rect("Preview", _frame.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(300, 400));
            _previewBox.gameObject.AddComponent<Image>().color = Color.black;
            _previewBox.gameObject.AddComponent<RectMask2D>();
            _preview = Rect("Camera", _previewBox, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(300, 400))
                .gameObject.AddComponent<RawImage>();
            _preview.raycastTarget = false; _preview.enabled = false;
            _big = Label("", _previewBox, 0, 400, 190);
            _big.rectTransform.anchorMin = Vector2.zero; _big.rectTransform.anchorMax = Vector2.one;
            _big.rectTransform.offsetMin = _big.rectTransform.offsetMax = Vector2.zero;
            _big.alignment = TextAlignmentOptions.Center; _big.fontStyle = FontStyles.Bold;
            _big.outlineWidth = .25f; _big.outlineColor = Color.black;

            _status = Label("", _content, -464, 78, 15);
            _status.alignment = TextAlignmentOptions.Top;

            _controls = Rect("Controls", _content, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            _nameLabel = Text(Button("", _controls.transform, new Vector2(110, -566), new Vector2(160, 44), () => Cycle(ref _name, Names.Length)));
            _lengthLabel = Text(Button("", _controls.transform, new Vector2(280, -566), new Vector2(160, 44), () => Cycle(ref _length, Lengths.Length)));
            _delayLabel = Text(Button("", _controls.transform, new Vector2(110, -616), new Vector2(160, 44), () => Cycle(ref _delay, Delays.Length)));
            _cameraLabel = Text(Button("", _controls.transform, new Vector2(280, -616), new Vector2(160, 44), SwitchCamera));
            _record = Button("RECORD", _controls.transform, new Vector2(195, -672), new Vector2(330, 52), Record);
            _record.GetComponent<Image>().color = new Color(.78f, .16f, .2f);
            _retry = Button("RETRY CLOUD SAVE", _controls.transform, new Vector2(110, -728), new Vector2(160, 44), RetryUpload);
            Button("CLOSE", _controls.transform, new Vector2(280, -728), new Vector2(160, 44), Close);

            _entry.SetActive(false); _panel.SetActive(false);
        }

        private void Update()
        {
            var safe = Screen.safeArea;
            _safe.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            _safe.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            _content.localScale = Vector3.one * Mathf.Min(1, _safe.rect.width / 390, _safe.rect.height / 770);
            bool main = SceneManager.GetActiveScene().name == FightConfig.MainSceneName;
            if (_uid != LeagueClient.Uid) { _uid = LeagueClient.Uid; _allowed = false; _accessChecked = false; }
            if (!Debug.isDebugBuild && !_accessChecked && Time.unscaledTime >= _nextCheck && !string.IsNullOrEmpty(_uid)) CheckAccess();
            _entry.SetActive(main && !_panel.activeSelf && !BotLabScreen.IsOpen && LabAccess.Visible(_allowed));
            if (!main && _panel.activeSelf && !_busy) Close();
            if (!_panel.activeSelf) return;
            LayoutPreview();
            if (_recording) CaptureFrame();
            DrainEncoded();
        }

        private async void CheckAccess()
        {
            _accessChecked = true;
            string requestedUid = _uid;
            try
            {
                var access = await BotRecordingClient.GetAccess();
                if (requestedUid == LeagueClient.Uid) _allowed = access.enabled;
            }
            catch (Exception) { _nextCheck = Time.unscaledTime + 30; _accessChecked = false; }
        }

        // ── Panel ──────────────────────────────────────────────────────────────────────

        private void Open()
        {
            _panel.SetActive(true);
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            try { EmoteCaptureStore.DropUploadedFrames(); } catch (Exception) { }
            Say("Stand the phone at chest height, step back 2–3 m so your whole body is in the frame, " +
                "and perform the gesture after the countdown.");
            Refresh();
            _cameraStart = StartCoroutine(StartCamera());
        }

        private void Close()
        {
            if (_busy) return;
            StopCamera();
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            _panel.SetActive(false);
        }

        private void Refresh()
        {
            _nameLabel.text = "EMOTE: " + Names[_name].ToUpperInvariant();
            _lengthLabel.text = "LENGTH: " + Lengths[_length] + " S";
            _delayLabel.text = "COUNTDOWN: " + Delays[_delay] + " S";
            _cameraLabel.text = _front ? "CAMERA: FRONT" : "CAMERA: BACK";
            _controls.SetActive(!_busy);
            _retry.interactable = PendingUpload() != null;
        }

        private void Cycle(ref int index, int count) { index = (index + 1) % count; Refresh(); }

        private void Say(string text) => _status.text = text;

        private static EmoteCaptureMeta PendingUpload()
        {
            foreach (var meta in EmoteCaptureStore.All())
                if (!meta.uploaded && System.IO.File.Exists(EmoteCaptureStore.ChunkPath(meta.id, 0))) return meta;
            return null;
        }

        // ── Camera ─────────────────────────────────────────────────────────────────────

        private IEnumerator StartCamera()
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam)) { Say("Camera permission denied."); yield break; }
            string device = null;
            foreach (var d in WebCamTexture.devices)
                if (d.isFrontFacing == _front) { device = d.name; break; }
            if (device == null && WebCamTexture.devices.Length > 0) device = WebCamTexture.devices[0].name;
            if (device == null) { Say("No camera found."); yield break; }
            _camera = new WebCamTexture(device, RequestWidth, RequestHeight, RequestFps);
            _camera.Play();
            float start = Time.realtimeSinceStartup;
            yield return new WaitUntil(() => _camera == null || _camera.width > 16 || Time.realtimeSinceStartup - start > 6f);
            if (_camera == null || _camera.width <= 16) { Say("Camera is not starting."); yield break; }
            _preview.texture = _camera; _preview.enabled = true;
        }

        private void StopCamera()
        {
            if (_cameraStart != null) { StopCoroutine(_cameraStart); _cameraStart = null; }
            if (_camera != null) { _camera.Stop(); Destroy(_camera); _camera = null; }
            _preview.texture = null; _preview.enabled = false;
        }

        private void SwitchCamera()
        {
            _front = !_front;
            StopCamera();
            Refresh();
            _cameraStart = StartCoroutine(StartCamera());
        }

        /// <summary>Shows the sensor image upright and, for the front camera, mirrored like a
        /// selfie — the recorded frames are untouched.</summary>
        private void LayoutPreview()
        {
            if (_camera == null || _camera.width <= 16) return;
            int angle = _camera.videoRotationAngle;
            bool sideways = angle % 180 != 0;
            float w = _camera.width, h = _camera.height;
            float shownAspect = sideways ? h / w : w / h;          // upright width / height
            var box = new Vector2(300, 400);
            float height = Mathf.Min(box.y, box.x / shownAspect), width = height * shownAspect;
            var rect = _preview.rectTransform;
            rect.sizeDelta = sideways ? new Vector2(height, width) : new Vector2(width, height);
            rect.localEulerAngles = new Vector3(0, 0, -angle);
            bool selfie = _front;
            rect.localScale = new Vector3(selfie ? -1 : 1, _camera.videoVerticallyMirrored ? -1 : 1, 1);
        }

        // ── Recording ──────────────────────────────────────────────────────────────────

        private void Record()
        {
            if (_busy || _camera == null || _camera.width <= 16) return;
            StartCoroutine(RecordFlow());
        }

        private IEnumerator RecordFlow()
        {
            _busy = true; Refresh();
            int seconds = Lengths[_length];
            Say("Step back. Recording starts after the countdown.");
            for (int s = Delays[_delay]; s > 0; s--)
            {
                _big.text = s.ToString(); _big.color = Color.white;
                GameAudio.Play(SoundCue.Countdown, s <= 3 ? 1.25f : 1f);
                yield return new WaitForSecondsRealtime(1f);
            }

            var meta = new EmoteCaptureMeta
            {
                id = Guid.NewGuid().ToString("N"), name = Names[_name], device = SystemInfo.deviceModel,
                createdUtc = DateTime.UtcNow.ToString("o"), width = _camera.width, height = _camera.height,
                rotation = ((_camera.videoRotationAngle % 360) + 360) % 360,
                verticallyMirrored = _camera.videoVerticallyMirrored, frontFacing = _front,
            };
            _writer = new EmoteCaptureStore.Writer(meta.id);
            lock (_lock)
            {
                _encoded.Clear();
                _buffers.Clear();
                for (int i = 0; i < EncoderBuffers; i++) _buffers.Push(new Color32[meta.width * meta.height]);
            }
            _sequence = _nextToWrite = _dropped = 0; _pending = 0;
            _recordStart = Time.realtimeSinceStartup; _lastFrame = -1f;
            _recording = true;
            _frame.color = new Color(.9f, .1f, .12f);
            GameAudio.Play(SoundCue.Confirm);
            while (Time.realtimeSinceStartup - _recordStart < seconds)
            {
                _big.text = Mathf.CeilToInt(seconds - (Time.realtimeSinceStartup - _recordStart)).ToString();
                _big.color = new Color(1f, .35f, .35f);
                yield return null;
            }
            _recording = false;
            meta.seconds = Time.realtimeSinceStartup - _recordStart;
            _frame.color = new Color(.2f, .25f, .5f);
            _big.text = ""; GameAudio.Play(SoundCue.RewardComplete);

            Say("Saving the take…");
            float wait = Time.realtimeSinceStartup;
            while (Volatile.Read(ref _pending) > 0 && Time.realtimeSinceStartup - wait < 10f) yield return null;
            DrainEncoded();
            _writer.Flush();
            meta.frames = _writer.Frames; meta.chunks = _writer.Chunks;
            meta.fps = meta.frames / Mathf.Max(.1f, meta.seconds);
            _writer = null;
            lock (_lock) { _buffers.Clear(); }
            if (meta.frames < 10)
            {
                Say("The camera delivered too few frames. Try again.");
                _busy = false; Refresh(); yield break;
            }
            EmoteCaptureStore.Save(meta);
            yield return Upload(meta, $"{meta.frames} frames, {meta.fps:0} fps" + (_dropped > 0 ? $", {_dropped} skipped" : ""));
        }

        /// <summary>Takes the camera's newest frame, at most <see cref="MaxFps"/> a second, and hands
        /// it to a worker to encode. With every buffer busy the frame is skipped rather than
        /// stalling the camera — the capture times make a skipped frame harmless.</summary>
        private void CaptureFrame()
        {
            if (_camera == null || !_camera.didUpdateThisFrame) return;
            float t = Time.realtimeSinceStartup - _recordStart;
            if (t - _lastFrame < 1f / (MaxFps + 1f)) return;
            Color32[] buffer;
            lock (_lock) { buffer = _buffers.Count > 0 ? _buffers.Pop() : null; }
            if (buffer == null) { _dropped++; return; }
            _lastFrame = t;
            _camera.GetPixels32(buffer);
            int sequence = _sequence++;
            uint width = (uint)_camera.width, height = (uint)_camera.height;
            Interlocked.Increment(ref _pending);
            Task.Run(() =>
            {
                byte[] jpeg = null;
                try { jpeg = ImageConversion.EncodeArrayToJPG(buffer, GraphicsFormat.R8G8B8A8_UNorm, width, height, 0, JpegQuality); }
                catch (Exception) { }
                lock (_lock)
                {
                    _encoded[sequence] = (t, jpeg);
                    _buffers.Push(buffer);
                }
                Interlocked.Decrement(ref _pending);
            });
        }

        /// <summary>Writes encoded frames in capture order as they come back from the workers.</summary>
        private void DrainEncoded()
        {
            if (_writer == null) return;
            lock (_lock)
            {
                while (_encoded.TryGetValue(_nextToWrite, out var frame))
                {
                    _encoded.Remove(_nextToWrite++);
                    if (frame.jpeg != null) _writer.Add(frame.seconds, frame.jpeg);
                }
            }
        }

        private void RetryUpload()
        {
            var meta = PendingUpload();
            if (_busy || meta == null) return;
            _busy = true; Refresh();
            StartCoroutine(Upload(meta, "saved take"));
        }

        private IEnumerator Upload(EmoteCaptureMeta meta, string info)
        {
            Say($"UPLOADING · {info}\nThe take is saved on the device.");
            var task = EmoteCaptureClient.Upload(meta, (done, total) =>
                Say($"UPLOADING {done}/{total} · {info}\nThe take is saved on the device."));
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted || task.IsCanceled)
            {
                string reason = task.Exception?.GetBaseException().Message ?? "cancelled";
                Say("SAVED ON DEVICE · CLOUD SAVE PENDING\n" + reason);
            }
            else
            {
                Say($"SAVED IN CLOUD · {meta.name.ToUpperInvariant()} · take {meta.id.Substring(0, 8)}\n" +
                    $"{info}. Process it on the desktop.");
                try { EmoteCaptureStore.DropUploadedFrames(); } catch (Exception) { }
            }
            _busy = false; Refresh();
        }

        private void OnDestroy()
        {
            StopCamera();
            if (_instance == this) _instance = null;
        }

        // ── UI helpers (same construction as BotLabScreen) ─────────────────────────────

        private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 pos, Vector2 size)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false); rt.anchorMin = min; rt.anchorMax = max;
            rt.pivot = new Vector2(.5f, .5f); rt.sizeDelta = size; rt.anchoredPosition = pos; return rt;
        }

        private static TMP_Text Label(string text, Transform parent, float y, float height, int size)
        {
            var rect = Rect("Label", parent, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, y), new Vector2(-36, height));
            rect.pivot = new Vector2(.5f, 1);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = TMP_Settings.defaultFontAsset;
            label.text = text; label.fontSize = size; label.color = Color.white;
            label.alignment = TextAlignmentOptions.TopLeft; label.raycastTarget = false;
            return label;
        }

        private static Button Button(string text, Transform parent, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(text.Length > 0 ? text : "Option", parent, new Vector2(.5f, 1), new Vector2(.5f, 1),
                new Vector2(position.x - 195, position.y), size);
            rect.gameObject.AddComponent<Image>().color = new Color(.20f, .25f, .50f);
            var button = rect.gameObject.AddComponent<Button>(); button.onClick.AddListener(action);
            var label = Label(text, rect, 0, size.y, 15);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(6, 0); label.rectTransform.offsetMax = new Vector2(-6, 0);
            label.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static TMP_Text Text(Button button) => button.GetComponentInChildren<TMP_Text>();
    }
}
