// Validation for emote capture uploads (see emoteCaptures.js). Pure, so it is unit-tested
// without Firebase.
const MAX_CAPTURES_PER_OWNER = 12;
const MAX_CAPTURES_OPEN = 40;        // waiting to be pulled, project-wide, for accounts not allowlisted
const MAX_CHUNKS = 20;
const MAX_CHUNK_BYTES = 2.5 * 1024 * 1024;
const MAX_FRAMES = 400;

const int = (v, min, max, what) => {
  if (!Number.isInteger(v) || v < min || v > max) throw new Error(`Invalid ${what}.`);
  return v;
};
const num = (v, min, max, what) => {
  if (typeof v !== 'number' || !Number.isFinite(v) || v < min || v > max) throw new Error(`Invalid ${what}.`);
  return v;
};

function validateMeta(meta) {
  if (!meta || typeof meta !== 'object') throw new Error('Missing capture metadata.');
  if (!/^[a-f0-9]{32}$/.test(meta.id || '')) throw new Error('Invalid capture ID.');
  if (!/^[a-z0-9_-]{1,24}$/.test(meta.name || '')) throw new Error('Invalid emote name.');
  return {
    version: 1, id: meta.id, name: meta.name,
    frames: int(meta.frames, 1, MAX_FRAMES, 'frame count'),
    chunks: int(meta.chunks, 1, MAX_CHUNKS, 'chunk count'),
    width: int(meta.width, 64, 4096, 'width'), height: int(meta.height, 64, 4096, 'height'),
    rotation: int(meta.rotation, 0, 359, 'rotation'),
    verticallyMirrored: meta.verticallyMirrored === true, frontFacing: meta.frontFacing === true,
    seconds: num(meta.seconds, .2, 30, 'duration'), fps: num(meta.fps, 1, 120, 'frame rate'),
    device: String(meta.device || '').slice(0, 80), createdUtc: String(meta.createdUtc || '').slice(0, 40),
  };
}

// A chunk is [int32 count] then count × ([int32 length][float32 seconds][JPEG bytes]).
function validateChunk(index, base64, meta) {
  int(index, 0, meta.chunks - 1, 'chunk index');
  if (typeof base64 !== 'string' || base64.length === 0 || base64.length > Math.ceil(MAX_CHUNK_BYTES / 3) * 4 + 4)
    throw new Error('Invalid chunk size.');
  const bytes = Buffer.from(base64, 'base64');
  if (bytes.length < 16 || bytes.length > MAX_CHUNK_BYTES) throw new Error('Invalid chunk size.');
  const count = bytes.readInt32LE(0);
  if (count < 1 || count > MAX_FRAMES) throw new Error('Invalid chunk.');
  let p = 4;
  for (let i = 0; i < count; i++) {
    if (p + 8 > bytes.length) throw new Error('Truncated chunk.');
    const length = bytes.readInt32LE(p);
    if (length < 4 || p + 8 + length > bytes.length) throw new Error('Truncated chunk.');
    if (bytes[p + 8] !== 0xFF || bytes[p + 9] !== 0xD8) throw new Error('Chunk frames must be JPEG.');
    p += 8 + length;
  }
  if (p !== bytes.length) throw new Error('Invalid chunk.');
  return bytes;
}

module.exports = { validateMeta, validateChunk, MAX_CAPTURES_PER_OWNER, MAX_CAPTURES_OPEN, MAX_CHUNKS, MAX_CHUNK_BYTES };
