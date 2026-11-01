// In-app purchases (docs/architecture/in-app-purchases.md).
//
//   verifyPurchase → the phone sends the StoreKit signed transaction; Apple's signature is checked
//                    against its root certificate, the transaction is claimed for this account
//                    (purchases/{transactionId}), and the answer says what to credit. The phone
//                    finishes the transaction with the store only after crediting it, so a purchase
//                    survives a crash or a lost connection: the store delivers it again.
//
// Verified gems are added up in purchaseTotals/{uid}: saveProgress counts them as evidence for the
// gems in a cloud save (progressRules.js).
const fs = require('fs');
const path = require('path');
const functions = require('firebase-functions/v1');
const { SignedDataVerifier, Environment, VerificationException } = require('@apple/app-store-server-library');
const admin = require('./admin');
const { FieldValue } = require('firebase-admin/firestore');
const P = require('./purchaseRules');
const db = admin.firestore();

const BUNDLE_ID = 'com.pushstars.app';
const APP_APPLE_ID = 6767797834;
const REGION = 'europe-west1';
const JWS_MAX_CHARS = 20000;
const fail = (code, message) => { throw new functions.https.HttpsError(code, message); };
const uidOf = context => context.auth?.uid || fail('unauthenticated', 'Sign in required.');

const roots = [fs.readFileSync(path.join(__dirname, 'certs', 'AppleRootCA-G3.cer'))];
const verifierFor = environment => new SignedDataVerifier(roots, false, environment, BUNDLE_ID, APP_APPLE_ID);
const production = verifierFor(Environment.PRODUCTION);
const sandbox = verifierFor(Environment.SANDBOX);

// App Review and TestFlight buy in the sandbox even with a production build: the App Store first,
// then the sandbox, as Apple asks. A sandbox purchase costs nothing, but only a tester's device can
// make one, and the signature proves it came from this app.
async function decode(jws) {
  try {
    return await production.verifyAndDecodeTransaction(jws);
  } catch (e) {
    if (!(e instanceof VerificationException)) throw e;
    return sandbox.verifyAndDecodeTransaction(jws);
  }
}

exports.verifyPurchase = functions.region(REGION).https.onCall(async (data, context) => {
  const uid = uidOf(context);
  const jws = data?.jws;
  if (typeof jws !== 'string' || !jws || jws.length > JWS_MAX_CHARS) fail('invalid-argument', 'Invalid purchase.');
  let transaction;
  try {
    transaction = await decode(jws);
  } catch (e) {
    functions.logger.warn('purchase not verified', { uid: uid.slice(0, 6), error: String(e.message || e).slice(0, 200) });
    fail('permission-denied', 'Purchase could not be verified.');
  }
  if (typeof transaction.transactionId !== 'string' || !transaction.transactionId) fail('permission-denied', 'Purchase could not be verified.');
  // Refunded: nothing to credit, and the phone may finish it.
  if (P.isRevoked(transaction)) return { status: 'revoked', gems: 0, hero: '', transactionId: transaction.transactionId };

  const claimRef = db.doc(`purchases/${transaction.transactionId}`);
  const grant = await db.runTransaction(async tx => {
    const claim = await tx.get(claimRef);
    const result = P.grantFor(transaction.productId, claim.exists ? claim.data() : null, uid);
    if (!result.isNew) return result;
    tx.create(claimRef, {
      uid, productId: transaction.productId, environment: String(transaction.environment || ''),
      originalTransactionId: String(transaction.originalTransactionId || ''),
      purchaseDateMs: transaction.purchaseDate || 0, gems: result.gems, claimedAtMs: Date.now(),
    });
    tx.set(db.doc(`purchaseTotals/${uid}`), {
      gems: FieldValue.increment(result.gems), count: FieldValue.increment(1), updatedAtMs: Date.now(),
    }, { merge: true });
    return result;
  });
  if (grant.status === 'unknown-product') {
    functions.logger.error('unknown product', { productId: transaction.productId });
    fail('failed-precondition', 'Unknown product.');
  }
  functions.logger.info('purchase', { uid: uid.slice(0, 6), productId: transaction.productId, status: grant.status, isNew: grant.isNew });
  return { status: grant.status, gems: grant.gems, hero: grant.hero, transactionId: transaction.transactionId };
});
