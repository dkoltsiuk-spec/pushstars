using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    [InitializeOnLoad]
    public static class AvatarCollectionValidation
    {
        private const string Key = "AvatarCollection.Validation";
        private const string Output = "output/avatar-collection";
        private static double _due;
        private static int _step;
        private static GameObject _sonic;
        private static GameObject _robot;
        private static bool _sawAccent, _sawReturn, _sawSecond;
        private static bool _robotAccent, _robotReturn, _robotSecond;
        private static AvatarCardPreview[] _previews;
        private static Vector3[] _heads;
        private static double _liveCheckTime;
        private static bool _liveChecked;

        static AvatarCollectionValidation()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (!SessionState.GetBool(Key, false)) return;
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    Application.runInBackground = true;
                    _due = EditorApplication.timeSinceStartup + 3;
                    _step = -2;
                    _sawAccent = _sawReturn = _sawSecond = false;
                    _robotAccent = _robotReturn = _robotSecond = false;
                    _liveChecked = false;
                    EditorApplication.update += Tick;
                }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    if (SessionState.GetBool(Key + "HadGender", false)) PlayerPrefs.SetInt("character.gender", SessionState.GetInt(Key + "Gender", 0));
                    else PlayerPrefs.DeleteKey("character.gender");
                    if (SessionState.GetBool(Key + "HadHome", false)) PlayerPrefs.SetInt(CharacterRoster.HomeAvatarKey, SessionState.GetInt(Key + "Home", 0));
                    else PlayerPrefs.DeleteKey(CharacterRoster.HomeAvatarKey);
                    PlayerPrefs.Save();
                    SessionState.SetBool(Key, false);
                    if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(Key + "Pass", false) ? 0 : 1);
                }
            };
        }

        [MenuItem("Tools/Push Stars/Validate Avatar Collection")]
        public static void Run()
        {
            AvatarCollectionSceneSetup.Run();
            Directory.CreateDirectory(Output);
            SessionState.SetBool(Key + "HadGender", PlayerPrefs.HasKey("character.gender"));
            SessionState.SetInt(Key + "Gender", PlayerPrefs.GetInt("character.gender"));
            SessionState.SetBool(Key + "HadHome", PlayerPrefs.HasKey(CharacterRoster.HomeAvatarKey));
            SessionState.SetInt(Key + "Home", PlayerPrefs.GetInt(CharacterRoster.HomeAvatarKey));
            SessionState.SetBool(Key + "Pass", false);
            SessionState.SetBool(Key, true);
            EditorApplication.isPlaying = true;
        }

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private static void Tick()
        {
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < _due) return;
            try
            {
                var screen = Object.FindFirstObjectByType<AvatarCollectionScreen>();
                Check(screen != null, "Collection controller missing.");
                if (_step == -2)
                {
                    Check(!screen.IsOpen, "Collection must start closed.");
                    var tap = Object.FindFirstObjectByType<AvatarCollectionTap>();
                    var e = new PointerEventData(EventSystem.current) { position = new Vector2(150, 250), button = PointerEventData.InputButton.Left };
                    tap.OnPointerDown(e); tap.OnBeginDrag(e); tap.OnPointerClick(e);
                    Check(!screen.IsOpen, "Drag incorrectly opened collection.");
                    tap.OnPointerDown(e); tap.OnPointerClick(e);
                    Check(!screen.IsOpen, "Home must depress before collection opens.");
                    _step = -3;
                    _due = EditorApplication.timeSinceStartup + .09;
                    return;
                }
                if (_step == -3)
                {
                    var tap = Object.FindFirstObjectByType<AvatarCollectionTap>();
                    var feedback = tap.GetComponent<UiTactile>();
                    Check(feedback.MotionTarget == screen.Roster.GetComponent<CharacterStage>().AvatarRoot, "Home feedback must affect the 3D avatar.");
                    Check(Mathf.Abs(feedback.PressScale - .98f) < .0001f &&
                        feedback.MotionTarget.localScale.x >= .979f && feedback.MotionTarget.localScale.x < .99f,
                        "Home avatar must depress gently by 2% before navigation.");
                    Check(!screen.IsOpen, "Collection covered the avatar press too early.");
                    _step = -1; _due = EditorApplication.timeSinceStartup + .14;
                    return;
                }
                if (_step == -1)
                {
                    Check(screen.IsOpen, "Home tap did not open collection.");
                    Check(screen.Cards[0].GetComponent<UiTactile>().RevealProgress > screen.Cards[3].GetComponent<UiTactile>().RevealProgress,
                        "Cards must reveal sequentially.");
                    _step = 0; _due = EditorApplication.timeSinceStartup + .6;
                    return;
                }
                if (_step == 0)
                {
                    Check(screen.Cards.Length == 5 && screen.Cards.All(c => c.gameObject.activeInHierarchy), "Expected five cards.");
                    _previews = screen.Overlay.GetComponentsInChildren<AvatarCardPreview>(true).Where(p => !p.FullBody).OrderBy(p => p.Slot).ToArray();
                    Check(_previews.Length == 5 && _previews.All(p => p.IsRendering && p.Image.texture is RenderTexture), "Five live render targets required.");
                    Check(Mathf.Approximately(_previews[1].SubjectOffsetX, 0) &&
                        Mathf.Approximately(_previews[2].SubjectOffsetX, 0),
                        "Female and male cards must use geometric centering without opposing offsets.");
                    var glowRect = (RectTransform)screen.Cards[1].transform.Find("Glow");
                    Check(glowRect.sizeDelta.x >= 188 && glowRect.sizeDelta.y >= 188,
                        "Card glow must be large enough to frame the character.");
                    Check(screen.Cards.All(c => c.transform.Find("LivePortrait").GetSiblingIndex() >
                        c.transform.Find("CleanCard").GetSiblingIndex()),
                        "Live portraits must render above the card surface.");
                    screen.Select(1);
                    Check(screen.Roster.Gender == CharacterGender.Female && screen.Actions[1].text == "SELECTED", "Female selection failed.");
                    screen.Select(2);
                    Check(screen.Roster.Gender == CharacterGender.Male && screen.Actions[2].text == "SELECTED", "Male selection failed.");
                    screen.InfoButtons[0].onClick.Invoke();
                    Check(screen.Roster.Gender == CharacterGender.Male && screen.InfoPanel.activeSelf, "Sonic should preview without equipping.");
                    Check(screen.PreviewPage.Preview.IsRendering && _previews.All(p => !p.IsRendering), "Preview page must render only the full-body model.");
                    Capture(screen, 780, 1688, "preview-sonic.png");
                    screen.PreviewPage.Action.onClick.Invoke();
                    Check(!screen.Roster.IsSonic && !screen.PreviewPage.Action.interactable && screen.Actions[0].text == "$5.99", "Premium Sonic must not equip without ownership.");
                    screen.InfoClose.onClick.Invoke();
                    Check(screen.Grid.gameObject.activeInHierarchy && !screen.PreviewPage.Preview.IsRendering, "Back must restore collection and stop full-body camera.");
                    screen.InfoButtons[3].onClick.Invoke();
                    Check(screen.Roster.Gender == CharacterGender.Male && screen.InfoTitle.text == "ROBOT" &&
                        screen.InfoPanel.activeSelf && screen.Actions[3].text == AvatarCollectionScreen.PriceLabel(3), "Robot should display its unlock price.");
                    Check(screen.Cards[3].transform.Find("Portrait").GetComponent<Image>().sprite != null, "Robot portrait missing.");
                    Check(screen.Cards[3].transform.Find("LockIcon") != null &&
                        screen.Cards[3].GetComponentsInChildren<AvatarCardSurface>(true).All(s => s.Locked),
                        "Locked robot card needs a lock icon and dark surfaces.");
                    var robotPreview = screen.Cards[3].GetComponentInChildren<AvatarCardPreview>(true);
                    Check(Mathf.Approximately(robotPreview.Tint.a, 1f) && Mathf.Abs(robotPreview.Tint.r - .70f) < .001f,
                        "Locked robot portrait must stay dark and opaque after its render target is built.");
                    Check(screen.InfoButtons.All(b => b.image.sprite != null && b.image.preserveAspect),
                        "All info buttons need the supplied underlay without distortion.");
                    Check(screen.InfoButtons.All(b => b.image.rectTransform.rect.width <= 34.01f &&
                        b.GetComponentInChildren<TMPro.TextMeshProUGUI>().rectTransform.anchoredPosition == Vector2.zero),
                        "Info underlays must be compact and their i labels exactly centered.");
                    Capture(screen, 750, 1334, "preview-robot.png");
                    screen.InfoClose.onClick.Invoke();
                    screen.InfoButtons[1].onClick.Invoke();
                    Check(screen.InfoTitle.text == "MADAM ENGRY", "Info button must navigate to the correct preview.");
                    screen.PreviewPage.Action.onClick.Invoke();
                    Check(screen.Roster.Gender == CharacterGender.Female && screen.PreviewPage.ActionText.text == "SELECTED", "Preview selection failed.");
                    screen.PreviewPage.Home.onClick.Invoke();
                    Check(!screen.IsOpen && !screen.PreviewPage.Preview.IsRendering, "Preview Home must close the collection.");
                    screen.Show();
                    screen.Filter(1);
                    Check(screen.Tabs[0].image.sprite == screen.ShortTabOff && screen.Tabs[1].image.sprite == screen.TabOn, "Tab shapes must persist when switching filters.");
                    Check(((RectTransform)screen.Tabs[0].transform).rect.width < ((RectTransform)screen.Tabs[1].transform).rect.width, "ALL must be shorter than the other tabs.");
                    Check(Mathf.Approximately(screen.Tabs[0].transform.localPosition.x + 74,
                        screen.Tabs[1].transform.localPosition.x) &&
                        Mathf.Approximately(screen.Tabs[1].transform.localPosition.x + 106,
                        screen.Tabs[2].transform.localPosition.x) &&
                        Mathf.Approximately(((RectTransform)screen.Tabs[0].transform).anchoredPosition.x, 20),
                        "Tabs must form a compact left-aligned group.");
                    Check(screen.Cards.Count(c => c.gameObject.activeSelf) == 2, "Opened filter failed.");
                    screen.Filter(2);
                    Check(!screen.Empty.gameObject.activeSelf && screen.Cards[0].gameObject.activeSelf &&
                        screen.Cards.Skip(1).All(c => !c.gameObject.activeSelf), "Premium filter must show only Sonic.");
                    Check(_previews[0].IsRendering && _previews.Skip(1).All(p => !p.IsRendering), "Hidden previews must stop rendering.");
                    screen.Home.onClick.Invoke(); Check(!screen.IsOpen, "Home failed.");
                    screen.Show(); screen.Back.onClick.Invoke(); Check(!screen.IsOpen, "Back failed.");
                    Check(_previews.All(p => !p.IsRendering), "Closing collection must stop all previews.");
                    screen.Show();
                    _heads = _previews.Select(p => p.Animator.GetBoneTransform(HumanBodyBones.Head).position).ToArray();
                    _liveCheckTime = EditorApplication.timeSinceStartup + 1.2;
                    Capture(screen, 780, 1688, "collection-portrait.png");
                    Capture(screen, 750, 1334, "collection-short-phone.png");
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Sonic/Sonic.prefab");
                    _sonic = Object.Instantiate(prefab, new Vector3(1000, 0, 0), Quaternion.identity);
                    var robotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Character/Robot/Robot.prefab");
                    _robot = Object.Instantiate(robotPrefab, new Vector3(1010, 0, 0), Quaternion.identity);
                    _due = EditorApplication.timeSinceStartup + .7;
                    _step = 1;
                    SessionState.SetFloat(Key + "Deadline", (float)EditorApplication.timeSinceStartup + 29);
                    return;
                }
                var animator = _sonic.GetComponentInChildren<Animator>();
                if (_step == 1)
                {
                    screen.Tabs[0].GetComponent<UiTactile>().Press();
                    _step = 2; _due = EditorApplication.timeSinceStartup + .1;
                    return;
                }
                if (_step == 2)
                {
                    var press = screen.Tabs[0].GetComponent<UiTactile>();
                    Check(press.transform.localScale.x < .99f, "Button must visibly depress.");
                    press.Release();
                    _step = 3;
                }
                if (!_liveChecked && EditorApplication.timeSinceStartup >= _liveCheckTime)
                {
                    for (int i = 0; i < _previews.Length; i++)
                        Check(Vector3.Distance(_heads[i], _previews[i].Animator.GetBoneTransform(HumanBodyBones.Head).position) > .0001f,
                            "Card " + i + " pose did not animate.");
                    Check(Mathf.Abs(screen.Tabs[0].transform.localScale.x - 1) < .001f, "Released button did not settle.");
                    Capture(screen, 780, 1688, "collection-animated.png");
                    _liveChecked = true;
                }
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.IsName("OffensiveIntro")) { if (_sawReturn) _sawSecond = true; _sawAccent = true; }
                if (_sawAccent && state.IsName("Idle")) _sawReturn = true;
                var robotState = _robot.GetComponentInChildren<Animator>().GetCurrentAnimatorStateInfo(0);
                if (robotState.IsName("OffensiveIntro")) { if (_robotReturn) _robotSecond = true; _robotAccent = true; }
                if (_robotAccent && robotState.IsName("Idle")) _robotReturn = true;
                if (_sawSecond && _robotSecond && _liveChecked)
                {
                    File.WriteAllText(Output + "/validation.txt", "PASS: full-body preview via info buttons; correct model/title; preview selection; back restores collection; home closes both; four live card poses animate; hidden cameras stop; filters; Sonic/Robot preview isolation; phone captures; both accent cycles. Original saved gender restored after Play Mode.\n");
                    Finish(true); return;
                }
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "Deadline", 0))
                    throw new InvalidOperationException($"Animation timing failed: Sonic={_sawAccent}/{_sawReturn}/{_sawSecond}; Robot={_robotAccent}/{_robotReturn}/{_robotSecond}");
            }
            catch (Exception e)
            {
                File.WriteAllText(Output + "/validation.txt", "FAIL: " + e);
                Debug.LogException(e);
                Finish(false);
            }
        }

        private static void Finish(bool pass)
        {
            SessionState.SetBool(Key + "Pass", pass);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }

        internal static void Capture(AvatarCollectionScreen source, int width, int height, string name)
        {
            // Render the actual serialized overlay at a controlled phone size without changing the home canvas.
            var cameraGo = new GameObject("CollectionCaptureCamera");
            var canvasGo = new GameObject("CollectionCaptureCanvas", typeof(RectTransform), typeof(Canvas));
            canvasGo.SetActive(false);
            var rt = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            try
            {
                var camera = cameraGo.AddComponent<Camera>();
                camera.transform.position = new Vector3(5000, 0, -10);
                camera.orthographic = true; camera.orthographicSize = height * .5f;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                camera.targetTexture = rt;
                var canvas = canvasGo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                var copy = Object.Instantiate(source.Overlay, canvasGo.transform);
                foreach (var preview in copy.GetComponentsInChildren<AvatarCardPreview>(true)) preview.enabled = false;
                foreach (var feedback in copy.GetComponentsInChildren<UiTactile>(true))
                {
                    // A deterministic settled-layout capture; timed motion is checked on the live objects.
                    feedback.transform.localScale = Vector3.one;
                    feedback.ResetVisual(); feedback.enabled = false;
                }
                var safe = copy.GetComponentInChildren<SafeAreaFitter>(true);
                if (safe != null) safe.enabled = false;
                var art = (RectTransform)copy.transform.Find(AnimationUtility.CalculateTransformPath(source.Art, source.Overlay.transform));
                var safeRect = (RectTransform)art.parent;
                canvasGo.SetActive(true);
                UiBuilder.Stretch(safeRect, 0, 25, 0, 42);
                Canvas.ForceUpdateCanvases();
                var size = safeRect.rect.size;
                float scale = size.x / 390;
                art.localScale = Vector3.one * scale;
                float heightInUnits = size.y / scale;
                art.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, heightInUnits);
                var grid = (RectTransform)copy.transform.Find(AnimationUtility.CalculateTransformPath(source.Grid, source.Overlay.transform));
                var viewport = (RectTransform)grid.parent;
                viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(1, heightInUnits + viewport.anchoredPosition.y));
                LayoutRebuilder.ForceRebuildLayoutImmediate(art);
                copy.GetComponentInChildren<AvatarPreviewPage>(true).Fit();
                Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
                File.WriteAllBytes(Output + "/" + name, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = old;
                Object.DestroyImmediate(canvasGo); Object.DestroyImmediate(cameraGo);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(texture);
            }
        }
    }
}
