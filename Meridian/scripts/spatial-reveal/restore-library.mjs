/** Restore only the audited accidental disclosure, with a revision-checked save. */
import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
const origin = 'http://127.0.0.1:5240', folder = 'output/spatial-qa/library-recovery';
const get = async () => { const response = await fetch(`${origin}/api/library`); assert.ok(response.ok); return response.json(); };
const canonical = value => Array.isArray(value) ? value.map(canonical) : value && typeof value === 'object' ? Object.fromEntries(Object.keys(value).sort().map(key => [key, canonical(value[key])])) : value;
const before = await get();
assert.equal(before.revision, 9, 'The library changed after the audit. Do not roll it back.');
const previousText = await readFile(path.join(process.env.LOCALAPPDATA, 'AtlasMeridian', 'library.json.bak'), 'utf8');
const previous = JSON.parse(previousText);
assert.equal(previous.revision, 8);
const candidate = structuredClone(before);
const entry = candidate.entries.find(item => item.id === 'a3d00829-1577-4dec-8591-24495b6c885d');
assert.ok(entry, 'Audited library section is missing.');
assert.equal(entry.collapsed, false);
entry.collapsed = true;
assert.deepEqual(canonical({ ...candidate, revision: 8 }), canonical(previous), 'There are additional changes; preserve them for review.');
await mkdir(folder, { recursive: true });
await writeFile(`${folder}/before.json`, JSON.stringify(before, null, 2));
await writeFile(`${folder}/previous-backup.json`, previousText);
await writeFile(`${folder}/candidate.json`, JSON.stringify(candidate, null, 2));
if (!process.argv.includes('--commit')) { console.log('Verified: only My canvases disclosure differs from the previous library. No writes made.'); } else {
const response = await fetch(`${origin}/api/library`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ library: candidate, expectedRevision: before.revision }) });
assert.ok(response.ok, `Library restore rejected: ${response.status} ${await response.clone().text()}`);
const saved = await response.json();
assert.equal(saved.revision, 10);
assert.deepEqual(canonical({ ...saved, revision: 9 }), canonical(candidate));
assert.deepEqual(canonical(await get()), canonical(saved));
await writeFile(`${folder}/restored.json`, JSON.stringify(saved, null, 2));
await writeFile(`${folder}/receipt.json`, JSON.stringify({ restoredAt: new Date().toISOString(), previousRevision: 9, revision: saved.revision, entryId: entry.id, changedField: 'collapsed', value: true, preservedEntries: saved.entries.length }, null, 2));
console.log(`Restored My canvases to collapsed. Revision ${saved.revision}; ${saved.entries.length} entries preserved.`);

}
