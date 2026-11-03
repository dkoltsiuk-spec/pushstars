// End-to-end check of reports and blocks against the Firebase emulators (never production):
//   firebase emulators:exec --only auth,firestore,functions --project demo-pushstars "node --test emulator/moderation.e2e.js"
// Blocking a player lists them in the blocked list; unblocking takes them out; a report is stored once.
const { test } = require('node:test');
const assert = require('node:assert/strict');

const PROJECT = process.env.GCLOUD_PROJECT || 'demo-pushstars';
assert.ok(PROJECT.startsWith('demo-'), 'Run only against a demo- project on the emulators.');
const AUTH = `http://${process.env.FIREBASE_AUTH_EMULATOR_HOST || '127.0.0.1:9099'}/identitytoolkit.googleapis.com/v1`;
const US = `http://127.0.0.1:5001/${PROJECT}/us-central1`;
const EU = `http://127.0.0.1:5001/${PROJECT}/europe-west1`;
const FIRESTORE = `http://${process.env.FIRESTORE_EMULATOR_HOST || '127.0.0.1:8080'}/v1/projects/${PROJECT}/databases/(default)/documents`;

async function post(url, body, token) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: JSON.stringify(body),
  });
  return { status: response.status, body: await response.json().catch(() => ({})) };
}
const guest = async () => (await post(`${AUTH}/accounts:signUp?key=demo`, { returnSecureToken: true })).body;
async function call(base, name, data, token) {
  const { body } = await post(`${base}/${name}`, { data }, token);
  if (body.error) throw Object.assign(new Error(body.error.message), { status: body.error.status });
  return body.result;
}
const doc = async path => fetch(`${FIRESTORE}/${path}`, { headers: { Authorization: 'Bearer owner' } });

test('a blocked player is listed, and unblocking takes them out', async () => {
  const me = await guest(), other = await guest();
  await call(US, 'ensureProfile', {}, me.idToken).catch(() => {});
  await call(US, 'ensureProfile', {}, other.idToken).catch(() => {});

  assert.deepEqual((await call(EU, 'getBlockedPlayers', {}, me.idToken)).players, []);
  assert.deepEqual(await call(EU, 'setPlayerBlocked', { targetUid: other.localId, blocked: true }, me.idToken), { status: 'ok' });
  const listed = (await call(EU, 'getBlockedPlayers', {}, me.idToken)).players;
  assert.equal(listed.length, 1, 'the blocked player is in the list');
  assert.equal(listed[0].uid, other.localId);
  assert.equal((await doc(`blockedBy/${other.localId}/list/${me.localId}`)).status, 200, 'the mirror is written');

  await call(EU, 'setPlayerBlocked', { targetUid: other.localId, blocked: false }, me.idToken);
  assert.deepEqual((await call(EU, 'getBlockedPlayers', {}, me.idToken)).players, []);
  assert.equal((await doc(`blockedBy/${other.localId}/list/${me.localId}`)).status, 404, 'the mirror goes too');
});

test('a report is stored once per reporter and target, and one cannot report oneself', async () => {
  const me = await guest(), other = await guest();
  await call(US, 'ensureProfile', {}, me.idToken).catch(() => {});
  await call(EU, 'reportPlayer', { targetUid: other.localId, name: 'Someone', reason: 'offensive_name', where: 'league' }, me.idToken);
  await call(EU, 'reportPlayer', { targetUid: other.localId, name: 'Someone', reason: 'offensive_name', where: 'league' }, me.idToken);
  const stored = await doc(`reports/${me.localId}_${other.localId}`);
  assert.equal(stored.status, 200);
  const counts = await (await doc(`reportCounts/${other.localId}`)).json();
  assert.equal(counts.fields.reporters.integerValue, '1', 'a repeated report counts once');
  await assert.rejects(call(EU, 'reportPlayer', { targetUid: me.localId, reason: 'other' }, me.idToken));
});
