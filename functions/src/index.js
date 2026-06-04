// Cloud Functions entry — phase 05 (backend core).
//
// Uses the v1 (Gen-1) API: Auth background triggers (onUserCreated/onUserDeleted) are
// only available in v1. Matchmaking (matchPlayers, onMatchFinished webhook) lands in
// phase 13; onGhostMatchFinished in phase 12.
require('./admin'); // initialise the Admin SDK once, before the handlers load

const { onUserCreated } = require('./onUserCreated');
const { onUserDeleted } = require('./onUserDeleted');
const { syncOfflineXp } = require('./syncOfflineXp');
const { updateDisplayName } = require('./updateDisplayName');

exports.onUserCreated = onUserCreated;
exports.onUserDeleted = onUserDeleted;
exports.syncOfflineXp = syncOfflineXp;
exports.updateDisplayName = updateDisplayName;
