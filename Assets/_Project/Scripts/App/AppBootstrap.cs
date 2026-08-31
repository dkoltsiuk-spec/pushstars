using System;
using Cysharp.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using PushStars.UI;
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
    /// resumes where it stopped); everything else goes to the main screen.</para>
    /// </summary>
    public class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private string _mainSceneName = "Main";
        [SerializeField] private LoadingScreen _loading;

        [Tooltip("Shortest time the loading screen stays up, so a fast launch is not a flash of " +
                 "logo. Real work longer than this is never cut short.")]
        [SerializeField, Range(0f, 4f)] private float _minVisibleSec = 1.2f;

        private float _startTime;

        private void Awake()
        {
            // Workout app: the user's hands are on the floor — they can't touch the screen to keep
            // it awake. Never let the display sleep while the app runs.
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _startTime = Time.realtimeSinceStartup;

            // Up before anything else, and it outlives every scene load — the first screen is
            // exactly where a performance problem has to be measurable, not just felt.
            PerfOverlay.Ensure();
        }

        private async void Start()
        {
            Report(0.05f, "Запуск…");
            await InitServicesAsync();

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
                await firebase.InitializeAsync();
                await UniTask.SwitchToMainThread();

                if (!firebase.IsReady)
                {
                    // Not fatal: the level test, the ghost duel and XP all work offline. The app
                    // opens without a backend rather than refusing to open.
                    Debug.LogError("[AppBootstrap] Firebase unavailable — continuing without backend.");
                    Report(0.6f, "Офлайн-режим");
                    return;
                }
                ServiceLocator.Register(firebase);

                Report(0.32f, "Вход…");
                var auth = new FirebaseAuthService();
                await auth.SignInAnonymouslyAsync();
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

        // ── Routing ──────────────────────────────────────────────────────────────────────────────

        /// <summary>Where this launch opens. See the class summary for why the two onboarding
        /// steps are checked separately.</summary>
        private string ResolveNextScene()
        {
            if (!OnboardingState.IntroSeen)
                return FightConfig.OnboardingSceneName;

            if (!OnboardingState.LevelTestDone)
            {
                FightRequest.LevelTest(_mainSceneName);
                return FightConfig.FightSceneName;
            }

            return _mainSceneName;
        }

        private async UniTask LoadNextSceneAsync(string sceneName)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                // A scene missing from Build Settings is a build mistake, not a runtime condition:
                // say so loudly and fall back to Main rather than hanging on the loading screen.
                Debug.LogError($"[AppBootstrap] Scene '{sceneName}' is not in the build — opening {_mainSceneName} instead.");
                sceneName = _mainSceneName;
            }

            var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
            {
                Report(0.65f + 0.3f * (op.progress / 0.9f));
                await UniTask.Yield();
            }

            Report(1f, "Поехали");

            // Hold for the minimum display time AND until the bar has visibly filled.
            while (Time.realtimeSinceStartup - _startTime < _minVisibleSec ||
                   (_loading != null && !_loading.Finished))
                await UniTask.Yield();

            op.allowSceneActivation = true;
            await UniTask.WaitUntil(() => op.isDone);
        }

        private void Report(float progress, string status = null) => _loading?.Report(progress, status);
    }
}
