const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { DISPLAY_NAME_MIN, DISPLAY_NAME_MAX } = require('./constants');

const db = admin.firestore();

// Atomically renames the player across users/{uid} and their leaderboard entry.
// Recommended over a client batch (the client can't write leaderboard under the rules).
exports.updateDisplayName = functions.https.onCall(async (data, context) => {
  if (!context.auth) {
    throw new functions.https.HttpsError('unauthenticated', 'Sign in required.');
  }

  let name = data && data.displayName;
  if (typeof name !== 'string') {
    throw new functions.https.HttpsError('invalid-argument', 'displayName must be a string.');
  }
  name = name.trim();
  if (name.length < DISPLAY_NAME_MIN || name.length > DISPLAY_NAME_MAX) {
    throw new functions.https.HttpsError(
      'invalid-argument',
      `displayName must be ${DISPLAY_NAME_MIN}–${DISPLAY_NAME_MAX} characters.`,
    );
  }

  const uid = context.auth.uid;
  const userRef = db.doc(`users/${uid}`);
  const userSnap = await userRef.get();
  if (!userSnap.exists) {
    throw new functions.https.HttpsError('not-found', 'User profile not found.');
  }

  const league = userSnap.data().leaderboardLeague || userSnap.data().rank || 'bronze';

  const batch = db.batch();
  batch.update(userRef, { displayName: name });

  // Mirror into the leaderboard entry only if it already exists (created later by the
  // leaderboard Cloud Function in phase 11).
  const lbRef = db.doc(`leaderboard/${league}/players/${uid}`);
  const lbSnap = await lbRef.get();
  if (lbSnap.exists) {
    batch.update(lbRef, { displayName: name });
  }

  await batch.commit();
  return { status: 'ok' };
});
