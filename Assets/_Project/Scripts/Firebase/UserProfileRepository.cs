using System;
using System.Threading.Tasks;
using Firebase.Firestore;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>
    /// Reads the signed-in player's <c>users/{uid}</c> document into a <see cref="UserProfile"/>.
    /// Returns a defaults snapshot (<c>Exists = false</c>) when services aren't ready or the doc
    /// is missing — so the UI renders gracefully before any games are played.
    /// </summary>
    public class UserProfileRepository
    {
        public async Task<UserProfile> GetAsync()
        {
            if (!ServiceLocator.TryGet<FirestoreService>(out var fs) ||
                !ServiceLocator.TryGet<FirebaseAuthService>(out var auth) ||
                string.IsNullOrEmpty(auth.Uid) || fs.Db == null)
            {
                return new UserProfile { Exists = false };
            }

            try
            {
                var snap = await fs.Db.Collection("users").Document(auth.Uid).GetSnapshotAsync();
                return snap.Exists ? Map(snap) : new UserProfile { Exists = false };
            }
            catch (Exception e)
            {
                Debug.LogError($"[UserProfileRepo] {e.Message}");
                return new UserProfile { Exists = false };
            }
        }

        private static UserProfile Map(DocumentSnapshot d)
        {
            T Field<T>(string key, T fallback) => d.ContainsField(key) ? d.GetValue<T>(key) : fallback;

            return new UserProfile
            {
                Exists      = true,
                DisplayName = Field("displayName", "Игрок"),
                Rank        = Field("rank", "bronze"),
                Trophies    = Field<long>("trophies", 0),
                Xp          = Field<long>("xp", 0),
                Aura        = Field<long>("aura", 0),
                WinStreak   = Field<long>("winStreak", 0),
                TotalWins   = Field<long>("totalWins", 0),
                TotalLosses = Field<long>("totalLosses", 0),
                TotalReps   = Field<long>("totalReps", 0),
                WinRate     = Field<double>("winRate", 0),
            };
        }
    }
}
