// Emote capture drafts: a phone records the owner performing a gesture (JPEG frames in a few
// chunks) and parks the take in a private Storage folder; the desktop mocap pipeline
// (Tools/EmoteMocap/pull_captures.js) pulls it with the project owner's credentials, turns it
// into an emote clip and deletes the cloud copy. Nothing here is readable by clients.
const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { validateMeta, validateChunk, MAX_CAPTURES_PER_OWNER, MAX_CAPTURES_OPEN } = require('./emoteCaptureCodec');

const fail = (code, text) => { throw new functions.https.HttpsError(code, text); };
const uidOf = context => context.auth?.uid || fail('unauthenticated', 'Sign in required.');
const folder = (uid, id) => `emote-captures/${uid}/${id}/`;
const chunkName = index => `chunk_${String(index).padStart(3, '0')}.bin`;

// One call per chunk; every call carries the take's metadata, so a retry of any chunk in any
// order is safe. The take is marked ready when its last missing chunk lands.
exports.saveEmoteCaptureChunk = functions.runWith({ memory: '512MB', timeoutSeconds: 120 }).https.onCall(async (data, context) => {
  const uid = uidOf(context);
  let meta, bytes;
  try {
    meta = validateMeta(data?.meta);
    bytes = validateChunk(data?.index, data?.data, meta);
  } catch (e) { fail('invalid-argument', e.message); }
  const bucket = admin.storage().bucket(), dir = folder(uid, meta.id);
  const metaFile = bucket.file(dir + 'meta.json');
  const [known] = await metaFile.exists();
  if (!known) {
    // Quota before the first byte is stored: a few takes per account, and a ceiling on takes
    // waiting to be pulled across the project unless the account is on the recorder allowlist.
    const [mine] = await bucket.getFiles({ prefix: `emote-captures/${uid}/`, matchGlob: '**/meta.json' });
    if (mine.length >= MAX_CAPTURES_PER_OWNER) fail('resource-exhausted', 'Capture library is full. Pull the takes on the desktop first.');
    const access = await admin.firestore().doc(`botRecorderAccess/${uid}`).get();
    if (!(access.exists && access.data().enabled === true)) {
      const [open] = await bucket.getFiles({ prefix: 'emote-captures/', matchGlob: '**/meta.json' });
      if (open.length >= MAX_CAPTURES_OPEN) fail('resource-exhausted', 'Too many captures are waiting to be processed.');
    }
    await metaFile.save(JSON.stringify({ ...meta, ownerUid: uid, status: 'uploading' }),
      { resumable: false, contentType: 'application/json', metadata: { cacheControl: 'private, no-store' } });
  }
  await bucket.file(dir + chunkName(data.index)).save(bytes,
    { resumable: false, contentType: 'application/octet-stream', metadata: { cacheControl: 'private, no-store' } });
  const [stored] = await bucket.getFiles({ prefix: dir + 'chunk_' });
  const ready = stored.length >= meta.chunks;
  if (ready) {
    await metaFile.save(JSON.stringify({ ...meta, ownerUid: uid, status: 'ready', savedAt: new Date().toISOString() }),
      { resumable: false, contentType: 'application/json', metadata: { cacheControl: 'private, no-store' } });
  }
  return { id: meta.id, received: stored.length, chunks: meta.chunks, ready, cloudPath: dir };
});
