const { test } = require('node:test');
const assert = require('node:assert/strict');
const F = require('../src/friendsRules');

const duel = (extra = {}) => ({
  mode: 'friend', state: 'joined', hostUid: 'host', hostName: 'TurboWalrus', hostHero: 3, hostGender: 1,
  guestUid: 'guest', guestName: 'IronPanda', guestHero: 0, guestGender: 0, ...extra,
});

test('a guest taking the seat of a friend duel makes both players friends, once', () => {
  assert.equal(F.joinedFriendDuel(duel({ guestUid: '', state: 'waiting' }), duel()), true);
  assert.equal(F.joinedFriendDuel(duel(), duel({ state: 'started' })), false, 'later writes of the same duel');
  assert.equal(F.joinedFriendDuel(null, duel({ mode: 'pvp' })), false, 'strangers from the queue are not friends');
  assert.equal(F.joinedFriendDuel(duel({ guestUid: '' }), duel({ guestUid: 'host' })), false);
  assert.equal(F.joinedFriendDuel(duel({ guestUid: '' }), null), false);
});

test('each side keeps an entry about the other', () => {
  const [a, b] = F.entriesOf(duel(), 500);
  assert.deepEqual(a, { owner: 'host', friend: { uid: 'guest', name: 'IronPanda', hero: 0, gender: 0, lastDuelMs: 500 } });
  assert.deepEqual(b, { owner: 'guest', friend: { uid: 'host', name: 'TurboWalrus', hero: 3, gender: 1, lastDuelMs: 500 } });
});

test('the call goes once its code is no longer open', () => {
  assert.equal(F.inviteSpent(duel({ state: 'waiting' }), duel({ state: 'joined' })), true);
  assert.equal(F.inviteSpent(duel({ state: 'waiting' }), duel({ state: 'cancelled' })), true);
  assert.equal(F.inviteSpent(duel({ state: 'waiting' }), null), true);
  assert.equal(F.inviteSpent(duel({ state: 'waiting' }), duel({ state: 'waiting' })), false);
  assert.equal(F.inviteSpent(duel({ state: 'joined' }), duel({ state: 'started' })), false);
});

test('a row takes the current name and trophies and tells online from the heartbeat', () => {
  const entry = { uid: 'f', name: 'OldName', hero: 2, gender: 1, lastDuelMs: 10 };
  const now = 1_000_000;
  const online = F.row(entry, { displayName: 'NewName', trophies: 42.7 }, { expiresAt: now + 30000 }, now);
  assert.deepEqual(online, { uid: 'f', name: 'NewName', hero: 2, gender: 1, trophies: 42, online: true,
    lastSeenMs: now + 30000 - F.PRESENCE_WINDOW_MS, lastDuelMs: 10 });
  const away = F.row(entry, null, { expiresAt: { toMillis: () => now - 1 } }, now);
  assert.equal(away.online, false);
  assert.equal(away.name, 'OldName');
  assert.equal(away.lastSeenMs, now - 1 - F.PRESENCE_WINDOW_MS);
  assert.equal(F.row(entry, null, null, now).lastSeenMs, 0, 'never seen');
});

test('online friends come first, then the most recently seen', () => {
  const rows = F.sortRows([
    { uid: 'old', online: false, lastSeenMs: 5, lastDuelMs: 1 },
    { uid: 'on', online: true, lastSeenMs: 1, lastDuelMs: 1 },
    { uid: 'recent', online: false, lastSeenMs: 0, lastDuelMs: 9 },
  ]);
  assert.deepEqual(rows.map(r => r.uid), ['on', 'recent', 'old']);
});

test('only the host of an open code calls a friend into it, not too often', () => {
  const now = 100000;
  const ok = { uid: 'host', friendUid: 'guest', friend: { lastInvitedAtMs: 0 }, code: { expiresAtMs: now + 1 },
    match: duel({ state: 'waiting', guestUid: '', codeExpiresAtMs: now + 1 }), now };
  assert.equal(F.inviteError(ok), null);
  assert.equal(F.inviteError({ ...ok, friend: null })[0], 'permission-denied');
  assert.equal(F.inviteError({ ...ok, code: null, match: null })[0], 'not-found');
  assert.equal(F.inviteError({ ...ok, uid: 'guest', friendUid: 'host' })[0], 'permission-denied');
  assert.equal(F.inviteError({ ...ok, match: { ...ok.match, state: 'joined', guestUid: 'x' } })[0], 'failed-precondition');
  assert.equal(F.inviteError({ ...ok, match: { ...ok.match, codeExpiresAtMs: now } })[0], 'failed-precondition');
  assert.equal(F.inviteError({ ...ok, friend: { lastInvitedAtMs: now - 1000 } })[0], 'resource-exhausted');
  assert.equal(F.inviteError({ ...ok, friend: { lastInvitedAtMs: now - F.INVITE_COOLDOWN_MS } }), null);
});
