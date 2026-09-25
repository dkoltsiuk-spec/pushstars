using System;
using System.Collections;
using System.Net.Mail;
using Cysharp.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using PushStars.Core;
using PushStars.Services;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PushStars.UI
{
    /// <summary>Built on the existing settings root so authored scenes need no regeneration.</summary>
    public sealed class SettingsAuthDialog : MonoBehaviour
    {
        private GameObject _root;
        private RectTransform _card, _safe;
        private TMP_FontAsset _font;
        private AuthDialogStyle _style;
        private CanvasGroup _group;
        private Image _providerBadge;
        private Sprite _appleBadge, _googleBadge;
        private Sprite _primaryPlate, _secondaryPlate;
        private TextMeshProUGUI _emailCaption, _passwordCaption;
        private Button _handle;
        private float _sheetHeight, _shown;
        private Coroutine _transition;
        private bool _hasStatus;
        private TextMeshProUGUI _title, _detail, _status, _primaryLabel, _modeLabel;
        private TMP_InputField _email, _password;
        private Button _primary, _mode, _reset, _cancel;
        private string _provider;
        private bool _register, _busy;
        public bool IsBusy => _busy;

        public void Build(ProfileSettingsActions owner)
        {
            _style = Resources.Load<AuthDialogStyle>("AuthDialogStyle");
            if (_style == null) throw new InvalidOperationException("AuthDialogStyle resource is missing.");
            _font = _style.Regular;
            _primaryPlate = SlicePlate(_style.Primary);
            _secondaryPlate = SlicePlate(_style.Secondary);
            _appleBadge = owner.Apple.image.sprite;
            _googleBadge = owner.Google.image.sprite;
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
            _card = Rect(_safe, "Card", 0, 0, 390, 566);
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
            _detail = Text(_card, "Detail", 30, 95, 330, 52, 15);
            _detail.color = new Color32(193, 221, 255, 255);
            _providerBadge = Rect(_card, "ProviderBadge", 147, 99, 96, 52).gameObject.AddComponent<Image>();
            _providerBadge.preserveAspect = true; _providerBadge.raycastTarget = false;
            _emailCaption = Text(_card, "EmailCaption", 28, 149, 330, 22, 14);
            _emailCaption.text = "Email address"; _emailCaption.alignment = TextAlignmentOptions.MidlineLeft;
            _passwordCaption = Text(_card, "PasswordCaption", 28, 226, 330, 22, 14);
            _passwordCaption.text = "Password"; _passwordCaption.alignment = TextAlignmentOptions.MidlineLeft;
            _email = Input("EmailAddress", "you@example.com", 174, false);
            _password = Input("Password", "Enter password", 251, true);
            _status = Text(_card, "Status", 28, 310, 334, 54, 14);
            _status.color = new Color32(255, 235, 160, 255);
            _primary = ActionButton("Continue", 376, out _primaryLabel, 226, 82, 55, true);
            _mode = ActionButton("Mode", 440, out _modeLabel, 316, 37, 49);
            _reset = ActionButton("ResetPassword", 503, out var resetLabel, 190, 25, 38);
            resetLabel.text = "Forgot password?";
            _cancel = ActionButton("Cancel", 503, out var cancelLabel, 133, 232, 38);
            cancelLabel.text = "CANCEL";
            _primary.onClick.AddListener(() => Run(false).Forget());
            _mode.onClick.AddListener(() => { _register = !_register; Refresh(); });
            _reset.onClick.AddListener(() => Run(true).Forget());
            _cancel.onClick.AddListener(Close);
            _root.SetActive(false);
        }

        public void Show(string provider)
        {
            if (_busy) return;
            _provider = provider;
            _register = false;
            _email.text = "";
            _password.text = "";
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            Refresh();
            _shown = 0;
            FitSheet();
            Animate(1);
        }

        private void Refresh()
        {
            bool email = _provider == "password";
            _title.text = email ? (_register ? "CREATE ACCOUNT" : "EMAIL SIGN IN")
                : (_provider == "apple.com" ? "APPLE ID" : "GOOGLE");
            _email.gameObject.SetActive(email);
            _password.gameObject.SetActive(email);
            _emailCaption.gameObject.SetActive(email);
            _passwordCaption.gameObject.SetActive(email);
            _providerBadge.gameObject.SetActive(!email);
            _providerBadge.sprite = _provider == "apple.com" ? _appleBadge : _googleBadge;
            _reset.gameObject.SetActive(email);
            _primaryLabel.text = _register ? (email ? "CREATE ACCOUNT" : "SAVE PROGRESS") : "SIGN IN";
            _modeLabel.text = _register ? "USE EXISTING ACCOUNT" : (email ? "CREATE ACCOUNT" : "SAVE CURRENT PROGRESS");
            _detail.text = _register
                ? "Keep your current profile and cloud progress\nby linking this account."
                : "Load your saved cloud profile.\nGuest progress won't be merged.";
            _status.text = "";
            if (ServiceLocator.TryGet<FirebaseAuthService>(out var auth) && !auth.IsAnonymous)
                _status.text = "Signed in" + (string.IsNullOrEmpty(auth.Email) ? "." : " as " + auth.Email);
            LayoutContent();
        }

        private void LayoutContent()
        {
            bool email = _provider == "password";
            _hasStatus = !string.IsNullOrEmpty(_status.text);
            float messageSpace = _hasStatus ? 58 : 0;
            _status.gameObject.SetActive(_hasStatus);
            _sheetHeight = (email ? 508 : 432) + messageSpace;
            _card.sizeDelta = new Vector2(390, _sheetHeight);
            Place(_detail.rectTransform, 30, email ? 94 : 169, 330, 52);
            Place(_status.rectTransform, 28, email ? 310 : 230, 334, 54);
            Place((RectTransform)_primary.transform, 82, (email ? 318 : 240) + messageSpace, 226, 55);
            Place((RectTransform)_mode.transform, 37, (email ? 382 : 305) + messageSpace, 316, 49);
            Place((RectTransform)_reset.transform, 25, 445 + messageSpace, 190, 38);
            Place((RectTransform)_cancel.transform, email ? 232 : 128, (email ? 445 : 367) + messageSpace, 133, 38);
        }

        public void Close()
        {
            if (_busy) return;
            _password.text = "";
            _email.text = "";
            Animate(0);
        }

        private async UniTask Run(bool reset)
        {
            if (_busy) return;
#if UNITY_EDITOR || (!UNITY_IOS && !UNITY_ANDROID)
            if (_provider != "password")
            {
                _status.text = "Apple and Google sign-in are available in the mobile app. Use Email here.";
                return;
            }
#endif
            string email = _email.text.Trim();
            string password = _password.text;
            if (_provider == "password")
            {
                string error = ValidateEmail(email, password, _register, reset);
                if (error != null) { _status.text = error; return; }
            }
            SetBusy(true);
            _status.text = "Connecting...";
            // Let the busy state paint before Firebase enters its native dependency check.
            await UniTask.Yield();
            try
            {
                var auth = await FirebaseAuthService.EnsureReadyAsync();
                await UniTask.SwitchToMainThread();
                if (this == null) return;
                string previousUid = auth.Uid;
                if (reset)
                {
                    await auth.SendPasswordResetAsync(email);
                }
                else if (_provider == "password")
                {
                    if (_register) await auth.RegisterEmailAsync(email, password);
                    else await auth.SignInEmailAsync(email, password);
                }
                else
                {
                    _status.text = "Complete sign-in in the system window...";
                    await auth.ConnectProviderAsync(_provider, _register);
                }
                await UniTask.SwitchToMainThread();
                if (this == null) return;
                _password.text = "";
                _status.text = reset ? "If this email has an account, a password reset link has been sent."
                    : "Signed in successfully.";
                if (!reset && previousUid != auth.Uid)
                {
                    // Rebind repositories/presenters to the chosen UID; do not reuse the guest view.
                    SceneManager.LoadScene("Main", LoadSceneMode.Single);
                }
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
            if (exception is FirebaseException firebase)
            {
                switch ((AuthError)firebase.ErrorCode)
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
            if (exception is PlatformNotSupportedException || exception is TimeoutException)
                return exception.Message;
            // Do not expose SDK exception payloads (which can include credentials) in UI/logs.
            return "Sign-in failed. Check your connection and try again.";
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _primary.interactable = _mode.interactable = _reset.interactable = _cancel.interactable = !busy;
            _handle.interactable = !busy;
            _email.interactable = _password.interactable = !busy;
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
