const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const F = require('../src/nameFilter');
const { ADJECTIVES, NOUNS, displayNameOf, nameFor } = require('../src/playerNames');

// NameFilterTests.cs pins the same samples.
const REFUSED = ['fuck', 'FuCk You', 'fuuuck', 'f_u_c_k', 'fu-ck', 'fuсk', 'sh1t', 'B1tch', 'nigga', 'BigAss', 'a s s',
  'Хуй', 'хуйло', 'пиздец', 'Сука', 'с у к а', 'cyka', 'suka', 'pidor', 'Мудак', 'ёбаный', 'блядь',
  'caralho', 'PUTA', 'filho da puta', 'Hitler', 'x1488x', 'admin', 'Moderator1', 'Pushup Stars', 'Apple', 'Official'];
const ALLOWED = ['Dana', 'Дана', 'Denis_07', 'Class', 'Spicy', 'Peacock', 'Raccoon', 'Lebanon', 'Knight', 'Computador',
  'Рубль', 'Корабль', 'Сабля', 'João', 'Lucas Fit', 'IronBadger', 'dimon_push', 'LucasFit', 'SwiftOtter', 'Katya_K',
  'joao_br', 'MAKS2009', 'tigr_ufa', 'Rafa_10'];

test('objectionable names are refused however they are spelled', () => {
  for (const name of REFUSED) assert.ok(F.isObjectionable(name), name);
});

test('ordinary names are not caught', () => {
  for (const name of ALLOWED) assert.ok(!F.isObjectionable(name), name);
});

test('every generated nickname passes the filter', () => {
  for (const a of ADJECTIVES) for (const n of NOUNS) assert.ok(!F.isObjectionable(a + n), a + n);
});

test('a name to be saved is checked for length, characters and content', () => {
  assert.equal(F.nameError('Dana'), '');
  assert.equal(F.nameError(' Dana '), '');
  assert.equal(F.nameError('D'), 'length');
  assert.equal(F.nameError('A'.repeat(19)), 'length');
  assert.equal(F.nameError('Dana <b>'), 'characters');
  assert.equal(F.nameError('Dana★'), 'characters');
  assert.equal(F.nameError('Сука'), 'objectionable');
});

test('other players see the generated name instead of a filtered or hidden one', () => {
  assert.equal(displayNameOf({ displayName: 'Сука' }, 'u1'), nameFor('u1'));
  assert.equal(displayNameOf({ displayName: 'Dana', nameHidden: true }, 'u1'), nameFor('u1'));
  assert.equal(displayNameOf({ displayName: 'Dana' }, 'u1'), 'Dana');
});

test('the app filters with the same lists', () => {
  const source = fs.readFileSync(path.join(__dirname, '../../Assets/_Project/Scripts/Core/NameFilter.cs'), 'utf8');
  const listOf = name => {
    const body = source.match(new RegExp(`${name}\\s*=\\s*(?:new\\s*\\[\\]\\s*)?\\{([\\s\\S]*?)\\};`));
    assert.ok(body, name);
    return Array.from(body[1].replace(/\/\/.*$/gm, '').matchAll(/"([^"]+)"/g), m => m[1]);
  };
  assert.deepEqual(listOf('Anywhere'), F.ANYWHERE);
  assert.deepEqual(listOf('WholeWords'), F.WHOLE_WORDS);
  assert.deepEqual(listOf('Numbers'), F.NUMBERS);
});
