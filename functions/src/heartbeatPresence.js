const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { Timestamp } = require('firebase-admin/firestore');

// Server-only collection (the existing Firestore rules deny client access).
// One document per authenticated account counts multiple devices only once.
// Expired documents never contribute, even before optional TTL cleanup runs.
// Each document also keeps the UTC day of its last heartbeat: the accounts whose day is today are
// the players who were in the game today (the search screen shows it — online alone is small at
// launch). They are counted, not incremented into one shared document: right after 00:00 UTC every
// online phone sends its first heartbeat of the day within 30 s, more than one document can take.
// Both counts cost a read per 1000 matching accounts, so once either passes COUNT_CACHE_FROM an
// instance answers from its last count for COUNT_CACHE_MS instead of recounting every heartbeat.
const COUNT_CACHE_FROM = 1000, COUNT_CACHE_MS = 20000;
let cached = null; // { day, atMs, online, today }

exports.heartbeatPresence = functions.https.onCall(async (_data, context) => {
  if (!context.auth) {
    throw new functions.https.HttpsError('unauthenticated', 'Sign in required.');
  }
  const db = admin.firestore();
  const now = Date.now();
  const day = new Date(now).toISOString().slice(0, 10);
  await db.doc(`onlinePresence/${context.auth.uid}`).set({
    expiresAt: Timestamp.fromMillis(now + 75000),
    day,
  });
  const small = cached && cached.online < COUNT_CACHE_FROM && cached.today < COUNT_CACHE_FROM;
  if (!cached || small || cached.day !== day || now - cached.atMs >= COUNT_CACHE_MS) {
    const presence = db.collection('onlinePresence');
    const [online, today] = await Promise.all([
      presence.where('expiresAt', '>', Timestamp.fromMillis(now)).count().get(),
      presence.where('day', '==', day).count().get(),
    ]);
    cached = { day, atMs: now, online: online.data().count, today: today.data().count };
  }
  // A reused count may predate this phone's own heartbeat, which is online and in today's figure.
  return { online: Math.max(cached.online, 1), today: Math.max(cached.today, 1) };
});
