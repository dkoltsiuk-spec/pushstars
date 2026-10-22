const { FieldValue } = require('firebase-admin/firestore');
const { nameFor } = require('./playerNames');

// The canonical default profile document. Shared by onUserCreated (Auth trigger) and
// ensureProfile (callable backfill), so the schema lives in exactly one place.
// Without a provider name the player starts with the nickname the app derives from the uid.
function defaultProfile(displayName, uid) {
  return {
    displayName: displayName || nameFor(uid),
    avatarState: {},
    rank: 'bronze',
    trophies: 0,
    xp: 0,
    aura: 0,
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
