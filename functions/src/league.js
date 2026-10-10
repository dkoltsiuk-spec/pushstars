const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { defaultProfile } = require('./profileDefaults');
const C = require('./constants');
const { cleanClapTimes } = require('./clapTimes');
const db = admin.firestore();
const { Timestamp } = admin.firestore;
const MAX_TROPHIES = 999999999;
const LEAGUES = ['bronze', 'silver', 'gold', 'diamond'];
const fail = (code, message) => { throw new functions.https.HttpsError(code, message); };
const uidOf = context => context.auth?.uid || fail('unauthenticated', 'Sign in required.');
const leagueFor = trophies => LEAGUES[trophies >= 1200 ? 3 : trophies >= 800 ? 2 : trophies >= 400 ? 1 : 0];
const standing = (trophies, uid) => `${String(MAX_TROPHIES - trophies).padStart(9, '0')}:${uid}`;

function seasonAt(now = Date.now()) {
  const d = new Date(now), y = d.getUTCFullYear(), m = d.getUTCMonth();
  return { id: `${y}-${String(m + 1).padStart(2, '0')}`, startAt: Timestamp.fromMillis(Date.UTC(y, m, 1)),
    endAt: Timestamp.fromMillis(Date.UTC(y, m + 1, 1)), name: `${y}-${String(m + 1).padStart(2, '0')}` };
}
function entry(uid, profile) {
  const trophies = Math.min(MAX_TROPHIES, Math.max(0, profile.trophies || 0));
  const displayName = String(profile.displayName || 'PLAYER').replace(/[<>]/g, '').slice(0, 20);
  return { uid, displayName, trophies, league: leagueFor(trophies),
    standing: standing(trophies, uid), bestTimes: profile.rankedBestTimes || [],
    bestClapTimes: profile.rankedBestClapTimes || [] };
}
function publicRow(row) {
  return { uid: row.uid, displayName: row.displayName, trophies: row.trophies, league: row.league, standing: row.standing };
}
function seasonRef(season) { return db.doc(`seasons/${season.id}`); }
function playerRef(season, uid) { return seasonRef(season).collection('players').doc(uid); }
function ensureSeason(tx, snap, season) {
  if (!snap.exists) tx.create(seasonRef(season), { ...season, isActive: true, policy: 'monthly-carry-v1' });
}
function publicSeason(s) {
  return { id: s.id, name: s.name, startAtMs: s.startAt.toMillis(), endAtMs: s.endAt.toMillis() };
}
function validId(id) { return typeof id === 'string' && /^[a-zA-Z0-9_-]{16,80}$/.test(id); }
function validateTimes(times, duration) {
  if (!Array.isArray(times) || times.length > C.MAX_REPS_PER_MATCH ||
      typeof duration !== 'number' || !Number.isFinite(duration) || duration < 1 || duration > 60)
    fail('invalid-argument', 'Invalid repetition timeline.');
  let previous = -1;
  for (const time of times) {
    if (typeof time !== 'number' || !Number.isFinite(time) || time < 0 || time > duration || time - previous < 0.4)
      fail('invalid-argument', 'Repetitions must be ordered, within the set, and at least 0.4 seconds apart.');
    previous = time;
  }
}

// Opening a season enrols the authenticated player. Local saves are never imported.
async function join(uid) {
  return db.runTransaction(async tx => {
    const season = seasonAt();
    const userRef = db.doc(`users/${uid}`);
    const [user, meta, existing] = await tx.getAll(userRef, seasonRef(season), playerRef(season, uid));
    const profile = user.exists ? user.data() : defaultProfile();
    ensureSeason(tx, meta, season);
    if (!user.exists) tx.create(userRef, profile);
    const row = entry(uid, profile);
    if (!existing.exists || existing.data().displayName !== row.displayName)
      tx.set(playerRef(season, uid), row);
    return { season, profile };
  });
}

exports.getLeagueStandings = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context);
  const joined = await join(uid);
  const requested = data?.seasonId || joined.season.id;
  if (!/^\d{4}-(0[1-9]|1[0-2])$/.test(requested) || requested > joined.season.id)
    fail('invalid-argument', 'Invalid season.');
  const requestedLeague = data?.league || '';
  if (requestedLeague && !LEAGUES.includes(requestedLeague)) fail('invalid-argument', 'Invalid league.');
  const cursor = data?.cursor || '';
  if (typeof cursor !== 'string' || cursor.length > 200) fail('invalid-argument', 'Invalid cursor.');
  const limit = 25;
  // One read-only transaction provides internally consistent rows, counts and own position.
  return db.runTransaction(async tx => {
    const metaRef = db.doc(`seasons/${requested}`);
    const [meta, own] = await tx.getAll(metaRef, metaRef.collection('players').doc(uid));
    if (!meta.exists) fail('not-found', 'Season not found.');
    const ownData = own.exists ? own.data() : null;
    const league = requestedLeague || ownData?.league || leagueFor(joined.profile.trophies || 0);
    const base = metaRef.collection('players').where('league', '==', league).orderBy('standing');
    const pageQuery = cursor ? base.startAfter(cursor).limit(limit + 1) : base.limit(limit + 1);
    const [page, count, preceding] = await Promise.all([
      tx.get(pageQuery), tx.get(base.count()), cursor ? tx.get(base.endAt(cursor).count()) : null,
    ]);
    let ownRank = 0;
    if (ownData) {
      const ahead = await tx.get(metaRef.collection('players').where('league', '==', ownData.league)
        .orderBy('standing').endBefore(ownData.standing).count());
      ownRank = ahead.data().count + 1;
    }
    const offset = preceding ? preceding.data().count : 0;
    const rows = page.docs.slice(0, limit).map((d, i) => ({ ...publicRow(d.data()), rank: offset + i + 1 }));
    return { season: publicSeason(meta.data()), league, total: count.data().count,
      rows, own: ownData ? { ...publicRow(ownData), rank: ownRank } : null,
      nextCursor: page.size > limit ? rows[rows.length - 1].standing : '',
      seeded: joined.profile.rankedSeeded === true, serverTimeMs: Date.now() };
  }, { readOnly: true });
});

// A stable client-generated request ID makes a timed-out start safe to retry.
// Only one outstanding ranked session per user; its opponent is frozen by the server.
exports.beginRankedMatch = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context), id = data?.requestId, mode = data?.mode;
  if (!validId(id) || !['assessment', 'ghost'].includes(mode)) fail('invalid-argument', 'Invalid match request.');
  const previous = await db.doc(`users/${uid}`).get();
  if (previous.exists && previous.data().activeRankedId && previous.data().activeRankedUntil <= Date.now())
    await expireSession(uid, previous.data().activeRankedId);
  return db.runTransaction(async tx => {
    const now = Date.now(), userRef = db.doc(`users/${uid}`), ref = db.doc(`rankedSessions/${uid}_${id}`);
    const [user, old] = await tx.getAll(userRef, ref);
    const p = user.exists ? user.data() : defaultProfile();
    if (old.exists) {
      const s = old.data();
      if (s.mode !== mode) fail('already-exists', 'Request ID already used.');
      if (s.receipt || s.expiresAtMs <= now) fail('failed-precondition', 'Session already ended.');
      return s.public;
    }
    if (p.activeRankedUntil > now) fail('failed-precondition', 'A ranked set is already open. Finish it or wait for expiry.');
    if (mode === 'assessment' && p.rankedSeeded) fail('failed-precondition', 'Calibration is already complete.');
    if (mode === 'ghost' && (!p.rankedSeeded || !p.rankedBestTimes?.length))
      fail('failed-precondition', 'Complete an online calibration from the League screen first.');
    let opponentTimes = mode === 'ghost' ? p.rankedBestTimes : [], opponentUid = uid, opponentName = 'YOUR GHOST';
    // Claps of the same set the times come from; absent on sets recorded before they were kept.
    let opponentClapTimes = mode === 'ghost' ? p.rankedBestClapTimes || [] : [];
    if (mode === 'ghost') {
      const pool = await tx.get(seasonRef(seasonAt(now)).collection('players')
        .where('league', '==', leagueFor(p.trophies || 0)).orderBy('standing').limit(50));
      const candidates = pool.docs.map(d => d.data()).filter(row => row.uid !== uid && row.bestTimes?.length);
      candidates.sort((a, b) => Math.abs(a.bestTimes.length - p.rankedBestTimes.length)
        - Math.abs(b.bestTimes.length - p.rankedBestTimes.length));
      if (candidates.length) {
        const opponent = candidates[0];
        opponentTimes = opponent.bestTimes; opponentUid = opponent.uid; opponentName = `${opponent.displayName} · GHOST`;
        opponentClapTimes = opponent.bestClapTimes || [];
      }
    }
    const publicData = { id, mode, uid, opponentTimes, opponentClapTimes, opponentUid, opponentName,
      expiresAtMs: now + 15 * 60000 };
    tx.create(ref, { uid, mode, state: 'ready', createdAtMs: now, expiresAtMs: publicData.expiresAtMs,
      opponentTimes, opponentUid, opponentName, public: publicData });
    tx.set(userRef, { ...p, activeRankedId: id, activeRankedUntil: publicData.expiresAtMs });
    return publicData;
  });
});

exports.startRankedMatch = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context), id = data?.id;
  if (!validId(id)) fail('invalid-argument', 'Invalid session.');
  return db.runTransaction(async tx => {
    const ref = db.doc(`rankedSessions/${uid}_${id}`), userRef = db.doc(`users/${uid}`), now = Date.now();
    const [snap, user] = await tx.getAll(ref, userRef);
    if (!snap.exists || snap.data().expiresAtMs <= now || snap.data().receipt)
      fail('failed-precondition', 'Session expired.');
    const s = snap.data();
    if (!user.exists || user.data().activeRankedId !== id) fail('failed-precondition', 'Session is no longer active.');
    if (!s.startedAtMs) {
      tx.update(ref, { startedAtMs: now, state: 'started', expiresAtMs: now + 120000 });
      tx.update(userRef, { activeRankedUntil: now + 120000 });
    }
    return { startedAtMs: s.startedAtMs || now };
  });
});

exports.finishRankedMatch = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context), id = data?.id;
  if (!validId(id)) fail('invalid-argument', 'Invalid session.');
  validateTimes(data.repTimes, data.durationSec);
  await expireSession(uid, id);
  return settle(uid, id, data);
});

async function settle(uid, id, data, expired = false) {
  return db.runTransaction(async tx => {
    const now = Date.now(), season = seasonAt(now), ref = db.doc(`rankedSessions/${uid}_${id}`);
    const userRef = db.doc(`users/${uid}`);
    const [session, user, meta] = await tx.getAll(ref, userRef, seasonRef(season));
    if (!session.exists || !user.exists) fail('not-found', 'Session not found.');
    const s = session.data(), p = user.data();
    if (s.receipt) return s.receipt;
    if (!s.startedAtMs || (!expired && now > s.expiresAtMs) || p.activeRankedId !== id)
      fail('failed-precondition', 'Session expired or not started.');
    if (expired && (now < s.expiresAtMs || s.mode !== 'ghost')) return null;
    if (now - s.startedAtMs + 1500 < data.durationSec * 1000)
      fail('failed-precondition', 'Set completed too early.');
    // Ranked ghost rounds run the full minute. Shortened solo calibrations are allowed.
    if (s.mode === 'ghost' && data.durationSec < 59)
      fail('failed-precondition', 'A ranked duel must last 60 seconds.');
    const reps = data.repTimes.length, opponentReps = s.opponentTimes.length;
    if (s.mode === 'assessment' && !reps) fail('failed-precondition', 'Calibration needs at least one repetition.');
    const day = Math.floor(now / 86400000);
    const streak = reps > 0 ? (p.rankedActiveDay === day ? p.rankedStreak || 1
      : p.rankedActiveDay === day - 1 ? (p.rankedStreak || 0) + 1 : 1) : p.rankedStreak || 0;
    const won = s.mode === 'ghost' && reps > opponentReps;
    const draw = s.mode === 'ghost' && reps === opponentReps;
    const bonus = won ? Math.max(0, streak - 1) : 0;
    const before = p.trophies || 0;
    let trophies = before;
    if (s.mode === 'assessment' && !p.rankedSeeded) {
      trophies = reps >= 50 ? 420 : reps >= 35 ? 280 : reps >= 20 ? 150 : reps >= 10 ? 60 : 0;
    } else if (s.mode === 'ghost' && !draw) trophies += won ? C.TROPHY_GHOST_WIN + bonus : -C.TROPHY_GHOST_LOSS;
    trophies = Math.min(MAX_TROPHIES, Math.max(0, trophies));
    // The best set and its claps are replaced together, or a ghost would clap at another set's times.
    const best = reps > (p.rankedBestTimes?.length || 0);
    const next = { ...p, trophies, rank: leagueFor(trophies), rankedSeeded: true,
      rankedStreak: streak, activeRankedId: '', activeRankedUntil: 0,
      rankedBestTimes: best ? data.repTimes : p.rankedBestTimes || [],
      rankedBestClapTimes: best ? cleanClapTimes(data.clapTimes, data.repTimes, data.durationSec)
        : p.rankedBestClapTimes || [],
      lastMatchAt: Timestamp.fromMillis(now), totalReps: (p.totalReps || 0) + reps };
    if (reps > 0) next.rankedActiveDay = day;
    if (s.mode === 'ghost' && !draw) {
      next.totalWins = (p.totalWins || 0) + (won ? 1 : 0);
      next.totalLosses = (p.totalLosses || 0) + (won ? 0 : 1);
      next.winStreak = won ? (p.winStreak || 0) + 1 : 0;
      next.winRate = next.totalWins / (next.totalWins + next.totalLosses);
    }
    const receipt = { id, uid, seasonId: season.id, trophies, league: next.rank,
      trophyDelta: trophies - before, streakBonus: bonus, streakDays: streak, won, draw, reps, opponentReps, expired };
    ensureSeason(tx, meta, season);
    tx.set(userRef, next);
    tx.set(playerRef(season, uid), entry(uid, next));
    tx.update(ref, { receipt, completedAtMs: now, state: 'completed' });
    tx.create(db.doc(`matches/${uid}_${id}`), { playerUids: [uid], mode: s.mode, exercise: 'pushups',
      createdAt: Timestamp.fromMillis(now), durationSec: data.durationSec, receipt,
      playerAUid: uid, playerBUid: '', playerAReps: reps, playerBReps: opponentReps,
      playerATrophyDelta: trophies - before, playerBTrophyDelta: 0, winnerUid: won ? uid : '', draw });
    return receipt;
  });
}

async function expireSession(uid, id) {
  const snap = await db.doc(`rankedSessions/${uid}_${id}`).get();
  if (!snap.exists || snap.data().receipt || snap.data().expiresAtMs > Date.now()) return;
  if (snap.data().startedAtMs && snap.data().mode === 'ghost')
    await settle(uid, id, { repTimes: [], durationSec: 60 }, true);
}

exports.cancelRankedMatch = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context), id = data?.id;
  if (!validId(id)) fail('invalid-argument', 'Invalid session.');
  return db.runTransaction(async tx => {
    const ref = db.doc(`rankedSessions/${uid}_${id}`), userRef = db.doc(`users/${uid}`);
    const [session, user] = await tx.getAll(ref, userRef);
    // A started duel cannot be cancelled to avoid a loss. It must settle or expire.
    if (session.exists && !session.data().startedAtMs && !session.data().receipt) {
      tx.update(ref, { expiresAtMs: 0, state: 'cancelled' });
      if (user.exists && user.data().activeRankedId === id) tx.update(userRef, { activeRankedUntil: 0, activeRankedId: '' });
    }
    return { status: 'ok' };
  });
});

// Scores are written immediately; no full-table rewrite every five minutes is needed.
// The old season's player documents become the immutable archive at the UTC boundary.
exports.rotateLeagueSeason = functions.pubsub.schedule('every 5 minutes').timeZone('UTC').onRun(async () => {
  const current = seasonAt();
  await db.runTransaction(async tx => {
    const meta = await tx.get(seasonRef(current));
    ensureSeason(tx, meta, current);
  });
  const older = await db.collection('seasons').where('isActive', '==', true).get();
  const writer = db.bulkWriter();
  for (const doc of older.docs)
    if (doc.id < current.id) writer.update(doc.ref, { isActive: false });
  await writer.close();
  const expired = await db.collection('rankedSessions').where('state', '==', 'started')
    .where('expiresAtMs', '<=', Date.now()).limit(100).get();
  for (const doc of expired.docs) {
    const s = doc.data();
    if (s.mode === 'ghost') await expireSession(s.uid, s.public.id);
    else await doc.ref.update({ state: 'expired' });
  }
});

module.exports.seasonAt = seasonAt;
module.exports.entry = entry;
