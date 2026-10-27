import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';

const folder = 'output/branch-atlas', file = path.join(process.env.LOCALAPPDATA, 'AtlasMeridian/library.json');
let baseline;
try { baseline = JSON.parse(await readFile(`${folder}/library-baseline.json`, 'utf8')); }
catch (error) { if (error.code !== 'ENOENT') throw error; baseline = JSON.parse(await readFile(file, 'utf8')); await writeFile(`${folder}/library-baseline.json`, JSON.stringify(baseline, null, 2)); }
const atlas = JSON.parse(await readFile(`${folder}/atlas.json`, 'utf8'));
const manifest = JSON.parse(await readFile(`${folder}/manifest.json`, 'utf8'));
const removed = new Set(baseline.entries.filter(e => e.projectId === atlas.id && e.canvasId !== atlas.entryCanvasId && manifest.oldCanvasBranches[e.canvasId]).map(e => e.id));
const entry = baseline.entries.find(e => e.projectId === atlas.id && e.canvasId === atlas.entryCanvasId);
assert.ok(entry, 'Existing atlas library entry must be retained');
const library = structuredClone(baseline);
library.entries = library.entries.filter(e => !removed.has(e.id));
for (const e of library.entries) {
  if (removed.has(e.parentId)) e.parentId = entry.id;
  if (e.id === entry.id) e.title = atlas.title;
}
assert.equal(removed.size, manifest.before.canvases - 1);
assert.ok(library.entries.every(e => !e.parentId || library.entries.some(p => p.id === e.parentId)));
await writeFile(`${folder}/library.json`, JSON.stringify(library, null, 2));
await writeFile(`${folder}/library-report.json`, JSON.stringify({ revision: baseline.revision, before: baseline.entries.length, after: library.entries.length, removedNavigationEntries: [...removed], retainedAtlasEntry: entry.id }, null, 2));
console.log(JSON.stringify({ before: baseline.entries.length, after: library.entries.length, removedObsoleteCanvasEntries: removed.size }));
