/** Read-only live verification for a single new atlas installation. */
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';

const directory = path.resolve(process.argv[2] ?? '');
assert.ok(process.argv[2], 'Supply an atlas output directory.');
const json = async name => JSON.parse((await readFile(path.join(directory, name), 'utf8')).replace(/^\uFEFF/, ''));
const [draft, installed, aliases] = await Promise.all(['draft.json', 'installed.json', 'aliases.json'].map(json));
const response = await fetch(`http://127.0.0.1:5240/api/projects/${encodeURIComponent(installed.id)}`);
assert.ok(response.ok, `Cannot read back installed project: ${response.status}`);
const readback = await response.json();
assert.deepEqual(readback, installed, 'Live readback differs from the installation receipt.');
const ids = new Map([[draft.id, installed.id]]);
for (const key of ['canvases', 'nodes', 'links', 'assets']) {
  assert.equal(draft[key].length, installed[key].length, `${key} count changed`);
  draft[key].forEach((entity, index) => {
    assert.notEqual(entity.id, installed[key][index].id, `${key} did not receive independent identities`);
    ids.set(entity.id, installed[key][index].id);
  });
}
function remap(value) {
  if (typeof value === 'string') return ids.get(value) ?? value;
  if (Array.isArray(value)) return value.map(remap);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, item]) => [ids.get(key) ?? key, remap(item)]));
  return value;
}
const expected = remap(draft);
expected.revision = installed.revision;
for (const [id, snapshot] of Object.entries(expected.snapshots)) {
  assert.ok(installed.snapshots[id], `Missing snapshot ${id}`);
  snapshot.savedAt = installed.snapshots[id].savedAt;
}
assert.deepEqual(expected, installed, 'Installed graph/content/style differs beyond fresh IDs, revision and snapshot times.');
const mappedAliases = remap(aliases);
await writeFile(path.join(directory, 'installed.aliases.json'), JSON.stringify(mappedAliases, null, 2) + '\n');
await writeFile(path.join(directory, 'installed-readback.json'), JSON.stringify(readback, null, 2) + '\n');
const proof = {
  projectId: installed.id, title: installed.title, revision: installed.revision,
  url: `http://127.0.0.1:5240/?project=${installed.id}`,
  works: installed.canvases.length, nodes: installed.nodes.length, links: installed.links.length, assets: installed.assets.length,
  passed: true, savedReadbackExact: true, independentIdentities: true, authoredGraphPreserved: true,
  draftSha256: createHash('sha256').update(JSON.stringify(draft)).digest('hex'),
  installedSha256: createHash('sha256').update(JSON.stringify(installed)).digest('hex'),
};
await writeFile(path.join(directory, 'installation-proof.json'), JSON.stringify(proof, null, 2) + '\n');
console.log(JSON.stringify(proof));
