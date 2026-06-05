const admin = require('./admin');
const { FieldValue } = admin.firestore;

// The canonical default profile document. Shared by onUserCreated (Auth trigger) and
// ensureProfile (callable backfill), so the schema lives in exactly one place.
function defaultProfile(displayName) {
  return {
    displayName: displayName || 'Игрок',
    avatarState: {},
    rank: 'bronze',
    trophies: 0,
    xp: 0,
    winStreak: 0,
    totalWins: 0,
    totalLosses: 0,
    totalReps: 0,
    winRate: 0,
    leaderboardRank: 0,
    leaderboardLeague: 'bronze',
    lastMatchAt: null,
    createdAt: FieldValue.serverTimestamp(),
    fcmToken: '',
    offlineXpPending: 0,
    offlineXpSyncToken: '',
  };
}

module.exports = { defaultProfile };
