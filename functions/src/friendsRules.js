// Pure rules of the friends list (friends.js reads and writes; friends.test.js pins these).

const LIST_MAX = 100;            // friends shown (most recent duel first)
const INVITE_COOLDOWN_MS = 15000; // the same friend is called at most this often

// The guest has just taken the seat of a friend-code duel: both players become friends.
function joinedFriendDuel(before, after) {
  return !!after && after.mode === 'friend' && !!after.hostUid && !!after.guestUid
    && after.hostUid !== after.guestUid && !(before && before.guestUid);
}

// The open code a friend was called into is gone (joined, cancelled, expired): the call too.
function inviteSpent(before, after) {
  return !!before && before.mode === 'friend' && before.state === 'waiting' && (!after || after.state !== 'waiting');
}

// Each side's entry about the other, as the duel saw them.
function entriesOf(match, now) {
  const host = { uid: match.hostUid, name: match.hostName || '', hero: match.hostHero || 0, gender: match.hostGender === 1 ? 1 : 0 };
  const guest = { uid: match.guestUid, name: match.guestName || '', hero: match.guestHero || 0, gender: match.guestGender === 1 ? 1 : 0 };
  return [
    { owner: host.uid, friend: { ...guest, lastDuelMs: now } },
    { owner: guest.uid, friend: { ...host, lastDuelMs: now } },
  ];
}

const millisOf = t => !t ? 0 : typeof t === 'number' ? t : typeof t.toMillis === 'function' ? t.toMillis() : 0;

// One row of getFriends: the stored entry, refreshed by the friend's profile and presence.
// lastSeenMs is when their last heartbeat was (the presence window is PRESENCE_WINDOW_MS long).
const PRESENCE_WINDOW_MS = 75000;
function row(entry, profile, presence, now) {
  const expires = millisOf(presence && presence.expiresAt);
  const name = profile && typeof profile.displayName === 'string' && profile.displayName.trim() ? profile.displayName.trim() : entry.name;
  return {
    uid: entry.uid,
    name: String(name || '').slice(0, 24),
    hero: Number.isInteger(entry.hero) ? entry.hero : 0,
    gender: entry.gender === 1 ? 1 : 0,
    trophies: profile && Number.isFinite(profile.trophies) ? Math.max(0, Math.floor(profile.trophies)) : 0,
    online: expires > now,
    lastSeenMs: expires > 0 ? Math.min(now, expires - PRESENCE_WINDOW_MS) : 0,
    lastDuelMs: entry.lastDuelMs || 0,
  };
}

// Online first; then whoever was seen (or fought) most recently.
function sortRows(rows) {
  return rows.sort((a, b) => (b.online - a.online) || (Math.max(b.lastSeenMs, b.lastDuelMs) - Math.max(a.lastSeenMs, a.lastDuelMs)));
}

// Whether `uid` may call `friendUid` into the duel behind `code` right now.
function inviteError({ uid, friendUid, friend, code, match, now }) {
  if (!friend) return ['permission-denied', 'Not your friend.'];
  if (!code || !match) return ['not-found', 'Room not found.'];
  if (match.hostUid !== uid) return ['permission-denied', 'Not your room.'];
  if (match.mode !== 'friend' || match.state !== 'waiting' || match.codeExpiresAtMs <= now || code.expiresAtMs <= now)
    return ['failed-precondition', 'Code expired.'];
  if (match.guestUid && match.guestUid !== friendUid) return ['already-exists', 'This room is already full.'];
  if (now - (friend.lastInvitedAtMs || 0) < INVITE_COOLDOWN_MS) return ['resource-exhausted', 'Already called. Wait a moment.'];
  return null;
}

module.exports = { LIST_MAX, INVITE_COOLDOWN_MS, PRESENCE_WINDOW_MS, joinedFriendDuel, inviteSpent, entriesOf, row, sortRows, inviteError };
