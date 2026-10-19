// Single source of truth — keep in sync with docs/architecture/constants.md.
module.exports = {
  MAX_REPS_PER_MATCH: 65,
  MATCH_DURATION_SEC: 60,

  TROPHY_WIN: 25,
  TROPHY_LOSS: 15,
  TROPHY_GHOST_WIN: 12, // floor(TROPHY_WIN / 2)
  TROPHY_GHOST_LOSS: 7, // floor(TROPHY_LOSS / 2)

  MATCHMAKING_TIMEOUT_SEC: 35,

  // Live duel (Photon, phases 13–14) — keep in sync with constants.md «Photon».
  LIVE_DUEL_CODE_TTL_SEC: 300,        // a friend-duel code lives 5 minutes
  LIVE_DUEL_RECONNECT_SEC: 10,        // Photon PlayerTtl and the client's forfeit grace
  LIVE_DUEL_START_LEAD_SEC: 4,        // shared start is scheduled this far ahead (3-2-1 + 1 s)
  LIVE_DUEL_SETTLE_GRACE_SEC: 120,    // after the set: how long a missing result is waited for
  LIVE_DUEL_REPS_TOLERANCE: 3,        // reps above what the opponent watched live that still count
  // Trophies by live mode. A friend duel is friendly (the room sheet promises "No trophies");
  // 'pvp' is the queue's ranked live duel when it arrives.
  LIVE_DUEL_TROPHIES: { friend: { win: 0, loss: 0 }, pvp: { win: 25, loss: 15 } },
  GHOST_MAX_FILE_BYTES: 204800, // 200 KB
  LEADERBOARD_CACHE_SEC: 60,

  XP_PER_REP: 10,

  // displayName bounds (shared by updateDisplayName + future client validation).
  DISPLAY_NAME_MIN: 2,
  DISPLAY_NAME_MAX: 20,
};
