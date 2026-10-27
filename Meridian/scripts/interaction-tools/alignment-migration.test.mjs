import test from 'node:test';
import assert from 'node:assert/strict';
import { addCanvas, addNode, commitSnapshots, newProject } from '../../src/core/model.ts';
import { makeAlignmentCandidate, auditAlignmentCandidate } from './prepare-alignment-migration.mjs';

test('only Work alignment changes; node Free overrides, sticky/grid, assets and exact snapshots survive', () => {
  let baseline = newProject('Migration fixture');
  baseline.revision = 7; baseline.canvases[0].snapping = { stickyEdges: false, snapToGrid: true, smartAlign: false };
  addCanvas(baseline, 'Inherit');
  addCanvas(baseline, 'Already enabled').snapping = { stickyEdges: true, smartAlign: true };
  const free = addNode(baseline, baseline.entryCanvasId, 'media', 'Deliberately Free');
  free.snapping = { stickyEdges: false, snapToGrid: false, smartAlign: false };
  baseline.assets.push({ id: crypto.randomUUID(), name: 'original.txt', mime: 'text/plain', base64: 'SGk=' });
  // The helper consumes saved HTTP JSON, not model-only null-prototype maps.
  baseline = JSON.parse(JSON.stringify(commitSnapshots(baseline)));
  const before = structuredClone(baseline), candidate = makeAlignmentCandidate(baseline);
  const report = auditAlignmentCandidate(baseline, candidate);
  assert.deepEqual(baseline, before); assert.equal(report.changedWorks.length, 2);
  assert.equal(report.preservedNodeFreeMovementOverrides.length, 1);
  assert.deepEqual(candidate.nodes, baseline.nodes); assert.deepEqual(candidate.snapshots, baseline.snapshots);
  assert.deepEqual(candidate.canvases[0].snapping, { stickyEdges: false, snapToGrid: true, smartAlign: true });
  assert.deepEqual(candidate.canvases[1].snapping, { smartAlign: true });
  for (const alter of [
    copy => { copy.nodes.find(node => node.id === free.id).snapping.smartAlign = true; },
    copy => { copy.canvases[0].snapping.stickyEdges = true; },
    copy => { copy.canvases[0].style = { background: '#ffffff' }; },
    copy => { Object.values(copy.snapshots)[0].savedAt = '2099-01-01'; },
    copy => { copy.assets[0].base64 = 'AAAA'; },
    copy => { copy.revision++; },
  ]) { const changed = structuredClone(candidate); alter(changed); assert.throws(() => auditAlignmentCandidate(baseline, changed)); }
});
