using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed class ProfileIdentityEditor : MonoBehaviour
    {
        public TextMeshProUGUI NameLabel, NameShadow;
        public RawImage Avatar;
        public Image ShowcasePortrait;
        public Sprite[] ShowcaseSprites;
        public Button EditName, EditAvatar;
        public bool FollowNameEnd;
        public RectTransform ModalParent;
        public Texture2D[] AvatarTextures;
        public Rect[] AvatarCrops;
        public string[] AvatarNames;
        public Sprite Circle, Rounded, YellowPlate, DarkPlate;
        public TMP_FontAsset Font;
        public TMP_InputField NameInput { get; private set; }
        private GameObject _modal, _nameGroup, _avatarGroup;
        private TextMeshProUGUI _title, _feedback;
        private Image[] _selection;
        private bool _editingAvatar;
        private int _draftAvatar;
        private string _scope;
        private string _positionedName;
        private Vector2 _positionedNameSize;

        private static string Scope
        {
            get
            {
                if (ServiceLocator.TryGet<FirebaseAuthService>(out var auth) && !string.IsNullOrEmpty(auth.Uid))
                    return "profile.identity." + auth.Uid + ".";
                return "profile.identity.local.";
            }
        }

        public static string ResolveName(string fallback) => PlayerPrefs.GetString(Scope + "name",
            PlayerPrefs.GetString("profile.identity.local.name", fallback));

        private void Awake()
        {
            BuildModal();
            EditName.onClick.AddListener(OpenName);
            EditAvatar.onClick.AddListener(OpenAvatar);
            RefreshAvatar();
        }

        private void OnEnable() { RefreshAvatar(); }
        private void OnDisable() { if (_modal != null) _modal.SetActive(false); }
        private void OnDestroy() { if (_modal != null) Destroy(_modal); }

        private void LateUpdate()
        {
            if (_scope != Scope) RefreshAvatar();
            if (NameShadow != null && NameShadow.text != NameLabel.text) NameShadow.text = NameLabel.text;
            if (FollowNameEnd && (_positionedName != NameLabel.text || _positionedNameSize != NameLabel.rectTransform.rect.size || NameLabel.havePropertiesChanged))
                RefreshNameEditPosition();
            if (_modal != null && _modal.activeSelf && Input.GetKeyDown(KeyCode.Escape)) Cancel();
        }

        public void RefreshNameEditPosition()
        {
            if (!FollowNameEnd || NameLabel == null || EditName == null) return;
            NameLabel.ForceMeshUpdate();
            var bounds = NameLabel.textBounds;
            // The 24px gear sits within a 44px touch target, beside the visible glyphs.
            EditName.transform.position = NameLabel.rectTransform.TransformPoint(new Vector3(bounds.max.x - 7f, bounds.max.y + 21f, 0));
            _positionedName = NameLabel.text;
            _positionedNameSize = NameLabel.rectTransform.rect.size;
        }

        private void RefreshAvatar()
        {
            _scope = Scope;
            int index = Mathf.Clamp(PlayerPrefs.GetInt(_scope + "avatar", PlayerPrefs.GetInt("profile.identity.local.avatar", 0)), 0, AvatarTextures.Length - 1);
            Avatar.texture = AvatarTextures[index];
            Avatar.uvRect = AvatarCrops[index];
            if (ShowcasePortrait != null && ShowcaseSprites != null && index < ShowcaseSprites.Length)
                ShowcasePortrait.sprite = ShowcaseSprites[index];
        }

        public void OpenName() => Open(false);
        public void OpenAvatar() => Open(true);
        private void Open(bool avatar)
        {
            _editingAvatar = avatar;
            _scope = Scope;
            _draftAvatar = Mathf.Clamp(PlayerPrefs.GetInt(_scope + "avatar", PlayerPrefs.GetInt("profile.identity.local.avatar", 0)), 0, AvatarTextures.Length - 1);
            NameInput.SetTextWithoutNotify(NameLabel.text);
            _title.text = avatar ? "CHOOSE AVATAR" : "EDIT NAME";
            _nameGroup.SetActive(!avatar);
            _avatarGroup.SetActive(avatar);
            _feedback.text = "Saved on this device";
            _modal.SetActive(true);
            _modal.transform.SetAsLastSibling();
            DrawSelection();
            if (!avatar) { NameInput.Select(); NameInput.ActivateInputField(); }
        }

        public void Cancel() { _modal.SetActive(false); }

        public void Save()
        {
            if (_scope != Scope) { Cancel(); return; }
            if (_editingAvatar)
                PlayerPrefs.SetInt(_scope + "avatar", _draftAvatar);
            else
            {
                string name = NameInput.text.Trim();
                if (name.Length < 2 || name.Length > 18)
                { _feedback.text = "Use 2-18 characters."; return; }
                foreach (char c in name)
                    if (!char.IsLetterOrDigit(c) && c != ' ' && c != '_' && c != '-')
                    { _feedback.text = "Use letters, numbers, spaces, _ or -."; return; }
                PlayerPrefs.SetString(_scope + "name", name);
                NameLabel.text = name;
                NameShadow.text = name;
            }
            PlayerPrefs.Save();
            RefreshAvatar();
            Cancel();
        }

        private void DrawSelection()
        {
            for (int i = 0; i < _selection.Length; i++)
                _selection[i].color = i == _draftAvatar ? new Color32(255, 202, 0, 255) : new Color32(72, 100, 180, 255);
        }

        private void BuildModal()
        {
            var modal = Rect(ModalParent, "ProfileIdentityDialog", Vector2.zero, Vector2.zero);
            modal.anchorMin = Vector2.zero; modal.anchorMax = Vector2.one; modal.offsetMin = modal.offsetMax = Vector2.zero;
            var shade = modal.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, .75f);
            _modal = modal.gameObject;
            var panel = Rect(modal, "Panel", Vector2.zero, new Vector2(342, 370));
            float scale = Mathf.Min(1, Mathf.Max(1, ModalParent.rect.width - 24) / 342f);
            panel.localScale = Vector3.one * scale;
            var image = panel.gameObject.AddComponent<Image>(); image.sprite = Rounded; image.type = Image.Type.Sliced; image.color = new Color32(26, 55, 135, 255);
            _title = Label(panel, "Title", "EDIT NAME", new Vector2(0, 147), new Vector2(310, 38), 25);
            var ng = Rect(panel, "NameEditor", new Vector2(0, 35), new Vector2(298, 115)); _nameGroup = ng.gameObject;
            Label(ng, "Hint", "YOUR PLAYER NAME", new Vector2(0, 37), new Vector2(290, 25), 13);
            var field = Rect(ng, "NameInput", new Vector2(0, -7), new Vector2(294, 49));
            var fieldImage = field.gameObject.AddComponent<Image>(); fieldImage.sprite = Rounded; fieldImage.type = Image.Type.Sliced; fieldImage.color = new Color32(10, 27, 76, 255);
            NameInput = field.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect(field, "Viewport", Vector2.zero, new Vector2(266, 41)); viewport.gameObject.AddComponent<RectMask2D>();
            var text = Label(viewport, "Text", "", Vector2.zero, new Vector2(266, 41), 22);
            text.richText = false;
            NameInput.textViewport = viewport; NameInput.textComponent = text; NameInput.targetGraphic = fieldImage;
            NameInput.characterLimit = 18; NameInput.lineType = TMP_InputField.LineType.SingleLine;
            var ag = Rect(panel, "AvatarPicker", new Vector2(0, 21), new Vector2(304, 190)); _avatarGroup = ag.gameObject;
            _selection = new Image[AvatarTextures.Length];
            for (int i = 0; i < AvatarTextures.Length; i++)
            {
                int index = i;
                Vector2 pos = new Vector2(-76 + (i % 2) * 152, 48 - (i / 2) * 94);
                var tile = Rect(ag, "Avatar" + i, pos, new Vector2(130, 86));
                var border = tile.gameObject.AddComponent<Image>(); border.sprite = Rounded; border.type = Image.Type.Sliced;
                _selection[i] = border;
                var button = tile.gameObject.AddComponent<Button>(); button.targetGraphic = border;
                button.onClick.AddListener(() => { _draftAvatar = index; DrawSelection(); });
                var face = Rect(tile, "Circle", new Vector2(0, 8), new Vector2(60, 60));
                var disk = face.gameObject.AddComponent<Image>(); disk.sprite = Circle; disk.color = new Color32(212, 221, 241, 255); disk.raycastTarget = false;
                face.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                var raw = Rect(face, "Portrait", Vector2.zero, new Vector2(60, 60)).gameObject.AddComponent<RawImage>();
                raw.texture = AvatarTextures[i]; raw.uvRect = AvatarCrops[i]; raw.raycastTarget = false;
                Label(tile, "Caption", AvatarNames[i], new Vector2(0, -31), new Vector2(122, 18), 11);
            }
            _feedback = Label(panel, "Feedback", "Saved on this device", new Vector2(0, -105), new Vector2(306, 35), 13);
            MakeButton(panel, "Cancel", "CANCEL", new Vector2(-79, -153), DarkPlate, Cancel);
            MakeButton(panel, "Save", "SAVE", new Vector2(79, -153), YellowPlate, Save);
            _modal.SetActive(false);
        }

        private void MakeButton(Transform p, string name, string caption, Vector2 pos, Sprite sprite, UnityEngine.Events.UnityAction action)
        {
            var rt = Rect(p, name, pos, new Vector2(135, 45)); var image = rt.gameObject.AddComponent<Image>(); image.sprite = sprite;
            var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = image; b.onClick.AddListener(action);
            Label(rt, "Label", caption, new Vector2(0, 2), new Vector2(125, 37), 19);
        }
        private TextMeshProUGUI Label(Transform parent, string name, string value, Vector2 pos, Vector2 size, float fontSize)
        {
            var t = Rect(parent, name, pos, size).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Font; t.text = value; t.fontSize = fontSize; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
            return t;
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(.5f, .5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size; return rt;
        }
    }
}
