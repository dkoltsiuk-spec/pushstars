using System;
using System.IO;
using PushStars.Fight;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace PushStars.Editor
{
    [InitializeOnLoad]
    public static class PlacementTutorialVideoValidation
    {
        const string Key = "PushStars.PlacementVideoQA", Report = "output/onboarding/video-validation.txt";
        static int _step, _loops;
        static double _due, _deadline;
        static GuidedOnboardingController _guide;
        static GameObject _panel;
        static PlacementTutorialVideo _video;
        static VideoPlayer _player;
        static PlacementTutorialVideoValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    _step = _loops = 0; _due = EditorApplication.timeSinceStartup + 3; _deadline = _due + 50;
                    SessionState.SetBool(Key + ".background", Application.runInBackground);
                    Application.runInBackground = true; EditorApplication.update += Tick;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    EditorApplication.update -= Tick;
                    Application.runInBackground = SessionState.GetBool(Key + ".background", false);
                    SessionState.EraseBool(Key);
                }
            };
        }

        [MenuItem("Tools/Push Stars/Onboarding/Validate Placement Video")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
            Directory.CreateDirectory("output/onboarding");
            File.WriteAllText(Report, "Isolated placement video Play Mode validation; camera permission flow excluded.\n");
            SessionState.SetBool(Key, true); EditorApplication.isPlaying = true;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                if (EditorApplication.timeSinceStartup > _deadline) throw new Exception("Video QA timed out.");
                if (_guide != null && _guide.IsTransitioning) return;
                _due = EditorApplication.timeSinceStartup + .3;
                switch (_step++)
                {
                    case 0: _guide = UnityEngine.Object.FindFirstObjectByType<GuidedOnboardingController>(); _guide.DismissGreeting(); break;
                    case 1: _guide.Advance(); break;
                    case 2:
                        var so = new SerializedObject(_guide);
                        _panel = (GameObject)so.FindProperty("_placement").objectReferenceValue;
                        var bubble = (CanvasGroup)so.FindProperty("_bubble").objectReferenceValue;
                        var placementSprite = (Sprite)so.FindProperty("_placementBubbleSprite").objectReferenceValue;
                        Check(placementSprite != null && bubble.GetComponent<Image>().sprite != placementSprite, "Other steps retain original bubble");
                        bubble.GetComponent<Image>().sprite = placementSprite;
                        var rect = (RectTransform)bubble.transform; rect.sizeDelta = new Vector2(352, 355); rect.anchoredPosition = new Vector2(0, 183);
                        var speech = (TextMeshProUGUI)so.FindProperty("_speech").objectReferenceValue;
                        speech.text = "Place your phone on the floor."; speech.maxVisibleCharacters = int.MaxValue; speech.rectTransform.sizeDelta = new Vector2(320, 30);
                        var button = (Button)so.FindProperty("_action").objectReferenceValue;
                        ((RectTransform)button.transform).anchoredPosition = new Vector2(0, 49);
                        ((TextMeshProUGUI)so.FindProperty("_actionLabel").objectReferenceValue).text = "I'M READY";
                        _panel.SetActive(true); _due += 2; break;
                    case 3:
                        _video = _panel.GetComponentInChildren<PlacementTutorialVideo>(); _player = _video.GetComponent<VideoPlayer>();
                        Check(_player.isPlaying && _player.frame > 0 && _video.GetComponent<RawImage>().texture == _player.texture, "Moving frames rendered");
                        Check(_player.isLooping && _player.audioOutputMode == VideoAudioOutputMode.None, "Silent loop configured");
                        Check(_panel.transform.Find("VideoFrame").GetComponent<Image>().sprite != null && _video.GetComponent<PlacementVideoFrame>() != null, "Supplied frame connected");
                        foreach (var size in new[] { new Vector2Int(390,844), new Vector2Int(320,568), new Vector2Int(430,932) })
                        {
                            GuidedOnboardingValidation.Prepare(size); GuidedOnboardingValidation.Capture("video-placement-" + size.x);
                            var r = _video.GetComponent<RectTransform>().rect; Check(Mathf.Abs(r.width/r.height - 16f/9)<.01f, "16:9 at " + size);
                        }
                        _player.loopPointReached += Looped; _due += 11; break;
                    case 4:
                        Check(_loops > 0 && _player.isPlaying, "Completed a loop"); _player.loopPointReached -= Looped;
                        _video.SendMessage("OnApplicationPause", true); Check(!_player.isPlaying, "Background stops playback");
                        _video.SendMessage("OnApplicationPause", false); _due += 2; break;
                    case 5:
                        Check(_player.isPlaying && _player.frame > 0, "Foreground resumes");
                        _panel.SetActive(false); Check(!_player.isPlaying, "Closing stops playback");
                        _panel.SetActive(true); _due += 2; break;
                    case 6: Check(_player.isPlaying && _player.time < 4, "Reopening starts from beginning"); Finish("PASS"); break;
                }
            }
            catch (Exception e) { Finish("FAIL: " + e); }
        }
        static void Looped(VideoPlayer player) => _loops++;
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); File.AppendAllText(Report, "OK: " + message + "\n"); }
        static void Finish(string message) { EditorApplication.update -= Tick; File.AppendAllText(Report, message + "\n"); EditorApplication.isPlaying = false; }
    }
}
