// Push notifications (Firebase Cloud Messaging) — the "notify me when an opponent is found"
// switch on the search screen.
//
//   registerPushToken → the phone's FCM token, its language; an empty token forgets the phone
//   callPlayer        → (used by liveDuel.js) "an opponent is waiting" to one standby searcher
//
// pushTokens/{uid} is server-only (firestore.rules deny clients). It also keeps the call
// budget: one call per PUSH_CALL_COOLDOWN_SEC and PUSH_CALLS_PER_DAY a UTC day, so a player
// who allowed notifications is never spammed by a busy evening.
const functions = require('firebase-functions/v1');
const admin = require('./admin');
const C = require('./constants');
const db = admin.firestore();

const fail = (code, message) => { throw new functions.https.HttpsError(code, message); };
const uidOf = context => context.auth?.uid || fail('unauthenticated', 'Sign in required.');
const tokenRef = uid => db.doc(`pushTokens/${uid}`);
const dayOf = ms => new Date(ms).toISOString().slice(0, 10);
const LANGS = ['en', 'ru', 'pt-BR'];

// Copy per app language (Localization.NormalizeLanguage on the phone).
const TEXT = {
  queue: {
    en: { title: 'Opponent found! ⚔️', body: '{name} is waiting for a duel. Jump in within 30 seconds!' },
    ru: { title: 'Соперник найден! ⚔️', body: '{name} ждёт дуэль. Заходи в течение 30 секунд!' },
    'pt-BR': { title: 'Oponente encontrado! ⚔️', body: '{name} está esperando um duelo. Entre em até 30 segundos!' },
  },
  friend: {
    en: { title: 'Duel invite! ⚔️', body: '{name} calls you to a push-up duel. Tap to join!' },
    ru: { title: 'Вызов на дуэль! ⚔️', body: '{name} зовёт тебя на дуэль в отжиманиях. Жми, чтобы принять!' },
    'pt-BR': { title: 'Convite para duelo! ⚔️', body: '{name} te chamou para um duelo de flexões. Toque para entrar!' },
  },
};

exports.registerPushToken = functions.https.onCall(async (data, context) => {
  const uid = uidOf(context);
  const token = typeof data?.token === 'string' ? data.token.trim() : '';
  if (!token) { await tokenRef(uid).delete(); return { status: 'forgotten' }; }
  if (token.length > 4096) fail('invalid-argument', 'Invalid token.');
  const platform = data?.platform === 'ios' ? 'ios' : data?.platform === 'android' ? 'android' : 'other';
  const lang = LANGS.includes(data?.lang) ? data.lang : 'en';
  await tokenRef(uid).set({ token, platform, lang, updatedAtMs: Date.now() }, { merge: true });
  return { status: 'ok' };
});

// The message as both platforms want it: shown in the tray / on the lock screen, gone after a
// minute (a late "they are waiting" is a lie), one at a time (collapse / tag).
// ttlSec: how long the call stays true (a friend's invitation lives as long as its code).
function message(token, lang, kind, vars, now, ttlSec = C.PUSH_CALL_TTL_SEC) {
  const texts = TEXT[kind] || TEXT.queue;
  const copy = texts[lang] || texts.en;
  const fill = s => s.replace(/\{(\w+)\}/g, (_, k) => String(vars[k] ?? ''));
  return {
    token,
    notification: { title: fill(copy.title), body: fill(copy.body) },
    // `at` lets the phone tell this call from the same one seen through its queue place.
    data: { kind, at: String(now), name: String(vars.name ?? '') },
    android: {
      priority: 'high', ttl: ttlSec * 1000,
      notification: { channelId: 'duels', icon: 'icon_small', sound: 'default', tag: kind },
    },
    apns: {
      headers: { 'apns-priority': '10', 'apns-expiration': String(Math.floor(now / 1000) + ttlSec),
        'apns-collapse-id': kind },
      payload: { aps: { sound: 'default' }, data: kind },
    },
  };
}

// Sends one call when the player's budget allows it. False when they have no token, were
// called a moment ago, or used today's calls; a dead token is forgotten.
async function callPlayer(uid, kind, vars, now = Date.now()) {
  const ref = tokenRef(uid);
  const target = await db.runTransaction(async tx => {
    const snap = await tx.get(ref);
    if (!snap.exists || !snap.data().token) return null;
    const t = snap.data();
    const day = dayOf(now);
    const count = t.callsDay === day ? t.callsCount || 0 : 0;
    if (now - (t.lastCalledAtMs || 0) < C.PUSH_CALL_COOLDOWN_SEC * 1000 || count >= C.PUSH_CALLS_PER_DAY) return null;
    tx.update(ref, { lastCalledAtMs: now, callsDay: day, callsCount: count + 1 });
    return t;
  });
  if (!target) { functions.logger.info('push skipped', { uid: uid.slice(0, 6), why: 'no token or budget' }); return false; }
  try {
    await admin.messaging().send(message(target.token, target.lang, kind, vars, now));
    return true;
  } catch (e) {
    const code = e?.errorInfo?.code || e?.code || '';
    if (code === 'messaging/registration-token-not-registered' || code === 'messaging/invalid-registration-token')
      await ref.delete().catch(() => {});
    else functions.logger.warn(`push to ${uid} failed: ${code || e?.message}`);
    return false;
  }
}

// A friend's personal call (friends.js inviteFriend): not from the stranger-call budget — the
// caller is rate-limited per friend instead. False when the friend has no token.
async function sendToFriend(uid, vars, ttlSec, now = Date.now()) {
  const snap = await tokenRef(uid).get();
  const t = snap.exists ? snap.data() : null;
  if (!t || !t.token) return false;
  try {
    await admin.messaging().send(message(t.token, t.lang, 'friend', vars, now, ttlSec));
    return true;
  } catch (e) {
    const code = e?.errorInfo?.code || e?.code || '';
    if (code === 'messaging/registration-token-not-registered' || code === 'messaging/invalid-registration-token')
      await tokenRef(uid).delete().catch(() => {});
    else functions.logger.warn(`friend push to ${uid} failed: ${code || e?.message}`);
    return false;
  }
}

exports.callPlayer = callPlayer;
exports.sendToFriend = sendToFriend;
exports.message = message;
