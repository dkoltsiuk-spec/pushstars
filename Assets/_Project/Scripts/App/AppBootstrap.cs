using System;
using Cysharp.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using PushStars.UI;
using PushStars.OTA;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.App
{
    /// <summary>
    /// Entry point loaded in Boot.unity: brings the services up behind the loading screen, then
    /// decides which screen the app actually opens on.
    ///
    /// <para><b>The router lives here</b> because this is the only place that runs on every launch
    /// before anything is on screen. A first launch goes to the intro; a launch that got through
    /// the intro but not the level test goes straight into the test (an app killed mid-onboarding
    /// resumes where it stopped); everything else goes to the main screen. Every one of those
    /// decisions reads local state, so the route is known before any network exists.</para>
    ///
    /// <para><b>The backend never blocks the launch.</b> It used to be awaited here, and on device
    /// that froze the app solid on "Соединение…": Firebase's dependency check blocks the main
    /// thread, and a main thread inside native code does not run the player loop — which is also
    /// why the timeout around it never fired, since <c>UniTask.Delay</c> ticks on that same loop.
    /// A timeout cannot rescue a synchronous block; only not being in its way can. Firebase is now
    /// started beside the launch, and <see cref="_initializeBackend"/> switches it off entirely
    /// while nothing in the app needs it.</para>
    /// </summary>
    public class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private string _mainSceneName = "Main";
        [SerializeField] private LoadingScreen _loading;

        [Tooltip("Shortest time the loading screen stays up, so a launch that finishes in 100 ms " +
                 "is not a flash of artwork. The bar is paced to the same number, so it is still " +
                 "moving for the whole wait. Real work longer than this is never cut short.")]
        [SerializeField, Range(0f, 4f)] private float _minVisibleSec = 1.5f;

        [Tooltip("Upper bound on the frame rate. The display's own refresh rate wins when it is " +
                 "lower, so a 60Hz panel gets 60 and a 120Hz one is still held here.")]
        [SerializeField, Range(30, 120)] private int _maxFrameRate = 60;

        [Tooltip("How long any one backend step may wait before it is written off. Only bounds the " +
                 "logging — the launch never waits for it either way.")]
        [SerializeField, Range(2f, 30f)] private float _serviceTimeoutSec = 8f;

        [Tooltip("Bring Firebase up at all. OFF while the app has nothing that needs it: the intro, " +
                 "the level test, the ghost duel and XP are all local. Turn back on with phase 11.5.")]
        [SerializeField] private bool _initializeBackend = false;

        private float _startTime;

        private void Awake()
        {
            // Workout app: the user's hands are on the floor — they can't touch the screen to keep
            // it awake. Never let the display sleep while the app runs.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            ApplyFrameRate(_maxFrameRate);
            _startTime = Time.realtimeSinceStartup;

            // One number decides how long a launch looks like it takes, and it lives here because
            // this is what actually holds the screen. Told the same figure, the bar spends the
            // whole hold travelling: on a machine where every service answers instantly the app
            // still shows a full sweep rather than a flash and a bar already at the end.
            _loading?.PaceOver(_minVisibleSec);

            // Up before anything else, and it outlives every scene load — the first screen is
            // exactly where a performance problem has to be measurable, not just felt.
            PerfOverlay.Ensure();
        }

        private async void Start()
        {
            Report(0.05f, "Запуск…");

            // The backend is started beside the launch, never in front of it. Where it runs at all
            // is decided by _initializeBackend; either way the router below only reads local state.
            if (_initializeBackend) InitServicesAsync().Forget();

            string next = ResolveNextScene();
            Report(0.65f, next == FightConfig.FightSceneName ? "Готовим замер…" : "Почти готово…");
            await LoadNextSceneAsync(next);
        }

        // ── Services ─────────────────────────────────────────────────────────────────────────────

        private async UniTask InitServicesAsync()
        {
            // Phase 04: Firebase (App deps → anonymous Auth → Firestore). Photon lands in phase 13.
            // Firebase callbacks may resume off the main thread, so we hop back after each await
            // (the final scene load must run on the main thread).
            try
            {
                Report(0.12f, "Соединение…");
                var firebase = new FirebaseService();
                bool inTime = await WithTimeout(firebase.InitializeAsync());
                await UniTask.SwitchToMainThread();

                if (!inTime || !firebase.IsReady)
                {
                    // Not fatal: the level test, the ghost duel and XP all work offline. The app
                    // opens without a backend rather than refusing to open.
                    Debug.LogError(inTime
                        ? "[AppBootstrap] Firebase unavailable — continuing without backend."
                        : $"[AppBootstrap] Firebase did not answer in {_serviceTimeoutSec}s — continuing without backend.");
                    Report(0.6f, "Офлайн-режим");
                    return;
                }
                ServiceLocator.Register(firebase);

                Report(0.32f, "Вход…");
                var auth = new FirebaseAuthService();
                if (!await WithTimeout(auth.SignInAnonymouslyAsync()))
                {
                    Debug.LogError($"[AppBootstrap] Sign-in did not answer in {_serviceTimeoutSec}s — continuing without backend.");
                    Report(0.6f, "Офлайн-режим");
                    return;
                }
                await UniTask.SwitchToMainThread();
                ServiceLocator.Register(auth);

                Report(0.5f, "Профиль…");
                var firestore = new FirestoreService();
                firestore.Configure();
                ServiceLocator.Register(firestore);
                // Phase 05: connectivity is now proven by the onUserCreated Cloud Function
                // seeding users/{uid}. The old _diagnostics/ping self-test is dropped — the
                // strict Firestore rules deny that path.

                Debug.Log("[AppBootstrap] Services initialized.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AppBootstrap] Service init failed: {e}");
                Report(0.6f, "Офлайн-режим");
            }
            finally
            {
                // Guarantee we are back on the main thread before the scene transition.
                await UniTask.SwitchToMainThread();
            }
        }

        /// <summary>Awaits <paramref name="work"/>, but stops waiting after
        /// <see cref="_serviceTimeoutSec"/>. Returns false when it timed out; the work itself keeps
        /// running in the background and may still register later.
        ///
        /// <para>Nothing on the backend is required to reach the first screen — the intro, the
        /// level test, the ghost duel and XP all work offline by design. A slow network must
        /// therefore cost a feature, never the launch; before this, a Firebase call that never
        /// answered held the app on the loading screen for as long as it liked.</para></summary>
        private async UniTask<bool> WithTimeout(System.Threading.Tasks.Task work)
        {
            int finishedFirst = await UniTask.WhenAny(
                work.AsUniTask(),
                UniTask.Delay(TimeSpan.FromSeconds(_serviceTimeoutSec), DelayType.Realtime));
            return finishedFirst == 0;
        }

        // ── Routing ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Where this launch opens. See the class summary for why the two onboarding
        /// steps are checked separately.</summary>
        private string ResolveNextScene()
        {
            if (!OnboardingState.IntroSeen)
                return FightConfig.OnboardingSceneName;

            // Postponed counts as answered. A player who tapped SKIP has said no to a
            // maximum-effort set today; asking again every single launch is how they stop opening
            // the app at all. The test is offered again from the main screen instead.
            if (!OnboardingState.LevelTestDone && !OnboardingState.LevelTestSkipped)
            {
                FightRequest.LevelTest(_mainSceneName);
                return FightConfig.FightSceneName;
            }

            return _mainSceneName;
        }

        private async UniTask LoadNextSceneAsync(string sceneName)
        {
            // Download a newer remote scene when one exists. A failed or slow network is harmless:
            // OtaSceneLoader falls back to the copy embedded in this player build.
            await OtaSceneLoader.PrepareAsync(sceneName, p => Report(0.65f + 0.3f * p));

            Report(1f, "Поехали");

            // Hold for the minimum display time AND until the bar has visibly filled.
            while (Time.realtimeSinceStartup - _startTime < _minVisibleSec ||
                   (_loading != null && !_loading.Finished))
                await UniTask.Yield();

            await OtaSceneLoader.LoadSceneAsync(sceneName);
        }

        /// <summary>Runs the app at the display's own refresh rate, bounded.
        ///
        /// <para>Left alone, <c>Application.targetFrameRate</c> is -1, and on a phone that does not
        /// mean "as fast as it can" — it means the platform default, which iOS puts at 30. The app
        /// was locked to half the panel's rate on hardware perfectly able to do more, and nothing
        /// said so.</para>
        ///
        /// <para>The bound is deliberate rather than uncapped. This is a workout app: the phone
        /// spends the whole set on the floor running pose inference and two character stages, and
        /// the frames past 60 buy nothing a lifter can see while paying for them in heat and
        /// battery. Raising <paramref name="cap"/> is all a 120Hz target would need — plus
        /// ProMotion enabled in the iOS player settings, which is off, and which is what holds
        /// those panels at 60 today.</para></summary>
        private static void ApplyFrameRate(int cap)
        {
            var rate = Screen.currentResolution.refreshRateRatio;
            // A headless or not-yet-initialised display reports nonsense; 60 is the safe read.
            int refresh = rate.value > 1.0 ? Mathf.RoundToInt((float)rate.value) : 60;
            Application.targetFrameRate = Mathf.Clamp(Mathf.Min(refresh, cap), 30, 240);
            Debug.Log($"[AppBootstrap] Display {refresh}Hz, target {Application.targetFrameRate} fps.");
        }

        private void Report(float progress, string status = null) => _loading?.Report(progress, status);
    }
}
