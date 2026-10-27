import assert from 'node:assert/strict';
import test from 'node:test';
import { executeWorker, geometryDiagnostics } from '../src/authoring-worker.mjs';
import { newProject } from '../../../src/core/model.ts';
import { reflowSmartLinks, smartLinkGeometry } from '../../../src/core/smart-links.ts';

async function fixture(nodes) {
  const base = newProject('Managed disclosure validation');
  const result = await executeWorker({ command: 'apply', project: base, recipe: { version: 1, nodes: [
    { key: 'rail', canvas: base.entryCanvasId, kind: 'junction', utilityVariant: 'joint', x: 100, y: 100,
      width: 12, height: 12, sizeMode: 'fixed', smartLink: { direction: 'down', gap: 48 },
      childLayout: 'spatial', childArrangement: 'free', collapsed: false },
    ...nodes.map(node => ({ canvas: base.entryCanvasId, sizeMode: 'fixed', childPosition: 'manual',
      style: { shape: 'rectangle', padding: 0 }, ...node })),
  ] } });
  return { ...result, workId: base.entryCanvasId };
}

test('expanded validation reflows a collapsed Smart Link branch without mutating its compact saved source', async () => {
  const { project, aliases, workId } = await fixture([
    { key: 'branch', parent: 'rail', kind: 'media', title: 'Branch', x: 154, y: 154, width: 100, height: 60,
      collapsed: true, childLayout: 'spatial', childArrangement: 'free' },
    { key: 'child', parent: 'branch', kind: 'media', title: 'Child', x: 154, y: 256, width: 100, height: 80 },
    { key: 'peer', parent: 'rail', kind: 'media', title: 'Peer', x: 154, y: 262, width: 100, height: 60 },
  ]);
  const before = structuredClone(project);
  const child = project.nodes.find(node => node.id === aliases.nodes.child);
  const peer = project.nodes.find(node => node.id === aliases.nodes.peer);
  assert(child.y < peer.y + peer.height && peer.y < child.y + child.height,
    'The compact source deliberately reserves no room for this hidden child.');
  const expanded = structuredClone(project);
  for (const node of expanded.nodes) node.collapsed = false;
  reflowSmartLinks(expanded, workId);
  assert(expanded.nodes.find(node => node.id === peer.id).y > peer.y, 'Opening the branch must move its managed peer.');

  const result = geometryDiagnostics(project, workId);
  assert.deepEqual(result.diagnostics, [], 'Inspect the reflowed expanded layout, not compact saved coordinates.');
  assert.equal(result.checkedSmartLinks, 1);
  assert(result.checkedSmartSegments >= 3, 'The real trunk and both member connectors were checked.');
  assert.deepEqual(project, before, 'Validation must preserve disclosure and exact authored geometry.');
});

test('authoring validation diagnoses a Smart Link whose handle is trapped in an unrelated node', async () => {
  const { project, aliases, workId } = await fixture([
    { key: 'member', parent: 'rail', kind: 'media', title: 'Member', x: 154, y: 154, width: 100, height: 60 },
    { key: 'obstacle', kind: 'media', title: 'Fixed obstacle', x: 70, y: 70, width: 70, height: 70, positionLocked: true },
  ]);
  const before = structuredClone(project), geometry = smartLinkGeometry(project, aliases.nodes.rail);
  assert(geometry.blockedNodeIds.includes(aliases.nodes.obstacle));
  const result = await executeWorker({ command: 'validate', project, workId });
  assert(result.diagnostics.some(issue => issue.code === 'smart-link-blocked' && issue.severity === 'error'
    && issue.nodeId === aliases.nodes.rail && issue.nodeIds.includes(aliases.nodes.obstacle)),
  'A withheld connector must be reported even though it cannot cross a body as a rendered segment.');
  assert.deepEqual(project, before);
});

test('Smart Link validation accepts indexed detours and bounded Window endpoint interiors', async () => {
  const { project, aliases, workId } = await fixture([
    { key: 'window', parent: 'rail', kind: 'container', title: '', x: 154, y: 154, width: 200, height: 100,
      collapsed: false, containerVariant: 'window', childLayout: 'contained', childArrangement: 'grid',
      grid: { columns: 1, rows: 2, cellWidth: 200, cellHeight: 100, windowColumns: 1, windowRows: 1, showHeaders: false },
      style: { shape: 'rectangle', padding: 0, childPadding: 0, borderWidth: 0 } },
    { key: 'visible', parent: 'window', kind: 'media', title: '', x: 154, y: 154, width: 200, height: 100, cell: { column: 1, row: 1 } },
    { key: 'offscreen', parent: 'window', kind: 'media', title: '', x: 154, y: 254, width: 200, height: 100, cell: { column: 1, row: 2 } },
    { key: 'peer', parent: 'rail', kind: 'media', title: 'Peer', x: 154, y: 302, width: 100, height: 60 },
    { key: 'obstacle', kind: 'media', title: 'Obstacle', x: 90, y: 140, width: 32, height: 28, positionLocked: true },
  ]);
  const before = structuredClone(project), geometry = smartLinkGeometry(project, aliases.nodes.rail);
  assert.equal(geometry.blockedNodeIds, undefined);
  assert(geometry.segments.some(segment => !segment.nodeId && (segment.from.x !== geometry.origin.x || segment.to.x !== geometry.origin.x)),
    'The tested trunk actually detours around the fixed obstacle.');
  const result = geometryDiagnostics(project, workId);
  assert.deepEqual(result.diagnostics, []);
  assert.equal(result.checkedSmartSegments, geometry.segments.length);
  assert.deepEqual(project, before, 'Window cell identities, coordinates, and source disclosure remain untouched.');
});
