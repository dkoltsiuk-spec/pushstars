using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Firebase.Auth;
using PushStars.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace PushStars.Services
{
    /// <summary>Uploads an Emote Lab take, chunk by chunk, to the private capture folder (cloud
    /// function <c>saveEmoteCaptureChunk</c>). Each chunk call is idempotent, so a retry re-sends
    /// whatever is still on the device.</summary>
    public static class EmoteCaptureClient
    {
        [Serializable] private sealed class Request { public EmoteCaptureMeta meta; public int index; public string data; }
        [Serializable] private sealed class Saved { public string id; public int received, chunks; public bool ready; }
        [Serializable] private sealed class Envelope { public Request data; }
        [Serializable] private sealed class Error { public string status, message; }
        [Serializable] private sealed class Response { public Saved result; public Error error; }

        public static async Task Upload(EmoteCaptureMeta meta, Action<int, int> progress = null)
        {
            await FirebaseAuthService.EnsureReadyAsync();
            bool ready = false;
            for (int i = 0; i < meta.chunks; i++)
            {
                progress?.Invoke(i, meta.chunks);
                string path = EmoteCaptureStore.ChunkPath(meta.id, i);
                if (!File.Exists(path)) throw new InvalidOperationException("The take's frames are no longer on this device.");
                var saved = await Call(new Request { meta = meta, index = i, data = Convert.ToBase64String(File.ReadAllBytes(path)) });
                if (saved.id != meta.id) throw new InvalidOperationException("Cloud save was not confirmed.");
                ready = saved.ready;
            }
            if (!ready) throw new InvalidOperationException("Cloud save is incomplete. Retry.");
            meta.uploaded = true;
            EmoteCaptureStore.Save(meta);
            progress?.Invoke(meta.chunks, meta.chunks);
        }

        // LeagueClient.Call times out after 15 s — right for a ranked result, too short for a
        // 2 MB chunk on a phone connection.
        private static async Task<Saved> Call(Request data)
        {
            if (!ServiceLocator.TryGet<FirebaseService>(out var firebase) || !firebase.IsReady)
                throw new InvalidOperationException("Connection unavailable.");
            var user = FirebaseAuth.DefaultInstance.CurrentUser;
            if (user == null) throw new InvalidOperationException("Sign in required.");
            string token = await user.TokenAsync(false);
            using (var request = new UnityWebRequest(
                       $"https://us-central1-{firebase.App.Options.ProjectId}.cloudfunctions.net/saveEmoteCaptureChunk", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Envelope { data = data })));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + token);
                request.timeout = 180;
                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();
                Response response = null;
                try { response = JsonUtility.FromJson<Response>(request.downloadHandler.text); } catch (ArgumentException) { }
                if (response?.error != null && !string.IsNullOrEmpty(response.error.message))
                    throw new InvalidOperationException(response.error.message);
                if (request.result != UnityWebRequest.Result.Success || response?.result == null || string.IsNullOrEmpty(response.result.id))
                    throw new InvalidOperationException(
                        $"Upload failed ({request.responseCode} {request.error}). Check the connection and retry.");
                return response.result;
            }
        }
    }
}
