using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using PushStars.Core;
using PushStars.OTA;

namespace PushStars.UI
{
    /// <summary>
    /// First launch, four pages: the camera permission, where to put the phone, which body you
    /// wear, and the level test. It ends by sending the player straight into that 60-second test —
    /// the onboarding is not finished when the text runs out, it is finished when the app knows how
    /// strong they are.
    ///
    /// <para><b>The permission comes first</b>, before any of the explaining. It is the one thing
    /// the player can refuse permanently, and it is easiest to say yes to while the app is still
    /// making its case — after four screens of reading, a system dialog reads as an interruption
    /// rather than the point.</para>
    ///
    /// <para><b>The phone-placement page is not decoration.</b> Every rejection the CV stack can
    /// report starts with a badly placed phone, and a player who reads that page once needs the
    /// in-duel guidance banner far less. It is the one page worth its screen.</para>
    ///
    /// <para>The first two pages carry their own buttons and hide the shared navigation; the rest
    /// use it. Which ones is a list in the Inspector, not a rule spelled out here.</para>
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
        [SerializeField] private string[] _nextLabels = { "", "ДАЛЕЕ", "ДАЛЕЕ", "ДАЛЕЕ", "НАЧАТЬ ЗАМЕР" };

        [Header("Pages with their own button")]
        [Tooltip("Indices of the pages that carry their own call to action. The shared navigation " +
                 "is hidden on these, so each has exactly one button on it.")]
        [SerializeField] private int[] _ownCtaPages = { 0, 1, 2 };

        [Tooltip("Shared navigation — dots, next, back — hidden on the pages named above.")]
        [SerializeField] private GameObject[] _chrome;

        [Tooltip("Asks for the camera, then moves on. On the first page: a permission dialog is " +
                 "far easier to say yes to before the player has read four screens, and every " +
                 "page after it describes something the camera is what does.")]
        [SerializeField] private Button _allowButton;

        [Tooltip("Buttons that do nothing but move on — OK, NEXT, the read-on link. They are one " +
                 "list rather than a field each because that is all they have ever done; a page " +
                 "needing more than that gets its own field, the way the camera one did.")]
        [SerializeField] private Button[] _advanceButtons;

        [Header("Gender page")]
        [SerializeField] private int _genderPageIndex = 2;

        [Tooltip("One per body on offer. Each card knows which gender it is and how to paint " +
                 "itself, so a third body is a scene change and nothing here.")]
        [SerializeField] private GenderChoiceCard[] _genderCards;

        [Header("Level-test page")]
        [SerializeField] private int _cameraPageIndex = 3;

        [Tooltip("Warns about a refused camera, and stays blank when there is nothing to warn " +
                 "about. Optional.")]
        [SerializeField] private TextMeshProUGUI _cameraStatus;

        [Tooltip("Puts the level test off and opens the main screen. The router stops sending the " +
                 "player back here afterwards — see OnboardingState.LevelTestSkipped.")]
        [SerializeField] private Button _skipButton;

        private static readonly Color DotOn = new Color32(245, 200, 66, 255);

        private int _page;
        private bool _starting;

        private void Start()
        {
            if (_nextButton != null) _nextButton.onClick.AddListener(Tap(Next));
            if (_backButton != null) _backButton.onClick.AddListener(Tap(Back));
            if (_allowButton != null) _allowButton.onClick.AddListener(Tap(AskForCamera));
            if (_skipButton != null) _skipButton.onClick.AddListener(Tap(SkipLevelTest));

            if (_advanceButtons != null)
                foreach (var button in _advanceButtons)
                    if (button != null) button.onClick.AddListener(Tap(Next));

            if (_genderCards != null)
                foreach (var card in _genderCards)
                {
                    if (card == null || card.Button == null) continue;
                    // Captured per card, not read off the loop variable inside the closure: every
                    // listener would otherwise pick whichever body the loop finished on.
                    var chosen = card.Gender;
                    // The lighter cue, and the one that means what it says: this is a selection
                    // moving between two things, not a step being taken. Fires on the already
                    // chosen card too — a tap that answers "yes, that one" still deserves an
                    // answer, and silence there reads as a missed press.
                    card.Button.onClick.AddListener(() => { Haptics.Selection(); PickGender(chosen); });
                }

            RefreshGenderFrames();
            ShowPage(0);
        }

        private void OnDestroy()
        {
            if (_nextButton != null) _nextButton.onClick.RemoveAllListeners();
            if (_backButton != null) _backButton.onClick.RemoveAllListeners();
            if (_allowButton != null) _allowButton.onClick.RemoveAllListeners();
            if (_skipButton != null) _skipButton.onClick.RemoveAllListeners();

            if (_advanceButtons != null)
                foreach (var button in _advanceButtons)
                    if (button != null) button.onClick.RemoveAllListeners();

            if (_genderCards != null)
                foreach (var card in _genderCards)
                    if (card != null && card.Button != null) card.Button.onClick.RemoveAllListeners();
        }

        /// <summary>
        /// Wraps a handler so the button that calls it knocks first.
        ///
        /// <para>The feedback belongs to the press, not to what the press does — which is why it
        /// is wired here and not at the top of <see cref="Next"/>. The camera page calls that
        /// method itself once the system dialog closes, and a buzz on the way out of an OS dialog
        /// nobody touched would be a lie about what just happened.</para>
        /// </summary>
        private UnityAction Tap(UnityAction handler)
            => () => { Haptics.Light(); handler(); };

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

            // A page with its own button hides the shared one rather than showing both. Two things
            // to press, one of which does nothing the player asked for, is how a permission screen
            // gets dismissed by accident.
            bool ownCta = _ownCtaPages != null && System.Array.IndexOf(_ownCtaPages, _page) >= 0;
            if (_chrome != null)
                foreach (var part in _chrome)
                    if (part != null) part.SetActive(!ownCta);

            // Last word on the back button, since it is also in the chrome the line above just
            // switched on: there is nothing behind the first page to go back to either way.
            if (_backButton != null) _backButton.gameObject.SetActive(_page > 0 && !ownCta);

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

        /// <summary>Repaints every card from what is actually saved, rather than toggling the two
        /// against each other — a page re-entered after a Back must show the truth, not the last
        /// thing that happened to be tapped.</summary>
        private void RefreshGenderFrames()
        {
            if (_genderCards == null) return;
            var gender = CharacterRoster.SavedGender;
            foreach (var card in _genderCards)
                if (card != null) card.SetSelected(card.Gender == gender);
        }

        // ── Camera ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Asks the OS for the camera, then moves on — whatever it answers.
        ///
        /// <para>A refusal is not a dead end: the pages behind this one still explain the game, and
        /// <see cref="BeginLevelTest"/> asks once more at the point where the camera is actually
        /// needed. Blocking here would leave a player who mis-tapped with no way forward and no
        /// second chance, since iOS only ever shows its dialog once.</para>
        /// </summary>
        private void AskForCamera()
        {
            if (_starting) return;
            StartCoroutine(AskForCameraRoutine());
        }

        private IEnumerator AskForCameraRoutine()
        {
            if (_allowButton != null) _allowButton.interactable = false;

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                Debug.LogWarning("[Onboarding] Camera permission refused — asked again before the level test.");

            if (_allowButton != null) _allowButton.interactable = true;
            Next();
        }

        /// <summary>Blank unless something is wrong. The page is an invitation, not a status
        /// board, and the permission was settled four pages ago — the only thing worth a line here
        /// is a refusal, which is still repairable at this point and silently fatal after it.</summary>
        private void RefreshCameraStatus()
        {
            if (_cameraStatus == null) return;
            _cameraStatus.text = Application.HasUserAuthorization(UserAuthorization.WebCam)
                ? ""
                : "Камера не разрешена — повторы не засчитаются";
        }

        /// <summary>
        /// Takes the player to the main screen without a level.
        ///
        /// <para>Marked skipped rather than done: they have no score, and nothing downstream should
        /// read one. The intro itself counts as seen — they have been through all of it — so the
        /// next launch opens on the main screen, and the test is offered there instead of being
        /// forced here.</para>
        /// </summary>
        private void SkipLevelTest()
        {
            if (_starting) return;
            _starting = true;

            OnboardingState.IntroSeen = true;
            OnboardingState.LevelTestSkipped = true;
            OtaSceneLoader.LoadScene(FightConfig.MainSceneName);
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
            OtaSceneLoader.LoadScene(FightConfig.FightSceneName);
        }
    }
}
