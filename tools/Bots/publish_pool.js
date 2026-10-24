#!/usr/bin/env node
// Promotes Bot Lab takes into the search's bot pool (functions/src/botPool.js).
//
//   node tools/Bots/publish_pool.js [--dry-run] [--refresh] [--min-reps 5] [--project push-stars-d620e]
//
// Every ready take in botDraftOwners/*/recordings with at least --min-reps reps becomes
// botPool/{id} + Storage bot-pool/{id}.json — the whole recording (motion included) under a
// player's face: name, flag, hero, gender, trophies. A bot already in the pool keeps the face it
// has (players may have met it), so running this again only adds new takes — unless --refresh,
// which re-applies FACES (matched by name) to bots already in the pool: a new hero or trophy
// count shows on every phone at its next search. To take a bot out of the search, set its
// `enabled` to false in the console.
//
// Auth: the Firebase CLI login on this machine (firebase login), as for `firebase deploy`.
const crypto = require('crypto');
const path = require('path');
const { execSync } = require('child_process');

const args = process.argv.slice(2);
const option = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const PROJECT = option('--project', 'push-stars-d620e');
const MIN_REPS = Number(option('--min-reps', '5'));
const DRY = args.includes('--dry-run');
const REFRESH = args.includes('--refresh');
const FIRESTORE = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)/documents`;
const STORAGE = 'https://storage.googleapis.com/storage/v1';

// Faces, handed out in order to takes that have none yet. Every bot has its own hero, paid ones
// included, so the search does not look like one account. Hero = AvatarCatalog slot:
// 1 QUEEN PUSH, 2 FIGHTER, 3 ROBOT, 4 GLADIATOR, 5 BOGATYR, 6 VIKING, 7 ZOMBIE, 8 TIGRESS,
// 9 SKINNY, 10 CHUBBY, 11 SKELETON, 12 GOBLIN, 13 FROST, 14 MAGMA; gender only matters for the
// base FIGHTER body. Trophies are modest — a player a few dozen duels in, growing with the set.
const FACES = [
  { name: 'dimon_push', country: 'RU', hero: 6, gender: 0, trophies: 74 },
  { name: 'LucasFit', country: 'BR', hero: 2, gender: 0, trophies: 35 },
  { name: 'SwiftOtter', country: 'US', hero: 11, gender: 0, trophies: 81 },
  { name: 'Katya.K', country: 'RU', hero: 1, gender: 1, trophies: 48 },
  { name: 'joao_br', country: 'BR', hero: 7, gender: 0, trophies: 103 },
  { name: 'MAKS2009', country: 'KZ', hero: 4, gender: 0, trophies: 156 },
  { name: 'IronBadger', country: 'DE', hero: 3, gender: 0, trophies: 142 },
  { name: 'tigr_ufa', country: 'RU', hero: 8, gender: 1, trophies: 88 },
  { name: 'Rafa_10', country: 'PT', hero: 10, gender: 0, trophies: 52 },
  { name: 'NoodleNinja', country: 'US', hero: 9, gender: 0, trophies: 60 },
  { name: 'vlad.pro', country: 'BY', hero: 5, gender: 0, trophies: 95 },
  { name: 'Bia_santos', country: 'BR', hero: 13, gender: 1, trophies: 70 },
  { name: 'Ilya_77', country: 'RU', hero: 14, gender: 0, trophies: 110 },
  { name: 'GoblinGabe', country: 'US', hero: 12, gender: 0, trophies: 40 },
];

async function accessToken() {
  const lib = path.join(execSync('npm root -g').toString().trim(), 'firebase-tools', 'lib');
  const auth = require(path.join(lib, 'auth')), scopes = require(path.join(lib, 'scopes'));
  const account = auth.getGlobalDefaultAccount();
  if (!account) throw new Error('Not logged in: run `firebase login`.');
  return (await auth.getAccessToken(account.tokens.refresh_token, [scopes.CLOUD_PLATFORM])).access_token;
}

// Firestore REST values, both ways (only the types the pool uses).
const toValue = v => Array.isArray(v) ? { arrayValue: { values: v.map(toValue) } }
  : typeof v === 'boolean' ? { booleanValue: v }
  : typeof v === 'number' ? (Number.isInteger(v) ? { integerValue: String(v) } : { doubleValue: v })
  : { stringValue: String(v) };
const fromValue = v => 'arrayValue' in v ? (v.arrayValue.values || []).map(fromValue)
  : 'integerValue' in v ? Number(v.integerValue) : 'doubleValue' in v ? v.doubleValue
  : 'booleanValue' in v ? v.booleanValue : 'stringValue' in v ? v.stringValue : null;
const fieldsOf = doc => Object.fromEntries(Object.entries(doc.fields || {}).map(([k, v]) => [k, fromValue(v)]));

async function main() {
  const token = await accessToken();
  const call = async (url, init = {}) => {
    const response = await fetch(url, { ...init, headers: { Authorization: 'Bearer ' + token, ...(init.headers || {}) } });
    if (!response.ok) throw new Error(`${response.status} ${url}\n${await response.text()}`);
    return response;
  };
  const buckets = (await (await call(`${STORAGE}/b?project=${PROJECT}`)).json()).items || [];
  const bucket = (buckets.find(b => b.name.endsWith('.firebasestorage.app')) || buckets.find(b => b.name.endsWith('.appspot.com')) || {}).name;
  if (!bucket) throw new Error('No default Storage bucket in ' + PROJECT);

  const drafts = (await (await call(`${FIRESTORE}:runQuery`, { method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ structuredQuery: { from: [{ collectionId: 'recordings', allDescendants: true }], limit: 500 } }) })).json())
    .filter(row => row.document && row.document.name.includes('/botDraftOwners/'))
    .map(row => fieldsOf(row.document))
    .filter(d => d.status === 'ready' && d.reps >= MIN_REPS)
    .sort((a, b) => String(a.id).localeCompare(String(b.id)));
  const pool = Object.fromEntries(((await (await call(`${FIRESTORE}/botPool?pageSize=300`)).json()).documents || [])
    .map(doc => [doc.name.split('/').pop(), fieldsOf(doc)]));
  const used = new Set(Object.values(pool).map(b => b.name));
  const faces = FACES.filter(f => !used.has(f.name));
  console.log(`${drafts.length} takes with ≥ ${MIN_REPS} reps, ${Object.keys(pool).length} already in the pool.`);

  for (const draft of drafts) {
    const known = pool[draft.id];
    const face = known ? REFRESH && FACES.find(f => f.name === known.name) : faces.shift();
    if (known && !face) { console.log(`= ${known.name} (${draft.reps} reps) already in the pool`); continue; }
    if (!face) { console.log(`! no face left for ${draft.id} — add one to FACES`); continue; }
    const bytes = await (await call(`${STORAGE}/b/${bucket}/o/${encodeURIComponent(draft.path)}?alt=media`)).text();
    const take = JSON.parse(bytes);
    const trophies = face.trophies;
    const recording = { ...take, displayName: face.name, countryCode: face.country, avatar: face.hero, gender: face.gender,
      trophies, achievementIds: [], cloudPath: '' };
    const json = JSON.stringify(recording);
    const hash = crypto.createHash('sha256').update(json).digest('hex');
    const file = `bot-pool/${draft.id}.json`;
    const entry = { name: face.name, country: face.country, hero: face.hero, gender: face.gender, trophies,
      reps: take.fight.reps, repTimes: take.fight.repTimes, clapTimes: take.fight.clapTimes || [],
      path: file, hash, enabled: true, sourceOwner: draft.ownerUid };
    console.log(`${known ? '~' : '+'} ${face.name} ${face.country} hero ${face.hero} · ${entry.reps} reps · ${trophies} trophies · ${(json.length / 1024).toFixed(0)} KB`);
    if (DRY) continue;
    await call(`https://storage.googleapis.com/upload/storage/v1/b/${bucket}/o?uploadType=media&name=${encodeURIComponent(file)}`,
      { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: json });
    await call(`${FIRESTORE}/botPool/${draft.id}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fields: Object.fromEntries(Object.entries(entry).map(([k, v]) => [k, toValue(v)])) }) });
  }
  if (DRY) console.log('Dry run: nothing written.');
}

main().catch(e => { console.error(e.message); process.exit(1); });
