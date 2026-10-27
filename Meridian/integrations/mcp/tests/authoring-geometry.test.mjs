import assert from 'node:assert/strict';
import { test } from 'node:test';
import { runAuthoringWorker } from '../src/authoring-service.mjs';

test('link routing detects an unrelated body and accepts a saved detour', async () => {
  const base = await runAuthoringWorker({ command: 'new', title: 'Route fixture' }), workId = base.entryCanvasId;
  const { project, aliases } = await runAuthoringWorker({ command: 'apply', project: base, recipe: { version: 1, nodes: [
    { key: 'a', canvas: workId, kind: 'media', title: 'A', x: 0, y: 0, width: 120, height: 80, sizeMode: 'fixed' },
    { key: 'b', canvas: workId, kind: 'media', title: 'B', x: 600, y: 0, width: 120, height: 80, sizeMode: 'fixed' },
    { key: 'obstacle', canvas: workId, kind: 'media', title: 'Obstacle', x: 280, y: 0, width: 120, height: 80, sizeMode: 'fixed' },
  ], links: [{ key: 'route', canvas: workId, source: 'a', target: 'b', kind: 'visual', relation: 'generic', sourceAnchor: { x: 1, y: .5 }, targetAnchor: { x: 0, y: .5 } }] } });
  const bad = await runAuthoringWorker({ command: 'validate', project, workId });
  assert(bad.diagnostics.some(issue => issue.code === 'link-crosses-node' && issue.nodeId === aliases.nodes.obstacle));
  const changed = await runAuthoringWorker({ command: 'apply', project, recipe: { version: 1, links: [{ key: 'route', existingId: aliases.links.route, points: [{ x: 180, y: 40 }, { x: 180, y: -80 }, { x: 550, y: -80 }, { x: 550, y: 40 }] }] } });
  const good = await runAuthoringWorker({ command: 'validate', project: changed.project, workId });
  assert(!good.diagnostics.some(issue => issue.code === 'link-crosses-node'));
});

test('intentional container interiors are exempt while closed external branch collisions are revealed', async () => {
  const base = await runAuthoringWorker({ command: 'new', title: 'Disclosure fixture' }), workId = base.entryCanvasId;
  const { project, aliases } = await runAuthoringWorker({ command: 'apply', project: base, recipe: { version: 1, nodes: [
    { key: 'container', canvas: workId, kind: 'container', title: 'Group', x: 0, y: 0, width: 500, height: 300, sizeMode: 'fixed', collapsed: false, childLayout: 'contained', childArrangement: 'free' },
    { key: 'inside', canvas: workId, parent: 'container', kind: 'media', title: 'Inside', x: 50, y: 50, width: 120, height: 80, sizeMode: 'fixed', childPosition: 'manual' },
    { key: 'branch', canvas: workId, kind: 'media', title: 'Branch', x: 700, y: 0, width: 120, height: 80, sizeMode: 'fixed', childLayout: 'spatial', childArrangement: 'free', collapsed: true },
    { key: 'child', canvas: workId, parent: 'branch', kind: 'media', title: 'Child', x: 1000, y: 0, width: 120, height: 80, sizeMode: 'fixed', childPosition: 'manual' },
    { key: 'neighbor', canvas: workId, kind: 'media', title: 'Neighbor', x: 1020, y: 20, width: 120, height: 80, sizeMode: 'fixed' },
  ] } });
  const result = await runAuthoringWorker({ command: 'validate', project, workId });
  const overlaps = result.diagnostics.filter(issue => issue.code === 'expanded-node-overlap');
  assert(overlaps.some(issue => issue.nodeIds.includes(aliases.nodes.child) && issue.nodeIds.includes(aliases.nodes.neighbor)));
  assert(!overlaps.some(issue => issue.nodeIds.includes(aliases.nodes.container) && issue.nodeIds.includes(aliases.nodes.inside)));
});

test('routing junctions exempt their enclosing ancestor but still detect an unrelated nested container', async () => {
  const base = await runAuthoringWorker({ command: 'new', title: 'Nested junction route fixture' }), workId = base.entryCanvasId;
  const { project, aliases } = await runAuthoringWorker({ command: 'apply', project: base, recipe: { version: 1, nodes: [
    { key: 'owner', canvas: workId, kind: 'container', title: 'Owner', x: 0, y: 0, width: 800, height: 500, sizeMode: 'fixed', collapsed: false, childLayout: 'contained', childArrangement: 'free' },
    { key: 'a', canvas: workId, parent: 'owner', kind: 'junction', title: '', utilityVariant: 'pivot', x: 94, y: 294, width: 12, height: 12, sizeMode: 'fixed', childPosition: 'manual' },
    { key: 'b', canvas: workId, parent: 'owner', kind: 'junction', title: '', utilityVariant: 'pivot', x: 644, y: 294, width: 12, height: 12, sizeMode: 'fixed', childPosition: 'manual' },
    { key: 'unrelated', canvas: workId, parent: 'owner', kind: 'container', title: 'Unrelated branch', x: 300, y: 80, width: 180, height: 120, sizeMode: 'fixed', childPosition: 'manual', collapsed: false, childLayout: 'contained', childArrangement: 'free' },
  ], links: [{ key: 'route', canvas: workId, parent: 'owner', source: 'a', target: 'b', kind: 'visual', relation: 'generic', direction: 'none' }] } });
  const good = await runAuthoringWorker({ command: 'validate', project, workId });
  assert.equal(good.validProject, true);
  assert.deepEqual(good.diagnostics.filter(issue => issue.code === 'link-crosses-node'), [], 'A junction route inside its own ancestor must be accepted.');

  const rerouted = await runAuthoringWorker({ command: 'apply', project, recipe: { version: 1, links: [{ key: 'route', existingId: aliases.links.route,
    points: [{ x: 180, y: 300 }, { x: 180, y: 140 }, { x: 560, y: 140 }, { x: 560, y: 300 }],
  }] } });
  const bad = await runAuthoringWorker({ command: 'validate', project: rerouted.project, workId });
  assert(bad.diagnostics.some(issue => issue.code === 'link-crosses-node' && issue.linkId === aliases.links.route && issue.nodeId === aliases.nodes.unrelated),
    'A sibling container is not an ancestor of either endpoint and must remain an obstacle.');
  assert(!bad.diagnostics.some(issue => issue.code === 'link-crosses-node' && issue.nodeId === aliases.nodes.owner), 'Only the real ancestor is exempt.');
});

async function windowFixture() {
  const base = await runAuthoringWorker({ command: 'new', title: 'Window clipping fixture' }), workId = base.entryCanvasId;
  const composed = await runAuthoringWorker({ command: 'apply', project: base, recipe: { version: 1, nodes: [
    { key: 'window', canvas: workId, kind: 'container', title: '', x: 0, y: 0, width: 200, height: 100, sizeMode: 'fixed', collapsed: false, containerVariant: 'window', childLayout: 'contained', childArrangement: 'grid', grid: { columns: 1, rows: 2, cellWidth: 200, cellHeight: 100, windowColumns: 1, windowRows: 1, showHeaders: false }, style: { padding: 0, childPadding: 0, borderWidth: 0 } },
    { key: 'visible', canvas: workId, parent: 'window', kind: 'media', title: '', x: 0, y: 0, width: 200, height: 100, sizeMode: 'fixed', cell: { column: 1, row: 1 }, style: { shape: 'rectangle' } },
    { key: 'offscreen', canvas: workId, parent: 'window', kind: 'media', title: '', x: 0, y: 100, width: 200, height: 100, sizeMode: 'fixed', cell: { column: 1, row: 2 }, style: { shape: 'rectangle' } },
    { key: 'peer', canvas: workId, kind: 'media', title: 'Outside peer', x: 30, y: 130, width: 60, height: 40, sizeMode: 'fixed', style: { shape: 'rectangle' } },
  ] } });
  return { ...composed, workId };
}

test('offscreen Window rows cannot overlap outside peers or expose invisible endpoint routes', async () => {
  const { project, aliases, workId } = await windowFixture();
  project.links.push({ id: crypto.randomUUID(), canvasId: workId, sourceId: aliases.nodes.visible, targetId: aliases.nodes.offscreen, kind: 'visual', relation: 'generic', points: [{ x: 60, y: 150 }] });
  const result = await runAuthoringWorker({ command: 'validate', project, workId });
  assert(!result.diagnostics.some(issue => issue.code === 'expanded-node-overlap' && issue.nodeIds.includes(aliases.nodes.offscreen) && issue.nodeIds.includes(aliases.nodes.peer)));
  assert(!result.diagnostics.some(issue => issue.code === 'link-crosses-node' && issue.nodeId === aliases.nodes.peer), 'The renderer hides a route when a Window endpoint is fully clipped.');
});

test('a partially clipped Window surface blocks only its visible portion', async () => {
  const { project, aliases, workId } = await windowFixture();
  // Test a partially clipped authored surface directly, without reflowing it.
  project.nodes.find(n => n.id === aliases.nodes.visible).x = 160;
  const peer = project.nodes.find(n => n.id === aliases.nodes.peer); Object.assign(peer, { x: 210, y: 30, width: 40, height: 30 });
  const outside = await runAuthoringWorker({ command: 'validate', project, workId });
  assert(!outside.diagnostics.some(issue => issue.code === 'expanded-node-overlap' && issue.nodeIds.includes(aliases.nodes.visible) && issue.nodeIds.includes(peer.id)), 'The clipped-away portion is not an obstacle.');
  peer.x = 180; peer.width = 15;
  const visible = await runAuthoringWorker({ command: 'validate', project, workId });
  assert(visible.diagnostics.some(issue => issue.code === 'expanded-node-overlap' && issue.nodeIds.includes(aliases.nodes.visible) && issue.nodeIds.includes(peer.id)), 'The exposed portion still blocks an unrelated peer.');
});

test('an exterior Expansion Tray escapes its source Window clip but hides with an offscreen source', async () => {
  const { project, aliases, workId } = await windowFixture();
  const source = project.nodes.find(n => n.id === aliases.nodes.visible), peer = project.nodes.find(n => n.id === aliases.nodes.peer);
  source.expansionTray = { offset: { x: 320, y: 0 }, size: { width: 180, height: 120 }, layoutVersion: 3 };
  const escaped = { id: crypto.randomUUID(), canvasId: workId, parentId: source.id, kind: 'media', title: 'Escaped tray child', x: 325, y: 10, width: 120, height: 80, sizeMode: 'fixed', childPosition: 'manual', style: { shape: 'rectangle' } };
  project.nodes.push(escaped); Object.assign(peer, { x: 340, y: 25 });
  const shown = await runAuthoringWorker({ command: 'validate', project, workId });
  assert(shown.diagnostics.some(issue => issue.code === 'expanded-node-overlap' && issue.nodeIds.includes(escaped.id) && issue.nodeIds.includes(peer.id)), 'A real exterior tray remains an outside obstacle.');
  source.y = 120;
  const hidden = await runAuthoringWorker({ command: 'validate', project, workId });
  assert(!hidden.diagnostics.some(issue => issue.code === 'expanded-node-overlap' && issue.nodeIds.includes(escaped.id) && issue.nodeIds.includes(peer.id)), 'The renderer hides a tray whose source is fully clipped.');
});
