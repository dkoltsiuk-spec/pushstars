const functions = require('firebase-functions/v1');
const admin = require('./admin');
const { validateRecording } = require('./botRecordingCodec');
const db = admin.firestore();
const fail = (code, text) => { throw new functions.https.HttpsError(code, text); };
const uidOf = context => context.auth?.uid || fail('unauthenticated', 'Sign in required.');
const folder = uid => db.doc(`botDraftOwners/${uid}`);
const recordingRef = (uid, id) => folder(uid).collection('recordings').doc(id);

exports.getBotRecorderAccess = functions.https.onCall(async (_, context) => {
  const uid = uidOf(context);
  const access = await db.doc(`botRecorderAccess/${uid}`).get();
  return { uid, enabled: access.exists && access.data().enabled === true };
});

// Private drafts only. There is deliberately no write path into public matchmaking here.
// Any authenticated owner may keep at most 15 immutable takes; production authoring UI is
// separately allowlisted, while Development builds can exercise the whole private pipeline.
exports.saveBotRecording = functions.runWith({ memory: '256MB', timeoutSeconds: 60 }).https.onCall(async (data, context) => {
  const uid = uidOf(context);
  let validated;
  try { validated = validateRecording(data?.recording, uid); }
  catch (e) { fail('invalid-argument', e.message); }
  const { recording, json, hash, frames, phaseFrames } = validated;
  const ref = recordingRef(uid, recording.id), owner = folder(uid);
  const path = `bot-drafts/${uid}/${recording.id}.json`;
  // Reserve the ID/quota before the upload. A retry reuses this exact reservation and file.
  await db.runTransaction(async tx => {
    const [existing, account] = await tx.getAll(ref, owner);
    if (existing.exists) {
      if (existing.data().hash !== hash) fail('already-exists', 'This recording ID already contains a different take.');
      return;
    }
    const count = account.exists ? account.data().count || 0 : 0;
    if (count >= 15) fail('resource-exhausted', 'The 15-recording test library is full.');
    tx.set(owner, { count: count + 1 }, { merge: true });
    tx.create(ref, { ownerUid: uid, id: recording.id, hash, path, status: 'uploading', frames, phaseFrames,
      displayName: recording.displayName, reps: recording.fight.reps, createdAt: admin.firestore.FieldValue.serverTimestamp() });
  });
  await admin.storage().bucket().file(path).save(json, { resumable: false, contentType: 'application/json',
    metadata: { cacheControl: 'private, no-store', metadata: { sha256: hash } } });
  await ref.update({ status: 'ready', savedAt: admin.firestore.FieldValue.serverTimestamp() });
  return { id: recording.id, cloudPath: path, frames, saved: true };
});

exports.getBotRecording = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context), id = data?.id;
  if (!/^[a-f0-9]{32}$/.test(id || '')) fail('invalid-argument', 'Invalid recording ID.');
  const snap = await recordingRef(uid, id).get();
  if (!snap.exists || snap.data().status !== 'ready') fail('not-found', 'Cloud recording is not ready.');
  const [bytes] = await admin.storage().bucket().file(snap.data().path).download();
  const validated = validateRecording(JSON.parse(bytes.toString('utf8')), uid);
  if (validated.hash !== snap.data().hash) fail('data-loss', 'Cloud recording failed its integrity check.');
  return { recording: { ...validated.recording, cloudPath: snap.data().path } };
});
