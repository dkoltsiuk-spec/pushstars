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
  // 'pvp' is the random queue's ranked live duel.
  LIVE_DUEL_TROPHIES: { friend: { win: 0, loss: 0 }, pvp: { win: 25, loss: 15 } },
  // Random live queue (matchPlayers).
  LIVE_QUEUE_HEARTBEAT_SEC: 5,        // a waiting place stays visible this long after its last poll
  LIVE_QUEUE_ENTRY_TTL_SEC: 60,       // cleanup removes a place this long after its last write
  LIVE_QUEUE_ROOM_TTL_SEC: 90,        // a queue match nobody started expires
  LIVE_QUEUE_POOL: 10,                // waiting places read per pairing attempt
  LIVE_QUEUE_GHOST_POOL: 50,          // season rows read to find a stranger's ghost after a miss
  // A stranger's set offered while searching must be close to the player's own best:
  // within max(BAND_MIN, BAND_PCT × best) reps, otherwise the phone offers its own shadow.
  LIVE_QUEUE_GHOST_BAND_MIN: 3,
  LIVE_QUEUE_GHOST_BAND_PCT: 0.2,
  // "Notify me when found" (low-online launch): a searcher who allowed notifications stays
  // callable after leaving the screen; another player's search sends them a push.
  LIVE_QUEUE_STANDBY_SEC: 1800,       // how long a searcher who left stays callable
  LIVE_QUEUE_BUSY_SEC: 150,           // left for a recorded fight: not called while it runs
  LIVE_QUEUE_RECALL_SEC: 45,          // a searcher calls standby players again this often
  LIVE_QUEUE_CALLS_PER_SEARCH: 2,     // standby players called at once
  LIVE_QUEUE_STANDBY_POOL: 20,        // standby places read per call
  PUSH_CALL_COOLDOWN_SEC: 180,        // one call per player per 3 minutes
  PUSH_CALLS_PER_DAY: 6,              // and at most this many a (UTC) day
  PUSH_CALL_TTL_SEC: 60,              // a call not delivered within a minute is dropped
  GHOST_MAX_FILE_BYTES: 204800, // 200 KB
  LEADERBOARD_CACHE_SEC: 60,

  XP_PER_REP: 10,

  // displayName bounds (shared by updateDisplayName + future client validation).
  DISPLAY_NAME_MIN: 2,
  DISPLAY_NAME_MAX: 20,
};
