// The bot pool's choices, kept apart from Firestore so node tests can run them (botPool.js reads
// and writes).
const validId = id => typeof id === 'string' && /^[a-f0-9]{32}$/.test(id);

const NEAREST = 2;       // the closest few take turns, so two searches in a row differ

// The bot to offer: one the player has not fought, nearest to their best set in reps. Unlike a
// stranger's set there is no level band — a bot is always better than "nobody", and the pool is
// what makes the game feel populated at launch. Null once every bot has been fought.
function pickBot(pool, fought, best, random = Math.random) {
  const seen = new Set(fought || []);
  const open = (pool || [])
    .filter(b => b && b.enabled !== false && validId(b.id) && !seen.has(b.id) && Array.isArray(b.repTimes) && b.repTimes.length > 0)
    .sort((a, b) => Math.abs(a.repTimes.length - best) - Math.abs(b.repTimes.length - best))
    .slice(0, NEAREST);
  if (!open.length) return null;
  return open[Math.min(open.length - 1, Math.floor(random() * open.length))];
}

// What the search card shows and the phone needs to fetch the bot's set: the same shape as a
// stranger's ghost, plus the bot's id and look.
function offerOf(bot) {
  return { uid: '', name: String(bot.name || 'PLAYER').slice(0, 20), trophies: bot.trophies || 0,
    repTimes: bot.repTimes, clapTimes: bot.clapTimes || [],
    botId: bot.id, country: bot.country || '', hero: bot.hero || 0, gender: bot.gender || 0 };
}

module.exports = { pickBot, offerOf, validId, NEAREST };
