const functions = require('firebase-functions/v1');
const admin = require('./admin');

const db = admin.firestore();
const { FieldValue } = admin.firestore;

// Idempotent offline-XP sync. The client generates a syncToken once per accumulation batch
// and keeps it until the call succeeds; replaying the same token is a no-op. Wrapped in a
// transaction so concurrent retries can't double-credit.
exports.syncOfflineXp = functions.https.onCall(async (data, context) => {
  if (!context.auth) {
    throw new functions.https.HttpsError('unauthenticated', 'Sign in required.');
  }

  const xpAmount = Number(data && data.xpAmount);
  const syncToken = data && data.syncToken;

  if (!Number.isFinite(xpAmount) || xpAmount <= 0) {
    throw new functions.https.HttpsError('invalid-argument', 'xpAmount must be a positive number.');
  }
  if (!syncToken || typeof syncToken !== 'string') {
    throw new functions.https.HttpsError('invalid-argument', 'syncToken is required.');
  }

  const userRef = db.doc(`users/${context.auth.uid}`);

  return db.runTransaction(async (tx) => {
    const snap = await tx.get(userRef);
    if (!snap.exists) {
      throw new functions.https.HttpsError('not-found', 'User profile not found.');
    }
    if (snap.data().offlineXpSyncToken === syncToken) {
      return { status: 'already_synced' }; // duplicate call — do nothing
    }

    tx.update(userRef, {
      xp: FieldValue.increment(xpAmount),
      offlineXpPending: 0,
      offlineXpSyncToken: syncToken,
    });
    return { status: 'ok' };
  });
});
