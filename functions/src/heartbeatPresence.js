const functions = require('firebase-functions/v1');
const admin = require('./admin');

// Server-only collection (the existing Firestore rules deny client access).
// One document per authenticated account counts multiple devices only once.
// Expired documents never contribute, even before optional TTL cleanup runs.
// The first heartbeat of a UTC day also counts the account into activeDays/{day}: the search
// screen shows how many players were in the game today (online alone is small at launch).
exports.heartbeatPresence = functions.https.onCall(async (_data, context) => {
  if (!context.auth) {
    throw new functions.https.HttpsError('unauthenticated', 'Sign in required.');
  }
  const db = admin.firestore();
  const now = Date.now();
  const day = new Date(now).toISOString().slice(0, 10);
  const presence = db.doc(`onlinePresence/${context.auth.uid}`);
  const previous = await presence.get();
  await presence.set({
    expiresAt: admin.firestore.Timestamp.fromMillis(now + 75000),
    day,
  });
  const today = db.doc(`activeDays/${day}`);
  if (!previous.exists || previous.data().day !== day) {
    await today.set({ count: admin.firestore.FieldValue.increment(1) }, { merge: true });
  }
  const [snapshot, active] = await Promise.all([
    db.collection('onlinePresence')
      .where('expiresAt', '>', admin.firestore.Timestamp.fromMillis(now))
      .count().get(),
    today.get(),
  ]);
  return { online: snapshot.data().count, today: active.exists ? active.data().count || 0 : 0 };
});
