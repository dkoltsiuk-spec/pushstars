const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { FieldValue } = require('firebase-admin/firestore');
const rules = require('./progressRules');

const db = admin.firestore();

// Cloud copy of a signed-in account's progress (docs/architecture/cloud-progress.md).
//   users/{uid}/progress/state  { rev, schema, snapshot (gzip+base64), summary, updatedAtMs, ... }
//   users/{uid}/progress/ghost  { data (gzip+base64), reps, updatedAtMs }
// Clients read and write only through these functions; Firestore rules deny direct writes.
const stateRef = uid => db.doc(`users/${uid}/progress/state`);
const ghostRef = uid => db.doc(`users/${uid}/progress/ghost`);

function caller(context) {
  if (!context.auth) throw new functions.https.HttpsError('unauthenticated', 'Sign in required.');
  // A guest's UID dies with the app; only accounts that can sign in again keep a cloud copy.
  if (context.auth.token && context.auth.token.firebase && context.auth.token.firebase.sign_in_provider === 'anonymous') {
    throw new functions.https.HttpsError('failed-precondition', 'Sign in with Apple, Google or email to save progress.');
  }
  return context.auth.uid;
}

const asHttps = (e) => e instanceof rules.RuleError ? new functions.https.HttpsError(e.code, e.message) : e;

exports.loadProgress = functions.https.onCall(async (data, context) => {
  const uid = caller(context);
  const [state, ghost] = await db.getAll(stateRef(uid), ghostRef(uid));
  if (!state.exists) return { exists: false, rev: 0, snapshot: '', hasGhost: false, ghost: '' };
  const s = state.data();
  return {
    exists: true, rev: s.rev, snapshot: s.snapshot,
    hasGhost: ghost.exists, ghost: ghost.exists ? ghost.data().data : '',
  };
});

// Saves over revision `baseRev` only; a phone that read an older copy gets 'aborted' and decides
// again (CloudSaveRules on the client). The revision counts saves, never time.
exports.saveProgress = functions.https.onCall(async (data, context) => {
  const uid = caller(context);
  const baseRev = Number(data && data.baseRev);
  if (!Number.isInteger(baseRev) || baseRev < 0) throw new functions.https.HttpsError('invalid-argument', 'baseRev is required.');
  const sendGhost = Boolean(data && data.sendGhost);
  let summary, ghost = null;
  try {
    summary = rules.inspectSnapshot(data && data.snapshot);
    if (sendGhost && data.ghost) ghost = rules.inspectGhost(data.ghost);
  } catch (e) {
    throw asHttps(e);
  }

  return db.runTransaction(async tx => {
    const snap = await tx.get(stateRef(uid));
    const previous = snap.exists ? snap.data() : null;
    const currentRev = previous ? previous.rev : 0;
    if (baseRev !== currentRev) {
      throw new functions.https.HttpsError('aborted', `Progress changed elsewhere (rev ${currentRev}).`);
    }
    const now = Date.now();
    try {
      rules.checkPace(previous && previous.summary, summary, previous ? (now - previous.updatedAtMs) / 1000 : 0);
    } catch (e) {
      throw asHttps(e);
    }
    const rev = currentRev + 1;
    tx.set(stateRef(uid), {
      rev, schema: 1, snapshot: data.snapshot, summary, updatedAtMs: now,
      updatedAt: FieldValue.serverTimestamp(),
    });
    if (sendGhost) {
      if (ghost) tx.set(ghostRef(uid), { data: data.ghost, reps: ghost.reps, updatedAtMs: now });
      else tx.delete(ghostRef(uid));
    }
    return { rev };
  });
});
