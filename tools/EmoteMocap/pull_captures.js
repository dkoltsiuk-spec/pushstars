#!/usr/bin/env node
// Pulls Emote Lab takes from the project's private Storage folder and turns each into an upright
// MP4 for the mocap pipeline.
//
//   node Tools/EmoteMocap/pull_captures.js [--delete] [--hflip] [--project push-stars-d620e]
//
// A take is recorded on the phone (Development build ▸ EMOTE LAB) and uploaded by the cloud function
// saveEmoteCaptureChunk as emote-captures/<uid>/<id>/meta.json + chunk_NNN.bin
// (chunk = [int32 count] then count × [int32 length][float32 seconds][JPEG]).
// Output: Tools/EmoteMocap/captures/<name>_<id8>.mp4 (+ .json). The frames carry their capture
// times, so the video keeps real timing even where the phone skipped a frame.
// --delete removes the cloud copy once the MP4 is written (frees the phone's 12-take quota).
//
// Auth: the Firebase CLI login on this machine (firebase login) — the same credentials
// `firebase deploy` uses. Needs ffmpeg on PATH.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync, execSync } = require('child_process');

const args = process.argv.slice(2);
const flag = name => args.includes(name);
const option = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const PROJECT = option('--project', 'push-stars-d620e');
const OUT = path.join(__dirname, 'captures');
const API = 'https://storage.googleapis.com/storage/v1';

async function accessToken() {
  const root = execSync('npm root -g').toString().trim();
  const lib = path.join(root, 'firebase-tools', 'lib');
  const auth = require(path.join(lib, 'auth')), scopes = require(path.join(lib, 'scopes'));
  const account = auth.getGlobalDefaultAccount();
  if (!account) throw new Error('Not logged in: run `firebase login`.');
  return (await auth.getAccessToken(account.tokens.refresh_token, [scopes.CLOUD_PLATFORM])).access_token;
}

async function main() {
  const token = await accessToken();
  const call = async (url, init = {}) => {
    const response = await fetch(url, { ...init, headers: { Authorization: 'Bearer ' + token } });
    if (!response.ok) throw new Error(`${response.status} ${url}`);
    return response;
  };
  const buckets = (await (await call(`${API}/b?project=${PROJECT}`)).json()).items || [];
  const bucket = (buckets.find(b => b.name.endsWith('.firebasestorage.app')) || buckets.find(b => b.name.endsWith('.appspot.com')) || {}).name;
  if (!bucket) throw new Error('No default Storage bucket in ' + PROJECT);

  const objects = [];
  for (let page = ''; page !== null;) {
    const list = await (await call(`${API}/b/${bucket}/o?prefix=emote-captures/&maxResults=1000${page && '&pageToken=' + page}`)).json();
    objects.push(...(list.items || []));
    page = list.nextPageToken || null;
  }
  const download = async name => Buffer.from(await (await call(`${API}/b/${bucket}/o/${encodeURIComponent(name)}?alt=media`)).arrayBuffer());
  const takes = objects.filter(o => o.name.endsWith('/meta.json'));
  if (takes.length === 0) { console.log('No takes in the cloud.'); return; }
  fs.mkdirSync(OUT, { recursive: true });

  for (const object of takes) {
    const dir = object.name.slice(0, -'meta.json'.length);
    const meta = JSON.parse((await download(object.name)).toString('utf8'));
    const label = `${meta.name}_${meta.id.slice(0, 8)}`;
    const video = path.join(OUT, label + '.mp4');
    if (meta.status !== 'ready') { console.log(`${label}: still uploading (${meta.status}), skipped`); continue; }
    if (fs.existsSync(video)) {
      console.log(`${label}: already pulled`);
    } else {
      const work = fs.mkdtempSync(path.join(os.tmpdir(), 'emote-take-'));
      const frames = [];
      const chunks = objects.filter(o => o.name.startsWith(dir + 'chunk_')).sort((a, b) => a.name.localeCompare(b.name));
      for (const chunk of chunks) {
        const bytes = await download(chunk.name);
        const count = bytes.readInt32LE(0);
        for (let i = 0, p = 4; i < count; i++) {
          const length = bytes.readInt32LE(p), seconds = bytes.readFloatLE(p + 4);
          const file = path.join(work, `f${String(frames.length).padStart(4, '0')}.jpg`);
          fs.writeFileSync(file, bytes.subarray(p + 8, p + 8 + length));
          frames.push({ file, seconds });
          p += 8 + length;
        }
      }
      if (frames.length < 2) { console.log(`${label}: no frames, skipped`); continue; }
      // Each frame is shown until the next one's capture time; the last for the typical interval.
      const typical = (frames[frames.length - 1].seconds - frames[0].seconds) / (frames.length - 1);
      const list = frames.map((f, i) =>
        `file '${f.file.replace(/\\/g, '/')}'\nduration ${(i + 1 < frames.length ? frames[i + 1].seconds - f.seconds : typical).toFixed(4)}`)
        .join('\n') + `\nfile '${frames[frames.length - 1].file.replace(/\\/g, '/')}'\n`;
      fs.writeFileSync(path.join(work, 'list.txt'), list);
      // Unity's videoRotationAngle is the clockwise turn that shows the sensor image upright.
      const filters = [];
      if (meta.verticallyMirrored) filters.push('vflip');
      if (meta.rotation === 90) filters.push('transpose=1');
      else if (meta.rotation === 180) filters.push('hflip', 'vflip');
      else if (meta.rotation === 270) filters.push('transpose=2');
      if (flag('--hflip')) filters.push('hflip');
      filters.push('fps=24', 'scale=trunc(iw/2)*2:trunc(ih/2)*2');
      execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', path.join(work, 'list.txt'),
        '-vf', filters.join(','), '-c:v', 'libx264', '-crf', '14', '-pix_fmt', 'yuv420p', video]);
      fs.writeFileSync(path.join(OUT, label + '.json'), JSON.stringify(meta, null, 2));
      fs.rmSync(work, { recursive: true, force: true });
      console.log(`${label}: ${frames.length} frames, ${(frames[frames.length - 1].seconds).toFixed(1)} s, ` +
        `${meta.width}x${meta.height} rot ${meta.rotation}${meta.frontFacing ? ' front' : ' back'} -> ${path.relative(process.cwd(), video)}`);
    }
    if (flag('--delete')) {
      for (const o of objects.filter(o => o.name.startsWith(dir)))
        await call(`${API}/b/${bucket}/o/${encodeURIComponent(o.name)}`, { method: 'DELETE' });
      console.log(`${label}: cloud copy deleted`);
    }
  }
}

main().catch(e => { console.error('pull_captures: ' + e.message); process.exit(1); });
