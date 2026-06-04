// Single source of truth — keep in sync with docs/architecture/constants.md.
module.exports = {
  MAX_REPS_PER_MATCH: 65,
  MATCH_DURATION_SEC: 60,

  TROPHY_WIN: 25,
  TROPHY_LOSS: 15,
  TROPHY_GHOST_WIN: 12, // floor(TROPHY_WIN / 2)
  TROPHY_GHOST_LOSS: 7, // floor(TROPHY_LOSS / 2)

  MATCHMAKING_TIMEOUT_SEC: 35,
  GHOST_MAX_FILE_BYTES: 204800, // 200 KB
  LEADERBOARD_CACHE_SEC: 60,

  XP_PER_REP: 10,

  // displayName bounds (shared by updateDisplayName + future client validation).
  DISPLAY_NAME_MIN: 2,
  DISPLAY_NAME_MAX: 20,
};
