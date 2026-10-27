import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, forkProject } from '../src/core/model.ts';
import {
  emptyLibrary, libraryDocumentFromProject, libraryScene, normalizeLibraryDocument, reconcileLibraryCanvas, validateLibraryCanvasDocument,
  type CanvasLibrary, type LibraryCatalogItem,
} from '../src/core/library.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { validateProject } from '../src/core/rules.ts';
import { resolveSnapping } from '../src/core/snapping.ts';

const catalog = (): LibraryCatalogItem[] => [{ id: 'greek-atlas', title: 'Greek mythology', revision: 4, updatedAt: '2026-10-07T20:00:00Z', entryCanvasId: 'genealogy',
  canvases: ['genealogy', 'epics', 'art'].map(id => ({ id, rootId: `root-${id}`, title: id, nodeCount: 20 })),
  links: [{ sourceCanvasId: 'genealogy', targetCanvasId: 'epics' }, { sourceCanvasId: 'epics', targetCanvasId: 'art' }],
}];
const legacy = (): CanvasLibrary => ({ schemaVersion: 1, revision: 12, entries: [
  { id: 'mythology', kind: 'section', title: 'Mythology', parentId: null, collapsed: false, x: -1200, y: 800, placement: 'free' },
  { id: 'greek', kind: 'canvas', title: 'My own atlas name', parentId: 'mythology', collapsed: true, x: 90, y: 80, projectId: 'greek-atlas' },
  { id: 'epic-launcher', kind: 'canvas', title: 'Read the epics', parentId: 'greek', collapsed: false, x: 770, y: -330, projectId: 'greek-atlas', canvasId: 'epics' },
] });

test('new and migrated home Works default to sticky edges and smart alignment while explicit preferences survive', () => {
  for (const project of [libraryScene(emptyLibrary(), []), normalizeLibraryDocument(emptyLibrary()).project, normalizeLibraryDocument(legacy(), catalog()).project]) {
    assert.equal(resolveSnapping(project.canvases[0]).stickyEdges, true);
    assert.equal(resolveSnapping(project.canvases[0]).smartAlign, true);
  }
  const saved = normalizeLibraryDocument(emptyLibrary());
  saved.project.canvases[0].snapping = { stickyEdges: false, snapToGrid: true, smartAlign: false };
  assert.deepEqual(normalizeLibraryDocument(saved).project.canvases[0].snapping, saved.project.canvases[0].snapping);
});

test('legacy library migrates to an ordinary graph without replacing titles, identifiers, positions or disclosure', () => {
  const source = legacy(), before = structuredClone(source), document = normalizeLibraryDocument(source, catalog());
  assert.deepEqual(source, before);
  assert.equal(document.schemaVersion, 2); assert.equal(document.revision, 12); assert.equal(document.project.revision, 12);
  assert.deepEqual(validateProject(document.project), []);
  for (const entry of source.entries) {
    const node = document.project.nodes.find(node => node.id === entry.id)!;
    assert.equal(node.kind, entry.kind === 'section' ? 'container' : 'canvas-link');
    assert.equal(node.title, entry.title); assert.equal(node.x, entry.x); assert.equal(node.y, entry.y);
    assert.equal(node.parentId, entry.parentId ?? document.project.canvases[0].rootId); assert.equal(node.collapsed, entry.collapsed);
  }
  assert.deepEqual(document.canvasTargets, { greek: { projectId: 'greek-atlas', canvasId: 'genealogy' }, 'epic-launcher': { projectId: 'greek-atlas', canvasId: 'epics' } });
  assert.deepEqual(document.project.nodes.find(node => node.id === 'greek')!.canvasTarget, document.canvasTargets.greek);
  assert.deepEqual(document.project.links.map(link => [link.sourceId, link.targetId, link.parentId]), [['mythology', 'greek', 'mythology'], ['greek', 'epic-launcher', 'greek']]);
  assert.equal(navigationIndex(document.project).visibleNode('epic-launcher')?.id, 'greek');
  assert.deepEqual(normalizeLibraryDocument(document), document);
});

test('external Canvas destinations are ordinary node content that survives copies and older version-two map migration', () => {
  const document = normalizeLibraryDocument(legacy(), catalog()), fork = forkProject(document.project);
  assert.deepEqual(fork.nodes.find(node => node.title === 'My own atlas name')!.canvasTarget, { projectId: 'greek-atlas', canvasId: 'genealogy' });
  const old = structuredClone(document);
  for (const node of old.project.nodes) delete node.canvasTarget;
  const normalized = normalizeLibraryDocument(old);
  assert.deepEqual(normalized.project.nodes.find(node => node.id === 'greek')!.canvasTarget, document.canvasTargets.greek);
  const draft = structuredClone(document.project), launcher = draft.nodes.find(node => node.id === 'greek')!;
  delete launcher.canvasTarget; launcher.targetId = draft.canvases[0].rootId;
  const converted = libraryDocumentFromProject(document, draft);
  assert.equal(converted.canvasTargets.greek, undefined);
  assert.ok(converted.discoveredTargets.includes('greek-atlas:genealogy'));
});

test('home supports normal arbitrary nodes, attachments, owned links, shapes and additional canvases', () => {
  const document = normalizeLibraryDocument(emptyLibrary()), project = structuredClone(document.project), canvasId = project.entryCanvasId;
  const group = addNode(project, canvasId, 'container', 'Research'); group.childLayout = 'spatial';
  const note = addNode(project, canvasId, 'text', 'Floating note'); Object.assign(note, { parentId: group.id, text: 'Authored content', style: { variant: 'label', shape: 'none' } });
  const image = addNode(project, canvasId, 'image', 'Diagram'); image.assetId = 'image-asset';
  project.assets.push({ id: 'image-asset', name: 'diagram.png', mime: 'image/png', base64: 'aW1hZ2U=' });
  const internalCanvas = addCanvas(project, 'New research canvas'), launcher = addNode(project, canvasId, 'canvas-link', 'Research canvas'); launcher.targetId = internalCanvas.rootId;
  project.links.push({ id: 'authored-link', canvasId, sourceId: group.id, targetId: image.id, parentId: group.id, kind: 'visual', relation: 'causal', direction: 'forward', label: 'illustrated by' });
  const saved = libraryDocumentFromProject(document, project);
  assert.deepEqual(saved.project, project); assert.deepEqual(saved.canvasTargets, {});
  assert.deepEqual(normalizeLibraryDocument(saved), saved);
  assert.deepEqual(reconcileLibraryCanvas(saved, []).project, project);
});

test('catalog discovery follows canvas hierarchy, preserves authored data, and does not invent a section category', () => {
  const source = normalizeLibraryDocument(emptyLibrary()), before = structuredClone(source), items = catalog(), discovered = reconcileLibraryCanvas(source, items);
  assert.deepEqual(source, before);
  const targets = new Map(Object.entries(discovered.canvasTargets).map(([id, target]) => [target.canvasId, discovered.project.nodes.find(node => node.id === id)!]));
  assert.equal(targets.size, 3); assert.ok([...targets.values()].every(node => node.kind === 'canvas-link'));
  assert.equal(targets.get('genealogy')!.parentId, discovered.project.canvases[0].rootId);
  assert.equal(targets.get('epics')!.parentId, targets.get('genealogy')!.id); assert.equal(targets.get('art')!.parentId, targets.get('epics')!.id);
  targets.get('epics')!.title = 'Personal title'; targets.get('epics')!.x = -4400; targets.get('epics')!.collapsed = false;
  const originalNodes = structuredClone(discovered.project.nodes);
  assert.deepEqual(reconcileLibraryCanvas(discovered, [{ ...items[0], title: 'Externally renamed' }]).project.nodes, originalNodes);
  assert.deepEqual(reconcileLibraryCanvas(discovered, items), discovered);
});

test('deleted launchers stay deleted while newly discovered canvases are added', () => {
  const source = reconcileLibraryCanvas(normalizeLibraryDocument(emptyLibrary()), catalog()), draft = structuredClone(source.project);
  const removed = Object.entries(source.canvasTargets).find(([, target]) => target.canvasId === 'art')![0];
  draft.nodes = draft.nodes.filter(node => node.id !== removed);
  const saved = libraryDocumentFromProject(source, draft), items = catalog();
  items[0].canvases.push({ id: 'new-chapter', rootId: 'new-root', title: 'New chapter', nodeCount: 9 });
  items[0].links.push({ sourceCanvasId: 'epics', targetCanvasId: 'new-chapter' });
  const refreshed = reconcileLibraryCanvas(saved, items);
  assert.ok(!refreshed.project.nodes.some(node => node.id === removed)); assert.ok(!Object.values(refreshed.canvasTargets).some(target => target.canvasId === 'art'));
  const addedId = Object.entries(refreshed.canvasTargets).find(([, target]) => target.canvasId === 'new-chapter')![0];
  const epicsId = Object.entries(refreshed.canvasTargets).find(([, target]) => target.canvasId === 'epics')![0];
  assert.equal(refreshed.project.nodes.find(node => node.id === addedId)!.parentId, epicsId);
  assert.deepEqual(reconcileLibraryCanvas(refreshed, items), refreshed);
});

test('migration avoids reserved root identity collisions and retains unavailable references', () => {
  const source = legacy(); source.entries[0].id = 'c9f781c1-124a-433e-a1d6-37dd1d8a7582'; source.entries[1].parentId = source.entries[0].id;
  const document = normalizeLibraryDocument(source);
  assert.notEqual(document.project.canvases[0].rootId, source.entries[0].id);
  assert.ok(document.project.nodes.some(node => node.id === source.entries[0].id));
  assert.deepEqual(document.canvasTargets.greek, { projectId: 'greek-atlas' });
  assert.deepEqual(validateProject(document.project), []);
  const collisions = legacy();
  for (const id of ['c9f781c1-124a-433e-a1d6-37dd1d8a7580', 'c9f781c1-124a-433e-a1d6-37dd1d8a7581', 'c9f781c1-124a-433e-a1d6-37dd1d8a7583', 'library-relationship-0'])
    collisions.entries.push({ id, kind: 'section', title: id, parentId: 'mythology', collapsed: false, x: 900, y: 800 });
  const collisionDocument = normalizeLibraryDocument(collisions);
  for (const entry of collisions.entries) assert.ok(collisionDocument.project.nodes.some(node => node.id === entry.id));
  assert.deepEqual(validateProject(collisionDocument.project), []);
});

test('corrupt project, dangling canvas metadata and inconsistent revisions are rejected instead of replaced', () => {
  const source = normalizeLibraryDocument(legacy(), catalog());
  assert.throws(() => normalizeLibraryDocument({ ...source, project: { ...source.project, revision: 0 } }), /revision/);
  assert.throws(() => normalizeLibraryDocument({ ...source, project: { ...source.project, nodes: [] } }), /invalid/);
  assert.throws(() => normalizeLibraryDocument({ ...source, canvasTargets: { missing: { projectId: 'somewhere' } } }), /existing Work/);
  assert.throws(() => validateLibraryCanvasDocument({ ...source, discoveredTargets: ['../bad:target'] }), /invalid/);
  assert.throws(() => normalizeLibraryDocument({ ...legacy(), entries: [{ ...legacy().entries[0], parentId: 'missing' }] }), /parent/);
});
