// Checks an App Store Connect API key from a PC in a few seconds, instead of finding out from a
// 40-minute UBA build that ends in "401 NOT_AUTHORIZED". Uses the same three values the UBA
// post-build script (ci/uba_postbuild_testflight.sh) reads, and prints the app's Apple ID for
// ASC_APP_ID. Nothing is stored or sent anywhere except api.appstoreconnect.apple.com.
//
//   node ci/check_asc_key.mjs <AuthKey_XXXXXXXXXX.p8> <ISSUER_ID> [KEY_ID] [BUNDLE_ID]
//
// KEY_ID defaults to the one in the file name, BUNDLE_ID to com.pushstars.app. To check the
// base64 you pasted into UBA instead of the file, pass it with --b64=<value> in place of the path.
import { createPrivateKey, sign } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { basename } from 'node:path';

const [source, issuerArg, keyArg, bundleArg] = process.argv.slice(2);
if (!source || !issuerArg) {
  console.log('usage: node ci/check_asc_key.mjs <AuthKey_XXXXXXXXXX.p8 | --b64=...> <ISSUER_ID> [KEY_ID] [BUNDLE_ID]');
  process.exit(2);
}

const issuer = issuerArg.trim();
const bundleId = (bundleArg ?? 'com.pushstars.app').trim();
const fromB64 = source.startsWith('--b64=');
const pem = fromB64
  ? Buffer.from(source.slice(6).replace(/\s+/g, ''), 'base64').toString('utf8')
  : readFileSync(source, 'utf8');
const keyId = (keyArg ?? (fromB64 ? '' : basename(source).match(/AuthKey_([A-Z0-9]{10})/)?.[1] ?? '')).trim();

const problems = [];
if (!pem.includes('-----BEGIN PRIVATE KEY-----')) problems.push('the key does not look like a .p8 file (no BEGIN PRIVATE KEY line)');
if (!/^[A-Z0-9]{10}$/.test(keyId)) problems.push(`key ID "${keyId}" is not 10 characters A-Z/0-9 (pass it as the 3rd argument)`);
if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(issuer)) problems.push(`issuer "${issuer}" is not a UUID`);
if (problems.length) {
  for (const p of problems) console.log('✗ ' + p);
  process.exit(1);
}

const b64url = (b) => Buffer.from(b).toString('base64url');
let privateKey;
try {
  privateKey = createPrivateKey(pem);
} catch (e) {
  console.log('✗ could not read the private key: ' + e.message);
  process.exit(1);
}
// Team keys carry "iss" = Issuer ID; Individual keys carry "sub": "user" and no issuer.
const tokenFor = (claims) => {
  const now = Math.floor(Date.now() / 1000);
  const header = b64url(JSON.stringify({ alg: 'ES256', kid: keyId, typ: 'JWT' }));
  const payload = b64url(JSON.stringify({ ...claims, iat: now, exp: now + 600, aud: 'appstoreconnect-v1' }));
  const signature = sign('sha256', Buffer.from(`${header}.${payload}`), { key: privateKey, dsaEncoding: 'ieee-p1363' });
  return `${header}.${payload}.${b64url(signature)}`;
};

const url = `https://api.appstoreconnect.apple.com/v1/apps?filter[bundleId]=${encodeURIComponent(bundleId)}&fields[apps]=name,bundleId`;
const call = (token) => fetch(url, { headers: { Authorization: `Bearer ${token}` } });
// process.exit() while fetch's sockets are still closing trips a libuv assertion on Windows,
// so the outcome goes out through process.exitCode.
async function check() {
  const res = await call(tokenFor({ iss: issuer }));
  const body = await res.json().catch(() => ({}));

  if (res.status === 401) {
    console.log(`✗ 401: Apple rejected key ${keyId} with issuer ${issuer}.`);
    const individual = await call(tokenFor({ sub: 'user' }));
    if (individual.status !== 401) {
      console.log('  But the same .p8 works as an INDIVIDUAL key (it was made under your profile, not under Team Keys).');
      console.log('  altool in UBA needs a TEAM key: Users and Access › Integrations › App Store Connect API › Team Keys ›');
      console.log('  generate one (role App Manager), download its .p8 and use its Key ID with the Issuer ID shown there.');
    } else {
      console.log('  The key ID, the issuer and the .p8 file do not belong together, or the key was revoked.');
      console.log('  Open Users and Access › Integrations › App Store Connect API › Team Keys: is the Key ID in the list');
      console.log('  (and Active)? Copy the Issuer ID from the top of that page. If the key is not there, generate a new one.');
    }
    return 1;
  }
  if (!res.ok) {
    console.log(`✗ HTTP ${res.status}: ${JSON.stringify(body.errors ?? body)}`);
    return 1;
  }
  console.log(`✓ key ${keyId} works (issuer ${issuer}).`);
  const app = body.data?.[0];
  if (!app) {
    console.log(`✗ but there is no app with bundle ID ${bundleId} in App Store Connect — create it (Apps › + › New App) before uploading.`);
    return 1;
  }
  console.log(`✓ app "${app.attributes.name}" (${app.attributes.bundleId}) — put ASC_APP_ID=${app.id} into the UBA target.`);
  return 0;
}
process.exitCode = await check();
