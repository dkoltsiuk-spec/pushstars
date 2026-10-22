const { test } = require('node:test');
const assert = require('node:assert/strict');
const { nameFor, isPlaceholder, displayNameOf, ADJECTIVES, NOUNS } = require('../src/playerNames');

test('the same uid always gets the same name, and the app derives the same one', () => {
  // PlayerNamesTests.cs pins these samples too.
  assert.equal(nameFor('abc123'), 'LuckyOctopus');
  assert.equal(nameFor('Zr8kQ2mPx9TfYw1LhN3s'), 'WittyLion');
  assert.equal(nameFor(''), 'LuckyKnight');
});

test('names are short single words the rename rules accept', () => {
  assert.equal(new Set(ADJECTIVES).size, ADJECTIVES.length);
  assert.equal(new Set(NOUNS).size, NOUNS.length);
  for (const a of ADJECTIVES) for (const n of NOUNS) {
    const name = a + n;
    assert.ok(name.length >= 2 && name.length <= 18 && /^[A-Za-z]+$/.test(name), name);
  }
});

test('different players mostly get different names', () => {
  const names = new Set();
  for (let i = 0; i < 1000; i++) names.add(nameFor(`uid-${i}`));
  assert.ok(names.size > 800, `only ${names.size} distinct names`);
});

test('placeholders give way to the generated name, chosen names stay', () => {
  for (const placeholder of [undefined, '', 'PLAYER', 'Player', ' Игрок ']) assert.ok(isPlaceholder(placeholder));
  assert.equal(displayNameOf({ displayName: 'Игрок' }, 'u1'), nameFor('u1'));
  assert.equal(displayNameOf(null, 'u1'), nameFor('u1'));
  assert.equal(displayNameOf({ displayName: 'DANA <3' }, 'u1'), 'DANA 3');
});
