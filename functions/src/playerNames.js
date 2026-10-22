// Brawl-style default nicknames ("TurboWalrus") so new players are not all "PLAYER".
// The name is derived from the uid, so the server and the app (PlayerNames.cs) agree on it
// without a round trip. Keep both word lists and the hash identical — playerNames.test.js and
// PlayerNamesTests.cs pin the same samples.
const ADJECTIVES = [
  'Swift', 'Brave', 'Turbo', 'Mighty', 'Sneaky', 'Lucky', 'Fuzzy', 'Iron',
  'Golden', 'Spicy', 'Cosmic', 'Wild', 'Silent', 'Rapid', 'Jolly', 'Grumpy',
  'Shiny', 'Crazy', 'Fierce', 'Sleepy', 'Hyper', 'Mega', 'Super', 'Epic',
  'Funky', 'Salty', 'Frosty', 'Blazing', 'Stormy', 'Neon', 'Atomic', 'Royal',
  'Savage', 'Chill', 'Bouncy', 'Crispy', 'Dizzy', 'Giant', 'Happy', 'Lazy',
  'Noble', 'Pixel', 'Quick', 'Rusty', 'Steel', 'Sunny', 'Thunder', 'Tiny',
  'Ultra', 'Wacky', 'Zesty', 'Bold', 'Clever', 'Daring', 'Fancy', 'Heavy',
  'Jumpy', 'Loud', 'Magic', 'Proud', 'Sly', 'Toxic', 'Witty', 'Zippy',
];
const NOUNS = [
  'Tiger', 'Panda', 'Walrus', 'Potato', 'Falcon', 'Shark', 'Rhino', 'Badger',
  'Gorilla', 'Penguin', 'Llama', 'Taco', 'Cactus', 'Dragon', 'Wizard', 'Pirate',
  'Viking', 'Robot', 'Comet', 'Rocket', 'Meteor', 'Golem', 'Yeti', 'Kraken',
  'Phoenix', 'Moose', 'Otter', 'Raccoon', 'Hamster', 'Bison', 'Cobra', 'Wolf',
  'Bear', 'Eagle', 'Fox', 'Hippo', 'Koala', 'Lion', 'Mammoth', 'Monkey',
  'Octopus', 'Parrot', 'Pickle', 'Muffin', 'Noodle', 'Pretzel', 'Waffle', 'Donut',
  'Burrito', 'Nugget', 'Banana', 'Coconut', 'Pumpkin', 'Cupcake', 'Samurai', 'Knight',
  'Titan', 'Goblin', 'Bandit', 'Ranger', 'Champ', 'Beast', 'Ninja', 'Mango',
];

// Names the app or the server used before there were generated ones.
const PLACEHOLDERS = new Set(['', 'player', 'игрок']);

// FNV-1a over UTF-16 code units, as C# walks a string.
function hash(seed) {
  let h = 0x811c9dc5;
  for (let i = 0; i < seed.length; i++) h = Math.imul(h ^ seed.charCodeAt(i), 0x01000193) >>> 0;
  return h;
}

function nameFor(uid) {
  const h = hash(String(uid || ''));
  return ADJECTIVES[h % ADJECTIVES.length] + NOUNS[Math.floor(h / ADJECTIVES.length) % NOUNS.length];
}

const isPlaceholder = name => PLACEHOLDERS.has(String(name || '').trim().toLowerCase());

// The name other players see: the chosen one, else the generated one.
function displayNameOf(profile, uid) {
  const name = profile?.displayName;
  return (isPlaceholder(name) ? nameFor(uid) : String(name)).replace(/[<>]/g, '').slice(0, 20);
}

module.exports = { nameFor, isPlaceholder, displayNameOf, ADJECTIVES, NOUNS };
