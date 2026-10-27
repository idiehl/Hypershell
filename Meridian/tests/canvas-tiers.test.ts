import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, newProject } from '../src/core/model.ts';
import { buildTierIndex, normalizeTierContext, parseTierContext, rememberCanvasTier } from '../src/core/tiers.ts';

test('tiers continue through library launchers without adding an artificial canvas generation', () => {
  const home = newProject('Library'), atlas = newProject('Atlas');
  const group = addNode(home, home.entryCanvasId, 'container', 'My canvases');
  const launch = addNode(home, home.entryCanvasId, 'canvas-link', 'Atlas');
  launch.parentId = group.id; launch.canvasTarget = { projectId: atlas.id, canvasId: atlas.entryCanvasId };
  const god = addNode(atlas, atlas.entryCanvasId, 'media', 'Zeus');
  const image = addNode(atlas, atlas.entryCanvasId, 'media', 'A picture'); image.parentId = god.id;
  const before = JSON.stringify([home, atlas]);
  const index = buildTierIndex([home, atlas], { home: { projectId: home.id } });
  assert.equal(index.nodeTier(home.id, home.canvases[0].rootId), 1);
  assert.equal(index.nodeTier(home.id, group.id), 2);
  assert.equal(index.nodeTier(home.id, launch.id), 3);
  assert.equal(index.nodeTier(atlas.id, atlas.canvases[0].rootId), 3);
  assert.equal(index.nodeTier(atlas.id, god.id), 4);
  assert.equal(index.nodeTier(atlas.id, image.id), 5);
  assert.equal(JSON.stringify([home, atlas]), before, 'Tier display must not edit document data.');
});

test('local root-ID and canvas-ID portals preserve tiers during direct opens and cross-canvas GUID jumps', () => {
  const project = newProject(), second = addCanvas(project, 'Second'), third = addCanvas(project, 'Third');
  const container = addNode(project, project.entryCanvasId, 'container');
  const firstPortal = addNode(project, project.entryCanvasId, 'canvas-link');
  firstPortal.parentId = container.id; firstPortal.targetId = second.rootId;
  const secondPortal = addNode(project, second.id, 'canvas-link'); secondPortal.targetId = third.id;
  const leaf = addNode(project, third.id, 'media');
  const index = buildTierIndex([project], { home: { projectId: project.id } });
  assert.equal(index.canvasTier(project.id, second.id), 3);
  assert.equal(index.canvasTier(project.id, third.id), 4);
  assert.equal(index.nodeTier(project.id, leaf.id), 5);
  const reopened = buildTierIndex([structuredClone(project)], { home: { projectId: project.id }, anchors: index.anchors() });
  assert.equal(reopened.nodeTier(project.id, leaf.id), 5);
});

test('multiple appearances choose the shallowest library route and cycles do not grow tiers on re-entry', () => {
  const home = newProject(), atlas = newProject(), next = addCanvas(atlas, 'Next');
  let parent = home.canvases[0].rootId;
  for (let count = 0; count < 4; count++) { const node = addNode(home, home.entryCanvasId, 'container'); node.parentId = parent; parent = node.id; }
  const deep = addNode(home, home.entryCanvasId, 'canvas-link'); deep.parentId = parent; deep.canvasTarget = { projectId: atlas.id };
  const shallow = addNode(home, home.entryCanvasId, 'canvas-link'); shallow.canvasTarget = { projectId: atlas.id };
  const forward = addNode(atlas, atlas.entryCanvasId, 'canvas-link'); forward.targetId = next.rootId;
  const backward = addNode(atlas, next.id, 'canvas-link'); backward.targetId = atlas.canvases[0].rootId;
  const returnHome = addNode(atlas, next.id, 'canvas-link'); returnHome.canvasTarget = { projectId: home.id };
  let anchors = [{ projectId: atlas.id, canvasId: atlas.entryCanvasId, tier: 400 }];
  for (let visit = 0; visit < 20; visit++) {
    const index = buildTierIndex([home, atlas], { home: { projectId: home.id }, anchors });
    assert.equal(index.canvasTier(home.id, home.entryCanvasId), 1);
    assert.equal(index.canvasTier(atlas.id, atlas.entryCanvasId), 2);
    assert.equal(index.canvasTier(atlas.id, next.id), 3);
    anchors = index.anchors().map(anchor => ({ ...anchor, canvasId: anchor.canvasId! }));
  }
});

test('saved navigation context preserves unlisted ancestry after reload while fresh library structure takes precedence', () => {
  const home = newProject(), unlisted = newProject(), child = addNode(unlisted, unlisted.entryCanvasId, 'media');
  const context = rememberCanvasTier({ version: 1, anchors: [] }, { projectId: unlisted.id, tier: 7 });
  const restored = parseTierContext(JSON.stringify(context));
  let index = buildTierIndex([home, unlisted], { home: { projectId: home.id }, anchors: restored.anchors });
  assert.equal(index.nodeTier(unlisted.id, child.id), 8);
  const launcher = addNode(home, home.entryCanvasId, 'canvas-link'); launcher.canvasTarget = { projectId: unlisted.id };
  index = buildTierIndex([home, unlisted], { home: { projectId: home.id }, anchors: restored.anchors });
  assert.equal(index.nodeTier(unlisted.id, child.id), 3);
  assert.equal(index.anchors().find(anchor => anchor.projectId === unlisted.id)?.tier, 2);
});

test('cycles through separately loaded external workspaces retain their original tiers across reloads', () => {
  const home = newProject(), a = newProject(), b = newProject();
  const forward = addNode(a, a.entryCanvasId, 'canvas-link'); forward.canvasTarget = { projectId: b.id, canvasId: b.entryCanvasId };
  const backward = addNode(b, b.entryCanvasId, 'canvas-link'); backward.canvasTarget = { projectId: a.id, canvasId: a.entryCanvasId };
  let context = rememberCanvasTier(null, { projectId: a.id, canvasId: a.entryCanvasId, tier: 7 });
  for (let visit = 0; visit < 20; visit++) {
    const current = visit % 2 ? b : a, portal = visit % 2 ? backward : forward;
    const index = buildTierIndex([home, current], { home: { projectId: home.id }, anchors: parseTierContext(JSON.stringify(context)).anchors });
    assert.equal(index.canvasTier(current.id, current.entryCanvasId), visit % 2 ? 8 : 7);
    context = rememberCanvasTier(context, { ...portal.canvasTarget!, tier: index.nodeTier(current.id, portal.id)! });
  }
  assert.equal(context.anchors.find(anchor => anchor.projectId === a.id)?.tier, 7);
  assert.equal(context.anchors.find(anchor => anchor.projectId === b.id)?.tier, 8);
});

test('disconnected canvases have local generations without manufacturing persistent library ancestry', () => {
  const home = newProject(), unlisted = newProject(), orphan = addCanvas(unlisted, 'Unlinked canvas');
  const child = addNode(unlisted, orphan.id, 'media');
  const index = buildTierIndex([home, unlisted], { home: { projectId: home.id } });
  assert.equal(index.canvasTier(unlisted.id, unlisted.entryCanvasId), 1);
  assert.equal(index.canvasTier(unlisted.id, orphan.id), 1);
  assert.equal(index.nodeTier(unlisted.id, child.id), 2);
  assert.equal(index.anchors().some(anchor => anchor.projectId === unlisted.id), false);
  assert.equal(index.nodeTier(unlisted.id, 'missing'), undefined);
  assert.equal(index.canvasTier('missing', orphan.id), undefined);
});

test('tiers are project-qualified even if imported workspaces reuse node and canvas IDs', () => {
  const home = newProject(), first = newProject(), second = structuredClone(first); second.id = 'second-project';
  const group = addNode(home, home.entryCanvasId, 'container');
  const a = addNode(home, home.entryCanvasId, 'canvas-link'); a.canvasTarget = { projectId: first.id };
  const b = addNode(home, home.entryCanvasId, 'canvas-link'); b.parentId = group.id; b.canvasTarget = { projectId: second.id };
  const index = buildTierIndex([home, first, second], { home: { projectId: home.id } });
  assert.equal(index.nodeTier(first.id, first.canvases[0].rootId), 2);
  assert.equal(index.nodeTier(second.id, second.canvases[0].rootId), 3);
});

test('deep hierarchies and malformed parent cycles stay finite without recursive stack limits', () => {
  const project = newProject(); let parent = project.canvases[0].rootId;
  for (let count = 0; count < 12_000; count++) { const child = addNode(project, project.entryCanvasId, 'media'); child.parentId = parent; parent = child.id; }
  const a = addNode(project, project.entryCanvasId, 'media'), b = addNode(project, project.entryCanvasId, 'media'); a.parentId = b.id; b.parentId = a.id;
  const index = buildTierIndex([project], { home: { projectId: project.id } });
  assert.equal(index.nodeTier(project.id, parent), 12_001);
  assert.equal(index.nodeTier(project.id, a.id), undefined);
  assert.equal(index.nodeTier(project.id, b.id), undefined);
});

test('context accepts only bounded identifiers and finite integer tiers and never trusts arbitrary serialized objects', () => {
  const context = normalizeTierContext({ version: 1, ignored: 'not copied', anchors: [
    { projectId: 'project', canvasId: 'canvas', tier: 8, run: '<script>' },
    { projectId: 'project', canvasId: 'canvas', tier: 3 },
    { projectId: 'project', canvasId: 'other', tier: 1.5 },
    { projectId: '../not-a-project', tier: 4 },
    { projectId: 'project', tier: Infinity },
    { projectId: 'project', tier: -1 },
    { projectId: 'project', tier: 1_000_001 },
    { projectId: 'x'.repeat(97), tier: 3 },
  ] });
  assert.deepEqual(context, { version: 1, anchors: [{ projectId: 'project', canvasId: 'canvas', tier: 3 }] });
  assert.deepEqual(parseTierContext('not json'), { version: 1, anchors: [] });
  assert.deepEqual(parseTierContext(' '.repeat(131_073)), { version: 1, anchors: [] });
  assert.deepEqual(normalizeTierContext({ version: 1, anchors: Array(513).fill({ projectId: 'project', tier: 4 }) }), { version: 1, anchors: [] });
  assert.deepEqual(rememberCanvasTier(context, { projectId: 'project', canvasId: 'canvas', tier: 9 }).anchors, [{ projectId: 'project', canvasId: 'canvas', tier: 3 }]);
  assert.deepEqual(rememberCanvasTier(context, { projectId: 'bad/value', tier: 9 }), context);
  const many = { version: 1 as const, anchors: Array.from({ length: 512 }, (_, i) => ({ projectId: `project-${i}`, tier: 2 })) };
  const bounded = rememberCanvasTier(many, { projectId: 'recent', tier: 5 });
  assert.equal(bounded.anchors.length, 512);
  assert.equal(bounded.anchors[0].projectId, 'project-1');
  assert.equal(bounded.anchors.at(-1)?.projectId, 'recent');
});
