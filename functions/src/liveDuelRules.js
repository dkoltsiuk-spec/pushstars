// Pure rules of a live duel (phase 13–14) — no Firestore here, so every outcome is unit-tested.
//
// What the server knows about a live duel, and where from:
//   • who is in it            — createLiveDuel / joinLiveDuel (authenticated callables)
//   • when it started         — startLiveDuel, called by both phones when the shared start is set
//   • each player's set       — finishLiveDuel: that player's own rep timeline, plus the count
//                               they saw the opponent reach live and whether the opponent left
//   • who really left         — Photon's webhook (onMatchFinished): Join / Leave / Close events
//   • who gave up             — finishLiveDuel with forfeit, the player's own word against themselves
//
// Nothing one phone says about the other decides the result on its own word. A player's reps
// come from their own timeline, capped by what the opponent watched them reach live (a phone
// cannot submit 50 while streaming 20). A walkover needs Photon's own record that the player
// walked out (firstGone), their own forfeit, or the opponent never reporting before the deadline.
const C = require('./constants');

const MATCH_ID = /^[A-Za-z0-9]{20}$/;
const ROOM_PREFIX = 'pd_';
const MAX_REPS = C.MAX_REPS_PER_MATCH;

const roomName = matchId => `${ROOM_PREFIX}${matchId}`;
const matchIdFromRoom = room => {
  if (typeof room !== 'string' || !room.startsWith(ROOM_PREFIX)) return null;
  const id = room.slice(ROOM_PREFIX.length);
  return MATCH_ID.test(id) ? id : null;
};

// Same rules as a ranked set (league.js validateTimes): ordered, inside the set, 0.4 s apart.
function timelineError(times, duration) {
  if (!Array.isArray(times) || times.length > MAX_REPS) return 'Invalid repetition timeline.';
  if (typeof duration !== 'number' || !Number.isFinite(duration) || duration < 1 || duration > C.MATCH_DURATION_SEC)
    return 'Invalid set duration.';
  let previous = -1;
  for (const time of times) {
    if (typeof time !== 'number' || !Number.isFinite(time) || time < 0 || time > duration || time - previous < 0.4)
      return 'Repetitions must be ordered, within the set, and at least 0.4 seconds apart.';
    previous = time;
  }
  return null;
}

// Who walked out of the set first, by Photon's own record (the webhook's join/leave log), or
// null. A player is gone once they have been away from the room
//   • for good — an explicit leave, or their seat's TTL running out (a leave that is not inactive),
//   • for the whole reconnect window (LIVE_DUEL_RECONNECT_SEC), or
//   • while the other player gave up waiting and left for good (the other phone counts the window
//     from the last message it heard, which is earlier than Photon notices).
// A rejoin after that changes nothing: the duel was already lost. Only away spells that start
// before the bell count — leaving after it is how every duel ends and proves nothing.
function firstGone(match, now = Date.now()) {
  if (!match.startedAtMs) return null;
  const bell = bellAt(match);
  const window = C.LIVE_DUEL_RECONNECT_SEC * 1000;
  const log = ((match.room && match.room.log) || []).filter(e => e.atMs >= match.startedAtMs)
    .sort((x, y) => x.atMs - y.atMs);
  const players = [match.hostUid, match.guestUid];
  const leftForGood = (uid, from, to) =>
    log.some(e => e.uid === uid && e.kind === 'leave' && !e.inactive && e.atMs >= from && e.atMs <= to);
  let culprit = null, culpritAt = Infinity;
  for (const uid of players) {
    const other = uid === players[0] ? players[1] : players[0];
    let since = null, final = false, gone = false;
    const qualifies = end => final || end - since >= window || leftForGood(other, since, end);
    for (const e of log) {
      if (e.uid !== uid) continue;
      if (e.kind === 'leave') {
        if (since === null) { if (e.atMs >= bell) break; since = e.atMs; final = !e.inactive; }
        else if (!e.inactive) final = true;
      } else if (e.kind === 'join' && since !== null) {
        if (qualifies(e.atMs)) { gone = true; break; }
        since = null; final = false;
      }
    }
    if (since === null || since >= culpritAt) continue;
    if (gone || qualifies(Math.max(now, since))) { culprit = uid; culpritAt = since; }
  }
  return culprit;
}

function bellAt(match) {
  return match.startedAtMs + (C.LIVE_DUEL_START_LEAD_SEC + C.MATCH_DURATION_SEC) * 1000 - 2000;
}

function trophyRule(mode) {
  return (C.LIVE_DUEL_TROPHIES && C.LIVE_DUEL_TROPHIES[mode]) || { win: 0, loss: 0 };
}

// Decides the duel, or returns null while it is still waiting for evidence.
// force: the settle deadline has passed — decide with whatever there is.
// now: when it is decided; an away spell still open is measured up to it.
function decide(match, { force = false, now = Date.now() } = {}) {
  const players = [match.hostUid, match.guestUid];
  if (!players[0] || !players[1] || !match.startedAtMs) return force ? abandoned(match, 'not-started') : null;
  const results = match.results || {};
  const [a, b] = players;
  const ra = results[a], rb = results[b];
  const flags = [];
  const reps = {};

  // A walkover: one of them said they walked out (the exit button, or back in the app after the
  // reconnect window), or Photon saw them do it. The first to go loses.
  const forfeits = players.filter(uid => results[uid] && results[uid].forfeit)
    .sort((x, y) => results[x].atMs - results[y].atMs);
  const gone = firstGone(match, now);
  // aborted: the opponent left during the countdown, before the set began, and this phone
  // called the duel off — the staying player's word against their own walkover, so it is
  // believed, unless Photon has the one saying it walking out first.
  const aborter = players.find(uid => results[uid] && results[uid].aborted);
  if (!forfeits.length && aborter && gone !== aborter) return abandoned(match, 'left-before-start');
  let loser = null;
  if (forfeits.length) { loser = forfeits[0]; flags.push('forfeit'); }
  else loser = gone;
  if (loser) {
    if (!ra && !rb && !force) return null;   // nobody's count yet
    const winner = loser === a ? b : a;
    const counted = (self, other) => results[self] ? results[self].repTimes.length
      : results[other] ? Math.min(results[other].opponentReps, MAX_REPS) : 0;
    reps[winner] = counted(winner, loser);
    reps[loser] = counted(loser, winner);
    return outcome(match, reps, true, flags, winner);
  }

  if (ra && rb) {
    for (const [self, mine, theirs] of [[a, ra, rb], [b, rb, ra]]) {
      let credited = mine.repTimes.length;
      // The other phone watched this one live the whole set: it cannot have done much more.
      if (!theirs.opponentLeft && !mine.opponentLeft && credited > theirs.opponentReps + C.LIVE_DUEL_REPS_TOLERANCE) {
        credited = theirs.opponentReps + C.LIVE_DUEL_REPS_TOLERANCE;
        flags.push(`capped:${self}`);
      }
      reps[self] = credited;
    }
    return outcome(match, reps, false, flags);
  }

  const present = ra ? a : rb ? b : null;
  if (!present) return force ? abandoned(match, 'no-results') : null;
  const absent = present === a ? b : a;
  const mine = results[present];
  if (!force) return null;
  reps[present] = mine.repTimes.length;
  reps[absent] = Math.min(mine.opponentReps, MAX_REPS);
  // The stayer's word alone, with nothing from Photon or the other phone by the deadline.
  if (mine.opponentLeft) return outcome(match, reps, true, ['walkover-by-deadline'], present);
  // The opponent stayed to the end by this phone's account but never reported (crashed after
  // the bell): their count is the one this phone watched.
  return outcome(match, reps, false, ['missing-result']);
}

function outcome(match, reps, walkover, flags, walkoverWinner) {
  const [a, b] = [match.hostUid, match.guestUid];
  let winnerUid = '';
  if (walkover) winnerUid = walkoverWinner;
  else if (reps[a] !== reps[b]) winnerUid = reps[a] > reps[b] ? a : b;
  const rule = trophyRule(match.mode);
  const receipts = {};
  for (const [self, other] of [[a, b], [b, a]]) {
    const won = winnerUid === self, draw = !winnerUid;
    receipts[self] = {
      state: 'settled', won, draw, walkover,
      reps: reps[self], opponentReps: reps[other],
      trophyDelta: draw ? 0 : won ? rule.win : -rule.loss,
      reason: walkover ? (won ? 'opponent-left' : 'left') : 'compared',
    };
  }
  return { winnerUid, draw: !winnerUid, walkover, reps, flags, receipts };
}

function abandoned(match, reason) {
  const receipts = {};
  for (const uid of [match.hostUid, match.guestUid].filter(Boolean))
    receipts[uid] = { state: 'settled', won: false, draw: true, walkover: false, reps: 0, opponentReps: 0, trophyDelta: 0, reason };
  return { winnerUid: '', draw: true, walkover: false, reps: {}, flags: [reason], receipts, abandoned: true };
}

module.exports = { MATCH_ID, roomName, matchIdFromRoom, timelineError, firstGone, decide, trophyRule };
