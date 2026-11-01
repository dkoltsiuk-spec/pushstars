// Pure rules of reports and blocks (moderation.js reads and writes; moderation.test.js pins these).

const REASONS = ['offensive_name', 'cheating', 'other'];
// Different players reporting one name before it is hidden, ahead of the moderator's look.
const AUTO_HIDE_REPORTERS = 3;
const BLOCK_LIST_MAX = 500;

const validUid = uid => typeof uid === 'string' && /^[A-Za-z0-9_-]{1,128}$/.test(uid);
const validBot = id => typeof id === 'string' && /^[a-f0-9]{32}$/.test(id);

// A report names a real account (targetUid) or a bot of the pool (targetBot), never both. The
// document key is reporter + target, so one player repeating a report changes nothing.
function reportOf(data, reporterUid, now) {
  const targetUid = validUid(data?.targetUid) ? data.targetUid : '';
  const targetBot = !targetUid && validBot(data?.targetBot) ? data.targetBot : '';
  const target = targetUid || (targetBot && `bot_${targetBot}`);
  if (!target || targetUid === reporterUid) return null;
  return {
    id: `${reporterUid}_${target}`,
    report: {
      reporterUid, targetUid, targetBot,
      name: String(data?.name || '').slice(0, 24),
      reason: REASONS.includes(data?.reason) ? data.reason : 'other',
      where: String(data?.where || '').slice(0, 24),
      state: 'open', createdAtMs: now,
    },
  };
}

// Whether this report is the one that hides the name: only an offensive-name report counts.
function hidesName(report, reporters) {
  return report.reason === 'offensive_name' && !!report.targetUid && reporters >= AUTO_HIDE_REPORTERS;
}

// Everyone this player must not meet, in either direction.
function avoidSet(blockedByMe, blockedMe) {
  return new Set([...(blockedByMe || []), ...(blockedMe || [])]);
}

module.exports = { REASONS, AUTO_HIDE_REPORTERS, BLOCK_LIST_MAX, validUid, validBot, reportOf, hidesName, avoidSet };
