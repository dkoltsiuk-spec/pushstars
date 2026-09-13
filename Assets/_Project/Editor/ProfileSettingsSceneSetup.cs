using System;
using System.IO;
using System.Linq;
using PushStars.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PushStars.Editor
{
    public static class ProfileSettingsSceneSetup
    {
        private const string ArtPath = "Assets/_Project/UI/Sprites/ProfileSettings/";
        private static TMP_FontAsset _bold, _regular;
        private static Material _outline;
        private static Sprite _round;
        private static readonly Color Blue = new Color32(26, 71, 211, 255);
        private static readonly Color Card = new Color32(30, 56, 133, 255);
        private static readonly Color Muted = new Color32(181, 199, 248, 255);

        [MenuItem("Push Stars/UI/Build Profile and Settings")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/_Project/Scenes/Main.unity") throw new InvalidOperationException("Open the Main scene first.");
            var shell = Find<MainShellView>(scene);
            var settings = Find<SettingsScreen>(scene);
            if (shell == null || settings == null) throw new InvalidOperationException("Main navigation is missing.");
            if (Find<ProfileDashboard>(scene) != null) throw new InvalidOperationException("Profile is already authored. Edit the existing objects.");
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("Library/ProfileSettingsBackup");
            File.Copy(scene.path, "Library/ProfileSettingsBackup/Main-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Build profile and settings screens");
            ImportArt();
            _bold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik Bold TMP.asset");
            _regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/UI/Fonts/Rubik TMP.asset");
            _round = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/pill_12.png");
            string materialPath = ArtPath + "ProfileOutline.mat";
            _outline = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (_outline == null)
            {
                _outline = new Material(_bold.material) { name = "ProfileOutline" };
                _outline.SetColor("_OutlineColor", Color.black);
                _outline.SetFloat("_OutlineWidth", .23f);
                _outline.SetFloat("_FaceDilate", .08f);
                _outline.DisableKeyword("UNDERLAY_ON");
                _outline.DisableKeyword("UNDERLAY_INNER");
                ShaderUtilities.UpdateShaderRatios(_outline);
                AssetDatabase.CreateAsset(_outline, materialPath);
            }
            var shellSo = new SerializedObject(shell);
            var profile = (GameObject)shellSo.FindProperty("_profilePanel").objectReferenceValue;
            var settingsSo = new SerializedObject(settings);
            var oldOverlay = (GameObject)settingsSo.FindProperty("_overlay").objectReferenceValue;
            var oldAvatar = profile.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.name == "Avatar");
            var avatar = oldAvatar != null ? oldAvatar.sprite : null;
            var legacy = Rect(profile.transform, "LegacyProfile", 0, 0, 390, 690);
            foreach (var child in profile.transform.Cast<Transform>().Where(t => t != legacy).ToArray())
                Undo.SetTransformParent(child, legacy, "Keep old profile layout");
            legacy.gameObject.SetActive(false);
            var presenter = profile.GetComponent<ProfilePresenter>();
            if (presenter != null) { Undo.RecordObject(presenter, "Disable old presenter"); presenter.enabled = false; }
            var placeholder = profile.GetComponent<ProfileView>();
            if (placeholder != null) { Undo.RecordObject(placeholder, "Disable placeholder"); placeholder.enabled = false; }
            Undo.RecordObject(oldOverlay, "Keep old settings layout");
            oldOverlay.name = "LegacySettingsOverlay";
            oldOverlay.SetActive(false);
            var overlay = Full(oldOverlay.transform.parent, "SettingsOverlay");
            Background(overlay);
            var actions = overlay.gameObject.AddComponent<ProfileSettingsActions>();
            actions.Screen = settings;
            actions.Shell = shell;
            var gear = BuildProfile(profile, avatar, overlay.gameObject);
            BuildSettings(overlay, settings, actions, gear);
            profile.SetActive(true);
            ((GameObject)shellSo.FindProperty("_duelPanel").objectReferenceValue).SetActive(false);
            ((GameObject)shellSo.FindProperty("_leaguePanel").objectReferenceValue).SetActive(false);
            overlay.gameObject.SetActive(false);
            EditorUtility.SetDirty(settings);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = profile;
            Debug.Log("[ProfileSettings] Authored profile and settings. Original scene backed up in Library/ProfileSettingsBackup.");
        }

        private static T Find<T>(Scene s) where T : Component => s.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<T>(true)).FirstOrDefault();

        private static void ImportArt()
        {
            AssetDatabase.Refresh();
            foreach (string path in Directory.GetFiles(ArtPath, "*.png"))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path.Replace('\\', '/'));
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = 1024;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        private static Button BuildProfile(GameObject root, Sprite avatar, GameObject overlay)
        {
            var background = Full(root.transform, "ProfileBackground");
            Background(background);
            var art = Scroll(root.transform, "ProfileScroll", 82, 690);
            var dash = root.AddComponent<ProfileDashboard>();
            var portrait = Pic(art, "Avatar", avatar != null && avatar != Theme().CircleShape ? avatar : S("Group 537"), 20, 22, 74, 74);
            portrait.preserveAspect = true;
            dash.PlayerName = Text(art, "PlayerName", "BEASTCORE", 108, 29, 223, 28, 21);
            dash.PlayerId = Text(art, "PlayerId", "#U347D348", 108, 62, 205, 22, 15, false, Muted);
            var gear = Button(art, "GearButton", S("Group 553"), "", 339, 28, 36, 36);
            gear.image.preserveAspect = true;
            dash.PreviewNote = Rect(art, "EditorPreview", 0, 0, 390, 690).gameObject;
            Text(dash.PreviewNote.transform, "PreviewLabel", "DESIGN PREVIEW / LIVE DATA IN PLAY", 108, 87, 255, 16, 8, false, Muted);
            Panel(art, "XpTrack", new Color32(25, 46, 107, 255), 29, 126, 270, 24);
            var fill = Panel(art, "XpFill", new Color32(255, 216, 92, 255), 29, 127, 269, 22);
            fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = .65f;
            dash.XpFill = fill;
            dash.Xp = Text(art, "Xp", "25/100", 60, 127, 228, 22, 13);
            dash.Xp.alignment = TextAlignmentOptions.Center;
            Pic(art, "LevelBadge", S("Group 532"), 17, 121, 38, 50);
            dash.Level = Text(art, "Level", "<size=9>LVL</size>\n2", 20, 123, 31, 40, 21);
            dash.Level.alignment = TextAlignmentOptions.Center;
            Pic(art, "StatisticsIcon", S("Vector (17)"), 307, 126, 27, 27);
            Pic(art, "FriendsIcon", S("Group 533"), 344, 126, 27, 27);
            Button(art, "Pushups", S("Group 554"), "PUSHUPS", 18, 190, 113, 40, 14);
            var squats = Button(art, "SquatsLocked", S("Group 555"), "SQUATS", 138, 190, 113, 40, 14);
            squats.transition = Selectable.Transition.None; squats.interactable = false;
            var pullups = Button(art, "PullupsLocked", S("Group 555"), "PULLUPS", 258, 190, 113, 40, 14);
            pullups.transition = Selectable.Transition.None; pullups.interactable = false;
            var lockSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/UI/Sprites/TrainingSettings/lock.png");
            Pic(art, "SquatsLock", lockSprite, 238, 184, 18, 22);
            Pic(art, "PullupsLock", lockSprite, 359, 184, 18, 22);
            Panel(art, "ActivityCard", Card, 18, 242, 354, 149);
            dash.Activity = Text(art, "Activity", "54", 31, 247, 95, 45, 38);
            dash.ActivityNote = Text(art, "ActivityNote", "+6 vs last week", 166, 255, 194, 24, 12, false, new Color32(206,255,0,255));
            dash.ActivityNote.alignment = TextAlignmentOptions.Right;
            float[] heights = { 16, 38, 84, 44, 68, 60, 88 };
            dash.Bars = new RectTransform[7];
            for (int i = 0; i < 7; i++)
            {
                var bar = Panel(art, "Bar" + i, Color.black, 29 + i * 48, 382 - heights[i], 41, heights[i]);
                bar.rectTransform.pivot = new Vector2(0, 0);
                bar.rectTransform.anchoredPosition = new Vector2(29 + i * 48, -382);
                var inner = Full(bar.transform, "Fill", 2);
                var image = inner.gameObject.AddComponent<Image>(); image.sprite = _round; image.type = Image.Type.Sliced;
                image.color = i == 5 ? (Color)new Color32(255,215,0,255) : new Color32(0,96,255,255);
                image.raycastTarget = false;
                dash.Bars[i] = bar.rectTransform;
            }
            dash.Wins = Stat(art, "Wins", "23", "Wins", 18);
            dash.WinRate = Stat(art, "WinRate", "61%", "Winrate", 140);
            dash.Reps = Stat(art, "Reps", "621", "Reps", 262);
            dash.Filters = new[] {
                Button(art, "AllFilter", S("Group 556"), "ALL", 18, 494, 67, 39, 15),
                Button(art, "PvpFilter", S("Group 557"), "PVP", 96, 494, 67, 39, 15),
                Button(art, "BossFilter", S("Group 557"), "BOSS", 174, 494, 75, 39, 15) };
            dash.SelectedPlate = S("Group 556");
            dash.IdlePlate = S("Group 557");
            dash.WinIcon = S("Group 535"); dash.LossIcon = S("Group 536");
            dash.History = Rect(art, "History", 18, 545, 354, 145);
            dash.RowTemplate = MatchRow(dash.History, "MatchTemplate", true, 0).gameObject;
            dash.RowTemplate.SetActive(false);
            MatchRow(dash.PreviewNote.transform, "PreviewWin", true, 545).anchoredPosition = new Vector2(18, -545);
            MatchRow(dash.PreviewNote.transform, "PreviewLoss", false, 627).anchoredPosition = new Vector2(18, -627);
            dash.Empty = Text(dash.History, "Empty", "", 0, 6, 354, 70, 18, false, Muted);
            dash.Empty.alignment = TextAlignmentOptions.Center;
            var nav = root.transform.parent.Find("BottomNav/Nav_Profile/Active");
            if (nav != null) dash.NavActive = nav.GetComponent<Image>();
            dash.ProfileActiveSprite = S("Group 537");
            return gear;
        }

        private static TextMeshProUGUI Stat(Transform parent, string name, string value, string label, float x)
        {
            Panel(parent, name + "Card", Card, x, 405, 110, 71);
            var t = Text(parent, name, value, x, 411, 110, 33, 27);
            t.alignment = TextAlignmentOptions.Center;
            var caption = Text(parent, name + "Label", label, x, 445, 110, 23, 16, false);
            caption.alignment = TextAlignmentOptions.Center;
            return t;
        }

        private static RectTransform MatchRow(Transform parent, string name, bool won, float y)
        {
            var shadow = Panel(parent, name, Color.black, 0, y, 354, 75);
            Panel(shadow.transform, "Card", Card, 2, 2, 348, 68);
            Pic(shadow.transform, "Result", S(won ? "Group 535" : "Group 536"), 11, 15, 43, 43);
            Text(shadow.transform, "Opponent", "vs NOX_92", 64, 14, 176, 23, 16);
            Text(shadow.transform, "Detail", "2h  /  60s  /  PUSHUPS", 64, 40, 200, 22, 11, false, Muted);
            var score = Text(shadow.transform, "Score", won ? "18 - 16" : "18 - 21", 255, 13, 90, 28, 23, true,
                won ? (Color)new Color32(207,255,0,255) : new Color32(255,75,30,255));
            score.alignment = TextAlignmentOptions.Right;
            var record = Text(shadow.transform, "Record", won ? "NEW RECORD" : "", 241, 43, 105, 18, 10, false, new Color32(207,255,0,255));
            record.alignment = TextAlignmentOptions.Right;
            return shadow.rectTransform;
        }

        private static void BuildSettings(RectTransform overlay, SettingsScreen settings, ProfileSettingsActions actions, Button gear)
        {
            var safe = Full(overlay, "SafeArea"); safe.gameObject.AddComponent<SafeAreaFitter>();
            var art = Scroll(safe, "SettingsScroll", 82, 635);
            var back = Button(art, "Back", S("Group 549"), "", 20, 24, 57, 44);
            var title = Text(art, "Title", "SETTINGS", 100, 27, 190, 39, 26); title.alignment = TextAlignmentOptions.Center;
            actions.Home = Button(art, "Home", S("Group 550"), "", 314, 24, 57, 44);
            actions.Apple = Button(art, "Apple", S("Group 546 (1)"), "", 20, 108, 100, 54);
            actions.Google = Button(art, "Google", S("Group 547"), "", 145, 108, 100, 54);
            actions.Email = Button(art, "Email", S("Group 548"), "", 270, 108, 100, 54);
            actions.Email.image.preserveAspect = true;
            var providerNames = new[] { "APPLE ID", "GOOGLE", "EMAIL" };
            for (int i = 0; i < 3; i++)
            {
                var label = Text(art, "ProviderLabel" + i, providerNames[i], 20 + i * 125, 169, 100, 22, 16);
                label.alignment = TextAlignmentOptions.Center;
            }
            var sound = Switch(art, "Sound", "MUSIC EFFECTS", "", "Group 538", 235, out var soundThumb);
            var music = Switch(art, "ExternalMusic", "KEEP MY MUSIC", "Game soundtrack off", "Group 539", 291, out var musicThumb);
            var notifications = Switch(art, "Notifications", "NOTIFICATIONS", "", "Group 540", 347, out var notificationThumb);
            actions.ExternalMusic = music;
            actions.Switches = new[] { sound, music, notifications };
            actions.SwitchThumbs = new[] { soundThumb, musicThumb, notificationThumb };
            actions.OnSprite = S("Group 544"); actions.OffSprite = S("Group 545");
            Pic(art, "LanguageIcon", S("Group 541"), 24, 410, 35, 39);
            Text(art, "LanguageTitle", "LANGUAGE", 72, 418, 165, 28, 16);
            actions.Language = Button(art, "Language", S("Group 543"), "ENGLISH", 270, 413, 100, 40, 13);
            actions.LanguageLabel = actions.Language.GetComponentInChildren<TextMeshProUGUI>();
            actions.Privacy = Button(art, "Privacy", S("Group 542"), "Privacy\nPolicy", 24, 477, 105, 56, 15);
            actions.Support = Button(art, "Support", S("Group 542"), "Support", 147, 477, 105, 56, 15);
            var delete = Button(art, "DeleteAccount", null, "<u>Delete account</u>", 24, 551, 147, 29, 15);
            delete.image.color = Color.clear;
            var version = Text(art, "Version", "v" + Application.version, 24, 604, 180, 20, 11, false, Muted);
            var noticePanel = Panel(safe, "Notice", new Color32(12,26,65,250), 0, 0, 330, 66);
            actions.Notice = Text(noticePanel.transform, "Message", "", 10, 6, 310, 54, 14, false);
            var noticeRt = noticePanel.rectTransform;
            noticeRt.anchorMin = noticeRt.anchorMax = new Vector2(.5f, 0);
            noticeRt.pivot = new Vector2(.5f, 0); noticeRt.anchoredPosition = new Vector2(0, 80);
            actions.Notice.alignment = TextAlignmentOptions.Center;
            noticePanel.gameObject.SetActive(false);
            actions.Ok = Button(safe, "Ok", Theme().PlateBattle, "OK", 0, 0, 114, 55, 22);
            var okRt = (RectTransform)actions.Ok.transform;
            okRt.anchorMin = okRt.anchorMax = new Vector2(.5f, 0); okRt.pivot = new Vector2(.5f, 0);
            okRt.anchoredPosition = new Vector2(0, 13);
            var confirm = Full(overlay, "ConfirmDelete");
            var shade = confirm.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, .8f);
            var dialog = Rect(confirm, "Dialog", 0, 0, 330, 235);
            dialog.anchorMin = dialog.anchorMax = new Vector2(.5f, .5f); dialog.pivot = new Vector2(.5f, .5f);
            Panel(dialog, "Body", Card, 0, 0, 330, 235);
            var question = Text(dialog, "Question", "DELETE ACCOUNT?", 20, 22, 290, 36, 23); question.alignment = TextAlignmentOptions.Center;
            var warning = Text(dialog, "Warning", "Your account and online progress will be deleted. This cannot be undone.", 25, 70, 280, 64, 16, false);
            warning.alignment = TextAlignmentOptions.Center;
            var no = Button(dialog, "Cancel", S("Group 543"), "CANCEL", 25, 160, 130, 48, 17);
            var yes = Button(dialog, "Confirm", Theme().PlateBattle, "DELETE", 176, 160, 130, 48, 17);
            confirm.gameObject.SetActive(false);
            var so = new SerializedObject(settings);
            // The opaque settings screen covers the shell; no legacy fade can leave navigation invisible.
            Set(so, "_overlay", overlay.gameObject); Set(so, "_juicy", null);
            Set(so, "_mainContent", null); Set(so, "_mainGroup", null);
            Set(so, "_gearButton", gear); Set(so, "_backButton", back);
            Set(so, "_soundToggle", sound); Set(so, "_vibrationToggle", null); Set(so, "_notificationsToggle", notifications);
            Set(so, "_langRuButton", null); Set(so, "_langEnButton", null); Set(so, "_langRuLabel", null); Set(so, "_langEnLabel", null);
            Set(so, "_privacyButton", null); Set(so, "_termsButton", null); Set(so, "_versionText", version);
            Set(so, "_deleteButton", delete); Set(so, "_confirmDialog", confirm.gameObject);
            Set(so, "_confirmYesButton", yes); Set(so, "_confirmNoButton", no);
            so.ApplyModifiedProperties();
        }

        private static Toggle Switch(Transform art, string name, string label, string detail, string icon, float y, out Image thumb)
        {
            Pic(art, name + "Icon", S(icon), 23, y, 37, 40);
            Text(art, name + "Label", label, 72, y + 4, 205, 27, 15);
            if (detail != "") Text(art, name + "Detail", detail, 72, y + 28, 210, 19, 12, false, Muted);
            var image = Pic(art, name, S("Group 546"), 299, y + 4.5f, 72, 30);
            image.raycastTarget = true;
            var toggle = image.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = image; toggle.isOn = true;
            thumb = Pic(image.transform, "Thumb", S("Group 544"), 0, 0, 42, 40);
            thumb.rectTransform.anchorMin = thumb.rectTransform.anchorMax = new Vector2(.5f, .5f);
            thumb.rectTransform.pivot = new Vector2(.5f, .5f); thumb.rectTransform.anchoredPosition = new Vector2(-15, 0);
            return toggle;
        }

        private static void Background(RectTransform root)
        {
            var image = root.gameObject.AddComponent<Image>(); image.color = Blue; image.raycastTarget = true;
            for (int row = 0; row < 8; row++) for (int col = 0; col < 4; col++)
            {
                var gear = Pic(root, "GearPattern", S("Group 553"), 0, 0, 77, 77);
                gear.color = new Color(.55f, .7f, 1f, .025f);
                var r = gear.rectTransform; r.anchorMin = r.anchorMax = new Vector2((col + .4f + (row % 2) * .35f) / 4, 1f - row / 7f);
                r.pivot = new Vector2(.5f, .5f); r.anchoredPosition = Vector2.zero;
                r.localRotation = Quaternion.Euler(0, 0, row * 13 + col * 21);
            }
        }

        private static RectTransform Scroll(Transform parent, string name, float bottom, float height)
        {
            var root = Full(parent, name); root.offsetMin = new Vector2(0, bottom);
            var scroll = root.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28;
            var viewport = Full(root, "Viewport"); viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear;
            var content = Full(viewport, "Content"); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1); content.sizeDelta = new Vector2(0, height);
            var art = Rect(content, "Art", 0, 0, 390, height);
            art.anchorMin = art.anchorMax = new Vector2(.5f, 1); art.pivot = new Vector2(.5f, 1);
            var fit = content.gameObject.AddComponent<DashboardWidthFit>(); fit.Art = art; fit.DesignHeight = height;
            scroll.viewport = viewport; scroll.content = content;
            return art;
        }

        private static PushStarsTheme Theme() => Resources.Load<PushStarsTheme>("PushStarsTheme");
        private static Sprite S(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(ArtPath + name + ".png");
        private static void Set(SerializedObject so, string key, Object value) { so.FindProperty(key).objectReferenceValue = value; }
        private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go, "Create profile UI");
            go.transform.SetParent(parent, false); go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h); return rt;
        }
        private static RectTransform Full(Transform parent, string name, float inset = 0)
        { var rt = Rect(parent, name, 0, 0, 0, 0); Stretch(rt, inset); return rt; }
        private static void Stretch(RectTransform rt, float inset = 0)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset); }
        private static Image Pic(Transform parent, string name, Sprite sprite, float x, float y, float w, float h)
        { var r = Rect(parent, name, x, y, w, h); var image = r.gameObject.AddComponent<Image>(); image.sprite = sprite; image.raycastTarget = false; return image; }
        private static Image Panel(Transform p, string name, Color color, float x, float y, float w, float h)
        { var image = Pic(p, name, _round, x, y, w, h); image.type = Image.Type.Sliced; image.color = color; return image; }
        private static TextMeshProUGUI Text(Transform p, string name, string text, float x, float y, float w, float h, float size, bool bold = true, Color? color = null)
        {
            var rt = Rect(p, name, x, y, w, h); var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = bold ? _bold : _regular; if (bold) t.fontSharedMaterial = _outline;
            t.text = text; t.fontSize = size; t.color = color ?? Color.white; t.raycastTarget = false;
            t.alignment = TextAlignmentOptions.MidlineLeft; t.enableAutoSizing = true; t.fontSizeMin = size * .8f; t.fontSizeMax = size;
            return t;
        }
        private static Button Button(Transform p, string name, Sprite sprite, string label, float x, float y, float w, float h, float fontSize = 17)
        {
            var image = Pic(p, name, sprite, x, y, w, h); image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            if (label != "") { var t = Text(image.transform, "Label", label, 4, 0, w - 8, h - 4, fontSize); t.alignment = TextAlignmentOptions.Center; }
            return button;
        }
    }
}
