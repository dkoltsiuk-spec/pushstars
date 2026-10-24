// Friends — the players this one has fought in a friend-code duel, kept so the next duel is one
// tap instead of a code read out loud (docs/design/friends.md).
//
//   onFriendDuelWrite  → liveDuels/{id}: the guest took the seat of a friend duel — both players
//                        land on each other's list (friends/{uid}/list/{friendUid}); the code
//                        a friend was called into is gone — the call (friendInvites/{uid}) too
//   getFriends         → the list with current names, trophies and who is online right now
//   inviteFriend       → the host of an open code calls one friend into it: an invitation the
//                        friend's app listens to (firestore.rules), plus a push
//   dismissFriendInvite → the called friend says no (or has seen it): the call goes
//   removeFriend       → drops a friend from both lists
//
// Online is the presence heartbeat (heartbeatPresence.js, onlinePresence/{uid}). The rules of
// who is a friend and who may be called live in friendsRules.js; this file only reads and writes.
const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { FieldValue } = require('firebase-admin/firestore');
const F = require('./friendsRules');
const { displayNameOf: nameOf } = require('./playerNames');
const { sendToFriend } = require('./push');
const db = admin.firestore();

const fail = (code, message) => { throw new functions.https.HttpsError(code, message); };
const uidOf = context => context.auth?.uid || fail('unauthenticated', 'Sign in required.');
const validUid = uid => typeof uid === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(uid);
const friendRef = (uid, friendUid) => db.doc(`friends/${uid}/list/${friendUid}`);
const inviteRef = uid => db.doc(`friendInvites/${uid}`);
// Next to Firestore (eur3), like the queue: the list is read every time the main screen shows.
const REGION = 'europe-west1';

exports.onFriendDuelWrite = functions.region(REGION).firestore.document('liveDuels/{matchId}').onWrite(async (change, context) => {
  const before = change.before.exists ? change.before.data() : null;
  const after = change.after.exists ? change.after.data() : null;
  const work = [];
  if (F.joinedFriendDuel(before, after)) {
    const batch = db.batch();
    for (const { owner, friend } of F.entriesOf(after, Date.now())) {
      batch.set(friendRef(owner, friend.uid), { ...friend, duels: FieldValue.increment(1) }, { merge: true });
    }
    work.push(batch.commit());
  }
  if (F.inviteSpent(before, after)) {
    work.push(db.collection('friendInvites').where('matchId', '==', context.params.matchId).get()
      .then(snap => Promise.all(snap.docs.map(d => d.ref.delete()))));
  }
  await Promise.all(work);
});

exports.getFriends = functions.region(REGION).https.onCall(async (_data, context) => {
  const uid = uidOf(context);
  const list = await db.collection(`friends/${uid}/list`).orderBy('lastDuelMs', 'desc').limit(F.LIST_MAX).get();
  if (list.empty) return { friends: [], serverTimeMs: Date.now() };
  const entries = list.docs.map(d => ({ ...d.data(), uid: d.id }));
  const [profiles, presence] = await Promise.all([
    db.getAll(...entries.map(e => db.doc(`users/${e.uid}`))),
    db.getAll(...entries.map(e => db.doc(`onlinePresence/${e.uid}`))),
  ]);
  const now = Date.now();
  const rows = entries.map((e, i) => F.row(e,
    profiles[i].exists ? profiles[i].data() : null, presence[i].exists ? presence[i].data() : null, now));
  return { friends: F.sortRows(rows), serverTimeMs: now };
});

exports.inviteFriend = functions.region(REGION).https.onCall(async (data, context) => {
  const uid = uidOf(context);
  const friendUid = data?.friendUid, codeValue = data?.code;
  if (!validUid(friendUid) || friendUid === uid) fail('invalid-argument', 'Invalid friend.');
  if (typeof codeValue !== 'string' || !/^\d{6}$/.test(codeValue)) fail('invalid-argument', 'Enter a 6-digit code.');
  const profile = await db.doc(`users/${uid}`).get();
  const fromName = nameOf(profile.exists ? profile.data() : null, uid);
  const sent = await db.runTransaction(async tx => {
    const [friendSnap, codeSnap] = await tx.getAll(friendRef(uid, friendUid), db.doc(`liveDuelCodes/${codeValue}`));
    const code = codeSnap.exists ? codeSnap.data() : null;
    const matchSnap = code && typeof code.matchId === 'string' ? await tx.get(db.doc(`liveDuels/${code.matchId}`)) : null;
    const match = matchSnap && matchSnap.exists ? matchSnap.data() : null;
    const now = Date.now();
    const error = F.inviteError({ uid, friendUid, friend: friendSnap.exists ? friendSnap.data() : null, code, match, now });
    if (error) fail(error[0], error[1]);
    tx.update(friendRef(uid, friendUid), { lastInvitedAtMs: now });
    tx.set(inviteRef(friendUid), {
      fromUid: uid, fromName, fromHero: match.hostHero || 0, fromGender: match.hostGender === 1 ? 1 : 0,
      code: codeValue, matchId: code.matchId, atMs: now, expiresAtMs: match.codeExpiresAtMs,
    });
    return { now, expiresAtMs: match.codeExpiresAtMs };
  });
  const ttlSec = Math.max(10, Math.floor((sent.expiresAtMs - sent.now) / 1000));
  const pushed = await sendToFriend(friendUid, { name: fromName }, ttlSec, sent.now).catch(() => false);
  return { status: 'sent', pushed, expiresAtMs: sent.expiresAtMs };
});

exports.dismissFriendInvite = functions.region(REGION).https.onCall(async (data, context) => {
  const uid = uidOf(context);
  await db.runTransaction(async tx => {
    const snap = await tx.get(inviteRef(uid));
    // Only the call that was seen: a newer one that arrived meanwhile stays.
    if (snap.exists && (!data?.matchId || snap.data().matchId === data.matchId)) tx.delete(inviteRef(uid));
  });
  return { status: 'ok' };
});

exports.removeFriend = functions.region(REGION).https.onCall(async (data, context) => {
  const uid = uidOf(context);
  const friendUid = data?.friendUid;
  if (!validUid(friendUid) || friendUid === uid) fail('invalid-argument', 'Invalid friend.');
  const batch = db.batch();
  batch.delete(friendRef(uid, friendUid));
  batch.delete(friendRef(friendUid, uid));
  await batch.commit();
  return { status: 'removed' };
});
