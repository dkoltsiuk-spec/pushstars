using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Firebase.Firestore;
using PushStars.Core;
using UnityEngine;

namespace PushStars.Services
{
    /// <summary>
    /// Paginated match history for the signed-in player. One <c>array-contains</c> query on
    /// <c>matches.playerUids</c> + <c>OrderBy createdAt DESC</c> (backed by the composite index
    /// from phase 05). Call <see cref="Reset"/> then <see cref="GetNextPageAsync"/> repeatedly
    /// for "load more". Returns an empty list until real matches exist (phases 12–14).
    /// </summary>
    public class MatchHistoryRepository
    {
        public const int PageSize = 20;

        private DocumentSnapshot _cursor;
        public bool ReachedEnd { get; private set; }

        public void Reset()
        {
            _cursor = null;
            ReachedEnd = false;
        }

        public async Task<List<MatchRecord>> GetNextPageAsync()
        {
            var result = new List<MatchRecord>();
            if (ReachedEnd) return result;

            if (!ServiceLocator.TryGet<FirestoreService>(out var fs) ||
                !ServiceLocator.TryGet<FirebaseAuthService>(out var auth) ||
                string.IsNullOrEmpty(auth.Uid) || fs.Db == null)
            {
                ReachedEnd = true;
                return result;
            }

            try
            {
                Query q = fs.Db.Collection("matches")
                    .WhereArrayContains("playerUids", auth.Uid)
                    .OrderByDescending("createdAt")
                    .Limit(PageSize);
                if (_cursor != null) q = q.StartAfter(_cursor);

                var snap = await q.GetSnapshotAsync();
                foreach (var doc in snap.Documents)
                {
                    result.Add(Map(doc, auth.Uid));
                    _cursor = doc;
                }
                if (result.Count < PageSize) ReachedEnd = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[MatchHistoryRepo] {e.Message}");
                ReachedEnd = true;
            }
            return result;
        }

        private static MatchRecord Map(DocumentSnapshot d, string uid)
        {
            T Field<T>(string key, T fallback) => d.ContainsField(key) ? d.GetValue<T>(key) : fallback;

            bool isA = Field("playerAUid", "") == uid;
            int myReps  = (int)(isA ? Field<long>("playerAReps", 0) : Field<long>("playerBReps", 0));
            int oppReps = (int)(isA ? Field<long>("playerBReps", 0) : Field<long>("playerAReps", 0));
            int delta   = (int)(isA ? Field<long>("playerATrophyDelta", 0) : Field<long>("playerBTrophyDelta", 0));

            DateTime created = DateTime.UtcNow;
            if (d.ContainsField("createdAt"))
            {
                try { created = d.GetValue<Timestamp>("createdAt").ToDateTime(); } catch { /* ghost mode null */ }
            }

            return new MatchRecord
            {
                MatchId      = d.Id,
                Mode         = Field("mode", "pvp"),
                Exercise     = Field("exercise", "pushups"),
                Won          = Field("winnerUid", "") == uid,
                MyReps       = myReps,
                OpponentReps = oppReps,
                TrophyDelta  = delta,
                CreatedAt    = created,
            };
        }
    }
}
