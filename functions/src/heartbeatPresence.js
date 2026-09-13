const functions = require('firebase-functions/v1');
const admin = require('./admin');

// Server-only collection (the existing Firestore rules deny client access).
// One document per authenticated account counts multiple devices only once.
// Expired documents never contribute, even before optional TTL cleanup runs.
exports.heartbeatPresence = functions.https.onCall(async (_data, context) => {
  if (!context.auth) {
    throw new functions.https.HttpsError('unauthenticated', 'Sign in required.');
  }
  const db = admin.firestore();
  const now = Date.now();
  await db.doc(`onlinePresence/${context.auth.uid}`).set({
    expiresAt: admin.firestore.Timestamp.fromMillis(now + 75000),
  });
  const snapshot = await db.collection('onlinePresence')
    .where('expiresAt', '>', admin.firestore.Timestamp.fromMillis(now))
    .count().get();
  return { online: snapshot.data().count };
});
