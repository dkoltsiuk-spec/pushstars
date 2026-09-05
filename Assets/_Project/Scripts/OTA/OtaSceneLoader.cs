using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;

namespace PushStars.OTA
{
    /// <summary>Loads OTA scenes when available and falls back to the scenes embedded in the app.</summary>
    public static class OtaSceneLoader
    {
        const float NetworkTimeoutSeconds = 8f;
        const int WebRequestTimeoutSeconds = 10;
        static bool _catalogAttempted;
        static bool _addressablesReady;
        static bool _webRequestsConfigured;

        static string Key(string sceneName) => sceneName switch
        {
            "Main" => "ota/Main",
            "Fight" => "ota/Fight",
            _ => null,
        };

        public static async UniTask PrepareAsync(string sceneName, Action<float> progress = null)
        {
            string key = Key(sceneName);
            if (key == null) { progress?.Invoke(1f); return; }
            if (!await EnsureCatalogAsync()) { progress?.Invoke(1f); return; }

            var size = Addressables.GetDownloadSizeAsync(key);
            if (!await Wait(size, NetworkTimeoutSeconds, null) || size.Status != AsyncOperationStatus.Succeeded)
            {
                if (size.IsValid() && size.IsDone) Addressables.Release(size);
                progress?.Invoke(1f);
                return;
            }

            long bytes = size.Result;
            Addressables.Release(size);
            if (bytes <= 0) { progress?.Invoke(1f); return; }

            var download = Addressables.DownloadDependenciesAsync(key, false);
            await Wait(download, NetworkTimeoutSeconds, progress);
            if (download.IsValid() && download.IsDone) Addressables.Release(download);
        }

        public static async UniTask LoadSceneAsync(string sceneName, LoadSceneMode mode = LoadSceneMode.Single,
                                                   Action<float> progress = null)
        {
            string key = Key(sceneName);
            if (key != null && await EnsureCatalogAsync())
            {
                var remote = Addressables.LoadSceneAsync(key, mode, true);
                // Web requests have their own timeout. Once a remote scene load has started,
                // let it finish before falling back so two Single loads cannot race each other.
                while (!remote.IsDone)
                {
                    progress?.Invoke(remote.PercentComplete);
                    await UniTask.Yield();
                }
                if (remote.Status == AsyncOperationStatus.Succeeded) return;

                Debug.LogWarning($"[OTA] Could not load '{key}', using embedded '{sceneName}'.");
                if (remote.IsValid()) Addressables.Release(remote);
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[OTA] Neither remote nor embedded scene '{sceneName}' is available.");
                return;
            }

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

        static async UniTask<bool> EnsureCatalogAsync()
        {
            if (_addressablesReady) return true;

            if (!_webRequestsConfigured)
            {
                _webRequestsConfigured = true;
                Addressables.WebRequestOverride = request => request.timeout = WebRequestTimeoutSeconds;
            }

            var init = Addressables.InitializeAsync(false);
            if (!await Wait(init, NetworkTimeoutSeconds, null) || init.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning("[OTA] Addressables initialization unavailable; using embedded scenes.");
                return false;
            }
            _addressablesReady = true;
            Addressables.Release(init);

            if (_catalogAttempted) return true;
            _catalogAttempted = true;

            var check = Addressables.CheckForCatalogUpdates(false);
            if (!await Wait(check, NetworkTimeoutSeconds, null) || check.Status != AsyncOperationStatus.Succeeded)
            {
                if (check.IsValid() && check.IsDone) Addressables.Release(check);
                return true; // the catalog bundled with the player remains usable
            }

            List<string> catalogs = check.Result;
            if (catalogs != null && catalogs.Count > 0)
            {
                var update = Addressables.UpdateCatalogs(catalogs, false);
                await Wait(update, NetworkTimeoutSeconds, null);
                if (update.IsValid() && update.IsDone) Addressables.Release(update);
            }
            Addressables.Release(check);
            return true;
        }

        static async UniTask<bool> Wait<T>(AsyncOperationHandle<T> handle, float timeout,
                                           Action<float> progress)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!handle.IsDone && Time.realtimeSinceStartup < deadline)
            {
                progress?.Invoke(handle.PercentComplete);
                await UniTask.Yield();
            }
            progress?.Invoke(handle.IsDone ? 1f : handle.PercentComplete);
            return handle.IsDone;
        }

        static async UniTask<bool> Wait(AsyncOperationHandle handle, float timeout,
                                        Action<float> progress)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!handle.IsDone && Time.realtimeSinceStartup < deadline)
            {
                progress?.Invoke(handle.PercentComplete);
                await UniTask.Yield();
            }
            progress?.Invoke(handle.IsDone ? 1f : handle.PercentComplete);
            return handle.IsDone;
        }
    }
}
