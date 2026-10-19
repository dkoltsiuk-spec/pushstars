// Live duel — server side of phases 13–14 (docs/plan/phase-13-photon-server-side.md).
//
// A friend duel is bound to the server before either phone touches Photon:
//   createLiveDuel  → host gets a six-digit code and a match (liveDuels/{matchId})
//   peekLiveDuel    → guest sees who invited before committing
//   joinLiveDuel    → guest takes the second seat; both phones then enter Photon room pd_{matchId}
//   startLiveDuel   → both phones report the shared start; the set is timed from here
//   finishLiveDuel  → each phone sends its own rep timeline; settles once the evidence is in
//   onMatchFinished → Photon webhook (Join / Leave / Close) with X-Photon-Token: who really left
//   photonAuth      → Photon custom authentication: the Photon UserId is the Firebase uid
//   cleanupMatchmaking → expires unused codes, settles duels whose results never all arrived
//
// The rules of the outcome live in liveDuelRules.js; this file only reads and writes.
const crypto = require('crypto');
const functions = require('firebase-functions/v1');
const admin = require('./admin');
const C = require('./constants');
const R = require('./liveDuelRules');
const { cleanClapTimes } = require('./clapTimes');
const db = admin.firestore();
const { Timestamp } = admin.firestore;

const FAR = 8640000000000000; // expiresAtMs of a finished duel: never picked up by cleanup again
const fail = (code, message) => { throw new functions.https.HttpsError(code, message); };
const uidOf = context => context.auth?.uid || fail('unauthenticated', 'Sign in required.');
const matchRef = id => db.doc(`liveDuels/${id}`);
const codeRef = code => db.doc(`liveDuelCodes/${code}`);
const validCode = code => typeof code === 'string' && /^\d{6}$/.test(code) && code !== '000000';
const validMatch = id => typeof id === 'string' && R.MATCH_ID.test(id);

function look(data) {
  const hero = Number.isInteger(data?.hero) && data.hero >= 0 && data.hero <= 999 ? data.hero : 0;
  const gender = data?.gender === 1 ? 1 : 0;
  const country = typeof data?.country === 'string' && /^[A-Z]{2}$/.test(data.country) ? data.country : '';
  return { hero, gender, country };
}
const nameOf = profile => String(profile?.displayName || 'PLAYER').replace(/[<>]/g, '').slice(0, 20);

function ticket(match) {
  return {
    matchId: match.id, code: match.code, roomName: R.roomName(match.id),
    hostUid: match.hostUid, hostName: match.hostName, hostHero: match.hostHero, hostGender: match.hostGender,
    guestUid: match.guestUid || '', expiresAtMs: match.codeExpiresAtMs, serverTimeMs: Date.now(),
  };
}

const ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
const randomId = () => Array.from(crypto.randomBytes(20), byte => ALPHABET[byte % ALPHABET.length]).join('');

function participant(match, uid) {
  if (match.hostUid !== uid && match.guestUid !== uid) fail('permission-denied', 'Not your duel.');
}

// The code as the guest sees it: whose it is, and whether it can still be joined.
async function openByCode(tx, code, uid) {
  const codeSnap = await tx.get(codeRef(code));
  const now = Date.now();
  if (!codeSnap.exists) fail('not-found', 'Room not found.');
  if (codeSnap.data().expiresAtMs <= now) fail('failed-precondition', 'Code expired.');
  const snap = await tx.get(matchRef(codeSnap.data().matchId));
  if (!snap.exists) fail('not-found', 'Room not found.');
  const match = { id: snap.id, ...snap.data() };
  if (!['waiting', 'joined'].includes(match.state) || match.codeExpiresAtMs <= now) fail('failed-precondition', 'Code expired.');
  if (match.hostUid === uid) fail('invalid-argument', 'This is your own invitation code.');
  if (match.guestUid && match.guestUid !== uid) fail('already-exists', 'This room is already full.');
  return match;
}

exports.createLiveDuel = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context), me = look(data);
  const profile = await db.doc(`users/${uid}`).get();
  const hostName = nameOf(profile.exists ? profile.data() : null);
  for (let attempt = 0; attempt < 8; attempt++) {
    const code = String(crypto.randomInt(1, 1000000)).padStart(6, '0');
    const id = randomId();
    const created = await db.runTransaction(async tx => {
      const now = Date.now();
      const existing = await tx.get(codeRef(code));
      if (existing.exists && existing.data().expiresAtMs > now) return null;   // code in use: draw again
      const expiresAtMs = now + C.LIVE_DUEL_CODE_TTL_SEC * 1000;
      const match = {
        mode: 'friend', code, state: 'waiting', createdAtMs: now, codeExpiresAtMs: expiresAtMs, expiresAtMs,
        hostUid: uid, hostName, hostHero: me.hero, hostGender: me.gender, hostCountry: me.country,
        guestUid: '', guestName: '', guestHero: 0, guestGender: 0, guestCountry: '',
        startedAtMs: 0, results: {}, room: { joins: {}, leaves: {}, closedAtMs: 0 },
      };
      tx.set(codeRef(code), { matchId: id, expiresAtMs });
      tx.create(matchRef(id), match);
      return { id, ...match };
    });
    if (created) return ticket(created);
  }
  fail('resource-exhausted', 'Could not issue a code. Try again.');
});

exports.peekLiveDuel = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context);
  if (!validCode(data?.code)) fail('invalid-argument', 'Enter a 6-digit code.');
  return db.runTransaction(async tx => ticket(await openByCode(tx, data.code, uid)), { readOnly: true });
});

exports.joinLiveDuel = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context), me = look(data);
  if (!validCode(data?.code)) fail('invalid-argument', 'Enter a 6-digit code.');
  const profile = await db.doc(`users/${uid}`).get();
  const guestName = nameOf(profile.exists ? profile.data() : null);
  return db.runTransaction(async tx => {
    const match = await openByCode(tx, data.code, uid);
    const guest = { guestUid: uid, guestName, guestHero: me.hero, guestGender: me.gender, guestCountry: me.country, state: 'joined' };
    tx.update(matchRef(match.id), guest);
    return ticket({ ...match, ...guest });
  });
});

exports.cancelLiveDuel = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context);
  if (!validMatch(data?.matchId)) fail('invalid-argument', 'Invalid duel.');
  await db.runTransaction(async tx => {
    const snap = await tx.get(matchRef(data.matchId));
    if (!snap.exists) return;
    const match = snap.data();
    participant(match, uid);
    const code = await tx.get(codeRef(match.code));
    // A duel that has started cannot be cancelled away; it settles.
    if (!['waiting', 'joined'].includes(match.state)) return;
    tx.update(matchRef(data.matchId), { state: 'cancelled', expiresAtMs: FAR });
    if (code.exists && code.data().matchId === data.matchId) tx.delete(codeRef(match.code));
  });
  return { status: 'ok' };
});

exports.startLiveDuel = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context);
  if (!validMatch(data?.matchId)) fail('invalid-argument', 'Invalid duel.');
  return db.runTransaction(async tx => {
    const snap = await tx.get(matchRef(data.matchId));
    if (!snap.exists) fail('not-found', 'Duel not found.');
    const match = snap.data();
    participant(match, uid);
    if (match.startedAtMs) return { startedAtMs: match.startedAtMs };
    if (match.state !== 'joined' || !match.guestUid) fail('failed-precondition', 'The duel is not ready to start.');
    const code = await tx.get(codeRef(match.code));
    const now = Date.now();
    const settleBy = now + (C.LIVE_DUEL_START_LEAD_SEC + C.MATCH_DURATION_SEC + C.LIVE_DUEL_SETTLE_GRACE_SEC) * 1000;
    tx.update(matchRef(data.matchId), { state: 'started', startedAtMs: now, settleByMs: settleBy, expiresAtMs: settleBy });
    // The room is full and running: the code is spent.
    if (code.exists && code.data().matchId === data.matchId) tx.delete(codeRef(match.code));
    return { startedAtMs: now };
  });
});

exports.finishLiveDuel = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context);
  if (!validMatch(data?.matchId)) fail('invalid-argument', 'Invalid duel.');
  const error = R.timelineError(data.repTimes, data.durationSec);
  if (error) fail('invalid-argument', error);
  const opponentLeft = data.opponentLeft === true;
  const opponentReps = data.opponentReps;
  if (!Number.isInteger(opponentReps) || opponentReps < 0 || opponentReps > C.MAX_REPS_PER_MATCH)
    fail('invalid-argument', 'Invalid opponent count.');
  // A duel runs the full minute; only a friend's forfeit ends it sooner.
  if (!opponentLeft && data.durationSec < C.MATCH_DURATION_SEC - 1)
    fail('failed-precondition', 'A live duel lasts 60 seconds.');
  return db.runTransaction(async tx => {
    const ref = matchRef(data.matchId);
    const snap = await tx.get(ref);
    if (!snap.exists) fail('not-found', 'Duel not found.');
    const match = snap.data();
    participant(match, uid);
    if (match.receipts) return match.receipts[uid];
    if (match.state !== 'started' || !match.startedAtMs) fail('failed-precondition', 'The duel has not started.');
    const now = Date.now();
    const results = { ...(match.results || {}) };
    if (!results[uid]) {
      if (now - match.startedAtMs + 1500 < data.durationSec * 1000) fail('failed-precondition', 'Set completed too early.');
      results[uid] = {
        repTimes: data.repTimes, clapTimes: cleanClapTimes(data.clapTimes, data.repTimes, data.durationSec),
        durationSec: data.durationSec, opponentReps, opponentLeft, atMs: now,
      };
    }
    const next = { ...match, results };
    const settled = await settleInto(tx, ref, next, now >= match.settleByMs);
    if (settled) return settled.receipts[uid];
    tx.update(ref, { results });
    return { state: 'pending' };
  });
});

// Reads both profiles and writes the outcome, inside the caller's transaction (all reads first).
async function settleInto(tx, ref, match, force) {
  const decided = R.decide(match, { force });
  if (!decided) return null;
  const now = Date.now();
  const uids = [match.hostUid, match.guestUid].filter(Boolean);
  const profiles = uids.length ? await tx.getAll(...uids.map(u => db.doc(`users/${u}`))) : [];
  const ranked = match.mode === 'pvp';
  profiles.forEach((snap, i) => {
    const uid = uids[i], receipt = decided.receipts[uid], p = snap.exists ? snap.data() : {};
    const update = { totalReps: (p.totalReps || 0) + (decided.reps[uid] || 0), lastMatchAt: Timestamp.fromMillis(now) };
    if (ranked && !decided.draw) {
      update.trophies = Math.max(0, (p.trophies || 0) + receipt.trophyDelta);
      receipt.trophyDelta = update.trophies - (p.trophies || 0);
      update.totalWins = (p.totalWins || 0) + (receipt.won ? 1 : 0);
      update.totalLosses = (p.totalLosses || 0) + (receipt.won ? 0 : 1);
      update.winStreak = receipt.won ? (p.winStreak || 0) + 1 : 0;
      update.winRate = update.totalWins / (update.totalWins + update.totalLosses);
    }
    tx.set(snap.ref || db.doc(`users/${uid}`), update, { merge: true });
  });
  const [a, b] = [match.hostUid, match.guestUid];
  tx.update(ref, {
    results: match.results || {}, receipts: decided.receipts, state: decided.abandoned ? 'abandoned' : 'settled',
    winnerUid: decided.winnerUid, flags: decided.flags, settledAtMs: now, expiresAtMs: FAR,
  });
  if (!decided.abandoned)
    tx.set(db.doc(`matches/live_${ref.id}`), {
      playerUids: [a, b], mode: match.mode, exercise: 'pushups', createdAt: Timestamp.fromMillis(now),
      durationSec: C.MATCH_DURATION_SEC, photonRoomId: R.roomName(ref.id),
      playerAUid: a, playerBUid: b, playerAReps: decided.reps[a] || 0, playerBReps: decided.reps[b] || 0,
      playerATrophyDelta: decided.receipts[a].trophyDelta, playerBTrophyDelta: decided.receipts[b].trophyDelta,
      winnerUid: decided.winnerUid, draw: decided.draw, walkover: decided.walkover, flags: decided.flags,
    });
  return decided;
}

// Photon webhook. Configure in the Photon Dashboard → Webhooks: BaseUrl = this function's URL,
// PathJoin "join", PathLeave "leave", PathClose "close" (PathCreate empty, so a misconfigured
// hook can never block room creation), CustomHttpHeaders {"X-Photon-Token": "<secret>"}.
exports.onMatchFinished = functions.https.onRequest(async (req, res) => {
  const secret = process.env.PHOTON_WEBHOOK_SECRET || '';
  const token = String(req.get('X-Photon-Token') || '');
  if (!secret) return res.status(503).send('Webhook secret not configured.');
  const a = Buffer.from(token), b = Buffer.from(secret);
  if (a.length !== b.length || !crypto.timingSafeEqual(a, b)) return res.status(401).send('Unauthorized');
  if (req.method !== 'POST') return res.status(405).send('POST only');
  const body = req.body || {};
  const id = R.matchIdFromRoom(body.GameId);
  const type = eventKind(req.path, body.Type);
  functions.logger.info('photon webhook', { type, photonType: String(body.Type || ''), path: req.path,
    room: String(body.GameId || '').slice(0, 40), actor: body.ActorNr ?? null });
  if (!id) return res.json({ ResultCode: 0, Message: 'Not a live duel room.' });
  await db.runTransaction(async tx => {
    const ref = matchRef(id);
    const snap = await tx.get(ref);
    if (!snap.exists) return;
    const match = snap.data(), now = Date.now(), uid = String(body.UserId || '');
    const room = { joins: {}, leaves: {}, closedAtMs: 0, ...(match.room || {}) };
    if (type === 'join' && (uid === match.hostUid || uid === match.guestUid)) room.joins = { ...room.joins, [uid]: now };
    else if (type === 'leave' && (uid === match.hostUid || uid === match.guestUid))
      room.leaves = { ...room.leaves, [uid]: { atMs: now, inactive: body.IsInactive === true } };
    else if (type === 'close') room.closedAtMs = now;
    else return;
    // A walkover the staying phone already claimed is confirmed by this leave. Otherwise the
    // leave waits: a rejoin inside the reconnect window may still undo it.
    if (match.state === 'started' && !match.receipts) await settleInto(tx, ref, { ...match, room }, false);
    tx.update(ref, { room });
  });
  return res.json({ ResultCode: 0, Message: 'OK' });
});

// Which hook fired. The path we configured (PathJoin "join" …) says it; Photon's own Type is
// finer-grained and differs by event — a leave arrives as "LeaveRequest", "ClientDisconnect",
// "TimeoutDisconnect", "PlayerTtlTimedOut"…, a persistent close as "Save".
function eventKind(path, photonType) {
  const fromPath = String(path || '').replace(/^\/+|\/+$/g, '').toLowerCase();
  if (['create', 'join', 'leave', 'close'].includes(fromPath)) return fromPath;
  const t = String(photonType || '').toLowerCase();
  if (t === 'create' || t === 'join') return t;
  if (t === 'close' || t === 'save') return 'close';
  return t ? 'leave' : '';
}

// Photon custom authentication: the client passes its Firebase ID token as "token"; the Photon
// UserId becomes the verified uid, so webhook events name real accounts.
exports.photonAuth = functions.https.onRequest(async (req, res) => {
  const token = String(req.query?.token || req.body?.token || '');
  if (!token) return res.json({ ResultCode: 3, Message: 'Missing token.' });
  try {
    const decoded = await admin.auth().verifyIdToken(token);
    return res.json({ ResultCode: 1, UserId: decoded.uid });
  } catch (e) {
    return res.json({ ResultCode: 2, Message: 'Invalid token.' });
  }
});

// Unused codes expire; duels whose results never all arrived are settled with what there is.
// One single-field range query (expiresAtMs) — no composite index needed.
exports.cleanupMatchmaking = functions.pubsub.schedule('every 5 minutes').timeZone('UTC').onRun(async () => sweep());

async function sweep(now = Date.now()) {
  const due = await db.collection('liveDuels').where('expiresAtMs', '<=', now).limit(200).get();
  let expired = 0, settled = 0;
  for (const doc of due.docs) {
    await db.runTransaction(async tx => {
      const snap = await tx.get(doc.ref);
      if (!snap.exists) return;
      const match = snap.data();
      if (match.expiresAtMs > Date.now()) return;
      if (['waiting', 'joined'].includes(match.state)) {
        const code = await tx.get(codeRef(match.code));
        tx.update(doc.ref, { state: 'expired', expiresAtMs: FAR });
        if (code.exists && code.data().matchId === doc.id) tx.delete(codeRef(match.code));
        expired++;
      } else if (match.state === 'started' && !match.receipts) {
        if (await settleInto(tx, doc.ref, match, true)) settled++;
      } else tx.update(doc.ref, { expiresAtMs: FAR });
    });
  }
  return { expired, settled };
}
exports.sweep = sweep;
