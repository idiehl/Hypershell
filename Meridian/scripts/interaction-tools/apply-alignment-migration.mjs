/** Guarded application of a captured Work-only alignment change. Dry run by default. */
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { auditAlignmentCandidate } from './prepare-alignment-migration.mjs';

const root = fileURLToPath(new URL('../../', import.meta.url));
const arguments_ = process.argv.slice(2), apply = arguments_.includes('--apply');
const directoryAt = arguments_.indexOf('--capture');
assert(directoryAt >= 0 && arguments_[directoryAt + 1], 'Pass --capture with a fresh migration capture directory.');
assert.equal(arguments_.length, apply ? 3 : 2);
const directory = path.resolve(arguments_[directoryAt + 1]);
const readJSON = async file => JSON.parse((await readFile(file, 'utf8')).replace(/^\uFEFF/, ''));
const hash = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
const manifest = await readJSON(path.join(directory, 'manifest.json'));
assert(manifest.getOnly && manifest.eligibleAtCapture && manifest.catalogIdsStable);
const origin = manifest.origin;
assert.equal(origin, 'http://127.0.0.1:5240');
const request = async (route, method = 'GET', body) => {
  const response = await fetch(origin + route, { method, cache: 'no-store', redirect: 'error',
    headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body), signal: AbortSignal.timeout(60000) });
  if (method === 'GET' && route.endsWith('/recovery') && response.status === 404) return null;
  assert(response.ok, `${method} ${route}: ${response.status} ${await response.clone().text()}`);
  return response.json();
};
const status = await request('/__meridian_status');
assert.equal(status.application, 'atlas-meridian');
assert(status.capabilities.includes('authoring-recovery-guard-v1'));
if (apply) {
  const application = await readJSON(path.join(root, 'output/interaction-tools-20261021/application.json'));
  assert(application.completed, 'Install and verify the reviewed interaction runtime first.');
  const editor = application.files.find(file => file.name === 'editor.js');
  assert(editor);
  assert.equal(createHash('sha256').update(await readFile(path.join(root, 'output/app/wwwroot/editor.js'))).digest('hex'), editor.after);
}
const items = [];
for (const item of [...manifest.projects, { ...manifest.library, id: 'library' }]) {
  const baseline = await readJSON(path.join(item.directory, 'baseline.json'));
  const candidate = await readJSON(path.join(item.directory, 'candidate.json'));
  assert.equal(hash(baseline), item.baselineSHA256);
  assert.equal(hash(candidate), item.candidateSHA256);
  const isLibrary = item.id === 'library', route = isLibrary ? '/api/library' : `/api/projects/${item.id}`;
  const audit = auditAlignmentCandidate(isLibrary ? baseline.project : baseline, isLibrary ? candidate.project : candidate);
  if (isLibrary) {
    const { project: ignoredA, ...a } = baseline, { project: ignoredB, ...b } = candidate;
    assert.deepEqual(a, b, 'Library envelope changed beyond Work settings.');
  }
  assert.deepEqual(await request(route), baseline, `Saved ${item.id} changed; capture it again.`);
  assert.equal(await request(route + '/recovery'), null, `Preserve the ${item.id} recovery draft before applying.`);
  items.push({ item, baseline, candidate, route, isLibrary, audit });
}
const currentIds = (await request('/api/library/catalog')).map(item => item.id).sort();
assert.deepEqual(currentIds, manifest.projects.map(item => item.id).sort());
const preflight = { checkedAt: new Date().toISOString(), apply, totalWorks: manifest.totals.projectWorks + manifest.totals.libraryWorks,
  preservedNodeFreeMovementOverrides: manifest.totals.preservedNodeFreeMovementOverrides,
  items: items.map(({ item }) => ({ id: item.id, revision: item.expectedRevision, changedWorks: item.changedWorks })) };
await writeFile(path.join(directory, 'preflight.json'), JSON.stringify(preflight, null, 2) + '\n');
if (!apply) { console.log(JSON.stringify({ ...preflight, liveWrites: false })); process.exit(0); }
const receiptFile = path.join(directory, 'application.json');
try { await readFile(receiptFile); throw new Error('An application receipt exists. Inspect it before retrying.'); }
catch (error) { if (error.code !== 'ENOENT') throw error; }
const receipt = { startedAt: new Date().toISOString(), completed: false, scope: manifest.scope, items: [] };
const record = () => writeFile(receiptFile, JSON.stringify(receipt, null, 2) + '\n');
await record();
try {
  for (const { item, baseline, candidate, route, isLibrary, audit } of items) {
    if (!item.changeRequired) { receipt.items.push({ id: item.id, skipped: true, revision: baseline.revision }); await record(); continue; }
    assert.deepEqual(await request(route), baseline, `Saved ${item.id} changed during application; stop and recapture.`);
    assert.equal(await request(route + '/recovery'), null, `A new recovery draft exists for ${item.id}; stop and preserve it.`);
    const checkpointName = 'Before automatic alignment · 2026-10-21';
    let checkpoint, saved;
    if (isLibrary) {
      checkpoint = await request(route + '/checkpoints', 'POST', { name: checkpointName, library: baseline });
      assert.deepEqual(await request(route + '/checkpoints/' + checkpoint.id), baseline);
      assert.deepEqual(await request(route), baseline);
      assert.equal(await request(route + '/recovery'), null);
      // Library PUT is revision guarded. A concurrently created recovery is retained by the host.
      saved = await request(route, 'PUT', { library: candidate, expectedRevision: baseline.revision });
    } else {
      saved = await request(route + '/authoring', 'POST', { project: candidate, expectedRevision: baseline.revision,
        expectedRecoverySavedAt: null, checkpointName });
      const checkpoints = await request(route + '/checkpoints');
      const matches = checkpoints.filter(entry => entry.name === checkpointName + ' · saved' && entry.revision === baseline.revision);
      assert(matches.length, 'Original saved checkpoint was not returned.');
      checkpoint = matches.sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0];
      assert.deepEqual(await request(route + '/checkpoints/' + checkpoint.id), baseline);
    }
    const expected = structuredClone(candidate); expected.revision = baseline.revision + 1;
    if (isLibrary) expected.project.revision = expected.revision;
    assert.deepEqual(saved, expected, `Host altered ${item.id} beyond the reviewed Work setting.`);
    assert.deepEqual(await request(route), expected);
    const recoveryAfter = await request(route + '/recovery');
    const entry = { id: item.id, title: baseline.title ?? 'Work Library', revisionBefore: baseline.revision, revision: saved.revision,
      changedWorks: audit.changedWorks.length, preservedNodeFreeMovementOverrides: audit.preservedNodeFreeMovementOverrides.length,
      checkpointId: checkpoint.id, checkpointName: checkpoint.name, savedSHA256: hash(saved),
      exactReadback: true, recoveryAfter: recoveryAfter?.savedAt ?? null, appliedAt: new Date().toISOString() };
    receipt.items.push(entry); await record();
    await writeFile(path.join(item.directory, 'saved-after.json'), JSON.stringify(saved, null, 2) + '\n');
    assert.equal(recoveryAfter, null, 'A concurrent recovery appeared. Preserve it and inspect before further writes.');
    console.log(JSON.stringify(entry));
  }
  receipt.completed = true; receipt.completedAt = new Date().toISOString(); await record();
} catch (error) { receipt.error = error.stack ?? String(error); await record(); throw error; }
