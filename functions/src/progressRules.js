// Cloud progress (users/{uid}/progress/*) — what a saved copy may contain. Pure, so the rules are
// unit-tested without Firestore (test/progressRules.test.js).
//
// The economy is earned on the phone (see docs/architecture/cloud-progress.md), so the server
// cannot replay it. What it can do is keep a cloud copy from being easier to inflate than playing:
//  1. structure — the two ledgers parse, every number is a sane non-negative integer, lists are
//     bounded, Aura never exceeds its peak;
//  2. evidence — every total is bounded by the workouts that earned it (reps per workout, XP per
//     rep, Aura and gems per workout and receipt);
//  3. pace — the workout count, which bounds everything else, cannot grow faster than workouts
//     can be done, measured on the server's clock since the previous save.
// The league never reads this copy: trophies stay server-authoritative (league.js).
const zlib = require('zlib');
const { MAX_REPS_PER_MATCH, XP_PER_REP } = require('./constants');

const LIMITS = {
  PACKED_SNAPSHOT_CHARS: 900000,   // base64 gzip; Firestore holds at most 1 MiB per document
  PACKED_GHOST_CHARS: 600000,
  UNPACKED_CHARS: 8 * 1024 * 1024, // refuses zip bombs before parsing
  LIST: 50000,                     // receipts, workouts, processed ids
  ID_CHARS: 200,
  PREFS: 80,
  PREF_STRING_CHARS: 200,
  AVATARS: 500,
  CASES: 500,
  MAX_COUNTER: 1e12,
};

// Most a single workout can pay, generously: form 1.5 × streak 1.5 per rep plus win and session XP;
// the biggest Aura moments × diamond stakes; the richest case.
const PER_WORKOUT = {
  REPS_PER_SECOND: 1.5,
  XP_PER_REP: Math.ceil(XP_PER_REP * 1.5 * 1.5) + 3,
  XP_BONUS: 550,
  AURA: 60000,
  TROPHIES: 30,
  GEMS_PER_CASE: 300,
  GEMS_PER_RECEIPT: 500,
};
// One-offs that do not come per workout: welcome, levels, promotions, streak milestones, slack.
const ALLOWANCE = { XP: 10000, AURA: 1000000, GEMS: 2000, TROPHIES: 500, CASES: 50, GEM_RECEIPTS: 50, REPS: 5000 };
// A workout takes a minute plus countdown and result screens.
const PACE = { SECONDS_PER_WORKOUT: 45, BURST: 20 };

class RuleError extends Error {
  constructor(code, message) { super(message); this.code = code; }
}
const fail = (message) => { throw new RuleError('invalid-argument', message); };
const tooMuch = (message) => { throw new RuleError('out-of-range', message); };

function unpack(packed, maxPacked) {
  if (typeof packed !== 'string' || packed.length === 0) fail('Missing progress.');
  if (packed.length > maxPacked) tooMuch('Progress is too large.');
  let text;
  try {
    text = zlib.gunzipSync(Buffer.from(packed, 'base64'), { maxOutputLength: LIMITS.UNPACKED_CHARS }).toString('utf8');
  } catch (e) {
    fail('Progress could not be read.');
  }
  try {
    return JSON.parse(text);
  } catch (e) {
    return fail('Progress is not valid JSON.');
  }
}

const isCount = (v) => Number.isInteger(v) && v >= 0 && v <= LIMITS.MAX_COUNTER;
function count(value, name) {
  // A save written before a field existed simply lacks it.
  if (value === undefined || value === null) return 0;
  if (!isCount(value)) fail(`${name} must be a non-negative integer.`);
  return value;
}
function list(value, name, max = LIMITS.LIST) {
  if (value === undefined || value === null) return [];
  if (!Array.isArray(value)) fail(`${name} must be a list.`);
  if (value.length > max) tooMuch(`${name} is too long.`);
  return value;
}
function ids(value, name) {
  const items = list(value, name);
  for (const id of items) if (typeof id !== 'string' || id.length === 0 || id.length > LIMITS.ID_CHARS) fail(`${name} has an invalid id.`);
  return items;
}
function parseInner(text, name) {
  if (text === undefined || text === null || text === '') return null;
  if (typeof text !== 'string' || text.length > LIMITS.UNPACKED_CHARS) fail(`${name} is invalid.`);
  try { return JSON.parse(text); } catch (e) { return fail(`${name} is not valid JSON.`); }
}

function inspectLedger(ledger) {
  if (!ledger) return { aura: 0, peakAura: 0, gems: 0, caseAwards: 0, gemReceipts: 0, heroes: 0, arenas: 0 };
  if (typeof ledger !== 'object' || !Number.isInteger(ledger.version) || ledger.version < 1 || ledger.version > 6) fail('Unsupported reward ledger.');
  const aura = count(ledger.aura, 'aura');
  const peakAura = Math.max(count(ledger.peakAura, 'peakAura'), ledger.version < 6 ? aura : 0);
  if (aura > peakAura) fail('aura cannot exceed its peak.');
  const avatars = list(ledger.avatars, 'avatars', LIMITS.AVATARS);
  for (const a of avatars) {
    if (!a || typeof a.id !== 'string' || !a.id || a.id.length > LIMITS.ID_CHARS) fail('avatars has an invalid id.');
    count(a.cards, 'cards');
    if (a.cards > 10000) tooMuch('Too many hero cards.');
  }
  const cases = list(ledger.cases, 'cases', LIMITS.CASES);
  for (const c of cases) {
    if (!c || typeof c.id !== 'string' || !c.id) fail('cases has an invalid id.');
    if (count(c.gems, 'case gems') > PER_WORKOUT.GEMS_PER_CASE) tooMuch('A case holds too many gems.');
  }
  ids(ledger.auraReceipts, 'auraReceipts');
  const gemReceipts = ids(ledger.gemReceipts, 'gemReceipts').length;
  const caseAwards = ids(ledger.awardedWorkouts, 'awardedWorkouts').length;
  ids(ledger.processedWorkoutIds, 'processedWorkoutIds');
  return {
    aura, peakAura, gems: count(ledger.gems, 'gems'), caseAwards, gemReceipts,
    heroes: avatars.filter(a => a.owned).length,
    arenas: ids(ledger.arenas, 'arenas').length,
  };
}

function inspectWorkouts(progress) {
  if (!progress) return { xp: 0, trophies: 0, totalReps: 0, workouts: 0, historyReps: 0, trophiesWon: 0 };
  if (typeof progress !== 'object' || progress.Version !== 1) fail('Unsupported workout save.');
  const workouts = list(progress.Workouts, 'Workouts');
  let historyReps = 0, trophiesWon = 0;
  for (const w of workouts) {
    if (!w || typeof w.Id !== 'string' || !w.Id || w.Id.length > LIMITS.ID_CHARS) fail('A workout has an invalid id.');
    const reps = count(w.Reps, 'Reps');
    const duration = Math.min(3600, count(w.DurationSec, 'DurationSec'));
    if (reps > Math.max(MAX_REPS_PER_MATCH, Math.ceil(duration * PER_WORKOUT.REPS_PER_SECOND))) tooMuch('A workout has more reps than its time allows.');
    historyReps += reps;
    // A win pays its trophies plus one per streak day; the assessment seeds up to 420.
    const streak = Math.min(count(w.StreakDays, 'StreakDays'), workouts.length);
    const delta = Number.isInteger(w.TrophyDelta) ? w.TrophyDelta : fail('A workout has invalid trophies.');
    if (delta > PER_WORKOUT.TROPHIES + streak + (w.Mode === 'assessment' ? 420 : 0)) tooMuch('A workout pays too many trophies.');
    if (delta > 0) trophiesWon += delta;
  }
  return {
    xp: count(progress.Xp, 'Xp'), trophies: count(progress.Trophies, 'Trophies'),
    totalReps: count(progress.TotalReps, 'TotalReps'), workouts: workouts.length, historyReps, trophiesWon,
  };
}

function inspectPrefs(prefs) {
  const items = list(prefs, 'prefs', LIMITS.PREFS);
  let bossCleared = 0;
  for (const p of items) {
    if (!p || typeof p.key !== 'string' || !p.key || p.key.length > 80) fail('prefs has an invalid key.');
    if (p.isString) { if (typeof p.stringValue !== 'string' || p.stringValue.length > LIMITS.PREF_STRING_CHARS) fail('prefs has an invalid value.'); }
    else if (!Number.isInteger(p.intValue)) fail('prefs has an invalid value.');
    if (p.key === 'boss_progress') bossCleared = p.intValue;
  }
  return { bossCleared };
}

/** Parses a packed snapshot and returns its summary, or throws RuleError. */
function inspectSnapshot(packed) {
  const snapshot = unpack(packed, LIMITS.PACKED_SNAPSHOT_CHARS);
  if (!snapshot || typeof snapshot !== 'object' || snapshot.version !== 1) fail('Unsupported progress snapshot.');
  const summary = {
    ...inspectLedger(parseInner(snapshot.ledger, 'ledger')),
    ...inspectWorkouts(parseInner(snapshot.workouts, 'workouts')),
    ...inspectPrefs(snapshot.prefs),
  };
  checkEvidence(summary);
  return summary;
}

/** Every total bounded by the workouts that earned it. */
function checkEvidence(s) {
  const w = s.workouts;
  // TotalReps is the history's sum plus counters kept before the history existed.
  if (s.totalReps > s.historyReps + ALLOWANCE.REPS) tooMuch('More reps than workouts.');
  if (s.xp > PER_WORKOUT.XP_PER_REP * Math.max(s.totalReps, s.historyReps) + PER_WORKOUT.XP_BONUS * w + ALLOWANCE.XP) tooMuch('More XP than reps earn.');
  if (s.peakAura > PER_WORKOUT.AURA * w + ALLOWANCE.AURA) tooMuch('More Aura than workouts earn.');
  if (s.caseAwards > w + ALLOWANCE.CASES) tooMuch('More cases than workouts.');
  if (s.gemReceipts > w + ALLOWANCE.GEM_RECEIPTS) tooMuch('More gem rewards than workouts.');
  // Gems come only from cases and boss-map islets today. Bought gem packs will need their store
  // receipts verified here before this bound can count them.
  if (s.gems > PER_WORKOUT.GEMS_PER_CASE * s.caseAwards + PER_WORKOUT.GEMS_PER_RECEIPT * s.gemReceipts + ALLOWANCE.GEMS) tooMuch('More gems than rewards pay.');
  if (s.trophies > s.trophiesWon + ALLOWANCE.TROPHIES) tooMuch('More trophies than workouts.');
  if (s.bossCleared < 0 || s.bossCleared > Math.min(64, w)) tooMuch('More bosses than fights.');
}

/** The workout count cannot grow faster than workouts can be done. */
function checkPace(previous, next, elapsedSec) {
  if (!previous) return; // first copy: bounded by evidence alone (saves from before cloud saves)
  const allowed = Math.floor(Math.max(0, elapsedSec) / PACE.SECONDS_PER_WORKOUT) + PACE.BURST;
  if (next.workouts - (previous.workouts || 0) > allowed) tooMuch('Workouts added faster than they can be done.');
}

/** Ghost record: only checks it is a bounded, valid set. Returns its reps. */
function inspectGhost(packed) {
  const ghost = unpack(packed, LIMITS.PACKED_GHOST_CHARS);
  if (!ghost || typeof ghost !== 'object') fail('Invalid ghost.');
  const reps = count(ghost.reps, 'ghost reps');
  if (!Array.isArray(ghost.repTimes) || ghost.repTimes.length !== reps) fail('Invalid ghost.');
  if (reps > MAX_REPS_PER_MATCH) tooMuch('Ghost has too many reps.');
  return { reps };
}

module.exports = { LIMITS, RuleError, inspectSnapshot, inspectGhost, checkEvidence, checkPace };
