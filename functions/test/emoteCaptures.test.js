const { test } = require('node:test');
const assert = require('node:assert/strict');
const { validateMeta, validateChunk, MAX_CHUNK_BYTES } = require('../src/emoteCaptureCodec');

const meta = () => ({ id: 'b'.repeat(32), name: 'loser', frames: 3, chunks: 2, width: 1280, height: 720,
  rotation: 90, verticallyMirrored: false, frontFacing: true, seconds: 5, fps: 24, device: 'test', createdUtc: '2026-10-04T10:00:00Z' });

function chunk(frames) {
  const parts = [Buffer.alloc(4)];
  parts[0].writeInt32LE(frames.length);
  for (const [seconds, jpeg] of frames) {
    const head = Buffer.alloc(8); head.writeInt32LE(jpeg.length); head.writeFloatLE(seconds, 4);
    parts.push(head, jpeg);
  }
  return Buffer.concat(parts);
}
const jpeg = n => Buffer.concat([Buffer.from([0xFF, 0xD8]), Buffer.alloc(n, 7)]);

test('metadata is normalised and bounded', () => {
  assert.equal(validateMeta(meta()).name, 'loser');
  assert.throws(() => validateMeta({ ...meta(), id: 'nope' }), /capture ID/);
  assert.throws(() => validateMeta({ ...meta(), name: '../x' }), /emote name/);
  assert.throws(() => validateMeta({ ...meta(), chunks: 99 }), /chunk count/);
  assert.throws(() => validateMeta({ ...meta(), frames: 0 }), /frame count/);
});

test('a well-formed chunk passes and comes back as bytes', () => {
  const bytes = chunk([[0, jpeg(100)], [.04, jpeg(50)]]);
  assert.deepEqual(validateChunk(1, bytes.toString('base64'), validateMeta(meta())), bytes);
});

test('malformed chunks are rejected', () => {
  const m = validateMeta(meta());
  assert.throws(() => validateChunk(2, chunk([[0, jpeg(10)]]).toString('base64'), m), /chunk index/);
  assert.throws(() => validateChunk(0, chunk([[0, Buffer.alloc(20, 1)]]).toString('base64'), m), /JPEG/);
  assert.throws(() => validateChunk(0, chunk([[0, jpeg(10)]]).subarray(0, 18).toString('base64'), m), /Truncated/);
  assert.throws(() => validateChunk(0, Buffer.concat([chunk([[0, jpeg(10)]]), Buffer.alloc(3)]).toString('base64'), m), /Invalid chunk/);
  assert.throws(() => validateChunk(0, Buffer.alloc(MAX_CHUNK_BYTES + 10).toString('base64'), m), /chunk size/);
  assert.throws(() => validateChunk(0, '', m), /chunk size/);
});
