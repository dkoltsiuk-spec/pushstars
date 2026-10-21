const { test } = require('node:test');
const assert = require('node:assert/strict');
const Module = require('node:module');
const path = require('node:path');

// In-memory Firestore with the one transaction rule that matters here: every read before any write.
function fakeDb(clock) {
  const docs = new Map();
  const copy = value => value === undefined ? undefined : structuredClone(value);
  const ref = p => ({
    path: p, id: p.split('/').pop(),
    get: async () => snap(p),
    set: async (data, options) => apply(['set', p, data, options]),
    update: async data => apply(['update', p, data]),
    delete: async () => apply(['delete', p]),
  });
  const snap = p => ({ exists: docs.has(p), id: p.split('/').pop(), ref: ref(p), data: () => copy(docs.get(p)) });
  function apply([op, p, data, options]) {
    if (op === 'delete') { docs.delete(p); return; }
    if (op === 'create' && docs.has(p)) throw Object.assign(new Error('exists'), { code: 'already-exists' });
    if (op === 'update' && !docs.has(p)) throw Object.assign(new Error('missing ' + p), { code: 'not-found' });
    const base = op === 'update' || options?.merge ? docs.get(p) || {} : {};
    docs.set(p, { ...base, ...copy(data) });
  }
  const ops = { '<=': (a, b) => a <= b, '==': (a, b) => a === b, '>': (a, b) => a > b };
  // Range filters return in their field's order, as Firestore does.
  const query = (name, filters = [], max = Infinity, order = null) => ({
    where: (field, op, value) => query(name, [...filters, [field, op, value]], max, order ?? (op === '==' ? null : field)),
    orderBy: field => query(name, filters, max, field),
    limit: n => query(name, filters, n, order),
    get: async () => {
      const depth = name.split('/').length + 1;
      let found = [...docs.keys()].filter(p => p.startsWith(name + '/') && p.split('/').length === depth)
        .filter(p => filters.every(([f, op, v]) => ops[op](docs.get(p)[f], v)));
      if (order) found.sort((a, b) => docs.get(a)[order] < docs.get(b)[order] ? -1 : docs.get(a)[order] > docs.get(b)[order] ? 1 : 0);
      found = found.slice(0, max).map(snap);
      return { docs: found, size: found.length, empty: !found.length };
    },
  });
  const db = {
    doc: ref,
    collection: name => ({ ...query(name), doc: id => ref(`${name}/${id}`) }),
    runTransaction: async fn => {
      const writes = [];
      const read = r => { if (writes.length) throw new Error('Transaction read after write: ' + r.path); return snap(r.path); };
      const tx = {
        get: async r => {
          if (r.path) return read(r);
          if (writes.length) throw new Error('Transaction query after write');
          return r.get();
        },
        getAll: async (...refs) => refs.map(read),
        set: (r, d, o) => writes.push(['set', r.path, d, o]),
        update: (r, d) => writes.push(['update', r.path, d]),
        create: (r, d) => writes.push(['create', r.path, d]),
        delete: r => writes.push(['delete', r.path]),
      };
      const result = await fn(tx);
      writes.forEach(apply);
      return result;
    },
  };
  return { db, docs };
}

function fixture({ secret = 'hook-secret' } = {}) {
  let now = 1_700_000_000_000;
  const { db, docs } = fakeDb();
  const firestore = Object.assign(() => db, { Timestamp: { fromMillis: ms => ms } });
  const tokens = { 'good-token': 'alice' };
  // FCM: sent messages are kept; a token listed in `dead` answers as unregistered.
  const sent = [], dead = new Set();
  const messaging = () => ({ send: async m => {
    if (dead.has(m.token)) throw Object.assign(new Error('gone'), { errorInfo: { code: 'messaging/registration-token-not-registered' } });
    sent.push(m); return 'id';
  } });
  const admin = { firestore, messaging, auth: () => ({ verifyIdToken: async t => { if (!tokens[t]) throw new Error('bad'); return { uid: tokens[t] }; } }) };
  class HttpsError extends Error { constructor(code, message) { super(message); this.code = code; } }
  const functions = { region: () => functions, logger: { info() {}, warn() {} }, https: { HttpsError, onCall: fn => fn, onRequest: fn => fn }, pubsub: { schedule: () => ({ timeZone: () => ({ onRun: fn => fn }) }) } };
  const src = path.join(__dirname, '../src');
  for (const key of Object.keys(require.cache)) if (key.startsWith(src)) delete require.cache[key];
  const load = Module._load;
  const realNow = Date.now;
  Module._load = function (request, parent, ...rest) {
    if (request === './admin') return admin;
    if (request === 'firebase-functions/v1') return functions;
    return load.call(this, request, parent, ...rest);
  };
  let live, push;
  try { live = require('../src/liveDuel'); push = require('../src/push'); } finally { Module._load = load; }
  Date.now = () => now;
  process.env.PHOTON_WEBHOOK_SECRET = secret;
  const as = uid => ({ auth: { uid } });
  return {
    live, push, docs, as, sent, dead,
    advance: ms => { now += ms; },
    restore: () => { Date.now = realNow; },
    seedUser: (uid, data) => docs.set(`users/${uid}`, { displayName: uid.toUpperCase(), totalReps: 0, trophies: 0, ...data }),
    hook: async (body, token = secret, path) => {
      const res = { code: 200, body: null, status(c) { this.code = c; return this; }, send(b) { this.body = b; return this; }, json(b) { this.body = b; return this; } };
      await live.onMatchFinished({ method: 'POST', path: path ?? '/' + (body.Type || '').toLowerCase(), body, get: h => h === 'X-Photon-Token' ? token : undefined }, res);
      return res;
    },
  };
}

const times = (n, step = 0.5) => Array.from({ length: n }, (_, i) => +(1 + i * step).toFixed(2));

// Two friends in one room, the shared start reported, the clock at the bell.
async function startedDuel(f, mode) {
  f.seedUser('alice'); f.seedUser('bob');
  const ticket = await f.live.createLiveDuel({ hero: 5, gender: 1, country: 'RU' }, f.as('alice'));
  await f.live.joinLiveDuel({ code: ticket.code, hero: 2, gender: 0 }, f.as('bob'));
  if (mode) f.docs.set(`liveDuels/${ticket.matchId}`, { ...f.docs.get(`liveDuels/${ticket.matchId}`), mode });
  await f.live.startLiveDuel({ matchId: ticket.matchId }, f.as('alice'));
  await f.live.startLiveDuel({ matchId: ticket.matchId }, f.as('bob'));
  return ticket;
}
const finish = (f, uid, matchId, reps, extra = {}) => f.live.finishLiveDuel({
  matchId, repTimes: times(reps), clapTimes: [], durationSec: 60, opponentReps: 0, opponentLeft: false, ...extra }, f.as(uid));

test('two friends meet by code, start together and the server compares both timelines', async () => {
  const f = fixture();
  try {
    const ticket = await startedDuel(f);
    assert.match(ticket.code, /^\d{6}$/);
    assert.equal(ticket.roomName, `pd_${ticket.matchId}`);
    assert.equal(ticket.hostName, 'ALICE');
    assert.equal(f.docs.has(`liveDuelCodes/${ticket.code}`), false, 'a started duel spends its code');
    f.advance(64000);
    assert.deepEqual(await finish(f, 'alice', ticket.matchId, 30, { opponentReps: 28 }), { state: 'pending' });
    const bob = await finish(f, 'bob', ticket.matchId, 28, { opponentReps: 30 });
    assert.equal(bob.state, 'settled'); assert.equal(bob.won, false); assert.equal(bob.reps, 28); assert.equal(bob.opponentReps, 30);
    const alice = await finish(f, 'alice', ticket.matchId, 30, { opponentReps: 28 });
    assert.equal(alice.won, true); assert.equal(alice.trophyDelta, 0, 'a friend duel is friendly');
    const match = f.docs.get(`matches/live_${ticket.matchId}`);
    assert.deepEqual(match.playerUids, ['alice', 'bob']);
    assert.equal(match.winnerUid, 'alice'); assert.equal(match.walkover, false);
    assert.equal(f.docs.get('users/alice').totalReps, 30);
    assert.equal(f.docs.get('users/bob').trophies, 0);
  } finally { f.restore(); }
});

test('codes refuse their owner, a third player, garbage and expiry', async () => {
  const f = fixture();
  try {
    f.seedUser('alice'); f.seedUser('bob'); f.seedUser('carol');
    const { code } = await f.live.createLiveDuel({}, f.as('alice'));
    await assert.rejects(f.live.peekLiveDuel({ code }, f.as('alice')), e => e.code === 'invalid-argument' && /own/.test(e.message));
    await assert.rejects(f.live.peekLiveDuel({ code: '12a456' }, f.as('bob')), e => e.code === 'invalid-argument');
    await assert.rejects(f.live.peekLiveDuel({ code: code === '999999' ? '999998' : '999999' }, f.as('bob')), e => e.code === 'not-found');
    assert.equal((await f.live.peekLiveDuel({ code }, f.as('bob'))).hostName, 'ALICE');
    await f.live.joinLiveDuel({ code }, f.as('bob'));
    await f.live.joinLiveDuel({ code }, f.as('bob'));   // a retried join is harmless
    await assert.rejects(f.live.joinLiveDuel({ code }, f.as('carol')), e => e.code === 'already-exists' && /full/.test(e.message));
    const late = await f.live.createLiveDuel({}, f.as('carol'));
    f.advance(301000);
    await assert.rejects(f.live.joinLiveDuel({ code: late.code }, f.as('bob')), e => /expired/.test(e.message));
    await assert.rejects(f.live.createLiveDuel({}, {}), e => e.code === 'unauthenticated');
  } finally { f.restore(); }
});

test('a set reported before the minute has run, or a forged timeline, is refused', async () => {
  const f = fixture();
  try {
    const { matchId } = await startedDuel(f);
    f.advance(30000);
    await assert.rejects(finish(f, 'alice', matchId, 20), e => /too early/.test(e.message));
    await assert.rejects(f.live.finishLiveDuel({ matchId, repTimes: [1, 1.1], durationSec: 60, opponentReps: 0 }, f.as('alice')),
      e => e.code === 'invalid-argument');
    await assert.rejects(f.live.finishLiveDuel({ matchId, repTimes: times(66, .5).map(t => t * .9), durationSec: 60, opponentReps: 0 }, f.as('alice')),
      e => e.code === 'invalid-argument');
    await assert.rejects(finish(f, 'alice', matchId, 10, { durationSec: 30 }), e => /60 seconds/.test(e.message));
    await assert.rejects(finish(f, 'carol', matchId, 10), e => e.code === 'permission-denied');
  } finally { f.restore(); }
});

test('reps far above what the opponent watched live are capped and flagged', async () => {
  const f = fixture();
  try {
    const { matchId } = await startedDuel(f);
    f.advance(64000);
    await finish(f, 'alice', matchId, 50, { opponentReps: 20 });
    const bob = await finish(f, 'bob', matchId, 20, { opponentReps: 21 });   // bob saw alice reach 21
    assert.equal(bob.opponentReps, 24); assert.equal(bob.won, false);
    assert.deepEqual(f.docs.get(`liveDuels/${matchId}`).flags, ['capped:alice']);
  } finally { f.restore(); }
});

test("a friend Photon saw leave mid-set loses by walkover as soon as the stayer reports", async () => {
  const f = fixture();
  try {
    const { matchId } = await startedDuel(f);
    f.advance(4000 + 20000);
    await f.hook({ Type: 'Leave', GameId: `pd_${matchId}`, UserId: 'bob', IsInactive: true });
    f.advance(12000);
    const alice = await finish(f, 'alice', matchId, 12, { durationSec: 30, opponentLeft: true, opponentReps: 9 });
    assert.equal(alice.state, 'settled'); assert.equal(alice.won, true); assert.equal(alice.walkover, true);
    assert.equal(alice.reason, 'opponent-left');
    const bob = await finish(f, 'bob', matchId, 30);
    assert.equal(bob.won, false); assert.equal(bob.reason, 'left');
  } finally { f.restore(); }
});

test('a walkover claim without Photon evidence waits for the opponent, then the deadline', async () => {
  const f = fixture();
  try {
    const { matchId } = await startedDuel(f);
    f.advance(4000 + 40000);
    // Bob left after rejoining once: the rejoin cancels the earlier leave.
    await f.hook({ Type: 'Leave', GameId: `pd_${matchId}`, UserId: 'bob' });
    await f.hook({ Type: 'Join', GameId: `pd_${matchId}`, UserId: 'bob' });
    assert.deepEqual(await finish(f, 'alice', matchId, 15, { durationSec: 40, opponentLeft: true, opponentReps: 14 }), { state: 'pending' });
    f.advance(200000);
    assert.deepEqual(await f.live.sweep(), { expired: 0, settled: 1, dequeued: 0 });
    const match = f.docs.get(`liveDuels/${matchId}`);
    assert.equal(match.receipts.alice.won, true);
    assert.deepEqual(match.flags, ['walkover-by-deadline']);
  } finally { f.restore(); }
});

test('both phones partitioned from each other: whoever did more reps wins, no walkover', async () => {
  const f = fixture();
  try {
    const { matchId } = await startedDuel(f);
    f.advance(4000 + 35000);
    await finish(f, 'alice', matchId, 14, { durationSec: 35, opponentLeft: true, opponentReps: 9 });
    const bob = await finish(f, 'bob', matchId, 16, { durationSec: 35, opponentLeft: true, opponentReps: 10 });
    assert.equal(bob.won, true); assert.equal(bob.walkover, false); assert.equal(bob.opponentReps, 14);
  } finally { f.restore(); }
});

test('leaving after the bell is not a forfeit', async () => {
  const f = fixture();
  try {
    const { matchId } = await startedDuel(f);
    f.advance(65000);
    await f.hook({ Type: 'Leave', GameId: `pd_${matchId}`, UserId: 'bob' });
    await f.hook({ Type: 'Close', GameId: `pd_${matchId}` });
    assert.deepEqual(await finish(f, 'alice', matchId, 10, { opponentLeft: true, opponentReps: 30 }), { state: 'pending' });
    const bob = await finish(f, 'bob', matchId, 30, { opponentReps: 10 });
    assert.equal(bob.won, true);
  } finally { f.restore(); }
});

test('the webhook needs the shared secret and ignores rooms that are not live duels', async () => {
  const f = fixture();
  try {
    const { matchId } = await startedDuel(f);
    assert.equal((await f.hook({ Type: 'Leave', GameId: `pd_${matchId}`, UserId: 'bob' }, 'wrong')).code, 401);
    assert.equal((await f.hook({ Type: 'Leave', GameId: `pd_${matchId}`, UserId: 'bob' }, '')).code, 401);
    assert.deepEqual(f.docs.get(`liveDuels/${matchId}`).room.leaves, {}, 'a refused call writes nothing');
    const other = await f.hook({ Type: 'Leave', GameId: 'someone-elses-room', UserId: 'bob' });
    assert.equal(other.code, 200); assert.equal(other.body.ResultCode, 0);
    const ok = await f.hook({ Type: 'Leave', GameId: `pd_${matchId}`, UserId: 'mallory' });
    assert.equal(ok.body.ResultCode, 0);
    assert.deepEqual(f.docs.get(`liveDuels/${matchId}`).room.leaves, {}, 'strangers are not recorded');
    const unset = fixture({ secret: '' });
    try { assert.equal((await unset.hook({ Type: 'Close', GameId: `pd_${matchId}` }, '')).code, 503); } finally { unset.restore(); }
  } finally { f.restore(); }
});

test('cleanup expires unused codes and frees them; cancel frees a code at once', async () => {
  const f = fixture();
  try {
    f.seedUser('alice'); f.seedUser('bob');
    const stale = await f.live.createLiveDuel({}, f.as('alice'));
    const cancelled = await f.live.createLiveDuel({}, f.as('bob'));
    await f.live.cancelLiveDuel({ matchId: cancelled.matchId }, f.as('bob'));
    assert.equal(f.docs.has(`liveDuelCodes/${cancelled.code}`), false);
    f.advance(301000);
    assert.deepEqual(await f.live.sweep(), { expired: 1, settled: 0, dequeued: 0 });
    assert.equal(f.docs.get(`liveDuels/${stale.matchId}`).state, 'expired');
    assert.equal(f.docs.has(`liveDuelCodes/${stale.code}`), false);
    assert.deepEqual(await f.live.sweep(), { expired: 0, settled: 0, dequeued: 0 }, 'done duels are not swept twice');
  } finally { f.restore(); }
});

test('a ranked live mode moves trophies, never below zero', async () => {
  const f = fixture();
  try {
    const { matchId } = await startedDuel(f, 'pvp');
    f.docs.set('users/alice', { ...f.docs.get('users/alice'), trophies: 100 });
    f.docs.set('users/bob', { ...f.docs.get('users/bob'), trophies: 5 });
    f.advance(64000);
    await finish(f, 'alice', matchId, 30, { opponentReps: 20 });
    const bob = await finish(f, 'bob', matchId, 20, { opponentReps: 30 });
    assert.equal(bob.trophyDelta, -5);
    assert.equal(f.docs.get('users/alice').trophies, 125);
    assert.equal(f.docs.get('users/bob').trophies, 0);
    assert.equal(f.docs.get('users/alice').totalWins, 1);
  } finally { f.restore(); }
});

test('Photon custom authentication turns a Firebase token into the Photon UserId', async () => {
  const f = fixture();
  try {
    const call = async query => { const res = { json(b) { this.body = b; return this; } }; await f.live.photonAuth({ query }, res); return res.body; };
    assert.deepEqual(await call({ token: 'good-token' }), { ResultCode: 1, UserId: 'alice' });
    assert.equal((await call({ token: 'forged' })).ResultCode, 2);
    assert.equal((await call({})).ResultCode, 3);
  } finally { f.restore(); }
});

test("Photon's real leave types (LeaveRequest, TimeoutDisconnect …) are recorded as leaves", async () => {
  const f = fixture();
  try {
    const { matchId } = await startedDuel(f);
    f.advance(4000 + 31000);
    // As Photon sends it: the configured path, its own finer Type.
    await f.hook({ Type: 'LeaveRequest', GameId: `pd_${matchId}`, UserId: 'bob', ActorNr: 2, IsInactive: false }, undefined, '/leave');
    assert.equal(f.docs.get(`liveDuels/${matchId}`).room.leaves.bob.inactive, false);
    f.advance(1000);
    await f.hook({ Type: 'Join', GameId: `pd_${matchId}`, UserId: 'bob', ActorNr: 2 }, undefined, '/join');
    f.advance(1000);
    await f.hook({ Type: 'TimeoutDisconnect', GameId: `pd_${matchId}`, UserId: 'bob', ActorNr: 2, IsInactive: true }, undefined, '/leave');
    const alice = await finish(f, 'alice', matchId, 12, { durationSec: 30, opponentLeft: true, opponentReps: 9 });
    assert.equal(alice.state, 'settled'); assert.equal(alice.walkover, true);
    // No usable path (an older config): Photon's own Type still decides.
    await f.hook({ Type: 'ClientDisconnect', GameId: `pd_${matchId}`, UserId: 'alice', ActorNr: 1 }, undefined, '/');
    assert.ok(f.docs.get(`liveDuels/${matchId}`).room.leaves.alice);
  } finally { f.restore(); }
});


// ── Random queue (matchPlayers) ──────────────────────────────────────────────────────────────

const seasonId = () => { const d = new Date(Date.now()); return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}`; };

test('two players searching at the same moment are paired into one ranked live duel', async () => {
  const f = fixture();
  try {
    f.seedUser('alice'); f.seedUser('bob');
    assert.equal((await f.live.enqueueLiveDuel({ hero: 5, gender: 1, country: 'RU', best: 30 }, f.as('alice'))).status, 'waiting');
    f.advance(1000);
    assert.equal((await f.live.pollLiveDuel({ hero: 5, gender: 1, best: 30 }, f.as('alice'))).status, 'waiting', 'alone in the queue');
    const bob = await f.live.enqueueLiveDuel({ hero: 2, gender: 0, best: 25 }, f.as('bob'));
    assert.equal(bob.status, 'matched'); assert.equal(bob.host, false, 'the one who found the waiting player is the guest');
    assert.equal(bob.opponentName, 'ALICE'); assert.equal(bob.opponentHero, 5); assert.equal(bob.opponentGender, 1);
    assert.equal(bob.roomName, `pd_${bob.matchId}`); assert.equal(bob.mode, 'pvp');
    // The waiting phone hears of it through its own place, which says everything the poll would.
    const place = f.docs.get('liveDuelQueue/alice');
    assert.equal(place.state, 'matched'); assert.equal(place.matchId, bob.matchId); assert.equal(place.roomName, bob.roomName);
    assert.equal(place.role, 'host'); assert.equal(place.opponent.name, 'BOB'); assert.equal(place.opponent.hero, 2);
    f.advance(800);
    const alice = await f.live.pollLiveDuel({ hero: 5, gender: 1 }, f.as('alice'));
    assert.equal(alice.status, 'matched'); assert.equal(alice.host, true); assert.equal(alice.matchId, bob.matchId);
    assert.equal(alice.opponentName, 'BOB'); assert.equal(alice.opponentHero, 2);
    const match = f.docs.get(`liveDuels/${bob.matchId}`);
    assert.equal(match.mode, 'pvp'); assert.equal(match.state, 'joined'); assert.equal(match.code, '');
    assert.equal(match.hostUid, 'alice'); assert.equal(match.guestUid, 'bob');
    // Leaving after the pairing does not undo it: the phone takes the match.
    assert.equal((await f.live.leaveLiveDuel({}, f.as('alice'))).matchId, bob.matchId);

    // From here it is an ordinary live duel, with the pvp trophies.
    await f.live.startLiveDuel({ matchId: bob.matchId }, f.as('alice'));
    await f.live.startLiveDuel({ matchId: bob.matchId }, f.as('bob'));
    f.advance(64000);
    await finish(f, 'alice', bob.matchId, 31, { opponentReps: 24 });
    const result = await finish(f, 'bob', bob.matchId, 24, { opponentReps: 31 });
    assert.equal(result.won, false); assert.equal(result.trophyDelta, 0, 'bob had no trophies to lose');
    assert.equal(f.docs.get('users/alice').trophies, 25);
    assert.equal(f.docs.get(`matches/live_${bob.matchId}`).mode, 'pvp');
  } finally { f.restore(); }
});

test('a phone that stopped polling drops out; a third searcher is never paired twice', async () => {
  const f = fixture();
  try {
    f.seedUser('alice'); f.seedUser('bob'); f.seedUser('carol'); f.seedUser('dave');
    await f.live.enqueueLiveDuel({}, f.as('alice'));
    f.advance(6000);                                     // alice's phone went quiet
    assert.equal((await f.live.enqueueLiveDuel({}, f.as('bob'))).status, 'waiting', 'a silent place is not offered');
    const carol = await f.live.enqueueLiveDuel({}, f.as('carol'));
    assert.equal(carol.status, 'matched'); assert.equal(carol.opponentUid, 'bob');
    assert.equal((await f.live.enqueueLiveDuel({}, f.as('dave'))).status, 'waiting', 'bob is taken');
    // Alice comes back and polls: dave is the one waiting now.
    const alice = await f.live.pollLiveDuel({}, f.as('alice'));
    assert.equal(alice.status, 'matched'); assert.equal(alice.opponentUid, 'dave');
    assert.equal([...f.docs.keys()].filter(k => k.startsWith('liveDuels/')).length, 2);
  } finally { f.restore(); }
});

test('nobody found: leaving frees the place and hands back a stranger\'s best set near your level', async () => {
  const f = fixture();
  try {
    f.seedUser('alice');
    const players = `seasons/${seasonId()}/players`;
    f.docs.set(`${players}/alice`, { uid: 'alice', displayName: 'ALICE', standing: '1', bestTimes: times(30) });
    f.docs.set(`${players}/bob`, { uid: 'bob', displayName: 'BOB', standing: '2', bestTimes: times(28), bestClapTimes: [3] });
    f.docs.set(`${players}/zed`, { uid: 'zed', displayName: 'ZED', standing: '3', bestTimes: [] });
    await f.live.enqueueLiveDuel({ best: 27 }, f.as('alice'));
    const left = await f.live.leaveLiveDuel({ best: 27 }, f.as('alice'));
    assert.equal(left.status, 'left');
    assert.equal(left.ghost.uid, 'bob', 'never your own row, never an empty set');
    assert.equal(left.ghost.repTimes.length, 28); assert.deepEqual(left.ghost.clapTimes, [3]);
    assert.equal(f.docs.has('liveDuelQueue/alice'), false);
    f.docs.delete(`${players}/bob`);
    assert.equal((await f.live.leaveLiveDuel({}, f.as('alice'))).ghost, null, 'no strangers: the phone falls back on its own');
  } finally { f.restore(); }
});

test('an abandoned pairing does not trap the next search; cleanup clears queue places and dead queue rooms', async () => {
  const f = fixture();
  try {
    f.seedUser('alice'); f.seedUser('bob'); f.seedUser('carol');
    await f.live.enqueueLiveDuel({}, f.as('alice'));
    const bob = await f.live.enqueueLiveDuel({}, f.as('bob'));
    // Alice's phone died before it saw the pairing; she searches again a minute later.
    f.advance(30000);
    assert.equal((await f.live.enqueueLiveDuel({}, f.as('alice'))).status, 'waiting', 'a stale pairing is dropped');
    assert.equal((await f.live.enqueueLiveDuel({}, f.as('carol'))).opponentUid, 'alice');
    f.advance(100000);
    const swept = await f.live.sweep();
    assert.equal(swept.dequeued, 3); assert.equal(swept.expired, 2, 'unstarted queue rooms expire');
    assert.equal(f.docs.get(`liveDuels/${bob.matchId}`).state, 'expired');
    // Bob gives up right after pairing: alice, who has not seen the pairing yet, keeps searching.
    await f.live.enqueueLiveDuel({}, f.as('alice'));
    const again = await f.live.enqueueLiveDuel({}, f.as('bob'));
    await f.live.cancelLiveDuel({ matchId: again.matchId }, f.as('bob'));
    assert.equal(f.docs.get(`liveDuels/${again.matchId}`).state, 'cancelled');
    assert.equal((await f.live.pollLiveDuel({}, f.as('alice'))).status, 'waiting');
    assert.equal((await f.live.enqueueLiveDuel({}, f.as('carol'))).opponentUid, 'alice');
    await assert.rejects(f.live.enqueueLiveDuel({}, {}), e => e.code === 'unauthenticated');
  } finally { f.restore(); }
});

test('"notify me": a searcher who left is called by the next search, within budget, and answers into a live duel', async () => {
  const f = fixture();
  try {
    f.seedUser('alice'); f.seedUser('bob'); f.seedUser('carol');
    await f.push.registerPushToken({ token: 'tok-alice', platform: 'ios', lang: 'ru' }, f.as('alice'));
    const first = await f.live.enqueueLiveDuel({ best: 30, notify: true }, f.as('alice'));
    assert.equal(first.status, 'waiting'); assert.equal(first.called, 0, 'nobody to call');
    // Alice takes the recorded fight offered meanwhile: her place stays, but not during that fight.
    const left = await f.live.leaveLiveDuel({ standby: true, ghost: false, best: 30 }, f.as('alice'));
    assert.equal(left.status, 'left'); assert.equal(left.ghost, null);
    assert.equal(f.docs.get('liveDuelQueue/alice').waitUntilMs, 0);
    f.advance(1000);
    assert.equal((await f.live.enqueueLiveDuel({ best: 28 }, f.as('bob'))).called, 0, 'alice is mid-fight');
    f.advance(150000);
    const recall = await f.live.pollLiveDuel({ best: 28 }, f.as('bob'));
    assert.equal(recall.status, 'waiting'); assert.equal(recall.called, 1);
    assert.equal(f.sent.length, 1);
    assert.equal(f.sent[0].token, 'tok-alice'); assert.match(f.sent[0].notification.title, /Соперник/);
    assert.match(f.sent[0].notification.body, /BOB/); assert.equal(f.sent[0].data.kind, 'queue');
    assert.equal(f.sent[0].android.notification.channelId, 'duels');
    const called = f.docs.get('liveDuelQueue/alice');
    assert.equal(called.callerName, 'BOB'); assert.ok(called.calledAtMs > 0);
    f.advance(10000);
    assert.equal((await f.live.pollLiveDuel({ best: 28 }, f.as('bob'))).called, 0, 'not every poll calls');
    f.advance(40000);
    assert.equal((await f.live.pollLiveDuel({ best: 28 }, f.as('bob'))).called, 0, 'one call per cooldown');
    assert.equal(f.sent.length, 1);
    // Alice opens the app on the push and searches: bob is still there.
    const alice = await f.live.enqueueLiveDuel({ best: 30, notify: true }, f.as('alice'));
    assert.equal(alice.status, 'matched'); assert.equal(alice.opponentUid, 'bob'); assert.equal(alice.host, false);
    assert.equal(f.docs.get('liveDuelQueue/alice').standbyUntilMs, 0, 'a pairing ends the standby');
    // Cancelling a search (no standby asked) frees the place for good.
    await f.live.enqueueLiveDuel({ notify: true }, f.as('carol'));
    await f.live.leaveLiveDuel({ ghost: false }, f.as('carol'));
    assert.equal(f.docs.has('liveDuelQueue/carol'), false);
  } finally { f.restore(); }
});

test('calls respect the daily budget and standby expiry; a dead token is forgotten; an empty token unregisters', async () => {
  const f = fixture();
  try {
    f.seedUser('alice'); f.seedUser('bob'); f.seedUser('dora');
    await f.push.registerPushToken({ token: 'tok-alice', platform: 'android', lang: 'xx' }, f.as('alice'));
    assert.equal(f.docs.get('pushTokens/alice').lang, 'en', 'unknown languages fall back to English');
    for (let i = 0; i < 8; i++) {
      await f.live.enqueueLiveDuel({ notify: true }, f.as('alice'));
      f.advance(6000);                                   // alice put the phone away
      await f.live.enqueueLiveDuel({}, f.as('bob'));
      await f.live.leaveLiveDuel({ ghost: false }, f.as('bob'));
      f.advance(181000);
    }
    assert.equal(f.sent.length, 6, 'PUSH_CALLS_PER_DAY');
    assert.match(f.sent[0].notification.title, /Opponent found/);
    // Standby lapses after LIVE_QUEUE_STANDBY_SEC; cleanup then removes the place.
    f.advance(24 * 3600 * 1000);
    await f.live.enqueueLiveDuel({ notify: true }, f.as('alice'));
    f.advance(1801000);
    await f.live.enqueueLiveDuel({}, f.as('bob'));
    assert.equal(f.sent.length, 6, 'a lapsed standby is not called');
    await f.live.leaveLiveDuel({ ghost: false }, f.as('bob'));
    assert.equal((await f.live.sweep()).dequeued, 1);
    // An uninstalled app: FCM says the token is gone, the server forgets it.
    await f.push.registerPushToken({ token: 'tok-dora', platform: 'ios', lang: 'pt-BR' }, f.as('dora'));
    f.dead.add('tok-dora');
    await f.live.enqueueLiveDuel({ notify: true }, f.as('dora'));
    f.advance(6000);
    assert.equal((await f.live.enqueueLiveDuel({}, f.as('bob'))).called, 0);
    assert.equal(f.docs.has('pushTokens/dora'), false);
    await f.push.registerPushToken({ token: '' }, f.as('alice'));
    assert.equal(f.docs.has('pushTokens/alice'), false);
    await assert.rejects(f.push.registerPushToken({ token: 'x' }, {}), e => e.code === 'unauthenticated');
  } finally { f.restore(); }
});

test("a fresh search offers a stranger only at the player's level", async () => {
  const f = fixture();
  try {
    f.seedUser('alice');
    const players = `seasons/${seasonId()}/players`;
    f.docs.set(`${players}/bob`, { uid: 'bob', displayName: 'BOB', standing: '1', trophies: 540, bestTimes: times(27) });
    f.docs.set(`${players}/zed`, { uid: 'zed', displayName: 'ZED', standing: '2', trophies: 900, bestTimes: times(12) });
    const near = await f.live.enqueueLiveDuel({ best: 30 }, f.as('alice'));
    assert.equal(near.ghost.uid, 'bob'); assert.equal(near.ghost.trophies, 540); assert.equal(near.ghost.repTimes.length, 27);
    await f.live.leaveLiveDuel({ ghost: false }, f.as('alice'));
    assert.equal((await f.live.enqueueLiveDuel({ best: 45 }, f.as('alice'))).ghost, null, 'nobody within the band');
    await f.live.leaveLiveDuel({ ghost: false }, f.as('alice'));
    assert.equal((await f.live.enqueueLiveDuel({ best: 0 }, f.as('alice'))).ghost, null, 'no level yet');
    assert.equal((await f.live.pollLiveDuel({ best: 30 }, f.as('alice'))).ghost, undefined, 'polls carry no offer');
  } finally { f.restore(); }
});
