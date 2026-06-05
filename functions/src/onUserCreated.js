const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { defaultProfile } = require('./profileDefaults');

const db = admin.firestore();

// Seeds a default profile document when a new Auth user is created (anonymous or upgraded).
// Idempotent: skips if the doc already exists. Admin SDK bypasses Security Rules, so this
// is the only writer of users/{uid} on create (rules deny client create).
exports.onUserCreated = functions.auth.user().onCreate(async (user) => {
  const ref = db.doc(`users/${user.uid}`);
  const snap = await ref.get();
  if (snap.exists) return null;

  await ref.set(defaultProfile(user.displayName));
  functions.logger.info(`Seeded profile for ${user.uid}`);
  return null;
});
