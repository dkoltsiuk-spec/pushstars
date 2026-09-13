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

        private void Start()
        {
            Home.onClick.AddListener(() => { Screen.Hide(); Shell.SwitchTab(TabId.Duel); });
            Ok.onClick.AddListener(Screen.Hide);
            Language.onClick.AddListener(() =>
            {
                _store.Language = _store.Language == "en" ? "ru" : "en";
                RefreshLanguage();
                ShowNotice("Language preference saved. Full translation is not available yet.");
            });
            Apple.onClick.AddListener(() => ShowNotice("Apple sign-in is not configured yet."));
            Google.onClick.AddListener(() => ShowNotice("Google sign-in is not configured yet."));
            Email.onClick.AddListener(() => ShowNotice("Email sign-in is not configured yet."));
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
        }

        private void OnEnable()
        {
            RefreshLanguage();
            for (int i = 0; i < Switches.Length; i++) DrawSwitch(i);
        }

        private void RefreshLanguage() { if (LanguageLabel != null) LanguageLabel.text = _store.Language == "ru" ? "RUSSIAN" : "ENGLISH"; }

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
