using Firebase.Firestore;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>
    /// Thin wrapper over Cloud Firestore. Enables offline persistence (critical for the
    /// offline-training retention story). Later phases add typed read/write helpers for
    /// users / matches / leaderboard.
    /// </summary>
    public class FirestoreService : IService
    {
        public FirebaseFirestore Db { get; private set; }

        /// <summary>Must run before any Firestore operation — settings are locked after first use.</summary>
        public void Configure()
        {
            Db = FirebaseFirestore.DefaultInstance;
            Db.Settings.PersistenceEnabled = true; // on by default on mobile; set explicitly
            Debug.Log("[Firestore] Configured — offline persistence enabled.");
        }
    }
}
