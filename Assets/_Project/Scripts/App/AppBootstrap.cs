using Cysharp.Threading.Tasks;
using PushStars.Core;
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
            // Phase 04+: Register Firebase, Photon, etc. here.
            // For now just a frame yield to keep the pattern async-ready.
            await UniTask.Yield();
            Debug.Log("[AppBootstrap] Services initialized.");
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
