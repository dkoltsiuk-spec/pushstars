const { test } = require('node:test');
const assert = require('node:assert/strict');
const B = require('../src/botPoolRules');

const id = n => String(n).padStart(32, 'a');
const bot = (n, reps, extra = {}) => ({ id: id(n), name: 'Bot' + n, enabled: true,
  repTimes: Array.from({ length: reps }, (_, i) => i + 1), ...extra });
const pool = [bot(1, 10), bot(2, 20), bot(3, 30), bot(4, 15)];

test('the offered bot is the nearest to the player in reps, among the closest few', () => {
  assert.equal(B.pickBot(pool, [], 14, () => 0).id, id(4));
  assert.equal(B.pickBot(pool, [], 14, () => .99).id, id(1), 'the second nearest takes turns');
  assert.equal(B.pickBot(pool, [], 40, () => 0).id, id(3), 'no level band: a strong player still meets a bot');
  assert.equal(B.pickBot(pool, [], 0, () => 0).id, id(1), 'a beginner too');
});

test('a bot the player has fought never comes back to their search', () => {
  assert.equal(B.pickBot(pool, [id(4)], 14, () => 0).id, id(1));
  assert.equal(B.pickBot(pool, [id(4), id(1)], 14, () => 0).id, id(2));
  assert.equal(B.pickBot(pool, pool.map(b => b.id), 14, () => 0), null, 'all met: the stranger or shadow comes instead');
});

test('switched-off and broken entries are never offered', () => {
  assert.equal(B.pickBot([bot(1, 10, { enabled: false })], [], 10), null);
  assert.equal(B.pickBot([bot(1, 10, { repTimes: [] })], [], 10), null);
  assert.equal(B.pickBot([{ ...bot(1, 10), id: 'nope' }], [], 10), null);
  assert.equal(B.pickBot(null, null, 10), null);
});

test('the offer looks like a stranger\'s set, with the bot\'s face', () => {
  const offer = B.offerOf({ ...bot(2, 3), name: 'SwiftOtter', trophies: 340, country: 'BR', hero: 5, gender: 1 });
  assert.deepEqual(offer, { uid: '', name: 'SwiftOtter', trophies: 340, repTimes: [1, 2, 3], clapTimes: [],
    botId: id(2), country: 'BR', hero: 5, gender: 1 });
  assert.equal(B.offerOf({ ...bot(2, 1), name: 'X'.repeat(40) }).name.length, 20);
});
