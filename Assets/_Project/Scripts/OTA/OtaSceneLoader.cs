using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.OTA
{
    /// <summary>
    /// Compatibility entry point for existing callers. Every platform loads the authored
    /// scenes embedded in the app. Retired OTA catalogs and cached bundles are never read.
    /// </summary>
    public static class OtaSceneLoader
    {
        public static UniTask PrepareAsync(string sceneName, Action<float> progress = null)
        {
            progress?.Invoke(1f);
            return UniTask.CompletedTask;
        }

        public static async UniTask LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single,
                                                   Action<float> progress = null)
        {
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
                throw new InvalidOperationException("Embedded scene is missing from Build Settings: " + sceneName);

            var local = SceneManager.LoadSceneAsync(sceneName, mode);
            while (local != null && !local.isDone)
            {
                progress?.Invoke(Mathf.Clamp01(local.progress / 0.9f));
                await UniTask.Yield();
            }
            progress?.Invoke(1f);
        }

        public static void LoadScene(string sceneName, LoadSceneMode mode = LoadSceneMode.Single)
            => LoadSceneAsync(sceneName, mode).Forget();
    }
}
