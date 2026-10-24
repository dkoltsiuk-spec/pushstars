// The bot pool: the owner's recorded sets (Bot Lab takes promoted by tools/Bots/publish_pool.js),
// each with a player's face — name, flag, hero, trophies. While the game has few players, a
// search with nobody to pair offers one of them as "an opponent at your level", the same card a
// stranger's season set gets. A player meets each bot once: taking its fight puts it in their
// botEncounters, and the search never offers it to them again.
//
//   botPool/{id}        name, country, hero, gender, trophies, reps, repTimes, clapTimes,
//                       path (Storage JSON: the whole BotRecording, motion included), hash, enabled
//   botEncounters/{uid} ids — bots this player has fought
//
// Both collections are server-only (firestore.rules has no match for them).
const crypto = require('crypto');
const functions = require('firebase-functions/v1');
const admin = require('./admin');
const db = admin.firestore();
const fail = (code, text) => { throw new functions.https.HttpsError(code, text); };
const uidOf = context => context.auth?.uid || fail('unauthenticated', 'Sign in required.');
const { pickBot, offerOf, validId } = require('./botPoolRules');
const encountersRef = uid => db.doc(`botEncounters/${uid}`);

const POOL_READ = 100;   // the whole pool, while it is a launch-sized one
// `foughtHere`: ids the phone remembers itself — its mark may not have reached the server yet.
async function botFor(uid, best, foughtHere) {
  const [pool, mine] = await Promise.all([
    db.collection('botPool').where('enabled', '==', true).limit(POOL_READ).get(),
    encountersRef(uid).get(),
  ]);
  const fought = [...(mine.exists ? mine.data().ids || [] : []), ...(Array.isArray(foughtHere) ? foughtHere.filter(validId) : [])];
  const bot = pickBot(pool.docs.map(d => ({ id: d.id, ...d.data() })), fought, best, () => crypto.randomInt(1000) / 1000);
  return bot ? offerOf(bot) : null;
}

// The whole recording (motion included) of a bot in the pool, for the fight itself. Any signed-in
// player may read one: it is what the search just offered them.
exports.getPoolBot = functions.runWith({ memory: '256MB' }).https.onCall(async (data, context) => {
  uidOf(context);
  if (!validId(data?.id)) fail('invalid-argument', 'Invalid bot.');
  const snap = await db.doc(`botPool/${data.id}`).get();
  if (!snap.exists || snap.data().enabled === false) fail('not-found', 'No such bot.');
  const [bytes] = await admin.storage().bucket().file(snap.data().path).download();
  const json = bytes.toString('utf8');
  if (crypto.createHash('sha256').update(json).digest('hex') !== snap.data().hash) fail('data-loss', 'Bot failed its integrity check.');
  return { recording: JSON.parse(json) };
});

// The player took this bot's fight: it leaves their search for good.
exports.markBotFought = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context);
  if (!validId(data?.id)) fail('invalid-argument', 'Invalid bot.');
  await encountersRef(uid).set({ ids: admin.firestore.FieldValue.arrayUnion(data.id) }, { merge: true });
  return { ok: true };
});

exports.botFor = botFor;
