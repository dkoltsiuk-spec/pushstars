using System.Threading.Tasks;
using Firebase;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>
    /// Owns the Firebase app lifecycle: resolves native dependencies and exposes the
    /// initialised <see cref="FirebaseApp"/>. Everything else (Auth, Firestore) only runs
    /// once <see cref="IsReady"/> is true.
    ///
    /// In the Unity Editor Firebase uses its desktop libraries and reads the config from
    /// the GoogleService-Info.plist / google-services.json placed under Assets/.
    /// </summary>
    public class FirebaseService : IService
    {
        public bool IsReady { get; private set; }
        public FirebaseApp App { get; private set; }

        public async Task InitializeAsync()
        {
            var status = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (status == DependencyStatus.Available)
            {
                App = FirebaseApp.DefaultInstance;
                IsReady = true;
                Debug.Log("[Firebase] Dependencies available — FirebaseApp ready.");
            }
            else
            {
                IsReady = false;
                Debug.LogError($"[Firebase] Could not resolve dependencies: {status}");
            }
        }
    }
}
