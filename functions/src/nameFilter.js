// Which chosen nicknames other players may see (App Store guideline 1.2: objectionable content is
// filtered). The phone checks a name as it is typed, the server when it is saved and again when it
// is shown, so a name saved before this filter existed, or sent by an old build, is hidden too.
// Keep the lists and the steps identical to NameFilter.cs — nameFilter.test.js compares the lists,
// and it and NameFilterTests.cs pin the same samples.
//
// A name is folded three ways before matching: plain (lowercase, no accents, digits read as
// letters), with Cyrillic look-alikes read as Latin ("fuсk" with a Cyrillic с) and with Latin
// look-alikes read as Cyrillic ("cyka"). ANYWHERE roots match inside a word, also with repeated
// letters collapsed ("fuuuck"); WHOLE_WORDS only as a whole word, because they sit inside innocent
// ones ("spic" in Spicy, "ass" in Class, "eban" in Lebanon).
const MIN_CHARS = 2;
const MAX_CHARS = 18;

const ANYWHERE = [
  // English
  'fuck', 'shit', 'bitch', 'cunt', 'asshole', 'ashole', 'nigg', 'faggot', 'retard', 'whore', 'slut',
  'porn', 'penis', 'vagina', 'dildo', 'wank', 'rapist', 'pussy', 'pusy', 'blowjob', 'motherf',
  // Hate
  'hitler', 'nazi', 'isis', 'heilh', 'sieghe',
  // Russian
  'хуй', 'хуе', 'хуя', 'пизд', 'ебат', 'ебан', 'ебал', 'ебл', 'долбоеб', 'уеб', 'заеб', 'бляд', 'блят',
  'мудак', 'мудил', 'пидор', 'пидар', 'педик', 'гандон', 'шлюх', 'залуп', 'дроч', 'нигер', 'хохол', 'жид',
  // Russian in Latin letters
  'pizd', 'blyat', 'blyad', 'mudak', 'pidor', 'pidar', 'huesos', 'gandon', 'shluh', 'zalup',
  // Portuguese
  'caralho', 'buceta', 'arrombad', 'piroca', 'xoxota', 'vagabund', 'cuzao', 'filhodaput', 'bosta',
  // Passing for the game or its staff
  'admin', 'moderator', 'pushupstars',
];

const WHOLE_WORDS = [
  'ass', 'arse', 'dick', 'cock', 'cum', 'tits', 'boob', 'boobs', 'rape', 'fag', 'fags', 'kys', 'nude', 'nudes',
  'spic', 'kike', 'chink', 'coon', 'tranny', 'bastard', 'sex', 'kkk',
  'бля', 'сука', 'суки', 'сучка', 'хер', 'нацист', 'гей', 'kurva', 'suka', 'huy', 'hui', 'nahui', 'blya', 'ebat', 'eban', 'ebanat',
  'puta', 'porra', 'merda', 'foda', 'viado', 'cu',
  'apple', 'google', 'support', 'staff', 'official', 'developer', 'dev',
];

// Hate numbers read as they are, before digits fold into letters.
const NUMBERS = ['1488'];

const DIGITS = { 0: 'o', 1: 'i', 3: 'e', 4: 'a', 5: 's', 7: 't', 8: 'b', 9: 'g' };
const TO_LATIN = { а: 'a', в: 'b', е: 'e', к: 'k', м: 'm', н: 'h', о: 'o', р: 'p', с: 'c', т: 't', у: 'y', х: 'x' };
const TO_CYRILLIC = { a: 'а', b: 'в', c: 'с', e: 'е', h: 'н', k: 'к', m: 'м', o: 'о', p: 'р', t: 'т', x: 'х', y: 'у' };

const isLetter = c => /^\p{L}$/u.test(c);
const isDigit = c => /^\p{Nd}$/u.test(c);
const isUpper = c => isLetter(c) && c !== c.toLowerCase();

// Lowercase, accents off (й becomes и, ё becomes е, in the lists as well), letters and digits only.
function plain(text) {
  let out = '';
  for (const c of text.normalize('NFD').toLowerCase()) {
    if (isDigit(c)) out += DIGITS[c] || '';
    else if (isLetter(c) && !/\p{M}/u.test(c)) out += c;
  }
  return out;
}

const mapped = (text, map) => Array.from(text, c => map[c] || c).join('');
const collapsed = text => text.replace(/(.)\1+/gu, '$1');

// Words as a reader sees them: split at anything else than a letter or digit, and at aB in camelCase.
function words(name) {
  const out = [];
  let word = '', previous = '';
  for (const c of name) {
    if (!isLetter(c) && !isDigit(c)) { if (word) out.push(word); word = ''; previous = ''; continue; }
    if (isUpper(c) && previous && !isUpper(previous)) { out.push(word); word = ''; }
    word += c;
    previous = c;
  }
  if (word) out.push(word);
  return out;
}

const folds = text => [text, mapped(text, TO_LATIN), mapped(text, TO_CYRILLIC)];
const ROOTS = ANYWHERE.map(plain);
const WHOLE = new Set(WHOLE_WORDS.map(plain));

function hasAllowedCharacters(name) {
  return /^[\p{L}\p{Nd} _-]+$/u.test(name);
}

function isObjectionable(name) {
  const text = String(name || '');
  if (NUMBERS.some(number => text.includes(number))) return true;
  const whole = plain(text);
  for (const fold of folds(whole)) {
    const squeezed = collapsed(fold);
    if (ROOTS.some(root => fold.includes(root) || squeezed.includes(root))) return true;
    if (WHOLE.has(fold)) return true;   // "s u k a", "a.s.s"
  }
  for (const word of words(text)) {
    for (const fold of folds(plain(word))) if (WHOLE.has(fold)) return true;
  }
  return false;
}

// Why a chosen name cannot be saved; '' when it can.
function nameError(name) {
  const text = String(name || '').trim();
  if (text.length < MIN_CHARS || text.length > MAX_CHARS) return 'length';
  if (!hasAllowedCharacters(text)) return 'characters';
  if (isObjectionable(text)) return 'objectionable';
  return '';
}

module.exports = { MIN_CHARS, MAX_CHARS, ANYWHERE, WHOLE_WORDS, NUMBERS, DIGITS, TO_LATIN, TO_CYRILLIC, isObjectionable, hasAllowedCharacters, nameError };
