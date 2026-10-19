// Pure rules of a live duel (phase 13–14) — no Firestore here, so every outcome is unit-tested.
//
// What the server knows about a live duel, and where from:
//   • who is in it            — createLiveDuel / joinLiveDuel (authenticated callables)
//   • when it started         — startLiveDuel, called by both phones when the shared start is set
//   • each player's set       — finishLiveDuel: that player's own rep timeline, plus the count
//                               they saw the opponent reach live and whether the opponent left
//   • who really left         — Photon's webhook (onMatchFinished): Join / Leave / Close events
//
// Nothing one phone says about the other decides the result on its own word. A player's reps
// come from their own timeline, capped by what the opponent watched them reach live (a phone
// cannot submit 50 while streaming 20). A walkover needs either Photon's own record that the
// opponent left, or the opponent never reporting before the settle deadline.
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

// Photon's record of a player leaving for good during the duel: a Leave after their last Join
// (a rejoin inside the reconnect window cancels it), or the room closing — either before the
// set's own end. Leaving after the bell is how every duel ends and proves nothing.
function leftForGood(match, uid) {
  if (!match.startedAtMs) return false;
  const bell = match.startedAtMs + (C.LIVE_DUEL_START_LEAD_SEC + C.MATCH_DURATION_SEC) * 1000 - 2000;
  const room = match.room || {};
  const leave = room.leaves && room.leaves[uid];
  const join = (room.joins && room.joins[uid]) || 0;
  if (leave && leave.atMs > join && leave.atMs < bell) return true;
  return !!room.closedAtMs && room.closedAtMs < bell;
}

function trophyRule(mode) {
  return (C.LIVE_DUEL_TROPHIES && C.LIVE_DUEL_TROPHIES[mode]) || { win: 0, loss: 0 };
}

// Decides the duel, or returns null while it is still waiting for evidence.
// force: the settle deadline has passed — decide with whatever there is.
function decide(match, { force = false } = {}) {
  const players = [match.hostUid, match.guestUid];
  if (!players[0] || !players[1] || !match.startedAtMs) return force ? abandoned(match, 'not-started') : null;
  const results = match.results || {};
  const [a, b] = players;
  const ra = results[a], rb = results[b];
  const flags = [];
  const reps = {};

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
  const evidence = leftForGood(match, absent);
  if (mine.opponentLeft && (evidence || force)) {
    reps[present] = mine.repTimes.length;
    reps[absent] = Math.min(mine.opponentReps, MAX_REPS);
    return outcome(match, reps, true, evidence ? [] : ['walkover-by-deadline'], present);
  }
  if (!force) return null;
  // The opponent stayed to the end by this phone's account but never reported (crashed after
  // the bell): their count is the one this phone watched.
  reps[present] = mine.repTimes.length;
  reps[absent] = Math.min(mine.opponentReps, MAX_REPS);
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

module.exports = { MATCH_ID, roomName, matchIdFromRoom, timelineError, leftForGood, decide, trophyRule };
