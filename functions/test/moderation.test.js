const { test } = require('node:test');
const assert = require('node:assert/strict');
const M = require('../src/moderationRules');

const BOT = 'a'.repeat(32);

test('a report names one target and is keyed by reporter and target', () => {
  const { id, report } = M.reportOf({ targetUid: 'bad', name: 'Name', reason: 'offensive_name', where: 'league' }, 'me', 5);
  assert.equal(id, 'me_bad');
  assert.deepEqual(report, { reporterUid: 'me', targetUid: 'bad', targetBot: '', name: 'Name', reason: 'offensive_name', where: 'league', state: 'open', createdAtMs: 5 });
  assert.equal(M.reportOf({ targetBot: BOT }, 'me', 5).id, `me_bot_${BOT}`);
});

test('a report without a valid target, or about oneself, is refused', () => {
  assert.equal(M.reportOf({}, 'me', 5), null);
  assert.equal(M.reportOf({ targetUid: 'me' }, 'me', 5), null);
  assert.equal(M.reportOf({ targetUid: 'a/b' }, 'me', 5), null);
  assert.equal(M.reportOf({ targetBot: 'short' }, 'me', 5), null);
});

test('an unknown reason counts as other, and long texts are cut', () => {
  const { report } = M.reportOf({ targetUid: 'x', reason: 'whatever', name: 'n'.repeat(99), where: 'w'.repeat(99) }, 'me', 5);
  assert.equal(report.reason, 'other');
  assert.equal(report.name.length, 24);
  assert.equal(report.where.length, 24);
});

test('enough different players reporting a name hide it; other reasons and bots do not', () => {
  const name = M.reportOf({ targetUid: 'x', reason: 'offensive_name' }, 'me', 5).report;
  assert.ok(!M.hidesName(name, M.AUTO_HIDE_REPORTERS - 1));
  assert.ok(M.hidesName(name, M.AUTO_HIDE_REPORTERS));
  assert.ok(!M.hidesName(M.reportOf({ targetUid: 'x', reason: 'cheating' }, 'me', 5).report, 99));
  assert.ok(!M.hidesName(M.reportOf({ targetBot: BOT, reason: 'offensive_name' }, 'me', 5).report, 99));
});

test('a block works in both directions', () => {
  const avoid = M.avoidSet(['a'], ['b']);
  assert.ok(avoid.has('a') && avoid.has('b') && !avoid.has('c'));
});
