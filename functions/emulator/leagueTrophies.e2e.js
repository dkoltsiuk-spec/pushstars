// End-to-end check of the league's trophies against the Firebase emulators (never production):
//   npm run test:emulator   (firebase emulators:exec --project demo-pushstars ...)
// A guest sends its workout ledger (as LeagueClient.SyncTrophies packs it) and the table shows the
// same trophies the phone does; more trophies than the wins in the ledger, or workouts added faster
// than they can be done, are refused and leave the table as it was.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');

const PROJECT = process.env.GCLOUD_PROJECT || 'demo-pushstars';
assert.ok(PROJECT.startsWith('demo-'), 'Run only against a demo- project on the emulators.');
const AUTH = `http://${process.env.FIREBASE_AUTH_EMULATOR_HOST || '127.0.0.1:9099'}/identitytoolkit.googleapis.com/v1`;
const FUNCTIONS = `http://127.0.0.1:5001/${PROJECT}/us-central1`;

const workoutsJson = JSON.parse(fs.readFileSync(path.join(__dirname, '..', 'test', 'fixtures', 'progress-snapshot.json'), 'utf8')).workouts;
const pack = obj => zlib.gzipSync(Buffer.from(JSON.stringify(obj), 'utf8')).toString('base64');

async function post(url, body, token) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: JSON.stringify(body),
  });
  return { status: response.status, body: await response.json().catch(() => ({})) };
}
const guest = async () => (await post(`${AUTH}/accounts:signUp?key=demo`, { returnSecureToken: true })).body;
async function call(name, data, token) {
  const { body } = await post(`${FUNCTIONS}/${name}`, { data }, token);
  if (body.error) { const e = new Error(body.error.message); e.status = body.error.status; throw e; }
  return body.result;
}

test('the league table shows the trophies the phone shows', async () => {
  const player = await guest();
  const ledger = JSON.parse(workoutsJson);
  assert.equal((await call('getLeagueStandings', {}, player.idToken)).own.trophies, 0);

  assert.deepEqual(await call('syncLeagueTrophies', { workouts: pack(ledger) }, player.idToken), { trophies: 163, league: 'bronze' });
  let page = await call('getLeagueStandings', {}, player.idToken);
  assert.equal(page.own.trophies, 163);
  assert.ok(page.rows.some(r => r.uid === player.localId && r.trophies === 163));

  // A loss lowers it just the same.
  ledger.Workouts.push({ ...ledger.Workouts[1], Id: 'duel-2', Won: false, TrophyDelta: -5, StreakBonusTrophies: 0 });
  ledger.Trophies = 158;
  assert.equal((await call('syncLeagueTrophies', { workouts: pack(ledger) }, player.idToken)).trophies, 158);

  // Inflated totals and impossible pace are refused; the table keeps the last accepted value.
  await assert.rejects(call('syncLeagueTrophies', { workouts: pack({ ...ledger, Trophies: 5000 }) }, player.idToken),
    e => e.status === 'OUT_OF_RANGE');
  const burst = { ...ledger, Workouts: [...ledger.Workouts] };
  for (let i = 0; i < 40; i++) burst.Workouts.push({ ...ledger.Workouts[1], Id: `fake-${i}`, TrophyDelta: 0 });
  await assert.rejects(call('syncLeagueTrophies', { workouts: pack(burst) }, player.idToken), e => e.status === 'OUT_OF_RANGE');
  page = await call('getLeagueStandings', {}, player.idToken);
  assert.equal(page.own.trophies, 158);

  await assert.rejects(call('syncLeagueTrophies', { workouts: pack(ledger) }), e => e.status === 'UNAUTHENTICATED');
});
