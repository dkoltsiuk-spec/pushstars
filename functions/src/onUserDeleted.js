const functions = require('firebase-functions/v1');
const admin = require('./admin');

const db = admin.firestore();

// GDPR: removes a user's own data when their Auth account is deleted.
// Deletes the profile, their leaderboard entry, and ghost-session metadata.
// (Match documents are shared with opponents and are pruned in a later phase;
//  Storage ghost files are cleaned when Storage lands in phase 12.)
exports.onUserDeleted = functions.auth.user().onDelete(async (user) => {
  const uid = user.uid;
  const userRef = db.doc(`users/${uid}`);

  const userSnap = await userRef.get();
  const league = userSnap.exists
    ? (userSnap.data().leaderboardLeague || userSnap.data().rank || 'bronze')
    : 'bronze';

  const batch = db.batch();
  batch.delete(userRef);
  batch.delete(db.doc(`onlinePresence/${uid}`));
  batch.delete(db.doc(`leaderboard/${league}/players/${uid}`));

  const ghostSnap = await db.collection(`ghost_sessions/${uid}/sessions`).get();
  ghostSnap.forEach((doc) => batch.delete(doc.ref));

  await batch.commit();
  // The monthly archive and ranked session receipts also belong to this account.
  const seasons = await db.collection('seasons').listDocuments();
  const sessions = await db.collection('rankedSessions').where('uid', '==', uid).get();
  const writer = db.bulkWriter();
  for (const season of seasons) writer.delete(season.collection('players').doc(uid));
  for (const session of sessions.docs) {
    writer.delete(session.ref);
    writer.delete(db.doc(`matches/${session.id}`));
  }
  await writer.close();
  functions.logger.info(`Deleted data for ${uid}`);
  return null;
});
