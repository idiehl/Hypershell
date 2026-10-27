/** Compare captured baselines and reviewed candidates before guarded authoring saves. */
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const folder = 'output/spatial-reveal', origin = 'http://127.0.0.1:5240';
const commit = process.argv.includes('--commit');
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const hash = value => createHash('sha256').update(JSON.stringify(canonical(value))).digest('hex');
const manifest = JSON.parse(await readFile(`${folder}/manifest.json`, 'utf8'));
async function get(url, optional = false) {
  const response = await fetch(origin + url);
  if (optional && response.status === 404) return null;
  assert.ok(response.ok, `${url}: ${response.status}`);
  return response.json();
}
const status = await get('/__meridian_status');
if (commit) assert.ok(status.capabilities.includes('spatial-children-v1'), 'Install and verify the spatial host first.');
const ready = [];
for (const item of manifest) {
  const baseline = JSON.parse(await readFile(item.baselineFile, 'utf8'));
  const [saved, recovery] = await Promise.all([get(`/api/projects/${item.id}`), get(`/api/projects/${item.id}/recovery`, true)]);
  assert.equal(hash(saved), item.baselineSavedHash, `Saved canvas changed: ${item.title}`);
  assert.equal(hash(saved), hash(baseline.saved));
  assert.equal(hash(recovery), item.baselineRecoveryHash, `Recovery changed: ${item.title}`);
  assert.equal(hash(recovery), hash(baseline.recovery));
  assert.equal(saved.revision, item.expectedRevision);
  assert.equal(recovery?.savedAt ?? null, item.expectedRecoverySavedAt);
  const text = await readFile(item.candidateFile, 'utf8');
  assert.equal(createHash('sha256').update(text).digest('hex'), item.candidateFileSha256, 'Reviewed candidate file changed.');
  const project = JSON.parse(text);
  assert.equal(hash(project), item.candidateHash);
  ready.push({ item, project });
}
await writeFile(`${folder}/preflight.json`, JSON.stringify({ checkedAt: new Date().toISOString(), commit, items: ready.map(({ item }) => ({ id: item.id, revision: item.expectedRevision, candidateHash: item.candidateHash, recovery: item.expectedRecoverySavedAt })) }, null, 2));
if (!commit) { console.log('Both reviewed candidates and their saved/recovery baselines match. No writes made.'); } else {
const receipts = [];
for (const { item, project } of ready) {
  const response = await fetch(`${origin}/api/projects/${item.id}/authoring`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ project, expectedRevision: item.expectedRevision, expectedRecoverySavedAt: item.expectedRecoverySavedAt, checkpointName: 'Before spatial children pass' }),
  });
  assert.ok(response.ok, `${item.title}: ${response.status} ${await response.clone().text()}`);
  const saved = await response.json();
  assert.equal(saved.id, item.id); assert.equal(saved.revision, item.expectedRevision + 1);
  const readback = await get(`/api/projects/${item.id}`);
  assert.equal(hash(readback), hash(saved), 'Installed readback differs.');
  assert.equal(await get(`/api/projects/${item.id}/recovery`, true), null, 'A new recovery exists; stop before further authoring.');
  const receipt = { id: item.id, revision: saved.revision, candidateHash: item.candidateHash, savedHash: hash(saved), installedAt: new Date().toISOString() };
  receipts.push(receipt);
  await writeFile(`${folder}/${item.id}/installed.json`, JSON.stringify(saved, null, 2));
  await writeFile(`${folder}/install-receipts.json`, JSON.stringify(receipts, null, 2));
  console.log(JSON.stringify(receipt));
}

}
