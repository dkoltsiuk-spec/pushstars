// End-to-end check of the online / players-today counts against the Firebase emulators (never production):
//   npm run test:emulator   (firebase emulators:exec --project demo-pushstars ...)
// Both figures are counted from the presence documents by real Firestore count queries; nothing
// else (no shared per-day document) is written on a heartbeat.
const { test } = require('node:test');
const assert = require('node:assert/strict');

const PROJECT = process.env.GCLOUD_PROJECT || 'demo-pushstars';
assert.ok(PROJECT.startsWith('demo-'), 'Run only against a demo- project on the emulators.');
const AUTH = `http://${process.env.FIREBASE_AUTH_EMULATOR_HOST || '127.0.0.1:9099'}/identitytoolkit.googleapis.com/v1`;
const FUNCTIONS = `http://127.0.0.1:5001/${PROJECT}/us-central1`;
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
async function heartbeat(token) {
  const { body } = await post(`${FUNCTIONS}/heartbeatPresence`, { data: {} }, token);
  if (body.error) throw new Error(body.error.message);
  return body.result;
}

test('online and players-today are counted from the presence documents', async () => {
  const before = await heartbeat((await guest()).idToken);
  const a = await guest(), b = await guest();
  assert.deepEqual(await heartbeat(a.idToken), { online: before.online + 1, today: before.today + 1 });
  assert.deepEqual(await heartbeat(a.idToken), { online: before.online + 1, today: before.today + 1 }, 'one account, once');
  assert.deepEqual(await heartbeat(b.idToken), { online: before.online + 2, today: before.today + 2 });
  const day = new Date().toISOString().slice(0, 10);
  const shared = await fetch(`${FIRESTORE}/activeDays/${day}`, { headers: { Authorization: 'Bearer owner' } });
  assert.equal(shared.status, 404, 'no shared per-day document');
});
