using System;
using System.Collections;
using System.Net.Mail;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Firebase.Auth;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>
    /// Account flows for Settings. Apple and Google go straight to the system window and link to
    /// the current guest; the bottom sheet only asks when the account already belongs to another
    /// profile, confirms sign-out, and hosts the email form. Built on the existing settings root
    /// so authored scenes need no regeneration.
    /// </summary>
    public sealed class SettingsAuthDialog : MonoBehaviour
    {
        private const string MainSceneName = "Main";
        private const string BootSceneName = "Boot";

        private ProfileSettingsActions _owner;
        private GameObject _root;
        private RectTransform _card, _safe;
        private TMP_FontAsset _font;
        private AuthDialogStyle _style;
        private CanvasGroup _group;
        private Image _providerBadge;
        private Sprite _appleBadge, _googleBadge, _emailBadge;
        private Sprite _primaryPlate, _secondaryPlate;
        private TextMeshProUGUI _emailCaption, _passwordCaption;
        private Button _handle;
        private float _sheetHeight, _shown, _target;
        private Coroutine _transition;
        private bool _hasStatus;
        private TextMeshProUGUI _title, _detail, _status, _primaryLabel;
        private TMP_InputField _email, _password;
        private Button _primary, _reset, _cancel;
        private bool _emailForm, _busy;
        private Func<Task> _confirm;
        private Action _back;
        public bool IsBusy => _busy;

        public void Build(ProfileSettingsActions owner)
        {
            _owner = owner;
            _style = Resources.Load<AuthDialogStyle>("AuthDialogStyle");
            if (_style == null) throw new InvalidOperationException("AuthDialogStyle resource is missing.");
            _font = _style.Regular;
            _primaryPlate = SlicePlate(_style.Primary);
            _secondaryPlate = SlicePlate(_style.Secondary);
            _appleBadge = owner.Apple.image.sprite;
            _googleBadge = owner.Google.image.sprite;
            _emailBadge = owner.Email.image.sprite;
            var root = Rect(transform, "AccountDialog", 0, 0, 0, 0);
            Stretch(root);
            _root = root.gameObject;
            _group = root.gameObject.AddComponent<CanvasGroup>();
            var dimmer = Rect(root, "Dimmer", 0, 0, 0, 0);
            Stretch(dimmer);
            var dim = dimmer.gameObject.AddComponent<Image>();
            dim.color = new Color(0, 0, .03f, .65f);
            var backdrop = dimmer.gameObject.AddComponent<Button>();
            backdrop.transition = Selectable.Transition.None;
            backdrop.onClick.AddListener(Close);
            _safe = Rect(root, "SafeArea", 0, 0, 0, 0);
            Stretch(_safe);
            _safe.gameObject.AddComponent<SafeAreaFitter>();
            _card = Rect(_safe, "Card", 0, 0, 390, 400);
            _card.anchorMin = _card.anchorMax = _card.pivot = new Vector2(.5f, 0);
            var background = _card.gameObject.AddComponent<Image>();
            background.sprite = _style.Sheet;
            var border = _card.gameObject.AddComponent<Outline>();
            border.effectColor = new Color32(59, 169, 255, 255);
            border.effectDistance = new Vector2(2, -2);
            var pattern = Rect(_card, "Pattern", 3, 42, 384, 524);
            pattern.anchorMin = Vector2.zero; pattern.anchorMax = Vector2.one;
            pattern.offsetMin = new Vector2(3, 0); pattern.offsetMax = new Vector2(-3, -42);
            pattern.gameObject.AddComponent<RectMask2D>();
            for (int row = 0; row < 6; row++) for (int col = 0; col < 4; col++)
            {
                var gear = Rect(pattern, "Gear", -28 + col * 124 + (row % 2) * 55, row * 110, 82, 82);
                gear.localRotation = Quaternion.Euler(0, 0, row * 16 + col * 12);
                var image = gear.gameObject.AddComponent<Image>();
                image.sprite = _style.Pattern; image.color = new Color(.5f, .7f, 1f, .012f);
                image.raycastTarget = false;
            }
            var handle = Rect(_card, "CloseHandle", 132, 0, 126, 36);
            handle.gameObject.AddComponent<Image>().color = Color.clear;
            _handle = handle.gameObject.AddComponent<Button>();
            _handle.onClick.AddListener(Close);
            _title = Text(_card, "Title", 20, 47, 350, 36, 25, true);
            _detail = Text(_card, "Detail", 30, 152, 330, 60, 15);
            _detail.color = new Color32(193, 221, 255, 255);
            _providerBadge = Rect(_card, "ProviderBadge", 147, 92, 96, 52).gameObject.AddComponent<Image>();
            _providerBadge.preserveAspect = true; _providerBadge.raycastTarget = false;
            _emailCaption = Text(_card, "EmailCaption", 28, 96, 330, 22, 14);
            _emailCaption.text = "Email address"; _emailCaption.alignment = TextAlignmentOptions.MidlineLeft;
            _passwordCaption = Text(_card, "PasswordCaption", 28, 176, 330, 22, 14);
            _passwordCaption.text = "Password"; _passwordCaption.alignment = TextAlignmentOptions.MidlineLeft;
            _email = Input("EmailAddress", "you@example.com", 121, false);
            _password = Input("Password", "Enter password", 201, true);
            _status = Text(_card, "Status", 28, 258, 334, 44, 14);
            _status.color = new Color32(255, 235, 160, 255);
            _primary = ActionButton("Continue", 262, out _primaryLabel, 226, 82, 55, true);
            _reset = ActionButton("ResetPassword", 330, out var resetLabel, 190, 25, 38);
            resetLabel.text = "Forgot password?";
            _cancel = ActionButton("Cancel", 330, out var cancelLabel, 133, 232, 38);
            cancelLabel.text = "CANCEL";
            _primary.onClick.AddListener(() => (_emailForm ? SubmitEmail(false) : RunConfirm()).Forget());
            _reset.onClick.AddListener(() => SubmitEmail(true).Forget());
            _cancel.onClick.AddListener(Back);
            _root.SetActive(false);
        }

        /// <summary>Apple or Google: system window first, a question only on an account collision.</summary>
        public async UniTaskVoid Connect(string provider)
        {
            if (_busy) return;
#if UNITY_EDITOR || (!UNITY_IOS && !UNITY_ANDROID)
            _owner.ShowNotice("Apple and Google sign-in are available in the mobile app. Use Email here.");
#else
            SetBusy(true);
            // Let the busy state paint before Firebase enters its native dependency check.
            await UniTask.Yield();
            try
            {
                var auth = await FirebaseAuthService.EnsureReadyAsync();
                await UniTask.SwitchToMainThread();
                var switchAccount = await auth.LinkProviderAsync(provider);
                await UniTask.SwitchToMainThread();
                if (this == null) return;
                if (switchAccount == null) Linked(provider);
                else AskSwitch(provider, switchAccount, null);
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                if (this != null && !IsCancel(exception)) _owner.ShowNotice(ErrorMessage(exception));
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                if (this != null) SetBusy(false);
            }
#endif
        }

        public void ShowEmail()
        {
            if (_busy) return;
            _email.text = "";
            _password.text = "";
            ShowEmailForm(true);
        }

        public void AskSignOut(string provider)
        {
            if (_busy) return;
            ShowConfirm(provider, "SIGN OUT?",
                "Your progress stays in your account.\nThis phone starts over as a guest.", "SIGN OUT",
                async () =>
                {
                    // Uploads the latest progress first; offline, this throws and nothing changes.
                    if (ServiceLocator.TryGet<FirebaseAuthService>(out var auth)) await CloudProgressSync.SignOutAsync(auth);
                    await UniTask.SwitchToMainThread();
                    // Boot re-initializes services and starts a fresh guest session.
                    SceneManager.LoadScene(BootSceneName, LoadSceneMode.Single);
                }, null);
        }

        private void AskSwitch(string provider, Func<Task> switchAccount, Action back)
        {
            string detail = provider == "apple.com"
                ? "This Apple ID already has a profile.\nIts saved progress replaces this phone's."
                : provider == "google.com"
                    ? "This Google account already has a profile.\nIts saved progress replaces this phone's."
                    : "This email already has a profile.\nIts saved progress replaces this phone's.";
            ShowConfirm(provider, "SWITCH ACCOUNT?", detail, "SWITCH", async () =>
            {
                await switchAccount();
                await UniTask.SwitchToMainThread();
                // The account's cloud copy replaces the guest's save before Main reads it. Offline,
                // the phone keeps the guest's save and the next Main load tries again.
                try { await CloudProgressSync.ReconcileAsync(force: true, reloadMain: false); }
                catch (Exception e) { Debug.LogWarning($"[Settings] Progress restore deferred: {e.Message}"); }
                await UniTask.SwitchToMainThread();
                // Rebind repositories/presenters to the chosen UID; do not reuse the guest view.
                SceneManager.LoadScene(MainSceneName, LoadSceneMode.Single);
            }, back);
        }

        private void Linked(string provider)
        {
            _owner.RefreshAccount();
            _owner.ShowNotice(provider == "apple.com" ? "Signed in with Apple. Progress is saved to your account."
                : provider == "google.com" ? "Signed in with Google. Progress is saved to your account."
                : "Signed in with email. Progress is saved to your account.");
            // Same UID as the guest: its progress becomes the account's first cloud copy.
            SaveLinkedProgress().Forget();
        }

        private static async UniTaskVoid SaveLinkedProgress()
        {
            try { await CloudProgressSync.ReconcileAsync(force: true); }
            catch (Exception e) { Debug.LogWarning($"[Settings] Progress upload deferred: {e.Message}"); }
        }

        private void ShowEmailForm(bool open)
        {
            _emailForm = true;
            _confirm = null;
            _back = null;
            _title.text = "EMAIL";
            _primaryLabel.text = "CONTINUE";
            _status.text = "";
            SetMode();
            if (open) Open();
        }

        private void ShowConfirm(string provider, string title, string detail, string action, Func<Task> confirm, Action back)
        {
            bool opening = !_root.activeSelf || _target < 1; // closed, or still sliding away
            _emailForm = false;
            _confirm = confirm;
            _back = back;
            _providerBadge.sprite = provider == "apple.com" ? _appleBadge : provider == "google.com" ? _googleBadge : _emailBadge;
            _title.text = title;
            _detail.text = detail;
            _primaryLabel.text = action;
            _status.text = "";
            SetMode();
            if (opening) Open();
        }

        private void SetMode()
        {
            _email.gameObject.SetActive(_emailForm);
            _password.gameObject.SetActive(_emailForm);
            _emailCaption.gameObject.SetActive(_emailForm);
            _passwordCaption.gameObject.SetActive(_emailForm);
            _reset.gameObject.SetActive(_emailForm);
            _providerBadge.gameObject.SetActive(!_emailForm);
            _detail.gameObject.SetActive(!_emailForm);
            LayoutContent();
        }

        private void Open()
        {
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            _shown = 0;
            FitSheet();
            Animate(1);
        }

        private void LayoutContent()
        {
            _hasStatus = !string.IsNullOrEmpty(_status.text);
            float messageSpace = _hasStatus ? 46 : 0;
            _status.gameObject.SetActive(_hasStatus);
            _sheetHeight = (_emailForm ? 388 : 346) + messageSpace;
            _card.sizeDelta = new Vector2(390, _sheetHeight);
            Place(_status.rectTransform, 28, _emailForm ? 258 : 214, 334, 44);
            Place((RectTransform)_primary.transform, 82, (_emailForm ? 262 : 220) + messageSpace, 226, 55);
            Place((RectTransform)_reset.transform, 25, 330 + messageSpace, 190, 38);
            Place((RectTransform)_cancel.transform, _emailForm ? 232 : 128, (_emailForm ? 330 : 288) + messageSpace, 133, 38);
        }

        public void Close()
        {
            if (_busy) return;
            _password.text = "";
            _email.text = "";
            _confirm = null;
            _back = null;
            Animate(0);
        }

        private void Back()
        {
            if (_busy) return;
            if (_back != null) _back();
            else Close();
        }

        private async UniTask RunConfirm()
        {
            if (_busy || _confirm == null) return;
            SetBusy(true);
            _status.text = "Connecting...";
            await UniTask.Yield();
            try
            {
                await _confirm();
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                if (this != null) _status.text = IsCancel(exception) ? "" : ErrorMessage(exception);
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                if (this != null) SetBusy(false);
            }
        }

        private async UniTask SubmitEmail(bool reset)
        {
            if (_busy) return;
            string email = _email.text.Trim();
            string password = _password.text;
            string error = ValidateEmail(email, password, true, reset);
            if (error != null) { _status.text = error; return; }
            SetBusy(true);
            _status.text = "Connecting...";
            await UniTask.Yield();
            try
            {
                var auth = await FirebaseAuthService.EnsureReadyAsync();
                await UniTask.SwitchToMainThread();
                if (this == null) return;
                if (reset)
                {
                    await auth.SendPasswordResetAsync(email);
                    await UniTask.SwitchToMainThread();
                    if (this != null) _status.text = "If this email has an account, a password reset link has been sent.";
                    return;
                }
                var switchAccount = await auth.LinkEmailAsync(email, password);
                await UniTask.SwitchToMainThread();
                if (this == null) return;
                if (switchAccount == null)
                {
                    _password.text = "";
                    SetBusy(false);
                    Close();
                    Linked("password");
                }
                else AskSwitch("password", switchAccount, () => ShowEmailForm(false));
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                if (this != null) _status.text = ErrorMessage(exception);
            }
            finally
            {
                password = null;
                await UniTask.SwitchToMainThread();
                if (this != null) SetBusy(false);
            }
        }

        private static bool IsCancel(Exception exception)
        {
            if (exception is AggregateException aggregate) exception = aggregate.GetBaseException();
            return exception is OperationCanceledException
                || FirebaseAuthService.AuthErrorOf(exception) == AuthError.Cancelled;
        }

        public static string ValidateEmail(string email, string password, bool register, bool reset)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email) || new MailAddress(email).Address != email)
                    return "Enter a valid email address.";
            }
            catch (FormatException) { return "Enter a valid email address."; }
            if (!reset && string.IsNullOrEmpty(password)) return "Enter your password.";
            if (!reset && register && password.Length < 6) return "Use a password with at least 6 characters.";
            return null;
        }

        public static string ErrorMessage(Exception exception)
        {
            if (exception is AggregateException aggregate) exception = aggregate.GetBaseException();
            if (exception is OperationCanceledException) return "Sign-in cancelled.";
            var code = FirebaseAuthService.AuthErrorOf(exception);
            if (code.HasValue)
            {
                switch (code.Value)
                {
                    case AuthError.EmailAlreadyInUse:
                    case AuthError.CredentialAlreadyInUse:
                        return "This account already exists. Choose USE EXISTING ACCOUNT to sign in.";
                    case AuthError.AccountExistsWithDifferentCredentials:
                        return "Use the sign-in method already linked to this email.";
                    case AuthError.ProviderAlreadyLinked:
                        return "This sign-in method is already linked to your account.";
                    case AuthError.InvalidEmail: return "Enter a valid email address.";
                    case AuthError.WeakPassword: return "Choose a stronger password.";
                    case AuthError.WrongPassword:
                    case AuthError.UserNotFound:
                    case AuthError.InvalidCredential: return "Email or password is incorrect. Try again or reset your password.";
                    case AuthError.NetworkRequestFailed: return "Check your internet connection and try again.";
                    case AuthError.TooManyRequests: return "Too many attempts. Please try again later.";
                    case AuthError.OperationNotAllowed: return "This sign-in method is currently unavailable.";
                    case AuthError.RequiresRecentLogin: return "Sign in again before changing your account.";
                    case AuthError.UserDisabled: return "This account is disabled. Contact support.";
                    case AuthError.Cancelled: return "Sign-in cancelled.";
                }
            }
            if (exception is PlatformNotSupportedException || exception is TimeoutException || exception is CloudSaveException)
                return exception.Message;
            // Do not expose SDK exception payloads (which can include credentials) in UI/logs.
            return "Sign-in failed. Check your connection and try again.";
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _primary.interactable = _reset.interactable = _cancel.interactable = !busy;
            _handle.interactable = !busy;
            _email.interactable = _password.interactable = !busy;
            _owner.SetAccountInteractable(!busy);
        }

        private void Update()
        {
            if (_root == null || !_root.activeSelf) return;
            if (!_busy && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) Close();
            if (_hasStatus != !string.IsNullOrEmpty(_status.text)) LayoutContent();
            FitSheet();
        }

        private void FitSheet()
        {
            // Keep all fields reachable on small screens and above the mobile keyboard.
            float keyboard = TouchScreenKeyboard.visible ? TouchScreenKeyboard.area.height : 0;
            float covered = Screen.height > 0 ? keyboard / Screen.height * _safe.rect.height : 0;
            float height = Mathf.Max(160, _safe.rect.height - covered - 12);
            float scale = Mathf.Min(_safe.rect.width / 390f, height / _sheetHeight);
            _card.localScale = Vector3.one * Mathf.Max(.1f, scale);
            _card.anchoredPosition = new Vector2(0, covered - _safe.offsetMin.y - (1 - _shown) * (_sheetHeight * scale + 40));
            _group.alpha = _shown;
        }

        private void Animate(float target)
        {
            _target = target;
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(Slide(target));
        }

        private IEnumerator Slide(float target)
        {
            _group.interactable = target > 0;
            float from = _shown;
            for (float elapsed = 0; elapsed < .24f; elapsed += Time.unscaledDeltaTime)
            {
                _shown = Mathf.Lerp(from, target, 1 - Mathf.Pow(1 - elapsed / .24f, 3));
                FitSheet();
                yield return null;
            }
            _shown = target;
            FitSheet();
            if (target == 0) _root.SetActive(false);
            _transition = null;
        }

        private void OnDisable()
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = null;
            _shown = 0;
            if (_password != null) _password.text = "";
            if (_root != null && !_busy) _root.SetActive(false);
        }

        private TMP_InputField Input(string name, string hint, float y, bool secret)
        {
            var rect = Rect(_card, name, 25, y, 340, 48);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = _style.Field; image.type = Image.Type.Sliced;
            image.color = new Color32(233, 245, 255, 255);
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black; outline.effectDistance = new Vector2(2, -2);
            var shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(0, -4);
            var input = rect.gameObject.AddComponent<TMP_InputField>(); input.targetGraphic = image;
            var viewport = Rect(rect, "Viewport", 15, 4, 310, 39);
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Text(viewport, "Text", 0, 0, 310, 39, 18);
            text.color = new Color32(15, 30, 65, 255); text.alignment = TextAlignmentOptions.MidlineLeft;
            var placeholder = Text(viewport, "Placeholder", 0, 0, 310, 39, 16);
            placeholder.text = hint; placeholder.color = new Color32(88, 117, 164, 255); placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            input.textViewport = viewport; input.textComponent = text; input.placeholder = placeholder;
            input.contentType = secret ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.EmailAddress;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterLimit = secret ? 128 : 254;
            input.richText = false;
            return input;
        }

        private Button ActionButton(string name, float y, out TextMeshProUGUI label, float width, float x, float height, bool primary = false)
        {
            var rect = Rect(_card, name, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = primary ? _primaryPlate : _secondaryPlate;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = image.sprite.rect.height / height;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.pressedColor = new Color(.75f, .84f, 1f);
            colors.disabledColor = new Color(.55f, .61f, .72f);
            button.colors = colors;
            label = Text(rect, "Label", 10, -2, width - 20, height - 3, height < 40 ? 15 : primary ? 23 : 18, true);
            label.enableAutoSizing = true; label.fontSizeMin = height < 40 ? 12 : 14;
            label.fontSizeMax = label.fontSize;
            return button;
        }

        private TextMeshProUGUI Text(Transform parent, string name, float x, float y, float width, float height, float size, bool outlined = false)
        {
            var text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = outlined ? _style.Bold : _font;
            text.fontSharedMaterial = outlined ? _style.Outline : _font.material;
            text.fontSize = size; text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.richText = false;
            text.UpdateMeshPadding();
            return text;
        }

        private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }

        private static Sprite SlicePlate(Sprite source)
        {
            // Preserve the original keyline, slanted ends and shadow on wider labels.
            var sprite = Sprite.Create(source.texture, source.rect, new Vector2(.5f, .5f),
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(40, 28, 40, 28));
            sprite.name = source.name + " Auth Slice";
            return sprite;
        }

        private void OnDestroy()
        {
            if (_primaryPlate != null) Destroy(_primaryPlate);
            if (_secondaryPlate != null) Destroy(_secondaryPlate);
        }
    }
}
