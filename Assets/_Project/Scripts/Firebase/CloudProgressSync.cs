using System;
using System.Threading.Tasks;
using PushStars.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PushStars.Services
{
    /// <summary>A cloud-save step the player has to hear about (sign-out with progress not uploaded).</summary>
    public sealed class CloudSaveException : Exception
    {
        public CloudSaveException(string message) : base(message) { }
    }

    /// <summary>
    /// Keeps a signed-in account's progress in <c>users/{uid}/progress</c> through the
    /// <c>loadProgress</c>/<c>saveProgress</c> functions, and decides what the phone's save is
    /// whenever the account may have changed (<see cref="CloudSaveRules"/>). Guests never upload.
    /// Uploads are debounced after every progress save, flushed when the app goes to the
    /// background, retried while offline, and skip a save the cloud already holds.
    /// See <c>docs/architecture/cloud-progress.md</c>.
    /// </summary>
    public static class CloudProgressSync
    {
        [Serializable] private sealed class Empty { }
        [Serializable] private sealed class SaveRequest { public long baseRev; public string snapshot; public bool sendGhost; public string ghost; }
        [Serializable] private sealed class SaveResponse { public long rev; }
        [Serializable] private sealed class LoadResponse { public bool exists, hasGhost; public long rev; public string snapshot, ghost; }

        private const string MainScene = "Main", BootScene = "Boot";
        private const string OwnerKey = "cloud.owner_uid", OwnerAnonymousKey = "cloud.owner_anonymous",
            RevKey = "cloud.rev", SyncedKey = "cloud.synced_hash", GhostKey = "cloud.ghost_hash", RejectedKey = "cloud.rejected_hash";
        private const float DebounceSec = 8f, RetrySec = 45f;

        private static Task _work = Task.CompletedTask;
        private static float _dueAt = -1;
        private static volatile bool _authReady;
        private static string _reconciledUid, _signedInTokenUid;
        private static bool _applyPending;

        public static string LastError { get; private set; }

        /// <summary>This phone's save belongs to a signed-in (not guest) account.</summary>
        public static bool IsLinkedOnThisPhone =>
            !string.IsNullOrEmpty(PlayerPrefs.GetString(OwnerKey, "")) && PlayerPrefs.GetInt(OwnerAnonymousKey, 1) == 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _work = Task.CompletedTask; _dueAt = -1; _authReady = false;
            _reconciledUid = null; _signedInTokenUid = null; _applyPending = false; LastError = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateRunner()
        {
            var go = new GameObject("CloudProgressSync") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
            ProgressEvents.Changed += Schedule;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != MainScene) return;
            // A restore that could not land mid-fight, or an account read that failed, retries here.
            if (_applyPending || OwnerDiffers()) Enqueue(() => ReconcileStep(true, false)).Forget();
            else Schedule();
        }

        /// <summary>Called by <see cref="FirebaseAuthService"/> once a session exists, possibly off
        /// the main thread; the runner picks it up on the next frame.</summary>
        internal static void NotifyAuthReady() => _authReady = true;

        /// <summary>Decides and applies who owns this phone's save. Once per account per launch
        /// unless <paramref name="force"/>; an applied restore reloads Main when it is on screen and
        /// <paramref name="reloadMain"/> (Settings reloads Main itself after a switch).</summary>
        public static Task ReconcileAsync(bool force = false, bool reloadMain = true)
            => Enqueue(() => !force && _reconciledUid != null && _reconciledUid == CurrentUid ? Task.CompletedTask : ReconcileStep(reloadMain, false));

        /// <summary>Uploads now if the save changed since the cloud copy.</summary>
        public static Task FlushAsync() => Enqueue(() => UploadStep(false));

        /// <summary>
        /// Leaves the account: the latest progress is uploaded first (a failure throws
        /// <see cref="CloudSaveException"/> and nothing changes), then the phone starts as a new guest.
        /// </summary>
        public static Task SignOutAsync(FirebaseAuthService auth) => Enqueue(async () =>
        {
            if (!auth.IsAnonymous && Owner == auth.Uid) await UploadStep(true);
            auth.SignOut();
            ForgetLocal();
        });

        /// <summary>After the account was deleted: nothing of it stays on the phone.</summary>
        public static Task ForgetAfterDeleteAsync() => Enqueue(() => { ForgetLocal(); return Task.CompletedTask; });

        private static void ForgetLocal()
        {
            ProgressStore.ResetToFresh(null);
            foreach (var key in new[] { OwnerKey, OwnerAnonymousKey, RevKey, SyncedKey, GhostKey, RejectedKey })
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            _reconciledUid = null; _applyPending = false; _dueAt = -1;
        }

        // ── Steps (main thread, one at a time) ────────────────────────────────────────────────

        private static Task Enqueue(Func<Task> step)
        {
            var previous = _work;
            _work = Run(previous, step);
            return _work;
        }

        private static async Task Run(Task previous, Func<Task> step)
        {
            try { await previous; } catch { /* reported by its own caller */ }
            await step();
        }

        private static async Task ReconcileStep(bool reloadMain, bool afterConflict)
        {
            var auth = Auth;
            string uid = auth?.Uid;
            if (string.IsNullOrEmpty(uid)) return;
            bool anonymous = auth.IsAnonymous;
            var local = LocalState(uid);

            LoadResponse loaded = null;
            ProgressSnapshot cloudSnapshot = null;
            var cloud = CloudSaveState.Unknown;
            if (!anonymous)
            {
                try
                {
                    // The functions refuse guest tokens; a guest that just linked may still hold one.
                    if (_signedInTokenUid != uid) { await auth.RefreshTokenAsync(); _signedInTokenUid = uid; }
                    loaded = await LeagueClient.Call<Empty, LoadResponse>("loadProgress", new Empty(), uid);
                    if (loaded.exists)
                    {
                        cloudSnapshot = ProgressSnapshot.FromJson(ProgressCodec.Unpack(loaded.snapshot));
                        cloud = CloudSaveState.At(loaded.rev, cloudSnapshot.Rank());
                    }
                    else cloud = CloudSaveState.Missing;
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    Debug.LogWarning($"[CloudSave] Could not read the account's progress: {e.Message}");
                    return;
                }
            }

            var action = CloudSaveRules.Decide(local, uid, anonymous, cloud);
            Debug.Log($"[CloudSave] {action} (local {local.Rank}, rev {local.Rev}{(local.Dirty ? ", changed" : "")}; " +
                      $"cloud {(cloud.Exists ? cloud.Rank + ", rev " + cloud.Rev : cloud.Known ? "none" : "unknown")}).");
            switch (action)
            {
                case CloudSaveAction.AdoptLocal:
                    SetOwner(uid, anonymous, cloud.Exists ? cloud.Rev : 0);
                    PlayerPrefs.DeleteKey(SyncedKey); PlayerPrefs.DeleteKey(GhostKey);
                    if (!anonymous) await UploadStep(false, afterConflict);
                    break;
                case CloudSaveAction.Upload:
                    // When the account moved on elsewhere and this copy wins, it goes up over that
                    // revision. Also where a guest that just linked becomes a signed-in owner.
                    SetOwner(uid, false, cloud.Rev);
                    await UploadStep(false, afterConflict);
                    break;
                case CloudSaveAction.ApplyCloud:
                    if (!CanReplaceNow()) { _applyPending = true; return; }
                    try { ProgressStore.Apply(cloudSnapshot, loaded.hasGhost ? ProgressCodec.Unpack(loaded.ghost) : "", uid); }
                    catch (Exception e)
                    {
                        // A copy this version cannot load (say, from a newer app) is left alone, and so is the phone.
                        LastError = e.Message;
                        Debug.LogError($"[CloudSave] The account's progress could not be restored: {e.Message}");
                        return;
                    }
                    SetOwner(uid, false, loaded.rev);
                    MarkSynced(uid);
                    Replaced(reloadMain);
                    break;
                case CloudSaveAction.ResetFresh:
                    if (!CanReplaceNow()) { _applyPending = true; return; }
                    ProgressStore.ResetToFresh(uid);
                    SetOwner(uid, anonymous, 0);
                    Replaced(reloadMain);
                    break;
            }
            _applyPending = false;
            _reconciledUid = uid;
        }

        private static async Task UploadStep(bool forSignOut, bool afterConflict = false)
        {
            var auth = Auth;
            string uid = auth?.Uid;
            if (string.IsNullOrEmpty(uid) || auth.IsAnonymous || Owner != uid) return;
            string json = ProgressStore.Capture(uid).ToJson(), ghost = ProgressStore.CaptureGhost();
            string hash = ProgressCodec.Hash(json), ghostHash = ProgressCodec.Hash(ghost);
            if (hash == PlayerPrefs.GetString(SyncedKey, "") && ghostHash == PlayerPrefs.GetString(GhostKey, "")) return;
            if (hash + ghostHash == PlayerPrefs.GetString(RejectedKey, "")) return;

            bool sendGhost = ghostHash != PlayerPrefs.GetString(GhostKey, "");
            var request = new SaveRequest { baseRev = Rev, snapshot = ProgressCodec.Pack(json), sendGhost = sendGhost, ghost = sendGhost ? ProgressCodec.Pack(ghost) : "" };
            try
            {
                var response = await LeagueClient.Call<SaveRequest, SaveResponse>("saveProgress", request, uid);
                SetRev(response.rev);
                PlayerPrefs.SetString(SyncedKey, hash);
                if (sendGhost) PlayerPrefs.SetString(GhostKey, ghostHash);
                PlayerPrefs.Save();
                LastError = null;
                Debug.Log($"[CloudSave] Uploaded rev {response.rev}{(sendGhost ? " with the ghost" : "")}.");
            }
            catch (LeagueCallException e) when (e.Status == "ABORTED" && !afterConflict)
            {
                // Another phone saved first: decide again against what it wrote.
                await ReconcileStep(!forSignOut, true);
            }
            catch (LeagueCallException e) when (e.Status == "INVALID_ARGUMENT" || e.Status == "OUT_OF_RANGE" || e.Status == "FAILED_PRECONDITION")
            {
                // Retrying the same copy cannot succeed; the next change tries again.
                PlayerPrefs.SetString(RejectedKey, hash + ghostHash);
                PlayerPrefs.Save();
                LastError = e.Message;
                Debug.LogWarning($"[CloudSave] The cloud refused this save ({e.Status}): {e.Message}");
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogWarning($"[CloudSave] Upload failed, will retry: {e.Message}");
                if (forSignOut)
                    throw new CloudSaveException("Your latest progress isn't saved yet. Connect to the internet and try again.");
                _dueAt = Time.realtimeSinceStartup + RetrySec;
            }
        }

        // ── State ─────────────────────────────────────────────────────────────────────────────

        private static FirebaseAuthService Auth => ServiceLocator.TryGet<FirebaseAuthService>(out var auth) ? auth : null;
        private static string CurrentUid => Auth?.Uid;
        private static string Owner => PlayerPrefs.GetString(OwnerKey, "");
        private static long Rev => long.TryParse(PlayerPrefs.GetString(RevKey, "0"), out var rev) ? rev : 0;

        private static bool OwnerDiffers()
        {
            var auth = Auth;
            return auth != null && auth.IsSignedIn && Owner != auth.Uid;
        }

        private static LocalSaveState LocalState(string uid)
        {
            var snapshot = ProgressStore.Capture(uid);
            string hash = ProgressCodec.Hash(snapshot.ToJson()), ghostHash = ProgressCodec.Hash(ProgressStore.CaptureGhost());
            return new LocalSaveState
            {
                OwnerUid = Owner,
                OwnerAnonymous = PlayerPrefs.GetInt(OwnerAnonymousKey, 1) != 0,
                Rev = Rev,
                Dirty = hash != PlayerPrefs.GetString(SyncedKey, "") || ghostHash != PlayerPrefs.GetString(GhostKey, ""),
                Rank = snapshot.Rank(),
            };
        }

        private static void SetOwner(string uid, bool anonymous, long rev)
        {
            PlayerPrefs.SetString(OwnerKey, uid);
            PlayerPrefs.SetInt(OwnerAnonymousKey, anonymous ? 1 : 0);
            PlayerPrefs.DeleteKey(RejectedKey);
            SetRev(rev);
        }

        private static void SetRev(long rev)
        {
            PlayerPrefs.SetString(RevKey, rev.ToString(System.Globalization.CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        /// <summary>The save now equals the cloud copy just restored.</summary>
        private static void MarkSynced(string uid)
        {
            PlayerPrefs.SetString(SyncedKey, ProgressCodec.Hash(ProgressStore.Capture(uid).ToJson()));
            PlayerPrefs.SetString(GhostKey, ProgressCodec.Hash(ProgressStore.CaptureGhost()));
            PlayerPrefs.Save();
        }

        /// <summary>The save may be swapped only where nothing holds the old one: before Boot routes
        /// or on Main, which is then reloaded. A fight keeps its save until it is over.</summary>
        private static bool CanReplaceNow()
        {
            string scene = SceneManager.GetActiveScene().name;
            return scene == MainScene || scene == BootScene;
        }

        private static void Replaced(bool reloadMain)
        {
            if (reloadMain && SceneManager.GetActiveScene().name == MainScene)
                SceneManager.LoadScene(MainScene, LoadSceneMode.Single);
        }

        private static void Schedule()
        {
            if (!IsLinkedOnThisPhone) return;
            _dueAt = Time.realtimeSinceStartup + DebounceSec;
        }

        private static async void Forget(this Task task)
        {
            try { await task; }
            catch (Exception e) { Debug.LogWarning($"[CloudSave] {e.Message}"); }
        }

        private sealed class Runner : MonoBehaviour
        {
            private void Update()
            {
                if (_authReady)
                {
                    _authReady = false;
                    ReconcileAsync().Forget();
                }
                if (_dueAt > 0 && Time.realtimeSinceStartup >= _dueAt)
                {
                    _dueAt = -1;
                    FlushAsync().Forget();
                }
            }

            // Phones kill backgrounded apps without warning: the last change goes up on the way out.
            private void OnApplicationPause(bool paused) { if (paused && IsLinkedOnThisPhone) FlushAsync().Forget(); }
            private void OnApplicationFocus(bool focused) { if (!focused && IsLinkedOnThisPhone) FlushAsync().Forget(); }
        }
    }
}
