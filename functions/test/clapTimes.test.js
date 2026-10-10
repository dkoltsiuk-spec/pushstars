const { test } = require('node:test');
const assert = require('node:assert/strict');
const { gzipSync } = require('node:zlib');
const { cleanClapTimes } = require('../src/clapTimes');
const { validateRecording, FRAME_BYTES } = require('../src/botRecordingCodec');

function take(clapTimes) {
  const raw = Buffer.alloc(12 + 4 * FRAME_BYTES);
  raw.writeUInt32LE(0x31525350); raw.writeInt32LE(95, 4); raw.writeInt32LE(4, 8);
  [0, 2, 17, 59.99].forEach((time, i) => { const p = 12 + i * FRAME_BYTES; raw[p] = 2; raw.writeFloatLE(time, p + 1); });
  const fight = { exercise: 'pushups', arenaId: 'crystal', reps: 4, repTimes: [2, 3, 17, 19], durationSec: 60,
    avgForm: 92, source: 'bot-recording', recordedAtUtc: '', motionBase64: gzipSync(raw).toString('base64') };
  if (clapTimes !== undefined) fight.clapTimes = clapTimes;
  return { version: 1, id: 'a'.repeat(32), ownerUid: 'owner', displayName: 'TEST BOT', countryCode: 'MD',
    trophies: 150, avatar: 0, gender: 0, achievementIds: [], fight };
}

test('keeps a well-formed clap timeline', () => {
  assert.deepEqual(cleanClapTimes([2.3, 17.4], [2, 3, 17, 19], 60), [2.3, 17.4]);
  assert.deepEqual(cleanClapTimes([], [2, 3], 60), []);
});
test('drops a malformed clap timeline whole instead of failing', () => {
  const reps = [2, 3, 17, 19];
  assert.deepEqual(cleanClapTimes(undefined, reps, 60), []);
  assert.deepEqual(cleanClapTimes('2.3', reps, 60), []);
  assert.deepEqual(cleanClapTimes([17.4, 2.3], reps, 60), []);        // not ascending
  assert.deepEqual(cleanClapTimes([2.3, 2.5], reps, 60), []);         // closer than a rep can be
  assert.deepEqual(cleanClapTimes([2.3, 61], reps, 60), []);          // past the end of the set
  assert.deepEqual(cleanClapTimes([2.3, NaN], reps, 60), []);
  assert.deepEqual(cleanClapTimes([1, 2, 3, 4, 5], reps, 60), []);    // more claps than reps
});
test('a bot recording carries its claps, and one without them is unchanged', () => {
  const plain = validateRecording(take(), 'owner');
  assert.equal('clapTimes' in plain.recording.fight, false);
  assert.equal(validateRecording(take([]), 'owner').hash, plain.hash);
  assert.equal(validateRecording(take([99]), 'owner').hash, plain.hash);
  const clapped = validateRecording(take([3.2, 19.3]), 'owner');
  assert.deepEqual(clapped.recording.fight.clapTimes, [3.2, 19.3]);
  assert.deepEqual(clapped.recording.fight.repTimes, [2, 3, 17, 19]);
});
