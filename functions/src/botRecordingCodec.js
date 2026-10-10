const { gunzipSync } = require('node:zlib');
const crypto = require('node:crypto');
const { cleanClapTimes } = require('./clapTimes');
const FRAME_BYTES = 441, MAX_FRAMES = 6000, MAX_COMPRESSED = 2 * 1024 * 1024;

function validateRecording(input, uid) {
  const bad = message => { throw new Error(message); };
  if (!input || input.version !== 1 || !/^[a-f0-9]{32}$/.test(input.id || '') || input.ownerUid !== uid)
    bad('Invalid recording identity.');
  const f = input.fight;
  if (!f || f.exercise !== 'pushups' || !Array.isArray(f.repTimes) || f.repTimes.length < 1 || f.repTimes.length > 65 ||
      f.reps !== f.repTimes.length || !Number.isFinite(f.durationSec) || f.durationSec < 1 || f.durationSec > 60 ||
      !Number.isFinite(f.avgForm) || f.avgForm < 0 || f.avgForm > 100) bad('Invalid fight.');
  let previous = -1;
  for (const time of f.repTimes) {
    if (!Number.isFinite(time) || time < 0 || time > f.durationSec || time - previous < .4) bad('Invalid repetition timing.');
    previous = time;
  }
  if (typeof f.motionBase64 !== 'string' || f.motionBase64.length > Math.ceil(MAX_COMPRESSED / 3) * 4 ||
      !/^[A-Za-z0-9+/]+={0,2}$/.test(f.motionBase64)) bad('Invalid animation encoding.');
  const compressed = Buffer.from(f.motionBase64, 'base64');
  const raw = gunzipSync(compressed, { maxOutputLength: 12 + MAX_FRAMES * FRAME_BYTES });
  if (raw.length < 12 || raw.readUInt32LE(0) !== 0x31525350 || raw.readInt32LE(4) !== 95) bad('Unsupported animation version.');
  const count = raw.readInt32LE(8);
  if (count < 2 || count > MAX_FRAMES || raw.length !== 12 + count * FRAME_BYTES) bad('Invalid animation size.');
  let lastPhase = -1, lastTime = -1, firstLive = null, lastLive = null;
  const phaseFrames = [0, 0, 0];
  for (let i = 0, offset = 12; i < count; i++, offset += FRAME_BYTES) {
    const phase = raw[offset], time = raw.readFloatLE(offset + 1);
    if (phase > 2 || phase < lastPhase || !Number.isFinite(time) || time < 0 || time > 60.1 ||
        (phase === lastPhase && time <= lastTime)) bad('Invalid animation clock.');
    for (let p = offset + 5; p < offset + FRAME_BYTES; p += 4) {
      const value = raw.readFloatLE(p);
      if (!Number.isFinite(value) || Math.abs(value) > 10000) bad('Invalid pose.');
    }
    phaseFrames[phase]++;
    if (phase === 2) { firstLive ??= time; lastLive = time; }
    lastPhase = phase; lastTime = time;
  }
  if (firstLive === null || firstLive > 1 || lastLive < f.durationSec - 1 || lastLive > f.durationSec + .1)
    bad('Animation does not cover the fight.');
  const text = (value, max) => typeof value === 'string' ? value.replace(/[<>\r\n]/g, '').slice(0, max) : '';
  if (!/^[A-Z]{2}$/.test(input.countryCode || '') || !Number.isInteger(input.avatar) || input.avatar < 0 || input.avatar > 3 ||
      ![0, 1].includes(input.gender) || !Number.isInteger(input.trophies) || input.trophies < 0 || input.trophies > 999999999)
    bad('Invalid bot profile.');
  // Left out when there are none, so a take recorded before claps were kept still hashes the same.
  const clapTimes = cleanClapTimes(f.clapTimes, f.repTimes, f.durationSec);
  const recording = { version: 1, id: input.id, ownerUid: uid, displayName: text(input.displayName, 20) || 'TEST BOT',
    countryCode: input.countryCode, trophies: input.trophies, avatar: input.avatar, gender: input.gender,
    achievementIds: Array.isArray(input.achievementIds) ? input.achievementIds.slice(0, 3).map(x => text(x, 40)) : [],
    fight: { exercise: 'pushups', arenaId: text(f.arenaId, 80), reps: f.reps, durationSec: f.durationSec,
      repTimes: f.repTimes, avgForm: f.avgForm, recordedAtUtc: text(f.recordedAtUtc, 40), source: 'bot-recording', motionBase64: f.motionBase64,
      ...(clapTimes.length ? { clapTimes } : {}) },
    cloudPath: '' };
  const json = JSON.stringify(recording);
  return { recording, json, hash: crypto.createHash('sha256').update(json).digest('hex'), phaseFrames, frames: count };
}
module.exports = { validateRecording, FRAME_BYTES, MAX_FRAMES };
