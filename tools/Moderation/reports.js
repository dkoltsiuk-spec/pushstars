#!/usr/bin/env node
// The moderator's queue of player reports (functions/src/moderation.js, docs/architecture/moderation.md).
//
//   node tools/Moderation/reports.js                               open reports, oldest first
//   node tools/Moderation/reports.js --hide <uid> [--report <id>]  hides the player's chosen name
//   node tools/Moderation/reports.js --lift <uid>                  lifts that, the player may rename
//   node tools/Moderation/reports.js --close <id>                  a report needing no action
//
// Auth: the Firebase CLI login on this machine (firebase login), as for `firebase deploy`.
const path = require('path');
const { execSync } = require('child_process');

const args = process.argv.slice(2);
const option = name => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : ''; };
const PROJECT = option('--project') || 'push-stars-d620e';
const FIRESTORE = `https://firestore.googleapis.com/v1/projects/${PROJECT}/databases/(default)`;

async function accessToken() {
  const lib = path.join(execSync('npm root -g').toString().trim(), 'firebase-tools', 'lib');
  const auth = require(path.join(lib, 'auth')), scopes = require(path.join(lib, 'scopes'));
  const account = auth.getGlobalDefaultAccount();
  if (!account) throw new Error('Not logged in: run `firebase login`.');
  return (await auth.getAccessToken(account.tokens.refresh_token, [scopes.CLOUD_PLATFORM])).access_token;
}

const fromValue = v => 'integerValue' in v ? Number(v.integerValue) : 'booleanValue' in v ? v.booleanValue : 'stringValue' in v ? v.stringValue : null;
const fieldsOf = doc => Object.fromEntries(Object.entries(doc.fields || {}).map(([k, v]) => [k, fromValue(v)]));

async function main() {
  const token = await accessToken();
  const call = async (url, init = {}) => {
    const response = await fetch(url, { ...init, headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json', ...(init.headers || {}) } });
    if (!response.ok) throw new Error(`${response.status} ${url}\n${await response.text()}`);
    return response.json();
  };
  const patch = (doc, field, value) => call(`${FIRESTORE}/documents/${doc}?updateMask.fieldPaths=${field}`, { method: 'PATCH', body: JSON.stringify({ fields: { [field]: value } }) });
  const closeReport = id => patch(`reports/${id}`, 'state', { stringValue: 'closed' });

  if (option('--hide')) {
    await patch(`users/${option('--hide')}`, 'nameHidden', { booleanValue: true });
    if (option('--report')) await closeReport(option('--report'));
    console.log(`Hidden: ${option('--hide')} now shows under the generated name.`);
    return;
  }
  if (option('--lift')) {
    await patch(`users/${option('--lift')}`, 'nameHidden', { booleanValue: false });
    console.log(`Lifted: ${option('--lift')}.`);
    return;
  }
  if (option('--close')) {
    await closeReport(option('--close'));
    console.log(`Closed: ${option('--close')}.`);
    return;
  }

  const rows = await call(`${FIRESTORE}/documents:runQuery`, {
    method: 'POST',
    body: JSON.stringify({ structuredQuery: {
      from: [{ collectionId: 'reports' }],
      where: { fieldFilter: { field: { fieldPath: 'state' }, op: 'EQUAL', value: { stringValue: 'open' } } },
      limit: 200,
    } }),
  });
  const open = rows.filter(r => r.document).map(r => ({ id: r.document.name.split('/').pop(), ...fieldsOf(r.document) }))
    .sort((a, b) => a.createdAtMs - b.createdAtMs);
  if (!open.length) { console.log('No open reports.'); return; }
  for (const r of open) {
    const hours = ((Date.now() - r.createdAtMs) / 3600000).toFixed(1);
    console.log(`${hours} h ago  ${r.reason}  "${r.name}"  target ${r.targetUid || 'bot ' + r.targetBot}  from ${r.where}\n  report ${r.id}`);
  }
}

main().catch(e => { console.error(e.message); process.exit(1); });
