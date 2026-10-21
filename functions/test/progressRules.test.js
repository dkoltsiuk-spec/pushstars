const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');
const rules = require('../src/progressRules');

// Written by the Unity EditMode test ProgressSnapshotTests.GoldenFile_* through the real ledgers.
const fixtureJson = fs.readFileSync(path.join(__dirname, 'fixtures', 'progress-snapshot.json'), 'utf8');
const fixturePacked = fs.readFileSync(path.join(__dirname, 'fixtures', 'progress-snapshot.packed.txt'), 'utf8').trim();
const pack = obj => zlib.gzipSync(Buffer.from(typeof obj === 'string' ? obj : JSON.stringify(obj), 'utf8')).toString('base64');

function edit(change) {
  const snapshot = JSON.parse(fixtureJson);
  const ledger = JSON.parse(snapshot.ledger);
  const workouts = JSON.parse(snapshot.workouts);
  change({ snapshot, ledger, workouts });
  snapshot.ledger = JSON.stringify(ledger);
  snapshot.workouts = JSON.stringify(workouts);
  return pack(snapshot);
}
const rejects = (packed, code, pattern) => assert.throws(() => rules.inspectSnapshot(packed),
  e => e instanceof rules.RuleError && e.code === code && (!pattern || pattern.test(e.message)));

test('reads the snapshot exactly as the Unity client packs it', () => {
  assert.equal(zlib.gunzipSync(Buffer.from(fixturePacked, 'base64')).toString('utf8'), fixtureJson);
  const summary = rules.inspectSnapshot(fixturePacked);
  assert.equal(summary.peakAura, 13000);
  assert.equal(summary.aura, 13000);
  assert.equal(summary.workouts, 2);
  assert.equal(summary.totalReps, 64);
  assert.equal(summary.xp, 1110);
  assert.equal(summary.caseAwards, 2);
  assert.equal(summary.bossCleared, 1);
  assert.ok(summary.gems > 0);
});

test('refuses input that is not a packed snapshot', () => {
  rejects('', 'invalid-argument');
  rejects(undefined, 'invalid-argument');
  rejects('not base64 gzip', 'invalid-argument');
  rejects(pack('{"version":2}'), 'invalid-argument', /Unsupported/);
  rejects(pack('[not json'), 'invalid-argument');
  rejects('A'.repeat(rules.LIMITS.PACKED_SNAPSHOT_CHARS + 1), 'out-of-range');
});

test('refuses broken ledgers', () => {
  rejects(edit(({ ledger }) => { ledger.gems = -5; }), 'invalid-argument');
  rejects(edit(({ ledger }) => { ledger.gems = 1.5; }), 'invalid-argument');
  rejects(edit(({ ledger }) => { ledger.aura = ledger.peakAura + 1; }), 'invalid-argument', /peak/);
  rejects(edit(({ ledger }) => { ledger.version = 7; }), 'invalid-argument');
  rejects(edit(({ workouts }) => { workouts.Version = 2; }), 'invalid-argument');
  rejects(edit(({ ledger }) => { ledger.auraReceipts = [''] ; }), 'invalid-argument');
});

test('every total is bounded by the workouts that earned it', () => {
  rejects(edit(({ ledger }) => { ledger.gems = 1e6; }), 'out-of-range', /gems/);
  rejects(edit(({ ledger }) => { ledger.peakAura = 1e9; ledger.aura = 1e9; }), 'out-of-range', /Aura/);
  rejects(edit(({ workouts }) => { workouts.Xp = 1e7; }), 'out-of-range', /XP/);
  rejects(edit(({ workouts }) => { workouts.TotalReps = 1e6; }), 'out-of-range', /reps/);
  rejects(edit(({ workouts }) => { workouts.Workouts[1].Reps = 500; }), 'out-of-range', /time/);
  rejects(edit(({ workouts }) => { workouts.Trophies = 99999; }), 'out-of-range', /trophies/);
  rejects(edit(({ snapshot }) => { snapshot.prefs.find(p => p.key === 'boss_progress').intValue = 16; }), 'out-of-range', /bosses/);
  rejects(edit(({ ledger }) => { ledger.awardedWorkouts = Array.from({ length: 500 }, (_, i) => `grant-${i}`); }), 'out-of-range', /cases/);
});

test('a real player far along still passes', () => {
  const packed = edit(({ ledger, workouts }) => {
    workouts.Workouts = Array.from({ length: 400 }, (_, i) => ({ ...workouts.Workouts[1], Id: `duel-${i}`, Reps: 45 }));
    workouts.TotalReps = 400 * 45;
    workouts.Xp = 400 * 45 * 15 + 400 * 150;
    workouts.Trophies = 2400;
    ledger.peakAura = 1200000; ledger.aura = 900000;
    ledger.awardedWorkouts = Array.from({ length: 300 }, (_, i) => `duel-${i}`);
    ledger.processedWorkoutIds = Array.from({ length: 400 }, (_, i) => `duel-${i}`);
    ledger.gemReceipts = ['boss-map:forest:gems:v1', 'boss-map:ice:gems:v1'];
    ledger.cases = [];
    ledger.gems = 300 * 150;
  });
  const summary = rules.inspectSnapshot(packed);
  assert.equal(summary.workouts, 400);
});

test('workouts cannot be added faster than they can be done', () => {
  const previous = { workouts: 10 };
  assert.doesNotThrow(() => rules.checkPace(null, { workouts: 5000 }, 0)); // first copy: evidence only
  assert.doesNotThrow(() => rules.checkPace(previous, { workouts: 30 }, 0)); // a burst
  assert.doesNotThrow(() => rules.checkPace(previous, { workouts: 110 }, 3600)); // an hour offline
  assert.throws(() => rules.checkPace(previous, { workouts: 31 }, 0), e => e.code === 'out-of-range');
  assert.throws(() => rules.checkPace(previous, { workouts: 500 }, 3600), e => e.code === 'out-of-range');
});

test('ghost sets must be consistent and bounded', () => {
  assert.equal(rules.inspectGhost(pack({ reps: 2, repTimes: [1.2, 2.5] })).reps, 2);
  assert.throws(() => rules.inspectGhost(pack({ reps: 3, repTimes: [1] })), e => e.code === 'invalid-argument');
  assert.throws(() => rules.inspectGhost(pack({ reps: 99, repTimes: Array(99).fill(1) })), e => e.code === 'out-of-range');
});
