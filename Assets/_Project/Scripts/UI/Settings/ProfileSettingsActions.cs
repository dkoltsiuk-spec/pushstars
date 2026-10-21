using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PushStars.UI
{
    public sealed class ProfileSettingsActions : MonoBehaviour
    {
        public SettingsScreen Screen;
        public MainShellView Shell;
        public Button Home, Ok, Language, Apple, Google, Email, Privacy, Support;
        public TextMeshProUGUI LanguageLabel, Notice;
        public Toggle ExternalMusic;
        public Toggle[] Switches;
        public Image[] SwitchThumbs;
        public Sprite OnSprite, OffSprite;
        private readonly ISettingsStore _store = new PlayerPrefsSettingsStore();
        private Coroutine _noticeRoutine;
        private SettingsAuthDialog _authDialog;
        private GameObject _languageDialog;
        private readonly List<GameObject> _guestOnly = new List<GameObject>();
        private GameObject _accountCard;
        private Image _accountIcon;
        private TextMeshProUGUI _accountName, _accountEmail;
        private Button _signOut;
        private string _accountProvider;

        private void Start()
        {
            Home.onClick.AddListener(() => { Screen.Hide(); Shell.SwitchTab(TabId.Duel); });
            Ok.onClick.AddListener(Screen.Hide);
            Language.onClick.AddListener(ShowLanguagePicker);
            _authDialog = gameObject.AddComponent<SettingsAuthDialog>();
            _authDialog.Build(this);
            Apple.onClick.AddListener(() => _authDialog.Connect("apple.com").Forget());
            Google.onClick.AddListener(() => _authDialog.Connect("google.com").Forget());
            Email.onClick.AddListener(_authDialog.ShowEmail);
            BuildAccountCard();
            RefreshAccount();
            Privacy.onClick.AddListener(() => ShowNotice("The published privacy policy link has not been configured yet."));
            Support.onClick.AddListener(() => ShowNotice("Support contact has not been configured yet."));
            ExternalMusic.SetIsOnWithoutNotify(GameAudio.KeepExternalMusic);
            ExternalMusic.onValueChanged.AddListener(value => GameAudio.KeepExternalMusic = value);
            for (int i = 0; i < Switches.Length; i++)
            {
                int index = i;
                Switches[i].onValueChanged.AddListener(value => DrawSwitch(index));
            }
            RefreshLanguage();
            for (int i = 0; i < Switches.Length; i++) DrawSwitch(i);
            Notice.transform.parent.gameObject.SetActive(false);
            // Allow the full privacy label to fit in all supported languages.
            var privacyRect = (RectTransform)Privacy.transform;
            privacyRect.sizeDelta = new Vector2(134, privacyRect.sizeDelta.y);
            var privacyLabel = Privacy.GetComponentInChildren<TMP_Text>().rectTransform;
            privacyLabel.anchorMin = Vector2.zero; privacyLabel.anchorMax = Vector2.one;
            privacyLabel.offsetMin = new Vector2(4, 4); privacyLabel.offsetMax = new Vector2(-4, -4);
            var supportRect = (RectTransform)Support.transform;
            supportRect.anchoredPosition = new Vector2(180, supportRect.anchoredPosition.y);
        }

        private void OnEnable()
        {
            Localization.LanguageChanged += RefreshLanguage;
            RefreshLanguage();
            for (int i = 0; i < Switches.Length; i++) DrawSwitch(i);
            RefreshAccount();
        }

        private void OnDisable()
        {
            Localization.LanguageChanged -= RefreshLanguage;
            if (_languageDialog != null) _languageDialog.SetActive(false);
        }

        private void RefreshLanguage()
        {
            if (LanguageLabel == null) return;
            LanguageLabel.text = Localization.NativeName(_store.Language);
            LanguageLabel.enableAutoSizing = true;
            LanguageLabel.fontSizeMin = 8;
            LanguageLabel.fontSizeMax = 13;
        }

        public void SelectLanguage(string language)
        {
            _store.Language = language;
            RefreshLanguage();
            if (_languageDialog != null) _languageDialog.SetActive(false);
        }

        public void ShowLanguagePicker()
        {
            if (_languageDialog == null) BuildLanguagePicker();
            _languageDialog.SetActive(true);
            _languageDialog.transform.SetAsLastSibling();
            foreach (var button in _languageDialog.GetComponentsInChildren<Button>())
            {
                if (!button.name.StartsWith("LanguageOption_")) continue;
                var label = button.GetComponentInChildren<TMP_Text>();
                label.color = button.name == "LanguageOption_" + _store.Language ? new Color32(255, 216, 92, 255) : Color.white;
            }
        }

        private void BuildLanguagePicker()
        {
            _languageDialog = new GameObject("LanguagePicker", typeof(RectTransform), typeof(Image), typeof(Button));
            var root = (RectTransform)_languageDialog.transform;
            root.SetParent(transform, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
            root.offsetMin = root.offsetMax = Vector2.zero;
            _languageDialog.GetComponent<Image>().color = new Color(0, 0, .03f, .75f);
            _languageDialog.GetComponent<Button>().onClick.AddListener(() => _languageDialog.SetActive(false));
            var card = new GameObject("LanguageCard", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)card.transform;
            rect.SetParent(root, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(300, 290);
            card.GetComponent<Image>().color = new Color32(30, 56, 133, 255);
            var title = Instantiate(LanguageLabel, rect);
            title.name = "LanguageTitle"; title.text = "LANGUAGE";
            title.fontSize = 20; title.fontSizeMax = 20;
            title.alignment = TextAlignmentOptions.Center;
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(.5f, .5f);
            title.rectTransform.pivot = new Vector2(.5f, .5f);
            title.rectTransform.sizeDelta = new Vector2(260, 36);
            title.rectTransform.anchoredPosition = new Vector2(0, 110);
            string[] languages = { PlayerPrefsSettingsStore.LangEn, PlayerPrefsSettingsStore.LangRu, PlayerPrefsSettingsStore.LangPtBr };
            for (int i = 0; i < languages.Length; i++)
            {
                string language = languages[i];
                var button = Instantiate(Language, rect);
                button.name = "LanguageOption_" + language;
                var buttonRect = (RectTransform)button.transform;
                buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(.5f, .5f);
                buttonRect.pivot = new Vector2(.5f, .5f);
                buttonRect.sizeDelta = new Vector2(260, 46);
                FitSlantPlate(button.image, 46);
                buttonRect.anchoredPosition = new Vector2(0, 54 - i * 60);
                var label = button.GetComponentInChildren<TMP_Text>();
                label.name = "NativeLanguageName"; label.text = Localization.NativeName(language);
                label.fontSize = 18; label.fontSizeMax = 18; label.enableAutoSizing = true;
                label.alignment = TextAlignmentOptions.Center;
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(12, 4); label.rectTransform.offsetMax = new Vector2(-12, -4);
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectLanguage(language));
            }
        }

        /// <summary>The slanted blue plate is sliced on its slanted ends (sprite border), scaled to
        /// the button height so the slant keeps its angle and only the straight middle stretches.</summary>
        public static void FitSlantPlate(Image plate, float height)
        {
            if (plate == null || plate.sprite == null) return;
            plate.type = Image.Type.Sliced; plate.preserveAspect = false; plate.fillCenter = true;
            plate.pixelsPerUnitMultiplier = plate.sprite.rect.height / plate.sprite.pixelsPerUnit * 100f / height;
        }

        /// <summary>Guest: the three sign-in buttons. Signed in: one card with the method used,
        /// a check, the account email and SIGN OUT, in the same slot.</summary>
        public void RefreshAccount()
        {
            if (_accountCard == null) return;
            string provider = ServiceLocator.TryGet<FirebaseAuthService>(out var auth) && !auth.IsAnonymous
                ? auth.PrimaryProvider : null;
            bool signedIn = provider != null;
            foreach (var guestOnly in _guestOnly) guestOnly.SetActive(!signedIn);
            _accountCard.SetActive(signedIn);
            if (!signedIn) return;
            _accountProvider = provider;
            _accountIcon.sprite = provider == "apple.com" ? Apple.image.sprite
                : provider == "google.com" ? Google.image.sprite : Email.image.sprite;
            _accountName.text = provider == "apple.com" ? "APPLE ID" : provider == "google.com" ? "GOOGLE" : "EMAIL";
            _accountEmail.text = MaskEmail(auth.Email);
        }

        public void SetAccountInteractable(bool interactable)
        {
            Apple.interactable = Google.interactable = Email.interactable = interactable;
            if (_signOut != null) _signOut.interactable = interactable;
        }

        private void BuildAccountCard()
        {
            var slot = Apple.transform.parent;
            _guestOnly.Add(Apple.gameObject); _guestOnly.Add(Google.gameObject); _guestOnly.Add(Email.gameObject);
            for (int i = 0; i < 3; i++)
            {
                var label = slot.Find("ProviderLabel" + i);
                if (label != null) _guestOnly.Add(label.gameObject);
            }
            var round = Notice.transform.parent.GetComponent<Image>().sprite;
            // Same slot as the provider row and its captions; drop-shadow card like the match rows.
            var card = Rect(slot, "AccountCard", 20, 104, 350, 88);
            var shadow = card.gameObject.AddComponent<Image>();
            shadow.sprite = round; shadow.type = Image.Type.Sliced; shadow.color = Color.black; shadow.raycastTarget = false;
            var face = Rect(card, "Card", 2, 2, 344, 82).gameObject.AddComponent<Image>();
            face.sprite = round; face.type = Image.Type.Sliced; face.color = new Color32(30, 56, 133, 255); face.raycastTarget = false;
            _accountIcon = Rect(card, "Provider", 12, 17, 76, 54).gameObject.AddComponent<Image>();
            _accountIcon.preserveAspect = true; _accountIcon.raycastTarget = false;
            var check = Rect(card, "Check", 98, 18, 24, 24).gameObject.AddComponent<Image>();
            check.sprite = OnSprite; check.preserveAspect = true; check.raycastTarget = false;
            var providerCaption = slot.Find("ProviderLabel1");
            TextMeshProUGUI captionSource = providerCaption != null ? providerCaption.GetComponent<TextMeshProUGUI>() : null;
            if (captionSource == null) captionSource = LanguageLabel;
            _accountName = Instantiate(captionSource, card);
            _accountName.name = "Method";
            Place(_accountName.rectTransform, 128, 17, 110, 26);
            _accountName.alignment = TextAlignmentOptions.MidlineLeft;
            _accountName.enableAutoSizing = true; _accountName.fontSizeMin = 12; _accountName.fontSizeMax = 18;
            _accountEmail = Instantiate(captionSource, card);
            _accountEmail.name = "AccountEmail";
            Place(_accountEmail.rectTransform, 98, 48, 140, 22);
            _accountEmail.font = Notice.font; _accountEmail.fontSharedMaterial = Notice.font.material;
            _accountEmail.color = new Color32(181, 199, 248, 255);
            _accountEmail.alignment = TextAlignmentOptions.MidlineLeft;
            _accountEmail.enableAutoSizing = true; _accountEmail.fontSizeMin = 10; _accountEmail.fontSizeMax = 13;
            _accountEmail.overflowMode = TextOverflowModes.Ellipsis;
            _signOut = Instantiate(Language, card);
            _signOut.name = "SignOut";
            Place((RectTransform)_signOut.transform, 240, 23, 98, 42);
            FitSlantPlate(_signOut.image, 42);
            var signOutLabel = _signOut.GetComponentInChildren<TextMeshProUGUI>();
            signOutLabel.text = "SIGN OUT";
            signOutLabel.enableAutoSizing = true; signOutLabel.fontSizeMin = 10; signOutLabel.fontSizeMax = 14;
            signOutLabel.rectTransform.anchorMin = Vector2.zero; signOutLabel.rectTransform.anchorMax = Vector2.one;
            signOutLabel.rectTransform.offsetMin = new Vector2(8, 4); signOutLabel.rectTransform.offsetMax = new Vector2(-8, -4);
            _signOut.onClick.RemoveAllListeners();
            _signOut.onClick.AddListener(() => _authDialog.AskSignOut(_accountProvider));
            _accountCard = card.gameObject;
            _accountCard.SetActive(false);
        }

        // Apple's private relay addresses are long and meaningless; any address shows as a***@domain.
        private static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return "";
            int at = email.IndexOf('@');
            return at <= 0 ? email : email[0] + "***" + email.Substring(at);
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            Place(rect, x, y, width, height);
            return rect;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }

        private void DrawSwitch(int i)
        {
            bool on = Switches[i].isOn;
            SwitchThumbs[i].sprite = on ? OnSprite : OffSprite;
            SwitchThumbs[i].rectTransform.anchoredPosition = new Vector2(on ? -15 : 15, 0);
        }

        public void ShowNotice(string message)
        {
            if (!isActiveAndEnabled) return;
            if (_noticeRoutine != null) StopCoroutine(_noticeRoutine);
            Notice.text = message;
            Notice.transform.parent.gameObject.SetActive(true);
            _noticeRoutine = StartCoroutine(ClearNotice());
        }

        private IEnumerator ClearNotice() { yield return new WaitForSecondsRealtime(4); Notice.transform.parent.gameObject.SetActive(false); }
    }
}
