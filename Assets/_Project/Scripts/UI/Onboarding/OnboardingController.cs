using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PushStars.Core;

namespace PushStars.UI
{
    /// <summary>
    /// First launch, four pages: what the app is, where to put the phone, which body you wear, and
    /// the camera permission. It ends by sending the player straight into the 60-second level test —
    /// the onboarding is not finished when the text runs out, it is finished when the app knows how
    /// strong they are.
    ///
    /// <para><b>The phone-placement page is not decoration.</b> Every rejection the CV stack can
    /// report starts with a badly placed phone, and a player who reads that page once needs the
    /// in-duel guidance banner far less. It is the one page worth its screen.</para>
    ///
    /// <para>Pages are plain GameObjects toggled in order; the tool that builds the scene owns their
    /// content. This component owns only the sequence, the gender choice and the permission
    /// request, so a redesign of any page changes nothing here.</para>
    /// </summary>
    public sealed class OnboardingController : MonoBehaviour
    {
        [Header("Pages (in order)")]
        [SerializeField] private GameObject[] _pages;
        [Tooltip("Dot per page, in the same order. Optional.")]
        [SerializeField] private Image[] _dots;

        [Header("Navigation")]
        [SerializeField] private Button _nextButton;
        [SerializeField] private TextMeshProUGUI _nextLabel;
        [SerializeField] private Button _backButton;
        [Tooltip("Label for the button on each page. Falls back to ДАЛЕЕ when short.")]
        [SerializeField] private string[] _nextLabels = { "ДАЛЕЕ", "ДАЛЕЕ", "ДАЛЕЕ", "НАЧАТЬ ЗАМЕР" };

        [Header("Gender page")]
        [SerializeField] private int _genderPageIndex = 2;
        [SerializeField] private Button _maleButton;
        [SerializeField] private Button _femaleButton;
        [SerializeField] private Image _maleFrame;
        [SerializeField] private Image _femaleFrame;

        [Header("Camera page")]
        [SerializeField] private int _cameraPageIndex = 3;
        [SerializeField] private TextMeshProUGUI _cameraStatus;

        /// <summary>Selection tints the whole card; the fill is translucent so the label on top
        /// of it stays legible.</summary>
        private static readonly Color SelectedFrame = new Color32(245, 200, 66, 64);
        private static readonly Color IdleFrame = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color DotOn = new Color32(245, 200, 66, 255);

        private int _page;
        private bool _starting;

        private void Start()
        {
            if (_nextButton != null) _nextButton.onClick.AddListener(Next);
            if (_backButton != null) _backButton.onClick.AddListener(Back);
            if (_maleButton != null) _maleButton.onClick.AddListener(() => PickGender(CharacterGender.Male));
            if (_femaleButton != null) _femaleButton.onClick.AddListener(() => PickGender(CharacterGender.Female));

            RefreshGenderFrames();
            ShowPage(0);
        }

        private void OnDestroy()
        {
            if (_nextButton != null) _nextButton.onClick.RemoveAllListeners();
            if (_backButton != null) _backButton.onClick.RemoveAllListeners();
            if (_maleButton != null) _maleButton.onClick.RemoveAllListeners();
            if (_femaleButton != null) _femaleButton.onClick.RemoveAllListeners();
        }

        // ── Paging ───────────────────────────────────────────────────────────────────────────────

        private void Next()
        {
            if (_starting) return;

            if (_page >= _pages.Length - 1)
            {
                StartCoroutine(BeginLevelTest());
                return;
            }
            ShowPage(_page + 1);
        }

        private void Back()
        {
            if (_starting || _page == 0) return;
            ShowPage(_page - 1);
        }

        private void ShowPage(int index)
        {
            _page = Mathf.Clamp(index, 0, _pages.Length - 1);

            for (int i = 0; i < _pages.Length; i++)
                if (_pages[i] != null) _pages[i].SetActive(i == _page);

            if (_dots != null)
                for (int i = 0; i < _dots.Length; i++)
                    if (_dots[i] != null)
                        _dots[i].color = i == _page ? DotOn : new Color(1f, 1f, 1f, 0.2f);

            if (_nextLabel != null)
                _nextLabel.text = _page < _nextLabels.Length ? _nextLabels[_page] : "ДАЛЕЕ";

            if (_backButton != null) _backButton.gameObject.SetActive(_page > 0);

            // Both stateful pages re-read their state on entry rather than trusting what was set
            // when the scene was built — a player who goes back and forth must see the truth.
            if (_page == _genderPageIndex) RefreshGenderFrames();
            if (_page == _cameraPageIndex) RefreshCameraStatus();
        }

        // ── Gender ───────────────────────────────────────────────────────────────────────────────

        private void PickGender(CharacterGender gender)
        {
            CharacterRoster.SaveGender(gender);
            RefreshGenderFrames();
        }

        private void RefreshGenderFrames()
        {
            var gender = CharacterRoster.SavedGender;
            if (_maleFrame != null) _maleFrame.color = gender == CharacterGender.Male ? SelectedFrame : IdleFrame;
            if (_femaleFrame != null) _femaleFrame.color = gender == CharacterGender.Female ? SelectedFrame : IdleFrame;
        }

        // ── Camera ───────────────────────────────────────────────────────────────────────────────

        private void RefreshCameraStatus()
        {
            if (_cameraStatus == null) return;
            _cameraStatus.text = Application.HasUserAuthorization(UserAuthorization.WebCam)
                ? "Камера разрешена"
                : "Сейчас система спросит разрешение на камеру";
        }

        /// <summary>Asks for the camera, then hands over to the level test. The request is made here
        /// rather than inside the duel so the permission dialog never lands on a player already in a
        /// plank — and a refusal is not fatal: the test still runs, counts nothing, and its result
        /// screen explains what to fix.</summary>
        private IEnumerator BeginLevelTest()
        {
            _starting = true;
            if (_nextLabel != null) _nextLabel.text = "…";

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                Debug.LogWarning("[Onboarding] Camera permission denied — the level test will not count reps.");

            OnboardingState.IntroSeen = true;
            FightRequest.LevelTest(FightConfig.MainSceneName);
            SceneManager.LoadScene(FightConfig.FightSceneName);
        }
    }
}
