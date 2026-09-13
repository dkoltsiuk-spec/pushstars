using System;
using System.Collections;
using System.Text;
using Firebase.Auth;
using PushStars.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace PushStars.Services
{
    /// <summary>Foreground players, counted by the server over a 75-second heartbeat window.
    /// Persists through scene changes. Unknown/offline is null, never a fabricated zero.</summary>
    public sealed class OnlinePresence : MonoBehaviour
    {
        public static OnlinePresence Instance { get; private set; }
        public int? Count { get; private set; }
        public event Action<int?> Changed;
        private Coroutine _loop;
        private UnityWebRequest _request;

        [Serializable] private class Response { public Result result; }
        [Serializable] private class Result { public int online; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Instance != null) return;
            new GameObject("OnlinePresence").AddComponent<OnlinePresence>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable() => _loop = StartCoroutine(Poll());

        private IEnumerator Poll()
        {
            while (true)
            {
                if (Application.internetReachability != NetworkReachability.NotReachable &&
                    ServiceLocator.TryGet<FirebaseService>(out var firebase) && firebase.IsReady &&
                    ServiceLocator.TryGet<FirebaseAuthService>(out var auth) && auth.IsSignedIn)
                {
                    var user = FirebaseAuth.DefaultInstance.CurrentUser;
                    if (user != null)
                    {
                        var token = user.TokenAsync(false);
                        float deadline = Time.realtimeSinceStartup + 15f;
                        while (!token.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                        if (token.IsCompleted && !token.IsFaulted && !token.IsCanceled)
                        {
                            string project = firebase.App.Options.ProjectId;
                            using (var request = new UnityWebRequest(
                                $"https://us-central1-{project}.cloudfunctions.net/heartbeatPresence", "POST"))
                            {
                                _request = request;
                                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes("{\"data\":{}}"));
                                request.downloadHandler = new DownloadHandlerBuffer();
                                request.SetRequestHeader("Content-Type", "application/json");
                                request.SetRequestHeader("Authorization", "Bearer " + token.Result);
                                request.timeout = 12;
                                yield return request.SendWebRequest();
                                int? count = null;
                                if (request.result == UnityWebRequest.Result.Success)
                                {
                                    try
                                    {
                                        var response = JsonUtility.FromJson<Response>(request.downloadHandler.text);
                                        if (response?.result != null && response.result.online >= 0)
                                            count = response.result.online;
                                    }
                                    catch (ArgumentException) { /* Invalid response remains unknown. */ }
                                }
                                Publish(count);
                                _request = null;
                            }
                        }
                        else
                        {
                            // Observe a failed token task so it cannot become an unobserved exception.
                            if (token.IsFaulted) _ = token.Exception;
                            Publish(null);
                        }
                    }
                    else Publish(null);
                }
                else Publish(null);
                yield return new WaitForSecondsRealtime(30f);
            }
        }

        private void Publish(int? count)
        {
            Count = count;
            Changed?.Invoke(count);
        }

        private void OnApplicationPause(bool paused)
        {
            StopPolling();
            Publish(null);
            if (!paused && isActiveAndEnabled) _loop = StartCoroutine(Poll());
        }

        private void Update()
        {
            if (Count.HasValue && Application.internetReachability == NetworkReachability.NotReachable)
                Publish(null);
        }

        private void StopPolling()
        {
            _request?.Abort();
            if (_loop != null) StopCoroutine(_loop);
            _loop = null;
            _request?.Dispose();
            _request = null;
        }

        private void OnDisable() => StopPolling();
        private void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
