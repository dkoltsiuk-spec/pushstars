// Reports and blocks between players (App Store guideline 1.2; docs/architecture/moderation.md).
//
//   reportPlayer       → reports/{reporter_target}: the moderator's queue (tools/Moderation/reports.js).
//                        Enough different players reporting one name hide it at once.
//   setPlayerBlocked   → blocks/{uid}/list/{target} + blockedBy/{target}/list/{uid}: the two never
//                        meet again in the queue, its calls or a recorded fight; the friendship ends.
//   getBlockedPlayers  → the list for Settings
//
// The rules live in moderationRules.js; this file only reads and writes. Clients have no direct
// access to any of these collections (firestore.rules).
const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { FieldValue } = require('firebase-admin/firestore');
const M = require('./moderationRules');
const { displayNameOf: nameOf } = require('./playerNames');
const db = admin.firestore();

const fail = (code, message) => { throw new functions.https.HttpsError(code, message); };
const uidOf = context => context.auth?.uid || fail('unauthenticated', 'Sign in required.');
const REGION = 'europe-west1';
const blockRef = (uid, target) => db.doc(`blocks/${uid}/list/${target}`);
const blockedByRef = (uid, blocker) => db.doc(`blockedBy/${uid}/list/${blocker}`);

// Everyone `uid` must not be paired with or offered: blocked by them, or having blocked them.
async function avoidFor(uid) {
  const [mine, theirs] = await Promise.all([
    db.collection(`blocks/${uid}/list`).limit(M.BLOCK_LIST_MAX).get(),
    db.collection(`blockedBy/${uid}/list`).limit(M.BLOCK_LIST_MAX).get(),
  ]);
  return M.avoidSet(mine.docs.map(d => d.id), theirs.docs.map(d => d.id));
}

// Hides a player's chosen name everywhere it is stored; the generated name shows from then on.
// The monthly standings store a copy per season: only the current one is on screen.
async function hideName(uid) {
  const batch = db.batch();
  batch.set(db.doc(`users/${uid}`), { nameHidden: true }, { merge: true });
  await batch.commit();
}

exports.reportPlayer = functions.region(REGION).https.onCall(async (data, context) => {
  const uid = uidOf(context);
  const parsed = M.reportOf(data, uid, Date.now());
  if (!parsed) fail('invalid-argument', 'Invalid player.');
  const { id, report } = parsed;
  const counterRef = report.targetUid ? db.doc(`reportCounts/${report.targetUid}`) : null;
  const reporters = await db.runTransaction(async tx => {
    const reportRef = db.doc(`reports/${id}`);
    const [existing, counter] = await tx.getAll(reportRef, ...(counterRef ? [counterRef] : []));
    const known = counter?.exists ? counter.data().reporters || 0 : 0;
    if (existing.exists) return known;   // one player, one report
    tx.set(reportRef, report);
    if (counterRef) tx.set(counterRef, { reporters: FieldValue.increment(1), lastAtMs: report.createdAtMs }, { merge: true });
    return known + 1;
  });
  // In the log as well: an alert on this message reaches the moderator without opening anything.
  functions.logger.warn('player report', { id, reason: report.reason, where: report.where, reporters });
  if (M.hidesName(report, reporters)) await hideName(report.targetUid);
  return { status: 'received' };
});

exports.setPlayerBlocked = functions.region(REGION).https.onCall(async (data, context) => {
  const uid = uidOf(context);
  const target = data?.targetUid;
  if (!M.validUid(target) || target === uid) fail('invalid-argument', 'Invalid player.');
  const batch = db.batch();
  if (data?.blocked === false) {
    batch.delete(blockRef(uid, target));
    batch.delete(blockedByRef(target, uid));
  } else {
    const profile = await db.doc(`users/${target}`).get();
    batch.set(blockRef(uid, target), { name: nameOf(profile.exists ? profile.data() : null, target), atMs: Date.now() });
    batch.set(blockedByRef(target, uid), { atMs: Date.now() });
    // Blocking ends the friendship both ways, and any call of theirs still ringing.
    batch.delete(db.doc(`friends/${uid}/list/${target}`));
    batch.delete(db.doc(`friends/${target}/list/${uid}`));
    const invite = await db.doc(`friendInvites/${uid}`).get();
    if (invite.exists && invite.data().fromUid === target) batch.delete(invite.ref);
  }
  await batch.commit();
  return { status: 'ok' };
});

exports.getBlockedPlayers = functions.region(REGION).https.onCall(async (_data, context) => {
  const uid = uidOf(context);
  const list = await db.collection(`blocks/${uid}/list`).orderBy('atMs', 'desc').limit(M.BLOCK_LIST_MAX).get();
  return { players: list.docs.map(d => ({ uid: d.id, name: d.data().name || '' })) };
});

exports.avoidFor = avoidFor;
exports.hideName = hideName;
