const functions = require('firebase-functions/v1');
const admin = require('./admin');

const db = admin.firestore();
const { FieldValue } = admin.firestore;

// Seeds a default profile document when a new Auth user is created (anonymous or upgraded).
// Idempotent: skips if the doc already exists. Admin SDK bypasses Security Rules, so this
// is the only writer of users/{uid} on create (rules deny client create).
exports.onUserCreated = functions.auth.user().onCreate(async (user) => {
  const ref = db.doc(`users/${user.uid}`);
  const snap = await ref.get();
  if (snap.exists) return null;

  await ref.set({
    displayName: user.displayName || 'Игрок',
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
  });

  functions.logger.info(`Seeded profile for ${user.uid}`);
  return null;
});
