using System;
using System.Linq;
using PushStars.Core;
using PushStars.OTA;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.Fight
{
    /// <summary>Private recording tools; runtime UI needs no scene rebuild. Development builds
    /// show the entry button, release builds require a server-side account allowlist.</summary>
    public sealed class BotLabScreen : MonoBehaviour
    {
        private static BotLabScreen _instance;
        /// <summary>The panel is up — other lab entries stay out from under it.</summary>
        public static bool IsOpen => _instance != null && _instance._panel != null && _instance._panel.activeSelf;
        private GameObject _entry, _panel;
        private RectTransform _safe;
        private RectTransform _content;
        private TMP_Text _status;
        private Button _record, _replay, _upload, _cloud;
        private bool _allowed, _busy, _accessChecked;
        private float _nextCheck;
        private BotRecording _latest;
        private string _uid;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var root = new GameObject("BotLab");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<BotLabScreen>();
            _instance.Build();
        }
        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 31000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(390, 844); scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            _safe = Rect("SafeArea", transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var entry = Button("BOT LAB", _safe, new Vector2(74, -105), new Vector2(112, 36), Open);
            _entry = entry.gameObject;
            _panel = Rect("BotLabPanel", _safe, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
            _panel.AddComponent<Image>().color = new Color(.025f, .035f, .07f, .98f);
            _content = Rect("Content", _panel.transform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(390, 710));
            Label("BOT RECORDING · PRIVATE TEST", _content, -32, 38, 20);
            _status = Label("", _content, -90, 235, 16);
            _record = Button("RECORD A NEW BOT", _content, new Vector2(195, -360), new Vector2(330, 48), Record);
            _replay = Button("FIGHT LOCAL RECORDING", _content, new Vector2(195, -419), new Vector2(330, 48), Replay);
            _upload = Button("RETRY CLOUD SAVE", _content, new Vector2(195, -478), new Vector2(330, 48), Upload);
            _cloud = Button("LOAD CLOUD COPY & FIGHT", _content, new Vector2(195, -537), new Vector2(330, 48), CloudReplay);
            Button("COPY ACCOUNT UID", _content, new Vector2(195, -596), new Vector2(330, 42), () =>
            { GUIUtility.systemCopyBuffer = LeagueClient.Uid ?? ""; BotRecorderFlow.Notice = "Account UID copied."; Refresh(); });
            Button("CLOSE", _content, new Vector2(195, -649), new Vector2(330, 42), () => _panel.SetActive(false));
            _entry.SetActive(false); _panel.SetActive(false);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        private void Update()
        {
            var safe = Screen.safeArea;
            _safe.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            _safe.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            _content.localScale = Vector3.one * Mathf.Min(1, _safe.rect.width / 390, _safe.rect.height / 710);
            bool main = SceneManager.GetActiveScene().name == FightConfig.MainSceneName;
            _entry.SetActive(main && LabAccess.Visible(_allowed));
            if (!main) { _panel.SetActive(false); return; }
            if (_uid != LeagueClient.Uid)
            {
                _uid = LeagueClient.Uid; _allowed = false; _accessChecked = false;
                if (!LabAccess.Visible(false)) _panel.SetActive(false);
            }
            if (!_accessChecked && Time.unscaledTime >= _nextCheck && !string.IsNullOrEmpty(_uid)) CheckAccess();
            if (BotRecorderFlow.ShowOnMain && LabAccess.Visible(_allowed))
            {
                BotRecorderFlow.ShowOnMain = false; Open();
                if (BotRecorderFlow.PendingUpload != null) Upload();
            }
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
            catch { _nextCheck = Time.unscaledTime + 30; _accessChecked = false; }
        }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == FightConfig.MainSceneName && FightRequest.IsBotTest)
            {
                FightRequest.Clear(); BotRecorderFlow.Motion = new GhostMotionClip();
                BotRecorderFlow.Notice = "Test cancelled. Previously saved recordings are safe.";
                BotRecorderFlow.ShowOnMain = true;
            }
        }
        private void Open()
        {
            try { _latest = BotRecordingStore.Latest(); }
            catch (Exception e) { BotRecorderFlow.Notice = "Could not read the saved recording: " + e.Message; }
            _panel.SetActive(true); Refresh();
        }
        private void Refresh()
        {
            string info = _latest == null ? "No recording yet." :
                $"{_latest.displayName} · {_latest.fight.reps} REPS\nID: {_latest.id}\n" +
                (string.IsNullOrEmpty(_latest.cloudPath) ? "DEVICE COPY · CLOUD SAVE PENDING" : "CLOUD SAVE CONFIRMED");
            _status.text = BotRecorderFlow.Notice + "\n\n" + info;
            _record.interactable = !_busy;
            _replay.interactable = !_busy && _latest != null;
            _upload.interactable = !_busy && _latest != null;
            _cloud.interactable = !_busy && _latest != null && !string.IsNullOrEmpty(_latest.cloudPath);
        }
        private void Record()
        {
            if (_busy || !CanLoad()) return;
            BotRecorderFlow.Motion = new GhostMotionClip();
            BotRecorderFlow.PendingUpload = null;
            FightRequest.RecordBot();
            OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
        }
        private bool CanLoad()
        {
            if (Application.CanStreamedLevelBeLoaded(FightConfig.PreparationSceneName) && Application.CanStreamedLevelBeLoaded(FightConfig.FightSceneName)) return true;
            BotRecorderFlow.Notice = "The build is missing FightPreparation or Fight. Rebuild the app."; Refresh(); return false;
        }
        private void Replay()
        {
            if (_busy || _latest == null || !CanLoad()) return;
            try
            {
                var motion = GhostMotionClip.Decode(_latest.fight.motionBase64);
                if (motion == null || !motion.HasPhase(GhostMotionClip.Live)) throw new InvalidOperationException("No recorded animation.");
                FightRequest.ReplayBot(_latest);
                OtaSceneLoader.LoadScene(FightConfig.PreparationSceneName);
            }
            catch (Exception e) { BotRecorderFlow.Notice = e.Message; Refresh(); }
        }
        private async void Upload()
        {
            if (_busy) return;
            var bot = BotRecorderFlow.PendingUpload ?? _latest;
            if (bot == null) return;
            _busy = true; BotRecorderFlow.Notice = "UPLOADING · Device copy is saved."; Refresh();
            try
            {
                await BotRecordingClient.Upload(bot);
                _latest = bot; BotRecorderFlow.PendingUpload = null;
                BotRecorderFlow.Notice = "BOT SAVED IN CLOUD\nUse LOAD CLOUD COPY & FIGHT to verify the uploaded take.";
            }
            catch (Exception e) { BotRecorderFlow.Notice = "SAVED ON DEVICE · CLOUD SAVE PENDING\n" + e.Message; }
            finally { _busy = false; Refresh(); }
        }
        private async void CloudReplay()
        {
            if (_busy || _latest == null) return;
            _busy = true; BotRecorderFlow.Notice = "DOWNLOADING CLOUD COPY…"; Refresh();
            try
            {
                _latest = await BotRecordingClient.Download(_latest.id);
                BotRecordingStore.Save(_latest);
                _busy = false; Replay();
            }
            catch (Exception e) { BotRecorderFlow.Notice = "CLOUD COPY NOT LOADED\n" + e.Message; }
            finally { _busy = false; Refresh(); }
        }
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
            // Fixed width with a centred anchor keeps the controls inside every phone's safe area.
            var rect = Rect(text, parent, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(position.x - 195, position.y), size);
            rect.gameObject.AddComponent<Image>().color = new Color(.20f, .25f, .50f);
            var button = rect.gameObject.AddComponent<Button>(); button.onClick.AddListener(action);
            var label = Label(text, rect, 0, size.y, 16);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(8, 0); label.rectTransform.offsetMax = new Vector2(-8, 0);
            label.alignment = TextAlignmentOptions.Center;
            return button;
        }
        private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; if (_instance == this) _instance = null; }
    }
}
