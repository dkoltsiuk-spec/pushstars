// End-to-end check of cloud progress against the Firebase emulators (never production):
//   npm run test:emulator   (firebase emulators:exec --project demo-pushstars ...)
// Plays the client protocol of CloudProgressSync over real Auth, Firestore rules and Functions:
// a guest earns progress, links Google (same UID) and uploads; a reinstall and a second phone
// sign in with that Google account and get the same save back; two phones racing get 'aborted';
// guests, strangers and direct writes are refused. Which copy a phone keeps (CloudSaveRules) is
// covered by the Unity EditMode tests; this covers what crosses the wire.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');

const PROJECT = process.env.GCLOUD_PROJECT || 'demo-pushstars';
assert.ok(PROJECT.startsWith('demo-'), 'Run only against a demo- project on the emulators.');
const AUTH = `http://${process.env.FIREBASE_AUTH_EMULATOR_HOST || '127.0.0.1:9099'}/identitytoolkit.googleapis.com/v1`;
const FIRESTORE = `http://${process.env.FIRESTORE_EMULATOR_HOST || '127.0.0.1:8080'}/v1/projects/${PROJECT}/databases/(default)/documents`;
const FUNCTIONS = `http://127.0.0.1:5001/${PROJECT}/us-central1`;

const fixtureJson = fs.readFileSync(path.join(__dirname, '..', 'test', 'fixtures', 'progress-snapshot.json'), 'utf8');
const clientPacked = fs.readFileSync(path.join(__dirname, '..', 'test', 'fixtures', 'progress-snapshot.packed.txt'), 'utf8').trim();
const pack = text => zlib.gzipSync(Buffer.from(text, 'utf8')).toString('base64');
const unpack = packed => zlib.gunzipSync(Buffer.from(packed, 'base64')).toString('utf8');
const ghostPacked = pack(JSON.stringify({ exercise: 'pushups', reps: 3, repTimes: [1.1, 2.4, 3.9], clapTimes: [], motionBase64: '', avgForm: 82 }));

async function post(url, body, token) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: JSON.stringify(body),
  });
  return { status: response.status, body: await response.json().catch(() => ({})) };
}

const auth = {
  guest: async () => (await post(`${AUTH}/accounts:signUp?key=demo`, { returnSecureToken: true })).body,
  // Google through the emulator's fake IdP; with idToken it links to that user, without it signs in.
  google: async (sub, idToken) => (await post(`${AUTH}/accounts:signInWithIdp?key=demo`, {
    ...(idToken ? { idToken } : {}),
    postBody: `id_token=${encodeURIComponent(JSON.stringify({ sub, email: `${sub}@example.com`, email_verified: true }))}&providerId=google.com`,
    requestUri: 'http://localhost', returnSecureToken: true, returnIdpCredential: true,
  })).body,
};

async function call(name, data, token) {
  const { body } = await post(`${FUNCTIONS}/${name}`, { data }, token);
  if (body.error) { const e = new Error(body.error.message); e.status = body.error.status; throw e; }
  return body.result;
}
const save = (token, baseRev, snapshot, ghost) => call('saveProgress', { baseRev, snapshot, sendGhost: ghost !== undefined, ghost: ghost || '' }, token);
const load = token => call('loadProgress', {}, token);

function withMoreWorkouts(json, extra) {
  const snapshot = JSON.parse(json);
  const workouts = JSON.parse(snapshot.workouts);
  for (let i = 0; i < extra; i++) workouts.Workouts.push({ ...workouts.Workouts[1], Id: `phone-b-${i}`, TrophyDelta: 0 });
  workouts.TotalReps += extra * workouts.Workouts[1].Reps;
  snapshot.workouts = JSON.stringify(workouts);
  return JSON.stringify(snapshot);
}

test('guest progress linked to Google follows the account to a reinstall and a second phone', async () => {
  // Phone A: a guest plays, then links Google from Settings. Linking keeps the UID.
  const guestA = await auth.guest();
  await assert.rejects(save(guestA.idToken, 0, clientPacked), e => e.status === 'FAILED_PRECONDITION'); // guests keep a device-only save
  const linked = await auth.google('player-1', guestA.idToken);
  assert.equal(linked.localId, guestA.localId);
  const tokenA = linked.idToken;

  // First upload, exactly the bytes the Unity client packs.
  assert.deepEqual(await save(tokenA, 0, clientPacked, ghostPacked), { rev: 1 });

  // Reinstall (or phone B): a new guest taps Google, the account already exists ("Switch account?").
  const reinstall = await auth.guest();
  const collision = await auth.google('player-1', reinstall.idToken);
  // signInWithIdp reports the collision either as an error or as errorMessage in a 200 reply.
  assert.match((collision.error && collision.error.message) || collision.errorMessage || JSON.stringify(collision),
    /FEDERATED_USER_ID_ALREADY_LINKED|CREDENTIAL_ALREADY_IN_USE/);
  assert.notEqual(collision.localId, reinstall.localId, 'the guest must not silently become the account');
  const switched = await auth.google('player-1');
  assert.equal(switched.localId, guestA.localId);

  const restored = await load(switched.idToken);
  assert.equal(restored.exists, true);
  assert.equal(restored.rev, 1);
  assert.equal(unpack(restored.snapshot), fixtureJson);
  assert.equal(restored.hasGhost, true);
  assert.equal(JSON.parse(unpack(restored.ghost)).reps, 3);

  // Phone B plays on and saves over rev 1; phone A, still at rev 1, is told to decide again.
  assert.deepEqual(await save(switched.idToken, 1, pack(withMoreWorkouts(fixtureJson, 3))), { rev: 2 });
  await assert.rejects(save(tokenA, 1, clientPacked), e => e.status === 'ABORTED');
  const latest = await load(tokenA);
  assert.equal(latest.rev, 2);
  assert.equal(JSON.parse(JSON.parse(unpack(latest.snapshot)).workouts).Workouts.length, 5);
  assert.equal(latest.hasGhost, true, 'a save without the ghost keeps the stored one');

  // A save that adds workouts faster than they can be done is refused and changes nothing.
  await assert.rejects(save(tokenA, 2, pack(withMoreWorkouts(fixtureJson, 200))), e => e.status === 'OUT_OF_RANGE');
  assert.equal((await load(tokenA)).rev, 2);
});

test('only the owner reads the cloud copy, and nobody writes it directly', async () => {
  const owner = await auth.google('player-2');
  await save(owner.idToken, 0, clientPacked);
  const stranger = await auth.google('player-3');
  const doc = `${FIRESTORE}/users/${owner.localId}/progress/state`;

  const own = await fetch(doc, { headers: { Authorization: `Bearer ${owner.idToken}` } });
  assert.equal(own.status, 200);
  const other = await fetch(doc, { headers: { Authorization: `Bearer ${stranger.idToken}` } });
  assert.equal(other.status, 403);
  const write = await fetch(doc, {
    method: 'PATCH', headers: { Authorization: `Bearer ${owner.idToken}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ fields: { rev: { integerValue: '99' } } }),
  });
  assert.equal(write.status, 403);
  // A forged wallet never reaches Firestore.
  const forged = JSON.parse(fixtureJson);
  const ledger = JSON.parse(forged.ledger); ledger.gems = 5000000; forged.ledger = JSON.stringify(ledger);
  await assert.rejects(save(owner.idToken, 1, pack(JSON.stringify(forged))), e => e.status === 'OUT_OF_RANGE');
  // The stranger has no copy of their own and cannot see the owner's.
  assert.deepEqual(await load(stranger.idToken), { exists: false, rev: 0, snapshot: '', hasGhost: false, ghost: '' });
});
