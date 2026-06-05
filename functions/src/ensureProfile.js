const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { defaultProfile } = require('./profileDefaults');

const db = admin.firestore();

// Backfill safety net: the client calls this once after sign-in so a profile always exists,
// even for accounts created before onUserCreated was deployed or if that trigger ever fails.
// Idempotent — returns 'exists' when the doc is already there.
exports.ensureProfile = functions.https.onCall(async (data, context) => {
  if (!context.auth) {
    throw new functions.https.HttpsError('unauthenticated', 'Sign in required.');
  }

  const ref = db.doc(`users/${context.auth.uid}`);
  const snap = await ref.get();
  if (snap.exists) return { status: 'exists' };

  const name = context.auth.token && context.auth.token.name;
  await ref.set(defaultProfile(name));
  functions.logger.info(`Backfilled profile for ${context.auth.uid}`);
  return { status: 'created' };
});
