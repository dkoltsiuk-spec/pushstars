// End-to-end check of the friends list against the Firebase emulators (never production):
//   firebase emulators:exec --only auth,firestore,functions --project demo-pushstars "node --test emulator/friends.e2e.js"
// A friend-code duel makes both players friends; the host of a new code calls the friend into
// it, the friend's invitation document appears and goes once the friend joins.
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
async function until(check, what) {
  for (let i = 0; i < 60; i++) { if (await check()) return; await new Promise(r => setTimeout(r, 250)); }
  assert.fail('timed out waiting for ' + what);
}

test('a friend duel makes friends, and a friend can be called into the next code', async () => {
  const a = await guest(), b = await guest();
  await call(US, 'ensureProfile', {}, a.idToken).catch(() => {});
  await call(US, 'ensureProfile', {}, b.idToken).catch(() => {});

  const first = await call(US, 'createLiveDuel', { hero: 1, gender: 1 }, a.idToken);
  await call(US, 'joinLiveDuel', { code: first.code, hero: 0, gender: 0 }, b.idToken);
  await until(async () => (await call(EU, 'getFriends', {}, a.idToken)).friends.length === 1, 'the friendship');
  const listA = (await call(EU, 'getFriends', {}, a.idToken)).friends;
  const listB = (await call(EU, 'getFriends', {}, b.idToken)).friends;
  assert.equal(listA[0].uid, b.localId);
  assert.equal(listB[0].uid, a.localId);
  assert.equal(listB[0].hero, 1);
  assert.equal(listB[0].gender, 1);

  // A stranger cannot call anyone, and nobody but the host calls into a code.
  const c = await guest();
  const second = await call(US, 'createLiveDuel', { hero: 1, gender: 1 }, a.idToken);
  await assert.rejects(call(EU, 'inviteFriend', { friendUid: a.localId, code: second.code }, c.idToken), e => e.status === 'PERMISSION_DENIED');
  await assert.rejects(call(EU, 'inviteFriend', { friendUid: a.localId, code: second.code }, b.idToken), e => e.status === 'PERMISSION_DENIED');

  const sent = await call(EU, 'inviteFriend', { friendUid: b.localId, code: second.code }, a.idToken);
  assert.equal(sent.status, 'sent');
  const invite = await (await doc(`friendInvites/${b.localId}`)).json();
  assert.equal(invite.fields.code.stringValue, second.code);
  await assert.rejects(call(EU, 'inviteFriend', { friendUid: b.localId, code: second.code }, a.idToken),
    e => e.status === 'RESOURCE_EXHAUSTED', 'calling again at once');

  await call(US, 'joinLiveDuel', { code: second.code, hero: 0, gender: 0 }, b.idToken);
  await until(async () => (await doc(`friendInvites/${b.localId}`)).status === 404, 'the spent call to go');

  await call(EU, 'removeFriend', { friendUid: b.localId }, a.idToken);
  assert.equal((await call(EU, 'getFriends', {}, a.idToken)).friends.length, 0);
  assert.equal((await call(EU, 'getFriends', {}, b.idToken)).friends.length, 0);
});
