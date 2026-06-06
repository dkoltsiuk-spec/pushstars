using System.Collections;
using Cysharp.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Full-screen Settings overlay (phase 07). Opened by the gear icon in the profile header,
    /// closed by the back button — same toggled-root + main-recede choreography as
    /// <see cref="SearchOpponentController"/>.
    ///
    /// Surfaces store-required preferences: sound, vibration, in-app notifications, language
    /// (RU/EN), Privacy Policy and Terms links, app version, and account deletion (GDPR). Toggles
    /// persist through <see cref="ISettingsStore"/>; deletion goes through
    /// <see cref="FirebaseAuthService.DeleteAccountAsync"/> after an explicit confirmation, then
    /// restarts the app from the Boot scene (which signs in a fresh anonymous account).
    ///
    /// Full localization of all strings is out of scope here — that lands in phase 17.
    /// </summary>
    public class SettingsScreen : MonoBehaviour
    {
        // Replace with the real published URLs before store submission.
        private const string PrivacyUrl = "https://pushstars.app/privacy";
        private const string TermsUrl   = "https://pushstars.app/terms";
        private const string BootSceneName = "Boot";

        [Header("Overlay (toggled root)")]
        [SerializeField] private GameObject  _overlay;
        [SerializeField] private JuicyScreen _juicy; // optional entrance choreography

        [Header("Main screen (recedes behind the overlay)")]
        [SerializeField] private RectTransform _mainContent;
        [SerializeField] private CanvasGroup   _mainGroup;
        [SerializeField] private float _mainRecedeScale = 0.92f;
        [SerializeField] private float _mainRecedeTime  = 0.34f;
        [SerializeField] private float _mainReturnTime  = 0.30f;

        [Header("Open / close")]
        [SerializeField] private Button _gearButton; // profile header — opens
        [SerializeField] private Button _backButton; // header back arrow — closes

        [Header("Toggles")]
        [SerializeField] private Toggle _soundToggle;
        [SerializeField] private Toggle _vibrationToggle;
        [SerializeField] private Toggle _notificationsToggle;

        [Header("Language (RU / EN)")]
        [SerializeField] private Button          _langRuButton;
        [SerializeField] private Button          _langEnButton;
        [SerializeField] private TextMeshProUGUI _langRuLabel;
        [SerializeField] private TextMeshProUGUI _langEnLabel;

        [Header("Legal / info")]
        [SerializeField] private Button          _privacyButton;
        [SerializeField] private Button          _termsButton;
        [SerializeField] private TextMeshProUGUI _versionText;

        [Header("Delete account")]
        [SerializeField] private Button     _deleteButton;
        [SerializeField] private GameObject _confirmDialog;     // hidden until delete is tapped
        [SerializeField] private Button     _confirmYesButton;
        [SerializeField] private Button     _confirmNoButton;

        private static readonly Color LangActive   = new Color(1f, 1f, 1f, 1f);
        private static readonly Color LangInactive = new Color32(136, 136, 170, 255); // TextSecondary

        private readonly ISettingsStore _store = new PlayerPrefsSettingsStore();

        private bool      _open;
        private bool      _deleting;
        private Coroutine _mainAnim;

        // Wire in Start so every Button/Toggle has finished its own Awake (mirrors the duel overlay).
        private void Start()
        {
            if (_gearButton != null) _gearButton.onClick.AddListener(Show);
            if (_backButton != null) _backButton.onClick.AddListener(Hide);

            if (_privacyButton != null) _privacyButton.onClick.AddListener(() => Application.OpenURL(PrivacyUrl));
            if (_termsButton   != null) _termsButton.onClick.AddListener(() => Application.OpenURL(TermsUrl));

            if (_langRuButton != null) _langRuButton.onClick.AddListener(() => SetLanguage(PlayerPrefsSettingsStore.LangRu));
            if (_langEnButton != null) _langEnButton.onClick.AddListener(() => SetLanguage(PlayerPrefsSettingsStore.LangEn));

            if (_deleteButton     != null) _deleteButton.onClick.AddListener(ShowConfirm);
            if (_confirmNoButton  != null) _confirmNoButton.onClick.AddListener(HideConfirm);
            if (_confirmYesButton != null) _confirmYesButton.onClick.AddListener(() => ConfirmDelete().Forget());

            BindToggles();
            LoadFromStore();

            if (_confirmDialog != null) _confirmDialog.SetActive(false);
            if (_overlay != null) _overlay.SetActive(false); // default closed
        }

        private void BindToggles()
        {
            if (_soundToggle != null)
                _soundToggle.onValueChanged.AddListener(v => _store.SoundEnabled = v);
            if (_vibrationToggle != null)
                _vibrationToggle.onValueChanged.AddListener(v => _store.VibrationEnabled = v);
            if (_notificationsToggle != null)
                _notificationsToggle.onValueChanged.AddListener(v => _store.NotificationsEnabled = v);
        }

        // Reflect persisted values without re-triggering the change listeners.
        private void LoadFromStore()
        {
            if (_soundToggle != null)         _soundToggle.SetIsOnWithoutNotify(_store.SoundEnabled);
            if (_vibrationToggle != null)     _vibrationToggle.SetIsOnWithoutNotify(_store.VibrationEnabled);
            if (_notificationsToggle != null) _notificationsToggle.SetIsOnWithoutNotify(_store.NotificationsEnabled);

            RefreshLanguage();

            if (_versionText != null) _versionText.text = $"v{Application.version}";
        }

        private void SetLanguage(string lang)
        {
            _store.Language = lang;
            RefreshLanguage();
            // Live re-localization of every string is phase 17; here we only persist the choice.
        }

        private void RefreshLanguage()
        {
            bool ru = _store.Language != PlayerPrefsSettingsStore.LangEn;
            if (_langRuLabel != null) _langRuLabel.color = ru ? LangActive : LangInactive;
            if (_langEnLabel != null) _langEnLabel.color = ru ? LangInactive : LangActive;
        }

        // ── Open / close ────────────────────────────────────────────────────────────
        public void Show()
        {
            if (_open || _overlay == null) return;
            _open = true;

            LoadFromStore(); // re-sync in case prefs changed elsewhere
            _overlay.SetActive(true);

            if (_juicy != null) _juicy.PlayIn();
            RecedeMain(true);
        }

        public void Hide()
        {
            if (!_open || _overlay == null) return;
            _open = false;

            HideConfirm();

            if (_juicy != null) _juicy.PlayOut(() => { if (_overlay != null) _overlay.SetActive(false); });
            else                _overlay.SetActive(false);

            RecedeMain(false);
        }

        // ── Delete account ──────────────────────────────────────────────────────────
        private void ShowConfirm()
        {
            if (_confirmDialog != null) _confirmDialog.SetActive(true);
        }

        private void HideConfirm()
        {
            if (_confirmDialog != null) _confirmDialog.SetActive(false);
        }

        private async UniTask ConfirmDelete()
        {
            if (_deleting) return;
            _deleting = true;
            if (_confirmYesButton != null) _confirmYesButton.interactable = false;

            try
            {
                if (ServiceLocator.TryGet<FirebaseAuthService>(out var auth))
                {
                    await auth.DeleteAccountAsync();
                    await UniTask.SwitchToMainThread();
                }
                else
                {
                    Debug.LogWarning("[Settings] Auth service unavailable — cannot delete account.");
                }

                // Restart from Boot, which re-initializes services and signs in fresh.
                SceneManager.LoadScene(BootSceneName, LoadSceneMode.Single);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Settings] Account deletion failed: {e}");
                _deleting = false;
                if (_confirmYesButton != null) _confirmYesButton.interactable = true;
                HideConfirm();
            }
        }

        // ── Main-screen recede (shared with SearchOpponentController) ─────────────────
        private void RecedeMain(bool recede)
        {
            if (_mainContent == null && _mainGroup == null) return;
            if (_mainAnim != null) StopCoroutine(_mainAnim);
            _mainAnim = StartCoroutine(RecedeRoutine(recede));
        }

        private IEnumerator RecedeRoutine(bool recede)
        {
            float dur   = recede ? _mainRecedeTime : _mainReturnTime;
            float scale = recede ? _mainRecedeScale : 1f;
            float alpha = recede ? 0f : 1f;

            if (_mainContent != null)
                StartCoroutine(UITween.Scale(_mainContent, _mainContent.localScale,
                                             Vector3.one * scale, dur, 0f, UITween.EaseOutCubic));
            if (_mainGroup != null)
                yield return UITween.Fade(_mainGroup, _mainGroup.alpha, alpha, dur, 0f);
            else
                yield return null;

            _mainAnim = null;
        }

        private void OnDestroy()
        {
            if (_gearButton != null) _gearButton.onClick.RemoveListener(Show);
            if (_backButton != null) _backButton.onClick.RemoveListener(Hide);
        }
    }
}
