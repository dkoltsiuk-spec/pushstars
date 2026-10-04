const { test } = require('node:test');
const assert = require('node:assert/strict');
const { gzipSync } = require('node:zlib');
const fs = require('node:fs'), vm = require('node:vm');
const { validateRecording, FRAME_BYTES } = require('../src/botRecordingCodec');

function take() {
  const raw = Buffer.alloc(12 + 4 * FRAME_BYTES);
  raw.writeUInt32LE(0x31525350); raw.writeInt32LE(95, 4); raw.writeInt32LE(4, 8);
  [0, 2, 17, 59.99].forEach((time, i) => {
    const p = 12 + i * FRAME_BYTES; raw[p] = 2; raw.writeFloatLE(time, p + 1);
    raw.writeFloatLE(1, p + 29); raw.writeFloatLE(1, p + 57);
  });
  return { version: 1, id: 'a'.repeat(32), ownerUid: 'owner', displayName: 'TEST BOT',
    countryCode: 'MD', trophies: 150, avatar: 0, gender: 0, achievementIds: [],
    fight: { exercise: 'pushups', arenaId: 'crystal', reps: 4, repTimes: [2, 3, 17, 19],
      durationSec: 60, avgForm: 92, source: 'bot-recording', recordedAtUtc: '', motionBase64: gzipSync(raw).toString('base64') } };
}
function fixture() {
  const docs = new Map(), files = new Map(); let failUpload = false;
  const ref = path => ({ path, get: async () => snap(path), collection: name => ({ doc: id => ref(`${path}/${name}/${id}`) }),
    update: async data => docs.set(path, { ...docs.get(path), ...data }) });
  const snap = path => ({ exists: docs.has(path), data: () => docs.get(path) });
  const firestore = () => ({ doc: ref, runTransaction: async fn => fn({
    getAll: async (...refs) => refs.map(r => snap(r.path)),
    create: (r, data) => docs.set(r.path, data), set: (r, data) => docs.set(r.path, { ...docs.get(r.path), ...data }),
  }) });
  firestore.FieldValue = { serverTimestamp: () => 123 };
  class HttpsError extends Error { constructor(code, message) { super(message); this.code = code; } }
  const https = { HttpsError, onCall: fn => fn };
  const admin = { firestore, storage: () => ({ bucket: () => ({ file: path => ({
    save: async data => { if (failUpload) throw new Error('network'); files.set(path, Buffer.from(data)); },
    download: async () => [files.get(path)],
  }) }) }) };
  const sandbox = { exports: {}, require: name => name === './admin' ? admin : name === './botRecordingCodec'
    ? { validateRecording } : { https, runWith: () => ({ https }) } };
  vm.runInNewContext(fs.readFileSync(require.resolve('../src/botRecordings'), 'utf8'), sandbox);
  return { ...sandbox.exports, docs, files, setFailure: value => { failUpload = value; } };
}
const auth = { auth: { uid: 'owner' } };

test('preserves irregular timestamps and pause frames exactly', () => {
  const input = take(), valid = validateRecording(input, 'owner');
  assert.deepEqual(valid.recording.fight.repTimes, [2, 3, 17, 19]);
  assert.equal(valid.recording.fight.motionBase64, input.fight.motionBase64);
  assert.equal(valid.frames, 4);
});
test('rejects wrong owner, unordered reps, missing poses and unsupported data', () => {
  assert.throws(() => validateRecording(take(), 'stranger'), /identity/);
  const wrong = take(); wrong.fight.repTimes = [2, 3, 1, 19];
  assert.throws(() => validateRecording(wrong, 'owner'), /timing/);
  wrong.fight.repTimes = [2, 3, 17, 19]; wrong.fight.motionBase64 = '';
  assert.throws(() => validateRecording(wrong, 'owner'), /encoding/);
  wrong.fight.motionBase64 = gzipSync(Buffer.alloc(12)).toString('base64');
  assert.throws(() => validateRecording(wrong, 'owner'), /version/);
});
test('unauthenticated upload and download are rejected', async () => {
  const f = fixture();
  await assert.rejects(f.saveBotRecording({ recording: take() }, {}), e => e.code === 'unauthenticated');
  await assert.rejects(f.getBotRecording({ id: take().id }, {}), e => e.code === 'unauthenticated');
  assert.equal(f.docs.size, 0);
});
test('cloud roundtrip is private, exact and idempotent', async () => {
  const f = fixture(), recording = take();
  const saved = await f.saveBotRecording({ recording }, auth);
  assert.equal(saved.saved, true);
  await f.saveBotRecording({ recording: { ...recording, cloudPath: saved.cloudPath } }, auth);
  assert.equal(f.docs.get('botDraftOwners/owner').count, 1);
  const loaded = await f.getBotRecording({ id: recording.id }, auth);
  assert.deepEqual(Array.from(loaded.recording.fight.repTimes), recording.fight.repTimes);
  assert.equal(loaded.recording.fight.motionBase64, recording.fight.motionBase64);
  await assert.rejects(f.getBotRecording({ id: recording.id }, { auth: { uid: 'stranger' } }), e => e.code === 'not-found');
  await assert.rejects(f.saveBotRecording({ recording: { ...recording, displayName: 'OTHER' } }, auth), e => e.code === 'already-exists');
});
test('failed upload is not ready; retry uses same quota reservation', async () => {
  const f = fixture(), recording = take(); f.setFailure(true);
  await assert.rejects(f.saveBotRecording({ recording }, auth), /network/);
  await assert.rejects(f.getBotRecording({ id: recording.id }, auth), e => e.code === 'not-found');
  f.setFailure(false); await f.saveBotRecording({ recording }, auth);
  assert.equal(f.docs.get('botDraftOwners/owner').count, 1);
  assert.equal((await f.getBotRecording({ id: recording.id }, auth)).recording.id, recording.id);
});
test('cloud integrity and per-owner quota are checked', async () => {
  const f = fixture(), recording = take();
  await f.saveBotRecording({ recording }, auth);
  const path = `bot-drafts/owner/${recording.id}.json`;
  f.files.set(path, Buffer.from(JSON.stringify({ ...recording, displayName: 'CHANGED' })));
  await assert.rejects(f.getBotRecording({ id: recording.id }, auth), e => e.code === 'data-loss');
  f.docs.set('botDraftOwners/owner', { count: 15 });
  await assert.rejects(f.saveBotRecording({ recording: { ...recording, id: 'b'.repeat(32) } }, auth), e => e.code === 'resource-exhausted');
});
