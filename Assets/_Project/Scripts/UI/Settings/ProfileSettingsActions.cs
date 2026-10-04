using System.Collections;
using PushStars.Core;
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

        private void Start()
        {
            Home.onClick.AddListener(() => { Screen.Hide(); Shell.SwitchTab(TabId.Duel); });
            Ok.onClick.AddListener(Screen.Hide);
            Language.onClick.AddListener(ShowLanguagePicker);
            _authDialog = gameObject.AddComponent<SettingsAuthDialog>();
            _authDialog.Build(this);
            Apple.onClick.AddListener(() => _authDialog.Show("apple.com"));
            Google.onClick.AddListener(() => _authDialog.Show("google.com"));
            Email.onClick.AddListener(() => _authDialog.Show("password"));
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
