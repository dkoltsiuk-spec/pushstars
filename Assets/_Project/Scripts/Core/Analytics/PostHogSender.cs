using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace PushStars.Core
{
    /// <summary>
    /// Delivers <see cref="Analytics"/> events to PostHog's <c>/batch/</c> endpoint with one
    /// UnityWebRequest at a time — no SDK.
    ///
    /// <para><b>Offline first.</b> Every event is written to a small queue file before any send, so
    /// a set finished on a plane, or an app killed in the background, still arrives later. Each
    /// event carries its own uuid and timestamp: a batch resent after a lost reply is deduplicated
    /// by PostHog and still lands on the day it happened.</para>
    ///
    /// <para><b>When it sends.</b> Shortly after an event, on launch (whatever an earlier run left),
    /// and when the app goes to the background. Failures back off up to ten minutes; a batch the
    /// server rejects as malformed is dropped rather than retried forever.</para>
    /// </summary>
    public sealed class PostHogSender : MonoBehaviour
    {
        private const int MaxQueued = 1000;
        private const int BatchSize = 50;
        private const float SendDelaySec = 3f;
        private const float MaxBackoffSec = 600f;
        // Back after this long counts as a new visit (a second app_open).
        private const float ResumeVisitSec = 30f * 60f;

        private static PostHogSender _instance;
        private static readonly List<string> Queue = new List<string>();
        private static bool _loaded;

        private bool _sending;
        private float _sendAt = -1f;
        private float _backoffSec = 15f;
        private DateTime _pausedAtUtc;
        private bool _paused;

        private static string QueuePath => Path.Combine(Application.persistentDataPath, "analytics_queue.jsonl");

        internal static void Enqueue(string eventJson)
        {
            LoadQueue();
            Queue.Add(eventJson);
            if (Queue.Count > MaxQueued) Queue.RemoveRange(0, Queue.Count - MaxQueued);
            SaveQueue();
            Ensure()?.ScheduleSend(SendDelaySec);
        }

        internal static void ClearQueue()
        {
            Queue.Clear();
            _loaded = true;
            try { if (File.Exists(QueuePath)) File.Delete(QueuePath); }
            catch (Exception e) { Debug.LogWarning($"[Analytics] Queue not cleared: {e.Message}"); }
        }

        private static PostHogSender Ensure()
        {
            if (_instance != null) return _instance;
            // Outside play mode (editor tools) the event stays queued for the next run.
            if (!Application.isPlaying) return null;
            var go = new GameObject("[PostHogSender]") { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PostHogSender>();
            return _instance;
        }

        /// <summary>Sends whatever an earlier run left behind, without waiting for a new event.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void FlushLeftovers()
        {
            if (!Analytics.Active) return;
            LoadQueue();
            if (Queue.Count > 0) Ensure()?.ScheduleSend(1f);
        }

        private void ScheduleSend(float delaySec)
        {
            float at = Time.realtimeSinceStartup + delaySec;
            if (_sendAt < 0f || at < _sendAt) _sendAt = at;
        }

        private void Update()
        {
            if (_sending || _sendAt < 0f || Time.realtimeSinceStartup < _sendAt) return;
            _sendAt = -1f;
            StartCoroutine(SendBatch());
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                _paused = true;
                _pausedAtUtc = DateTime.UtcNow;
                SaveQueue();
                if (!_sending && Queue.Count > 0) StartCoroutine(SendBatch());
                return;
            }
            if (!_paused) return;
            _paused = false;
            float away = (float)(DateTime.UtcNow - _pausedAtUtc).TotalSeconds;
            if (away >= ResumeVisitSec) Analytics.AppResumed(away);
            else if (Queue.Count > 0) ScheduleSend(1f);
        }

        private void OnApplicationQuit() => SaveQueue();

        private IEnumerator SendBatch()
        {
            LoadQueue();
            string key = Analytics.ApiKey;
            if (Queue.Count == 0 || string.IsNullOrEmpty(key) || !Analytics.Active) yield break;

            _sending = true;
            int count = Mathf.Min(BatchSize, Queue.Count);
            var body = new StringBuilder(count * 300);
            body.Append("{\"api_key\":");
            Analytics.MiniJson.WriteString(body, key);
            body.Append(",\"batch\":[");
            for (int i = 0; i < count; i++)
            {
                if (i > 0) body.Append(',');
                body.Append(Queue[i]);
            }
            body.Append("]}");

            using (var request = new UnityWebRequest(Analytics.Host + "/batch/", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 20;
                yield return request.SendWebRequest();

                long code = request.responseCode;
                if (request.result == UnityWebRequest.Result.Success && code >= 200 && code < 300)
                {
                    Drop(count);
                    _backoffSec = 15f;
                    if (Queue.Count > 0) ScheduleSend(0.5f);
                }
                else if (code == 400 || code == 413)
                {
                    // The payload itself is refused; resending it can never succeed.
                    Debug.LogWarning($"[Analytics] PostHog rejected {count} events ({code}): {request.downloadHandler.text}");
                    Drop(count);
                }
                else
                {
                    ScheduleSend(_backoffSec);
                    _backoffSec = Mathf.Min(_backoffSec * 2f, MaxBackoffSec);
                }
            }
            _sending = false;
        }

        private static void Drop(int count)
        {
            Queue.RemoveRange(0, Mathf.Min(count, Queue.Count));
            SaveQueue();
        }

        private static void LoadQueue()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(QueuePath)) return;
                foreach (string line in File.ReadAllLines(QueuePath))
                    if (!string.IsNullOrWhiteSpace(line)) Queue.Add(line);
            }
            catch (Exception e) { Debug.LogWarning($"[Analytics] Queue not loaded: {e.Message}"); }
        }

        private static void SaveQueue()
        {
            if (!_loaded) return;
            try
            {
                if (Queue.Count == 0) { if (File.Exists(QueuePath)) File.Delete(QueuePath); }
                else File.WriteAllLines(QueuePath, Queue);
            }
            catch (Exception e) { Debug.LogWarning($"[Analytics] Queue not saved: {e.Message}"); }
        }
    }
}
