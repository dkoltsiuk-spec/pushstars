using System;
using System.IO;
using System.Linq;
using PushStars.Core;
using PushStars.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class FriendDuelValidation
    {
        private const string Output = "output/friend-duel";
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run the isolated preview validation outside Play Mode.");
            ValidateState();
            var scene = EditorSceneManager.OpenPreviewScene(AuthoredScenes.MainPath);
            RenderTexture texture = null, characterTexture = null;
            var old = RenderTexture.active;
            try
            {
                var roots = scene.GetRootGameObjects();
                var c = roots.SelectMany(r => r.GetComponentsInChildren<FriendDuelController>(true)).Single();
                var home = c.transform.parent;
                foreach (Transform panel in home.parent)
                    if (panel.name == "LeaguePanel" || panel.name == "ProfilePanel") panel.gameObject.SetActive(false);
                home.gameObject.SetActive(true);
                var canvas = c.GetComponentInParent<Canvas>().rootCanvas;
                canvas.GetComponent<CanvasScaler>().enabled = false;
                foreach (var mirror in canvas.GetComponentsInChildren<DeviceSimulatorMirrorFix>(true))
                { mirror.enabled = false; mirror.transform.localRotation = Quaternion.identity; }
                canvas.renderMode = RenderMode.WorldSpace; canvas.scaleFactor = 1;
                var root = (RectTransform)canvas.transform;
                root.position = Vector3.zero; root.localScale = Vector3.one; root.sizeDelta = new Vector2(390, 844);
                var camera = new GameObject("FriendValidationCamera").AddComponent<Camera>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.transform.position = new Vector3(0, 0, -50);
                camera.orthographic = true; camera.orthographicSize = 422;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
                texture = new RenderTexture(780, 1688, 24); camera.targetTexture = texture; canvas.worldCamera = camera;
                var stage = roots.SelectMany(r => r.GetComponentsInChildren<CharacterStage>(true)).FirstOrDefault();
                if (stage != null)
                {
                    var so = new SerializedObject(stage);
                    var stageCamera = (Camera)so.FindProperty("_stageCamera").objectReferenceValue;
                    var target = (RawImage)so.FindProperty("_targetImage").objectReferenceValue;
                    characterTexture = new RenderTexture(720, 1280, 24);
                    stageCamera.scene = scene; stageCamera.targetTexture = characterTexture;
                    target.texture = characterTexture; target.color = Color.white; stageCamera.Render();
                }
                c.Initialize(); c.ModeSelector.SendMessage("Start"); c.ExerciseSettings.SendMessage("Start");
                var soloPosition = c.Presentation.PlayerImage.rectTransform.anchoredPosition;
                var soloScale = c.Presentation.PlayerImage.rectTransform.localScale;
                Require(home.Cast<Transform>().Count(t => t.name == "FriendSlot") == 1 && !home.Cast<Transform>().Any(t => t.name == "PlusSlot"), "Exactly one left friend slot");
                var modeBefore = SelectedGameMode.Current;
                Capture(camera, texture, "home");
                c.Slot.onClick.Invoke(); Capture(camera, texture, "invite-menu");
                c.Help.onClick.Invoke(); Capture(camera, texture, "how-to"); c.Primary.onClick.Invoke();
                c.Primary.onClick.Invoke();
                Require(c.Session.State == FriendDuelSession.Phase.Waiting && !c.BattleButton.interactable, "Invite waits; battle disabled");
                Require(c.ModeButton.interactable && c.ExerciseButton.interactable, "Locked mode explains on tap; exercise remains active");
                Capture(camera, texture, "waiting");
                c.Backdrop.onClick.Invoke(); Require(c.HasRoom && !c.IsOpen, "Dismissing does not cancel room");
                c.Open(); c.DemoAction.onClick.Invoke();
                Require(c.Session.State == FriendDuelSession.Phase.Joined && c.BattleButton.interactable, "Joined enables battle");
                Capture(camera, texture, "joined");
                c.Secondary.onClick.Invoke(); Capture(camera, texture, "home-with-friend");
                Require(c.ModeButton.GetComponentsInChildren<Image>(true).All(i => i.material == c.InactiveModeMaterial), "Whole mode artwork is desaturated");
                c.ModeButton.onClick.Invoke();
                Require(!c.ModeSelector.IsOpen && c.ModeHint.GetComponentInChildren<TMPro.TextMeshProUGUI>(true).text == "С друзьями доступна только дуэль PvP", "Locked mode shows explanation instead of mode picker");
                var hintGroup = c.ModeHint.GetComponent<CanvasGroup>(); hintGroup.alpha = 1;
                Capture(camera, texture, "locked-mode-hint"); hintGroup.alpha = 0;
                SelectedGameMode.Current = GameMode.Training;
                c.ExerciseButton.onClick.Invoke();
                Require(c.ExerciseSettings.IsOpen, "Exercise picker opens in friend room despite saved training mode");
                c.ExerciseSettings.SendMessage("OnDisable"); SelectedGameMode.Current = modeBefore;
                Require(c.Presentation.LoadedAvatar != null && c.Presentation.FriendRoot.childCount == 1, "Friend has a separate loaded model");
                Require(c.Presentation.FriendImage.texture != c.Presentation.PlayerImage.texture, "Portraits have independent render targets");
                Require(c.Presentation.PlayerImage.rectTransform.anchoredPosition == soloPosition && c.Presentation.PlayerImage.rectTransform.localScale == soloScale && c.Presentation.FriendImage.rectTransform.anchoredPosition.x < soloPosition.x, "Player keeps original centered pose; friend on left");
                Require(!c.Slot.transform.parent.gameObject.activeSelf, "Letter badge replaced by character");
                Require(c.Presentation.FriendImage.rectTransform.localScale.x < c.Presentation.PlayerImage.rectTransform.localScale.x, "Friend is smaller on the rear plane");
                Require(c.Presentation.FriendShadow.gameObject.activeSelf && c.Presentation.FriendShadow.rectTransform.localPosition.y > c.Presentation.PlayerShadow.rectTransform.localPosition.y, "Both shadows follow feet on separate depth planes");
                Require(Mathf.Abs(c.Presentation.FriendShadow.rectTransform.localPosition.x) < 195, "Friend camera projects its own stage onto the visible floor");
                Require(Mathf.Abs(c.Presentation.FriendCamera.WorldToViewportPoint(c.Presentation.FriendRoot.position).x - .5f) < .1f, "Friend camera frames the friend rather than the owner");
                var captionCorners = new Vector3[4]; c.BattleCaption.rectTransform.GetWorldCorners(captionCorners);
                var battleRect = (RectTransform)c.BattleButton.transform;
                Require(captionCorners.All(v => battleRect.rect.Contains(battleRect.InverseTransformPoint(v))), "Friend name lies inside BATTLE");
                c.FriendAvatarButton.onClick.Invoke(); Require(c.IsOpen, "Tapping friend opens room"); c.Back();
                c.Session.UpdateFriendAppearance("female"); c.Refresh();
                Require(c.Presentation.LoadedAvatar.name == "FriendAvatar_female", "Friend appearance comes from room data");
                Capture(camera, texture, "home-female-friend");
                c.Session.UpdateFriendAppearance("male"); c.Refresh();
                c.Presentation.gameObject.SetActive(false); c.Presentation.gameObject.SetActive(true); c.Refresh();
                Require(c.Presentation.LoadedAvatar != null && c.Presentation.FriendRoot.childCount == 1, "Returning to home restores one friend model");
                Require(c.HandleBattle(), "Private battle intercepts ghost route");
                Require(c.Session.LocalReady && !c.BattleButton.interactable, "First click waits for friend");
                Capture(camera, texture, "ready-check");
                c.DemoAction.onClick.Invoke();
                Require(c.Session.State == FriendDuelSession.Phase.Preparing, "Both required before preparation");
                Capture(camera, texture, "both-ready");
                c.DemoConnection.onClick.Invoke();
                Require(!c.BattleButton.interactable && !c.Session.LocalReady, "Disconnect locks battle and clears ready");
                Capture(camera, texture, "reconnecting");
                c.DemoConnection.onClick.Invoke(); Require(c.Session.State == FriendDuelSession.Phase.Joined, "Reconnect requires fresh ready");
                c.Tertiary.onClick.Invoke(); Capture(camera, texture, "leave-confirmation");
                c.Secondary.onClick.Invoke(); Require(c.HasRoom, "Cancel exit keeps friend");
                c.Tertiary.onClick.Invoke(); c.Primary.onClick.Invoke();
                Require(!c.HasRoom && c.ModeButton.interactable && c.ExerciseButton.interactable && c.BattleButton.interactable, "Exit restores home actions");
                Require(SelectedGameMode.Current == modeBefore, "Room does not overwrite saved mode");
                Require(c.ModeButton.GetComponentsInChildren<Image>(true).All(i => i.material != c.InactiveModeMaterial), "Leaving restores mode artwork color");
                Require(c.Presentation.PlayerImage.rectTransform.anchoredPosition == soloPosition && c.Presentation.PlayerImage.rectTransform.localScale == soloScale, "Leaving restores exact solo pose");
                Require(c.Presentation.LoadedAvatar == null && c.Presentation.FriendImage.texture == null && c.Presentation.FriendRoot.childCount == 0, "Leaving releases friend model and texture");
                Require(!c.Presentation.FriendShadow.gameObject.activeSelf, "Leaving hides friend shadow");
                c.Secondary.onClick.Invoke();
                c.CodeInput.text = "12"; c.Primary.onClick.Invoke(); Require(c.Message.text.Contains("6"), "Incomplete code error");
                c.CodeInput.text = "000000"; c.Primary.onClick.Invoke(); Require(c.Message.text.Contains("истёк"), "Expired code error");
                c.CodeInput.text = "111111"; c.Primary.onClick.Invoke(); Require(c.Message.text.Contains("заполнена"), "Full room error");
                c.CodeInput.text = "736482"; c.Primary.onClick.Invoke(); Require(c.Message.text.Contains("твой"), "Own code error");
                c.CodeInput.text = "482 731"; Capture(camera, texture, "join-code"); c.Primary.onClick.Invoke();
                Require(!c.HasRoom, "Resolve shows host before committing");
                Capture(camera, texture, "confirm-host"); c.Primary.onClick.Invoke();
                Require(c.HasRoom && !c.Session.IsHost && c.Session.FriendName == "ALEX_M", "Guest can join after confirmation");
                root.sizeDelta = new Vector2(320, 568); camera.orthographicSize = 284;
                RenderTexture.active = null;
                texture.Release(); texture.width = 640; texture.height = 1136; texture.Create();
                camera.aspect = 320f / 568f;
                c.SendMessage("Fit"); Capture(camera, texture, "room-small");
                Canvas.ForceUpdateCanvases();
                foreach (var button in c.Overlay.GetComponentsInChildren<Button>())
                {
                    var corners = new Vector3[4]; ((RectTransform)button.transform).GetWorldCorners(corners);
                    Require(corners.All(v => { var p = root.InverseTransformPoint(v); return p.y >= root.rect.yMin - 1 && p.y <= root.rect.yMax + 1 && p.x >= root.rect.xMin - 1 && p.x <= root.rect.xMax + 1; }), "Buttons fit on small screen: " + button.name);
                }
                File.WriteAllText(Output + "/validation.txt", "PASS: state deadlines, two-party readiness, reconnect, code errors, host confirmation, single plus, room actions, exit restoration, small screen fit, saved-mode preservation; independent male/female friend avatars, two-person layout, name inside BATTLE, avatar tap, reopen and render-resource cleanup.\n");
                Debug.Log("[FriendDuel] Validation PASS");
            }
            finally
            {
                RenderTexture.active = old; EditorSceneManager.ClosePreviewScene(scene);
                if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
                if (characterTexture != null) { characterTexture.Release(); Object.DestroyImmediate(characterTexture); }
            }
        }

        private static void ValidateState()
        {
            Require(FriendDuelSession.TryCode(" 012-345 ", out var code) && code == "012345", "Leading zero and paste separators");
            Require(!FriendDuelSession.TryCode("482a731", out _) && !FriendDuelSession.TryCode("4827317", out _), "Invalid characters and length");
            var s = new FriendDuelSession();
            s.Create("012345", 0); s.Ready(1); Require(!s.LocalReady, "Cannot ready without friend");
            s.Tick(300); Require(s.State == FriendDuelSession.Phase.Expired, "Invitation expires");
            s.FriendJoined("LATE"); Require(!s.HasRoom, "Late join cannot revive expired room");
            s.Create("123456", 400); s.FriendJoined("ALEX"); s.SetFriendReady(410); s.Ready(420);
            Require(s.State == FriendDuelSession.Phase.Preparing, "Guest-first readiness works");
            s.Disconnect(421); s.Reconnect(425);
            Require(s.State == FriendDuelSession.Phase.Joined && !s.LocalReady && !s.FriendReady, "Reconnect resets both ready flags");
            s.Ready(430); s.Ready(450); s.Tick(460);
            Require(s.State == FriendDuelSession.Phase.Joined && !s.LocalReady, "Repeated taps do not extend timeout");
            s.Disconnect(470); s.Tick(480); Require(!s.HasRoom, "Reconnect deadline closes room");
            s.Reconnect(481); Require(!s.HasRoom, "Late reconnect cannot revive room");
            s.Reset(); s.Create("000001", 0); s.Disconnect(297); s.Reconnect(301);
            Require(!s.HasRoom, "Reconnect does not renew expired invite");
            s.Join("482731", "ALEX"); s.Ready(500); s.SetFriendReady(531);
            Require(s.State == FriendDuelSession.Phase.Joined && !s.LocalReady && !s.FriendReady, "Late ready event cannot start a fight");
            s.FriendLeft(); Require(!s.HasRoom && s.Notice.Contains("вышел"), "Friend leaving closes room with explanation");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Capture(Camera camera, RenderTexture target, string name)
        {
            foreach (var label in camera.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<TMPro.TextMeshProUGUI>()))
                label.ForceMeshUpdate();
            foreach (var graphic in camera.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Graphic>())) graphic.SetAllDirty();
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); Directory.CreateDirectory(Output); File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG()); }
            finally { Object.DestroyImmediate(image); }
        }
    }
}
