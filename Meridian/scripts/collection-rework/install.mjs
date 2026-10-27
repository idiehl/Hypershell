/** Root-run installer. Default is a read-only preflight; --commit is explicit. */
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const root = 'output/collection-rework', origin = 'http://127.0.0.1:5240';
const commit = process.argv.includes('--commit');
const manifest = JSON.parse(await readFile(`${root}/manifest.json`, 'utf8'));
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(k => [k, canonical(value[k])])) : value;
const hash = value => createHash('sha256').update(JSON.stringify(canonical(value))).digest('hex');
async function get(url, optional = false) {
  const response = await fetch(origin + url);
  if (optional && response.status === 404) return null;
  if (!response.ok) throw new Error(`${url}: ${response.status}`);
  return response.json();
}
const status = await get('/__meridian_status');
if (commit) assert.ok(status.capabilities.includes('authoring-recovery-guard-v1'), 'Installed host lacks atomic recovery-aware authoring');
const ready = [];
for (const m of manifest) {
  const original = JSON.parse(await readFile(`${root}/${m.id}/saved.json`, 'utf8'));
  const originalRecovery = m.expectedRecoverySavedAt ? JSON.parse(await readFile(`${root}/${m.id}/recovery.json`, 'utf8')) : null;
  const [live, liveRecovery] = await Promise.all([get(`/api/projects/${m.id}`), get(`/api/projects/${m.id}/recovery`, true)]);
  assert.equal(hash(live), hash(original), `Saved canvas changed: ${m.title}`);
  assert.equal(hash(liveRecovery), hash(originalRecovery), `Recovery changed: ${m.title}`);
  assert.equal(live.revision, m.expectedRevision);
  assert.equal(liveRecovery?.savedAt ?? null, m.expectedRecoverySavedAt);
  const candidateText = await readFile(m.candidateFile, 'utf8');
  const candidateFileSha256 = createHash('sha256').update(candidateText).digest('hex');
  if (commit) assert.ok(m.candidateFileSha256, `Candidate is not frozen for review: ${m.title}`);
  if (m.candidateFileSha256) assert.equal(candidateFileSha256, m.candidateFileSha256, `Reviewed candidate changed: ${m.title}`);
  const project = JSON.parse(candidateText);
  ready.push({ m, project, baselineHash: hash(original), recoveryHash: hash(originalRecovery), candidateHash: hash(project), candidateFileSha256 });
}
await writeFile(`${root}/preflight.json`, JSON.stringify({ checkedAt: new Date().toISOString(), commit, projects: ready.map(({ m, project, ...checks }) => ({ id: m.id, title: m.title, expectedRevision: m.expectedRevision, expectedRecoverySavedAt: m.expectedRecoverySavedAt, ...checks })) }, null, 2));
if (!commit) { console.log('All four saved canvases and recoveries still match captured originals. No writes made.'); process.exit(0); }
const receipts = [];
for (const { m, project, ...checks } of ready) {
  const response = await fetch(`${origin}/api/projects/${m.id}/authoring`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ project, expectedRevision: m.expectedRevision, expectedRecoverySavedAt: m.expectedRecoverySavedAt, checkpointName: 'Before node and containment rework' }) });
  if (!response.ok) throw new Error(`${m.title}: ${response.status} ${await response.text()}`);
  const saved = await response.json();
  assert.equal(saved.id, m.id); assert.equal(saved.revision, m.expectedRevision + 1);
  const receipt = { id: m.id, title: m.title, revision: saved.revision, ...checks, savedHash: hash(saved), completedAt: new Date().toISOString() };
  receipts.push(receipt);
  await writeFile(`${root}/${m.id}/installed.json`, JSON.stringify(saved, null, 2));
  await writeFile(`${root}/install-receipts.json`, JSON.stringify(receipts, null, 2));
  console.log(JSON.stringify(receipt));
}
