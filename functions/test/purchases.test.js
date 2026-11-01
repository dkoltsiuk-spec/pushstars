const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const P = require('../src/purchaseRules');
const rules = require('../src/progressRules');

test('a gem pack pays one account, once, and tells it again if asked', () => {
  assert.deepEqual(P.grantFor('gems_120', null, 'me'), { status: 'ok', gems: 120, hero: '', isNew: true });
  assert.deepEqual(P.grantFor('gems_120', { uid: 'me' }, 'me'), { status: 'ok', gems: 120, hero: '', isNew: false });
  assert.deepEqual(P.grantFor('gems_120', { uid: 'other' }, 'me'), { status: 'claimed', gems: 0, hero: '', isNew: false });
});

test('a hero is restored to any account of the buyer, without counting as a new purchase', () => {
  assert.deepEqual(P.grantFor('hero_skeleton', null, 'me'), { status: 'ok', gems: 0, hero: 'skeleton', isNew: true });
  assert.deepEqual(P.grantFor('hero_skeleton', { uid: 'other' }, 'me'), { status: 'ok', gems: 0, hero: 'skeleton', isNew: false });
});

test('unknown products grant nothing, including names on the object prototype', () => {
  assert.equal(P.grantFor('gems_999', null, 'me').status, 'unknown-product');
  assert.equal(P.grantFor('constructor', null, 'me').status, 'unknown-product');
});

test('a refunded transaction is recognized', () => {
  assert.ok(P.isRevoked({ revocationDate: 1700000000000 }));
  assert.ok(!P.isRevoked({}));
});

test('the products match the app', () => {
  const root = path.join(__dirname, '../../Assets/_Project/Scripts');
  const catalog = fs.readFileSync(path.join(root, 'Core/Economy/StoreCatalog.cs'), 'utf8');
  for (const [id, product] of Object.entries(P.PRODUCTS)) {
    if (product.gems) assert.ok(catalog.includes(`"${id}", ${product.gems}`), `${id} pays ${product.gems}`);
  }
  const heroes = fs.readFileSync(path.join(root, 'Core/Economy/AvatarCatalog.cs'), 'utf8');
  for (const [id, product] of Object.entries(P.PRODUCTS)) {
    if (product.hero) assert.ok(new RegExp(`Id = "${product.hero}".*StoreProductId = "${id}"`).test(heroes), id);
  }
});

test('Apple\'s pinned root certificate is the one in the repo', () => {
  const cert = new crypto.X509Certificate(fs.readFileSync(path.join(__dirname, '../src/certs/AppleRootCA-G3.cer')));
  assert.equal(cert.fingerprint256, '63:34:3A:BF:B8:9A:6A:03:EB:B5:7E:9B:3F:5F:A7:BE:7C:4F:5C:75:6F:30:17:B3:A8:C4:88:C3:65:3E:91:79');
});

test('a cloud save may hold bought gems only when the server verified them', () => {
  const summary = { workouts: 0, totalReps: 0, historyReps: 0, xp: 0, peakAura: 0, caseAwards: 0, gemReceipts: 0, gems: 5000, trophies: 0, trophiesWon: 0, bossCleared: 0 };
  assert.throws(() => rules.checkEvidence(summary), /More gems/);
  assert.doesNotThrow(() => rules.checkEvidence(summary, { gems: 3000, count: 1 }));
  assert.throws(() => rules.checkEvidence({ ...summary, gems: 0, gemReceipts: 60 }), /gem rewards/);
  assert.doesNotThrow(() => rules.checkEvidence({ ...summary, gems: 0, gemReceipts: 60 }, { gems: 360, count: 10 }));
});
