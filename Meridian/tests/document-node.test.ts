import test from 'node:test';
import assert from 'node:assert/strict';
import { addNode, commitSnapshots, newProject } from '../src/core/model.ts';
import { documentNodeAsset, documentNodeExpanded, rememberDocumentNodeSize, revealDocumentNode } from '../src/core/document-node.ts';
import { containingFixedInterior } from '../src/core/sizing.ts';
import { navigationIndex } from '../src/core/navigation.ts';
import { revealChildren } from '../src/core/reveal.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { createPublicationPayload } from '../src/publication.ts';
import type { MeridianNode, MeridianProject } from '../src/core/types.ts';

function fixture(kind: 'document' | 'media' | 'text' = 'document') {
  const project = newProject('Original attached document');
  const asset = { id: 'original-document', name: 'Original.md', mime: 'application/octet-stream', base64: Buffer.from('# Original title\n\nThe complete original passage.').toString('base64') };
  project.assets.push(asset);
  const node = addNode(project, project.entryCanvasId, kind, 'Document cover');
  Object.assign(node, { x: 100, y: 120, width: 260, height: 160, sizeMode: 'fixed', contentType: 'asset', assetId: asset.id, attachments: [asset.id], text: 'Retained caption', style: { shape: 'rectangle', titleSize: 18, fontSize: 14 } });
  delete node.collapsed; delete node.containerVariant; delete node.childArrangement;
  const unrelated = addNode(project, project.entryCanvasId, 'media', 'Unrelated content');
  Object.assign(unrelated, { x: 10000, y: 10000, width: 200, height: 120, sizeMode: 'fixed', text: 'Keep my original frame.' });
  return { project, node, asset, unrelated };
}
const get = (project: MeridianProject, id: string) => project.nodes.find(node => node.id === id)!;
const frame = (node: MeridianNode) => ({ width: node.width, height: node.height, sizeMode: node.sizeMode });

test('legacy attachment nodes stay compact without creating frame records while inspecting them', () => {
  for (const kind of ['document', 'media', 'text'] as const) {
    const { project, node, asset } = fixture(kind), before = structuredClone(project);
    assert.equal(documentNodeAsset(project, node), asset); assert.equal(documentNodeExpanded(node), false);
    rememberDocumentNodeSize(project, node); assert.deepEqual(project, before); assert.equal(node.containerSizes, undefined);
    node.collapsed = false; assert.equal(documentNodeExpanded(node), false);
    node.containerSizes = { expanded: { width: 600, height: 500, sizeMode: 'fixed' } };
    assert.equal(documentNodeExpanded(node), true); node.collapsed = true; assert.equal(documentNodeExpanded(node), false);
  }
});

test('document eligibility follows the active original asset and ignores unsupported node roles or inactive content', () => {
  const { project, node, asset } = fixture();
  for (const [name, mime] of [['table.csv', 'text/csv'], ['guide.md', 'application/octet-stream'], ['notes.txt', 'text/plain'], ['reader.pdf', 'application/pdf']] as const) {
    Object.assign(asset, { name, mime }); assert.equal(documentNodeAsset(project, node), asset);
  }
  asset.name = 'photo.png'; asset.mime = 'image/png'; assert.equal(documentNodeAsset(project, node), undefined);
  asset.name = 'notes.txt'; asset.mime = 'text/plain'; node.contentType = 'text'; assert.equal(documentNodeAsset(project, node), undefined);
  node.contentType = 'asset';
  for (const kind of ['container', 'reference', 'canvas-link', 'input'] as const) assert.equal(documentNodeAsset(project, { ...node, kind }), undefined);
});

test('opening a document clones the project and grows only its original node using existing mode records', () => {
  const { project, node, asset, unrelated } = fixture(), before = structuredClone(project), opened = revealDocumentNode(project, node.id, true), result = get(opened, node.id);
  assert.deepEqual(project, before); assert.notEqual(opened, project); assert.equal(result.id, node.id); assert.equal(result.assetId, asset.id);
  assert.equal(documentNodeExpanded(result), true); assert.equal(result.collapsed, false); assert(result.width > node.width && result.height > node.height);
  assert.deepEqual(result.containerSizes?.collapsed, frame(node)); assert.deepEqual(result.containerSizes?.expanded, frame(result));
  assert.deepEqual(get(opened, unrelated.id), unrelated); assert.deepEqual(opened.nodes.filter(n => n.id !== node.id), project.nodes.filter(n => n.id !== node.id));
  assert.deepEqual(opened.assets, project.assets); assert.deepEqual(opened.links, project.links); assert.deepEqual(opened.snapshots, project.snapshots);
  const unchanged = { ...result }; for (const key of ['width', 'height', 'sizeMode', 'collapsed', 'containerSizes'] as const) delete (unchanged as Partial<MeridianNode>)[key];
  const original = { ...node }; for (const key of ['width', 'height', 'sizeMode', 'collapsed', 'containerSizes'] as const) delete (original as Partial<MeridianNode>)[key];
  assert.deepEqual(unchanged, original); assert.deepEqual(validateProject(opened), []);
});

test('Read/Collapse cycles restore separately resized open and closed document frames exactly', () => {
  const f = fixture(); let current = revealDocumentNode(f.project, f.node.id, true), node = get(current, f.node.id);
  Object.assign(node, { width: 710, height: 590, sizeMode: 'fixed' }); rememberDocumentNodeSize(current, node);
  current = revealDocumentNode(current, node.id, false); node = get(current, node.id); assert.deepEqual(frame(node), frame(f.node));
  Object.assign(node, { width: 320, height: 190, sizeMode: 'fixed' }); rememberDocumentNodeSize(current, node);
  for (let i = 0; i < 5; i++) {
    current = revealDocumentNode(current, node.id, true); node = get(current, node.id); assert.deepEqual(frame(node), { width: 710, height: 590, sizeMode: 'fixed' });
    current = revealDocumentNode(current, node.id, false); node = get(current, node.id); assert.deepEqual(frame(node), { width: 320, height: 190, sizeMode: 'fixed' });
  }
  assert.deepEqual(current.assets, f.project.assets); assert.deepEqual(get(current, f.unrelated.id), f.unrelated); assert.deepEqual(validateProject(current), []);
});

test('redundant read and collapse requests do not change identities, asset bytes or saved mode frames', () => {
  const { project, node } = fixture(); const opened = revealDocumentNode(project, node.id, true), again = revealDocumentNode(opened, node.id, true);
  assert.deepEqual(again, opened); assert.notEqual(again, opened);
  const closed = revealDocumentNode(opened, node.id, false); assert.deepEqual(revealDocumentNode(closed, node.id, false), closed);
});

test('a document inside an expanded fixed parent remains within its parent and leaves unrelated objects intact', () => {
  const { project, node, unrelated } = fixture(), parent = addNode(project, project.entryCanvasId, 'container', 'Bounded owner');
  Object.assign(parent, { x: 60, y: 70, width: 620, height: 430, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free', style: { childPadding: 10 } });
  node.parentId = parent.id; const before = structuredClone(project), opened = revealDocumentNode(project, node.id, true), result = get(opened, node.id), interior = containingFixedInterior(opened, result)!;
  assert(result.x >= interior.x && result.y >= interior.y); assert(result.x + result.width <= interior.x + interior.width + 1e-8); assert(result.y + result.height <= interior.y + interior.height + 1e-8);
  assert.deepEqual(project, before); assert.deepEqual(get(opened, unrelated.id), unrelated); assert.deepEqual(get(opened, parent.id), parent);
  const closed = revealDocumentNode(opened, node.id, false); assert.deepEqual(frame(get(closed, node.id)), frame(node));
});

test('allocated frame refreshes cannot overwrite already remembered open and closed document sizes', () => {
  const { project, node } = fixture(), parent = addNode(project, project.entryCanvasId, 'container', 'Allocated owner');
  Object.assign(parent, { x: 50, y: 60, width: 700, height: 500, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free' });
  node.parentId = parent.id; node.collapsed = false; node.containerSizes = { expanded: { width: 700, height: 600, sizeMode: 'fixed' }, collapsed: { width: 260, height: 160, sizeMode: 'fixed' } };
  const expected = structuredClone(node.containerSizes); Object.assign(node, { width: 350, height: 200 }); rememberDocumentNodeSize(project, node); assert.deepEqual(node.containerSizes, expected);
  node.collapsed = true; Object.assign(node, { width: 190, height: 120 }); rememberDocumentNodeSize(project, node); assert.deepEqual(node.containerSizes, expected);
});

test('switching modes inside a fixed parent preserves the authored frame records rather than its allocation', () => {
  const { project, node } = fixture(), parent = addNode(project, project.entryCanvasId, 'container', 'Allocation is not an authored reading size');
  Object.assign(parent, { x: 50, y: 60, width: 700, height: 500, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free' });
  node.parentId = parent.id; node.collapsed = false; Object.assign(node, { width: 350, height: 200 });
  node.containerSizes = { expanded: { width: 700, height: 600, sizeMode: 'fixed' }, collapsed: { width: 260, height: 160, sizeMode: 'fixed' } };
  const expected = structuredClone(node.containerSizes), closed = revealDocumentNode(project, node.id, false);
  assert.deepEqual(get(closed, node.id).containerSizes, expected);
  const reopened = revealDocumentNode(closed, node.id, true); assert.deepEqual(get(reopened, node.id).containerSizes, expected);
});

test('an explicit authored resize updates only the active document mode even inside a fixed parent', () => {
  const { project, node } = fixture(), parent = addNode(project, project.entryCanvasId, 'container', 'Author owns both frames');
  Object.assign(parent, { x: 50, y: 60, width: 900, height: 700, sizeMode: 'fixed', collapsed: false, containerVariant: 'contained', childLayout: 'contained', childArrangement: 'free' });
  node.parentId = parent.id; node.collapsed = false; node.containerSizes = { expanded: { width: 700, height: 600, sizeMode: 'fixed' }, collapsed: { width: 260, height: 160, sizeMode: 'fixed' } };
  Object.assign(node, { width: 550, height: 420 }); rememberDocumentNodeSize(project, node, { authored: true });
  assert.deepEqual(node.containerSizes.expanded, { width: 550, height: 420, sizeMode: 'fixed' }); assert.deepEqual(node.containerSizes.collapsed, { width: 260, height: 160, sizeMode: 'fixed' });
  let current = revealDocumentNode(project, node.id, false), closed = get(current, node.id); Object.assign(closed, { width: 290, height: 180 }); rememberDocumentNodeSize(current, closed, { authored: true });
  current = revealDocumentNode(current, node.id, true); assert.deepEqual(frame(get(current, node.id)), { width: 550, height: 420, sizeMode: 'fixed' });
  current = revealDocumentNode(current, node.id, false); assert.deepEqual(frame(get(current, node.id)), { width: 290, height: 180, sizeMode: 'fixed' });
});

test('tiny and enormous existing document frames retain positive finite geometry and exact closed dimensions', () => {
  for (const [width, height] of [[.25, .125], [54000, 28000], [600000000, 400000000]]) {
    const { project, node } = fixture(); Object.assign(node, { width, height });
    const opened = revealDocumentNode(project, node.id, true), result = get(opened, node.id);
    assert(Number.isFinite(result.width) && Number.isFinite(result.height) && result.width > 0 && result.height > 0 && result.width <= 1e9 && result.height <= 1e9);
    const closed = get(revealDocumentNode(opened, node.id, false), node.id); assert.equal(closed.width, width); assert.equal(closed.height, height);
  }
});

test('opening a document with children discloses its real branch without manufacturing replacement nodes', () => {
  const { project, node, unrelated } = fixture(), child = addNode(project, project.entryCanvasId, 'media', 'Original child');
  Object.assign(child, { parentId: node.id, x: 1200, y: 1300, width: 180, height: 120, sizeMode: 'fixed', childPosition: 'manual', placement: 'free' });
  const opened = revealDocumentNode(project, node.id, true); assert.equal(navigationIndex(opened).isNodeVisible(child.id), true); assert.equal(opened.nodes.length, project.nodes.length);
  assert.deepEqual(get(opened, child.id), child); assert.deepEqual(get(opened, unrelated.id), unrelated);
  const closed = revealDocumentNode(opened, node.id, false); assert.equal(navigationIndex(closed).isNodeVisible(child.id), false); assert.deepEqual(get(closed, child.id), child);
});

test('publication and JSON roundtrip preserve original IDs, original attachment bytes and version-one frame fields', () => {
  const f = fixture(), opened = revealDocumentNode(f.project, f.node.id, true), saved = commitSnapshots(opened), payload = createPublicationPayload(saved), portable = JSON.parse(JSON.stringify(payload.project)) as MeridianProject;
  assert.equal(portable.schemaVersion, 1); assert.equal(isProjectShape(portable), true); assert.deepEqual(validateProject(portable), []);
  assert.deepEqual(get(portable, f.node.id).containerSizes, get(opened, f.node.id).containerSizes); assert.equal(documentNodeExpanded(get(portable, f.node.id)), true);
  assert.deepEqual(portable.assets.find(asset => asset.id === f.asset.id), f.asset); assert.equal(get(portable, f.node.id).assetId, f.asset.id);
  assert.deepEqual(saved.snapshots[f.node.id]!.node.containerSizes, get(opened, f.node.id).containerSizes);
  assert.deepEqual(get(revealDocumentNode(portable, f.node.id, false), f.node.id).containerSizes?.collapsed, frame(f.node));
});

test('non-document disclosure retains the existing branch behavior', () => {
  const { project, node } = fixture(); node.contentType = 'text'; node.text = 'Ordinary node content';
  assert.deepEqual(revealDocumentNode(project, node.id, true), revealChildren(project, node.id, true));
});
