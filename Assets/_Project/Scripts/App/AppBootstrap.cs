using System;
using Cysharp.Threading.Tasks;
using PushStars.Core;
using PushStars.Services;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.App
{
    /// <summary>
    /// Entry point loaded in Boot.unity. Initializes core services then transitions to Main.unity.
    /// </summary>
    public class AppBootstrap : MonoBehaviour
    {
        [SerializeField] private string _mainSceneName = "Main";

        private async void Start()
        {
            await InitServicesAsync();
            await LoadMainSceneAsync();
        }

        private async UniTask InitServicesAsync()
        {
            // Phase 04: Firebase (App deps → anonymous Auth → Firestore). Photon lands in phase 13.
            // Firebase callbacks may resume off the main thread, so we hop back after each await
            // (the final scene load must run on the main thread).
            try
            {
                var firebase = new FirebaseService();
                await firebase.InitializeAsync();
                await UniTask.SwitchToMainThread();

                if (!firebase.IsReady)
                {
                    Debug.LogError("[AppBootstrap] Firebase unavailable — continuing without backend.");
                    return;
                }
                ServiceLocator.Register(firebase);

                var auth = new FirebaseAuthService();
                await auth.SignInAnonymouslyAsync();
                await UniTask.SwitchToMainThread();
                ServiceLocator.Register(auth);

                var firestore = new FirestoreService();
                firestore.Configure();
                ServiceLocator.Register(firestore);
                await firestore.SelfTestAsync();
                await UniTask.SwitchToMainThread();

                Debug.Log("[AppBootstrap] Services initialized.");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AppBootstrap] Service init failed: {e}");
            }
            finally
            {
                // Guarantee we are back on the main thread before the scene transition.
                await UniTask.SwitchToMainThread();
            }
        }

        private async UniTask LoadMainSceneAsync()
        {
            var op = SceneManager.LoadSceneAsync(_mainSceneName, LoadSceneMode.Single);
            op.allowSceneActivation = false;

            while (op.progress < 0.9f)
                await UniTask.Yield();

            op.allowSceneActivation = true;
            await UniTask.WaitUntil(() => op.isDone);
        }
    }
}
