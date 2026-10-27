import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, forkProject, newProject } from '../src/core/model.ts';
import { ensureNodeTags, isNodeTag, nodeTag, NODE_TAG_MAX_LENGTH } from '../src/core/node-tag.ts';
import { createNodeFromSpec, creationTypes, type NodeCreationSpec } from '../src/core/creation.ts';
import { activeNodeContent, hidesOwnContent } from '../src/core/content.ts';
import { isProjectShape, validateProject } from '../src/core/rules.ts';
import { updateNodeType } from '../src/core/node-editing.ts';
import { createProjectSearch } from '../src/core/navigation.ts';
import { isEnclosingContainer, isOrderedContainer } from '../src/core/cells.ts';
import { RULE_VOCABULARY } from '../src/core/rule-impact.ts';
import { copyNodes, pasteNodes } from '../src/core/clipboard.ts';
import { createModuleFromSelection, instantiateModule } from '../src/core/modules.ts';
import { createPublicationPayload, buildPublication, readPublication } from '../src/publication.ts';
import { applyRecipe } from '../src/core/authoring.ts';

function fixture() {
  const project = newProject('Identity and backdrop');
  const spec: NodeCreationSpec = { canvasId: project.entryCanvasId, parentId: project.canvases[0].rootId, type: 'media', tag: 'A stable identity', title: '', x: 100, y: 100, width: 220, height: 140 };
  return { project, spec };
}

test('legacy node and saved-observation tags migrate once without changing content or the source graph', () => {
  const { project } = fixture(), media = addNode(project, project.entryCanvasId, 'media', 'Original title');
  delete media.tag;
  project.snapshots.external = { node: { ...structuredClone(media), id: 'external', canvasId: 'remote-canvas', parentId: 'remote-root', title: '' }, value: null, savedAt: '2026-10-16T12:00:00Z' };
  const before = structuredClone(project), migrated = ensureNodeTags(project);
  assert.notEqual(migrated, project); assert.deepEqual(project, before);
  const copy = migrated.nodes.find(node => node.id === media.id)!;
  assert.equal(copy.tag, 'Original title'); assert.equal(copy.title, 'Original title');
  assert.equal(migrated.snapshots.external.node.tag, 'Media external');
  copy.title = ''; assert.equal(nodeTag(copy), 'Original title');
  assert.equal(ensureNodeTags(migrated), migrated, 'Complete migrations retain object identity.');
});

test('tags are independent inert metadata, including empty-title nodes, snapshots and independent forks', () => {
  const { project } = fixture(), media = addNode(project, project.entryCanvasId, 'media', 'Visible title');
  media.tag = 'Official handle'; media.text = 'Visible passage';
  media.title = '';
  const saved = commitSnapshots(project), fork = forkProject(saved);
  assert.equal(saved.snapshots[media.id].node.tag, 'Official handle');
  const copy = fork.nodes.find(node => node.kind === 'media')!;
  assert.notEqual(copy.id, media.id); assert.equal(copy.tag, 'Official handle'); assert.equal(copy.title, '');
  assert.equal(nodeTag({ ...media, tag: '   ', title: '  Legacy fallback  ' }), 'Legacy fallback');
  assert.match(nodeTag({ ...media, tag: '', title: '' }), /^Media /);
  assert.deepEqual(validateProject(saved), []);
});

test('tag schema validation rejects malformed live and snapshot values while preserving legacy files', () => {
  const { project } = fixture(), media = addNode(project, project.entryCanvasId, 'media', '');
  delete media.tag; assert.ok(isProjectShape(project));
  const saved = commitSnapshots(project);
  assert.equal(isNodeTag('x'.repeat(NODE_TAG_MAX_LENGTH)), true);
  assert.equal(isNodeTag('x'.repeat(NODE_TAG_MAX_LENGTH + 1)), false);
  for (const invalid of [null, false, 17, {}, [], 'x'.repeat(NODE_TAG_MAX_LENGTH + 1)]) for (const pinned of [false, true]) {
    const candidate = structuredClone(saved), target = pinned ? candidate.snapshots[media.id].node : candidate.nodes.find(node => node.id === media.id)!;
    (target as unknown as Record<string, unknown>).tag = invalid;
    assert.equal(isProjectShape(candidate), false);
    assert.ok(validateProject(candidate).length);
  }
});

test('new media and container nodes accept a tag without requiring a visible title or filler text', () => {
  for (const type of ['media', 'container'] as const) {
    const { project, spec } = fixture(), result = createNodeFromSpec(project, { ...spec, type });
    const node = result.project.nodes.find(node => node.id === result.nodeId)!;
    assert.equal(node.tag, 'A stable identity'); assert.equal(node.title, '');
    assert.equal(activeNodeContent(node).text ?? '', ''); assert.deepEqual(validateProject(result.project), []);
  }
  const { project, spec } = fixture();
  assert.throws(() => createNodeFromSpec(project, { ...spec, tag: 17 as unknown as string }), /tag.*text/i);
  assert.throws(() => createNodeFromSpec(project, { ...spec, tag: 'x'.repeat(8001) }), /8000/);
  assert.equal(addNode(project, project.entryCanvasId, 'block').title, '');
});

test('retired Block creation is rejected while blank Media retains every exterior display pattern', () => {
  const { project, spec } = fixture(), before = structuredClone(project);
  for (const bad of [{ title: 'Visible' }, { text: 'Visible' }, { asset: { id: 'asset', name: 'A', mime: 'image/png', base64: 'AQID' } }, { hyperlink: { url: 'https://example.com/' } }, { contentType: 'asset' as const }])
    assert.throws(() => createNodeFromSpec(project, { ...spec, type: 'block', ...bad }), /supported node type/i);
  assert.deepEqual(project, before);
  for (const variant of ['contained', 'horizontal', 'vertical', 'grid', 'spreadsheet', 'gallery', 'window'] as const) {
    const result = createNodeFromSpec(project, { ...spec, type: 'media', sizeMode: 'fixed', variant, ...(variant === 'window' ? { grid: { columns: 5, rows: 5, cellWidth: 100, cellHeight: 80, windowColumns: 3, windowRows: 2 } } : {}) });
    const block = result.project.nodes.find(node => node.id === result.nodeId)!;
    assert.equal(block.kind, 'media'); assert.equal(block.sizeMode, 'fixed'); assert.equal(block.childLayout, 'spatial');
    assert.equal(isEnclosingContainer(block), false); assert.equal(block.containerVariant, variant);
    assert.equal(isOrderedContainer(block), ['horizontal', 'vertical', 'grid', 'spreadsheet', 'window'].includes(variant));
    assert.equal(activeNodeContent(block).text, '');
  }
});

test('Block type changes preserve dormant media, tag and hierarchy and restore content when changed back', () => {
  const { project } = fixture(), media = addNode(project, project.entryCanvasId, 'media', 'A visual caption');
  media.tag = 'The official handle'; media.text = 'Recoverable passage'; media.contentType = 'asset'; media.assetId = 'picture';
  media.hyperlinks = [{ url: 'https://example.com/', label: 'Source' }];
  project.assets.push({ id: 'picture', name: 'Image', mime: 'image/png', base64: 'AQID' });
  const child = addNode(project, project.entryCanvasId, 'media', 'Child'); child.parentId = media.id;
  const changed = updateNodeType(project, media.id, { kind: 'block' }), block = changed.nodes.find(node => node.id === media.id)!;
  assert.equal(block.tag, media.tag); assert.equal(block.title, media.title); assert.equal(block.text, media.text); assert.equal(block.assetId, media.assetId);
  assert.deepEqual(activeNodeContent(block), { type: 'text', text: '' });
  assert.equal(changed.nodes.find(node => node.id === child.id)?.parentId, media.id);
  const restored = updateNodeType(changed, media.id, { kind: 'media' }).nodes.find(node => node.id === media.id)!;
  assert.equal(restored.tag, media.tag); assert.deepEqual(activeNodeContent(restored), { type: 'asset', assetId: 'picture' });
});

test('search names and result labels use the tag while visible title and body remain independently searchable', () => {
  const { project } = fixture(), node = addNode(project, project.entryCanvasId, 'media', 'Visible heading');
  node.tag = 'Official handle'; node.text = 'A unique passage';
  const search = createProjectSearch(project);
  assert.equal(search('Official handle')[0]?.nodeId, node.id); assert.equal(search('Official handle')[0]?.source, 'Tag');
  assert.equal(search('Visible heading')[0]?.title, 'Official handle'); assert.equal(search('unique passage')[0]?.title, 'Official handle');
  node.title = ''; assert.equal(createProjectSearch(project)('Official handle')[0]?.title, 'Official handle');
});

test('Media rules and declarative recipes retain independent tags and blank titles after Block is retired', () => {
  const { project, spec } = fixture(), root = project.nodes[0];
  assert.ok(!RULE_VOCABULARY.nodeKinds.includes('block'));
  root.rules = { nodeKinds: ['media'] };
  assert.deepEqual(creationTypes(project, root.id).map(type => type.value), ['media']);
  assert.deepEqual(validateProject(createNodeFromSpec(project, { ...spec, type: 'media' }).project), []);
  const result = applyRecipe(project, { version: 1, nodes: [{ key: 'blank-media', kind: 'media', canvas: project.entryCanvasId, tag: 'Recipe backdrop', title: '' }] });
  const block = result.project.nodes.find(node => node.kind === 'media')!;
  assert.equal(block.tag, 'Recipe backdrop'); assert.equal(block.title, ''); assert.equal(block.childLayout, 'spatial');
  assert.throws(() => applyRecipe(project, { version: 1, nodes: [{ key: 'bad', kind: 'media', canvas: project.entryCanvasId, tag: 123 as unknown as string }] }), /8000/);
});

test('publication, clipboard and Module insertion preserve tags and contentless Blocks with fresh identities', () => {
  const { project } = fixture(), block = addNode(project, project.entryCanvasId, 'block'); block.tag = '<handle> & metadata';
  const child = addNode(project, project.entryCanvasId, 'media', ''); child.tag = 'Child handle'; child.parentId = block.id;
  const other = addCanvas(project, 'Saved reference'), reference = addNode(project, other.id, 'reference'); reference.targetId = block.id;
  const saved = commitSnapshots(project), payload = createPublicationPayload(saved);
  assert.equal(payload.project.nodes.find(node => node.id === block.id)?.tag, block.tag);
  assert.equal(payload.project.snapshots[block.id].node.tag, block.tag);
  const reopened = readPublication(buildPublication(saved, '', ''));
  assert.equal(reopened.nodes.find(node => node.tag === block.tag)?.kind, 'block');
  assert.equal(reopened.nodes.find(node => node.tag === block.tag)?.title, '');
  const pasted = pasteNodes(saved, copyNodes(saved, [block.id]), saved.entryCanvasId);
  const newBlock = pasted.project.nodes.find(node => node.id === pasted.idMap[block.id])!;
  assert.notEqual(newBlock.id, block.id); assert.equal(newBlock.tag, block.tag); assert.equal(newBlock.title, '');
  const module = createModuleFromSelection(saved, [block.id, child.id], 'Reusable backdrops');
  const destination = newProject(), inserted = instantiateModule(destination, module, { canvasId: destination.entryCanvasId, x: 600, y: 600 });
  const moduleBlock = inserted.project.nodes.find(node => node.kind === 'block')!;
  assert.notEqual(moduleBlock.id, block.id); assert.equal(moduleBlock.tag, block.tag); assert.equal(moduleBlock.title, '');
  assert.equal(inserted.project.nodes.find(node => node.tag === 'Child handle')?.parentId, moduleBlock.id);
});
