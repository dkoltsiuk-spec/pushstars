const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function fixture() {
  let now = 100000, counts = 0;
  const documents = new Map();
  const firestore = () => ({
    doc: path => ({
      get: async () => { const value = documents.get(path); return { exists: value !== undefined, data: () => value }; },
      set: async data => { documents.set(path, { ...data }); },
    }),
    collection: name => ({
      where: (field, op, value) => ({
        count: () => ({ get: async () => { counts++; return { data: () => ({
          count: [...documents.entries()].filter(([path, data]) =>
            path.startsWith(`${name}/`) && (op === '==' ? data[field] === value : data[field] > value)).length,
        }) }; } }),
      }),
    }),
  });
  firestore.Timestamp = { fromMillis: value => value };
  class HttpsError extends Error { constructor(code, message) { super(message); this.code = code; } }
  const sandbox = {
    exports: {}, Date: Object.assign(function (ms) { return new Date(ms); }, { now: () => now }), Promise,
    require: name => name === './admin' ? { firestore }
      : name === 'firebase-admin/firestore' ? { Timestamp: firestore.Timestamp }
      : { https: { HttpsError, onCall: fn => fn } },
  };
  vm.runInNewContext(fs.readFileSync(require.resolve('../src/heartbeatPresence'), 'utf8'), sandbox);
  return { call: sandbox.exports.heartbeatPresence, documents, advance: ms => { now += ms; }, counts: () => counts };
}

test('requires authentication and writes nothing for an unauthenticated caller', async () => {
  const f = fixture();
  await assert.rejects(f.call({}, {}), error => error.code === 'unauthenticated');
  assert.equal(f.documents.size, 0);
});

test('counts active accounts once across repeated heartbeats and multiple devices', async () => {
  const f = fixture();
  assert.equal((await f.call({}, { auth: { uid: 'alice' } })).online, 1);
  assert.equal((await f.call({}, { auth: { uid: 'alice' } })).online, 1);
  assert.equal((await f.call({}, { auth: { uid: 'bob' } })).online, 2);
});

test('excludes an account at exactly the expiry boundary and reactivates on reconnect', async () => {
  const f = fixture();
  await f.call({}, { auth: { uid: 'alice' } });
  f.advance(75000);
  assert.equal((await f.call({}, { auth: { uid: 'bob' } })).online, 1);
  assert.equal((await f.call({}, { auth: { uid: 'alice' } })).online, 2);
});

test('ignores forged identity, expiry, and count supplied by the client', async () => {
  const f = fixture();
  const result = await f.call({ uid: 'victim', online: 317, expiresAt: Number.MAX_VALUE }, { auth: { uid: 'alice' } });
  assert.equal(result.online, 1);
  assert.equal(f.documents.has('onlinePresence/victim'), false);
  assert.equal(f.documents.get('onlinePresence/alice').expiresAt, 175000);
});

test('counts each account once per UTC day into the players-today figure', async () => {
  const f = fixture();
  assert.equal((await f.call({}, { auth: { uid: 'alice' } })).today, 1);
  assert.equal((await f.call({}, { auth: { uid: 'alice' } })).today, 1, 'heartbeats repeat within a day');
  f.advance(200000);
  assert.equal((await f.call({}, { auth: { uid: 'bob' } })).today, 2, 'offline players still count for the day');
  f.advance(24 * 3600 * 1000);
  const next = await f.call({}, { auth: { uid: 'alice' } });
  assert.equal(next.today, 1, 'a new day starts from zero');
  assert.equal(next.online, 1);
});

test('the first heartbeats of a day write only their own presence documents', async () => {
  const f = fixture();
  for (const uid of ['alice', 'bob', 'carol']) await f.call({}, { auth: { uid } });
  assert.deepEqual([...f.documents.keys()].filter(path => !path.startsWith('onlinePresence/')), []);
});

test('recounts every heartbeat while small, reuses a large count for a while', async () => {
  const f = fixture();
  await f.call({}, { auth: { uid: 'alice' } });
  await f.call({}, { auth: { uid: 'bob' } });
  assert.equal(f.counts(), 4, 'a small game is counted exactly');
  for (let i = 0; i < 1200; i++) f.documents.set(`onlinePresence/p${i}`, { expiresAt: 175000, day: '1970-01-01' });
  assert.equal((await f.call({}, { auth: { uid: 'carol' } })).online, 1203);
  assert.equal((await f.call({}, { auth: { uid: 'dave' } })).online, 1203, 'reused within the cache window');
  assert.equal(f.counts(), 6);
  f.advance(20000);
  const fresh = await f.call({}, { auth: { uid: 'erin' } });
  assert.equal(fresh.online, 1205);
  assert.equal(fresh.today, 1205);
  assert.equal(f.counts(), 8);
});
