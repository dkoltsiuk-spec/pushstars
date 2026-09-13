const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');

function fixture() {
  let now = 100000;
  const documents = new Map();
  const firestore = () => ({
    doc: path => ({ set: async data => documents.set(path, data) }),
    collection: name => ({
      where: (field, op, cutoff) => ({
        count: () => ({ get: async () => ({ data: () => ({
          count: [...documents.entries()].filter(([path, data]) =>
            path.startsWith(`${name}/`) && data[field] > cutoff).length,
        }) }) }),
      }),
    }),
  });
  firestore.Timestamp = { fromMillis: value => value };
  class HttpsError extends Error { constructor(code, message) { super(message); this.code = code; } }
  const sandbox = {
    exports: {}, Date: { now: () => now },
    require: name => name === './admin' ? { firestore } : { https: { HttpsError, onCall: fn => fn } },
  };
  vm.runInNewContext(fs.readFileSync(require.resolve('../src/heartbeatPresence'), 'utf8'), sandbox);
  return { call: sandbox.exports.heartbeatPresence, documents, advance: ms => { now += ms; } };
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
