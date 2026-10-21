// Cloud Functions entry — phase 05 (backend core).
//
// Uses the v1 (Gen-1) API: Auth background triggers (onUserCreated/onUserDeleted) are
// only available in v1. Live duels (phases 13–14: friend-code rooms, the random queue, the Photon webhook
// onMatchFinished, photonAuth, cleanupMatchmaking) live in liveDuel.js.
require('./admin'); // initialise the Admin SDK once, before the handlers load

const { onUserCreated } = require('./onUserCreated');
const { onUserDeleted } = require('./onUserDeleted');
const { ensureProfile } = require('./ensureProfile');
const { syncOfflineXp } = require('./syncOfflineXp');
const { updateDisplayName } = require('./updateDisplayName');

exports.onUserCreated = onUserCreated;
exports.onUserDeleted = onUserDeleted;
exports.ensureProfile = ensureProfile;
exports.syncOfflineXp = syncOfflineXp;
exports.updateDisplayName = updateDisplayName;
const progress = require('./progress');
exports.loadProgress = progress.loadProgress;
exports.saveProgress = progress.saveProgress;
exports.heartbeatPresence = require('./heartbeatPresence').heartbeatPresence;
const league = require('./league');
exports.getLeagueStandings = league.getLeagueStandings;
exports.beginRankedMatch = league.beginRankedMatch;
exports.startRankedMatch = league.startRankedMatch;
exports.finishRankedMatch = league.finishRankedMatch;
exports.cancelRankedMatch = league.cancelRankedMatch;
exports.rotateLeagueSeason = league.rotateLeagueSeason;
const botRecordings = require('./botRecordings');
exports.getBotRecorderAccess = botRecordings.getBotRecorderAccess;
exports.saveBotRecording = botRecordings.saveBotRecording;
exports.getBotRecording = botRecordings.getBotRecording;
exports.saveEmoteCaptureChunk = require('./emoteCaptures').saveEmoteCaptureChunk;
const liveDuel = require('./liveDuel');
exports.createLiveDuel = liveDuel.createLiveDuel;
exports.peekLiveDuel = liveDuel.peekLiveDuel;
exports.joinLiveDuel = liveDuel.joinLiveDuel;
exports.cancelLiveDuel = liveDuel.cancelLiveDuel;
exports.enqueueLiveDuel = liveDuel.enqueueLiveDuel;
exports.pollLiveDuel = liveDuel.pollLiveDuel;
exports.leaveLiveDuel = liveDuel.leaveLiveDuel;
exports.startLiveDuel = liveDuel.startLiveDuel;
exports.finishLiveDuel = liveDuel.finishLiveDuel;
exports.onMatchFinished = liveDuel.onMatchFinished;
exports.photonAuth = liveDuel.photonAuth;
exports.cleanupMatchmaking = liveDuel.cleanupMatchmaking;
exports.registerPushToken = require('./push').registerPushToken;
