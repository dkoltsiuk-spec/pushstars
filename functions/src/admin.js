// Shared Firebase Admin singleton. Requiring this module initialises the Admin SDK
// exactly once, so every function file can safely call admin.firestore() at load time.
const admin = require('firebase-admin');

if (!admin.apps.length) {
  admin.initializeApp();
}

module.exports = admin;
