/** Read-only capture of the current programming projects and Library. */
import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
const directory = path.resolve('output/programming-trees-20261021');
const origin = 'http://127.0.0.1:5240';
const ids = { csharp: '47f9024f-2a26-4a9a-91b9-83927029fbf3', cpp: '2a9aa31d-b13c-4d0b-9192-b1a94b7c0fce' };
await mkdir(directory, { recursive: true });
async function get(endpoint, optional = false) {
  const response = await fetch(origin + endpoint);
  if (optional && response.status === 404) return null;
  assert(response.ok, `${endpoint}: ${response.status}`); return response.json();
}
async function save(name, value) {
  const filename = path.join(directory, name); await mkdir(path.dirname(filename), { recursive: true });
  try { const prior = JSON.parse(await readFile(filename, 'utf8')); assert.deepEqual(prior, value, `Existing baseline differs: ${name}`); }
  catch (error) { if (error.code !== 'ENOENT') throw error; await writeFile(filename, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' }); }
}
const library = await get('/api/library');
const entries = await Promise.all(Object.entries(ids).map(async ([slug, id]) => {
  const first = await get(`/api/projects/${id}`);
  const recovery = await get(`/api/projects/${id}/recovery`, true);
  const last = await get(`/api/projects/${id}`); assert.deepEqual(last, first);
  await save(`${slug}/baseline.json`, first); await save(`${slug}/recovery-baseline.json`, recovery);
  const entry = first.canvases.find(work => work.id === first.entryCanvasId);
  return [slug, { id, revision: first.revision, works: first.canvases.length, nodes: first.nodes.length, entry, recoverySavedAt: recovery?.savedAt ?? null, sha256: createHash('sha256').update(JSON.stringify(first)).digest('hex') }];
}));
await save('library-baseline.json', library);
await save('library-recovery-baseline.json', await get('/api/library/recovery', true));
await save('catalog-baseline.json', await get('/api/library/catalog'));
assert.deepEqual(await get('/api/library'), library);
const receipt = { origin, libraryRevision: library.revision, projects: Object.fromEntries(entries), liveWrites: false };
await save('baseline-receipt.json', receipt); console.log(JSON.stringify(receipt));
