using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>
    /// Thin wrapper over Cloud Firestore. Enables offline persistence (critical for the
    /// offline-training retention story) and centralises error logging. Later phases add
    /// typed read/write helpers for users / matches / leaderboard.
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

        /// <summary>
        /// Connectivity self-test: writes a ping doc and reads it back. Satisfies the
        /// phase-04 acceptance ("successful GetSnapshotAsync on a test document").
        /// </summary>
        public async Task<bool> SelfTestAsync()
        {
            try
            {
                var doc = Db.Collection("_diagnostics").Document("ping");
                await doc.SetAsync(new Dictionary<string, object>
                {
                    { "ts",     FieldValue.ServerTimestamp },
                    { "client", "unity-editor" },
                });
                var snap = await doc.GetSnapshotAsync();
                Debug.Log($"[Firestore] Self-test OK — ping doc exists={snap.Exists}.");
                return snap.Exists;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Firestore] Self-test failed: {e.Message}");
                return false;
            }
        }
    }
}
